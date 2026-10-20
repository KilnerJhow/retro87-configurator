// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HidSharp;
using Retro87.Models;
using Retro87.Protocol;
using Retro87.Services;
using Retro87.Localization;

namespace Retro87.ViewModels;

/// <summary>
/// Main window view-model. Owns the device monitor, the protocol instance, the local profile
/// store and the read-only firmware notification service.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly DeviceMonitor _monitor;
    private readonly JpProtocolFactory _factory;
    private readonly FirmwareUpdateChecker _updates = new();
    private readonly ProfileStore _profiles = new();
    private readonly ConfigBackup _backups = new();

    private IJpProtocol? _protocol;
    private Window? _window;

    /// <summary>
    /// Reads the keyboard's own input reports so a Super Button is identified even when the
    /// desktop consumes the event (media keys) or the window is not focused.
    /// </summary>
    private SuperButtonInputListener? _inputListener;
    private DispatcherTimer? _pressedTimer;

    /// <summary>
    /// Super Button the raw vendor signal (see <see cref="SuperButtonRawDecoder"/>) last reported,
    /// awaiting a possible mapped-id press for the same physical event. <see cref="HandleSuperButtonPress"/>
    /// consumes it (to pick the right button when its target is shared with another); if nothing
    /// consumes it within <see cref="RawPortResolveDelayMs"/>, <see cref="ResolveRawPortPress"/>
    /// resolves it alone — the button has no mapped target, which is the whole reason this signal
    /// exists.
    /// </summary>
    private SuperButtonRawPress? _pendingRawPress;
    private long _pendingRawPressTick;
    private DispatcherTimer? _rawResolveTimer;
    private const int RawPortResolveDelayMs = 80;

    /// <summary>
    /// Super Buttons currently held down per the raw vendor signal — authoritative for the eight
    /// wireless ones, which <see cref="FlashPressed"/>'s fixed-duration timer must not clear early.
    /// </summary>
    private readonly HashSet<MappingRowViewModel> _heldRawRows = new();

    /// <summary>
    /// Id space of the connected family: the picker and the names must follow it, because the JP
    /// and Xbox families number the modifiers and the media/mouse ids differently.
    /// </summary>
    private MappingIdSpace _idSpace = MappingIdSpace.Jp;

    /// <summary>Every mapping row (85 board keys + 10 Super Buttons), in protocol order.</summary>
    private readonly List<MappingRowViewModel> _allMappings = new();

    /// <summary>Timer that asks which button was pressed when no key event arrives.</summary>
    private DispatcherTimer? _superQuestionTimer;

    /// <summary>
    /// Last configuration read from (or loaded for) the keyboard. Writes start from a copy of
    /// it so that data the UI shows but does not edit — above all the macro steps, which the
    /// macro tab only summarises — is written back unchanged instead of being erased.
    /// </summary>
    private KeyboardConfig? _loadedConfig;

    public MainWindowViewModel(DeviceMonitor monitor, JpProtocolFactory factory)
    {
        _monitor = monitor;
        _factory = factory;
        // The monitor polls on a timer thread; the list, the status line and the connection belong to the UI thread.
        _monitor.DevicesChanged += (_, devices) => Dispatcher.UIThread.Post(() => UpdateDevices(devices));
        UpdateDevices(_monitor.Devices);
        ReloadProfiles();
    }

    private readonly LanguagePreference _languagePreference = new();

    /// <summary>Languages offered in the header picker.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions => Loc.Languages;

    [ObservableProperty]
    private LanguageOption _selectedLanguage = Loc.Languages.First(l => l.Code == Loc.Instance.Code);

    /// <summary>
    /// The choice is saved and applied on the next start: the notice is written in the language that
    /// was picked, so it is the one the user will read after restarting.
    /// </summary>
    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        _languagePreference.Save(value.Code);
        LanguageNotice = value.Code == Loc.Instance.Code ? string.Empty : Loc.In(value.Code, "language_restart");
    }

    [ObservableProperty]
    private string _languageNotice = string.Empty;

    public ObservableCollection<DeviceInfo> Devices { get; } = new();

    /// <summary>The 85 board keys, in protocol order — the ones drawn on the keyboard.</summary>
    public ObservableCollection<MappingRowViewModel> Keys { get; } = new();

    /// <summary>
    /// The five connections of the Super Buttons block, in device order: the keyboard's own pair plus
    /// A, B, X and Y. The block reads them as step 1 of two (see <see cref="IsConnectionStepVisible"/>).
    /// </summary>
    public ObservableCollection<SuperConnectionViewModel> SuperConnections { get; } = new();

    /// <summary>The four jacks (A, B, X, Y) drawn as round ports; the keyboard's own pair is separate.</summary>
    public ObservableCollection<SuperConnectionViewModel> SuperPorts { get; } = new();

    /// <summary>The keyboard's own pair ("On the keyboard"), or null before the first read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardChip))]
    private SuperConnectionViewModel? _keyboardConnection;

    /// <summary>The keyboard's pair as a list of one, so the chip row can bind it like the others.</summary>
    public IReadOnlyList<SuperConnectionViewModel> KeyboardChip
        => KeyboardConnection is null ? Array.Empty<SuperConnectionViewModel>() : new[] { KeyboardConnection };

    /// <summary>
    /// Connection whose pair is in the block. <c>null</c> means step 1: nothing was chosen yet, so
    /// there is no pair on screen and the dashed frame asks the connection.
    /// </summary>
    [ObservableProperty]
    private SuperConnectionViewModel? _activeConnection;

    /// <summary>The two Super Buttons of the active connection, in device order.</summary>
    public IReadOnlyList<MappingRowViewModel> SuperPair
        => ActiveConnection?.Buttons ?? Array.Empty<MappingRowViewModel>();

    /// <summary>True once a connection was chosen: the pair exists and can be clicked.</summary>
    public bool IsPairVisible => ActiveConnection is not null;

    /// <summary>True while one of the pair's buttons is the one in the side panel.</summary>
    private bool PairHasSelection
        => ActiveConnection is not null
           && SelectedKey is { IsSuperButton: true } selected
           && ActiveConnection.Buttons.Contains(selected);

    /// <summary>Dashed frame on the connection chips — asking which connection.</summary>
    public bool IsConnectionStepVisible => ActiveConnection is null;

    /// <summary>Dashed frame on the pair — asking which of the two buttons, while none is chosen.</summary>
    public bool IsPairStepVisible => IsPairVisible && !PairHasSelection;

    /// <summary>Second step label, naming the connection the pair belongs to.</summary>
    public string PairStepText => ActiveConnection is null
        ? Loc.T("pair_step_none")
        : Loc.F("pair_step_conn", ActiveConnection.Caption);

    partial void OnActiveConnectionChanged(SuperConnectionViewModel? value)
    {
        OnPropertyChanged(nameof(SuperPair));
        OnPropertyChanged(nameof(PairStepText));
        OnPropertyChanged(nameof(IsPairVisible));
        OnPropertyChanged(nameof(IsConnectionStepVisible));
        OnPropertyChanged(nameof(IsPairStepVisible));
    }

    partial void OnSelectedKeyChanged(MappingRowViewModel? value)
        => OnPropertyChanged(nameof(IsPairStepVisible));

    /// <summary>True while the window is waiting for the next Super Button press.</summary>
    [ObservableProperty]
    private bool _isListeningForSuperButton;

    /// <summary>
    /// Last Super Button the listener identified, with its port. Empty until the first press;
    /// the block shows it as the "you pressed this" line (see <see cref="HasSuperButtonPress"/>).
    /// </summary>
    [ObservableProperty]
    private string _lastSuperButtonText = string.Empty;

    /// <summary>True once a Super Button was pressed in this session.</summary>
    public bool HasSuperButtonPress => LastSuperButtonText.Length > 0;

    partial void OnLastSuperButtonTextChanged(string value)
        => OnPropertyChanged(nameof(HasSuperButtonPress));

    /// <summary>
    /// Buttons offered when the press could not be resolved to a single one: the matching
    /// candidates (same target) or all ten when nothing matched.
    /// </summary>
    private IReadOnlyList<MappingRowViewModel> _superButtonChoices = Array.Empty<MappingRowViewModel>();

    public IReadOnlyList<MappingRowViewModel> SuperButtonChoices
    {
        get => _superButtonChoices;
        private set => SetProperty(ref _superButtonChoices, value);
    }

    partial void OnIsListeningForSuperButtonChanged(bool value)
    {
        if (!value) _superQuestionTimer?.Stop();
    }

    /// <summary>
    /// The Mecha Break edition prints symbols on the four ports instead of A/B/X/Y. The setting is
    /// local and session-only; the protocol keeps using A/B/X/Y.
    /// </summary>
    [ObservableProperty]
    private bool _useMechaBreakPorts = true;

    /// <summary>Label of the port-style toggle.</summary>
    public string PortStyleLabel => UseMechaBreakPorts ? Loc.T("ports_mecha") : Loc.T("ports_abxy");

    partial void OnUseMechaBreakPortsChanged(bool value)
    {
        OnPropertyChanged(nameof(PortStyleLabel));
        if (!IsMappingEmpty) RebuildKeyViews();
    }

    [RelayCommand]
    private void TogglePortStyle() => UseMechaBreakPorts = !UseMechaBreakPorts;

    /// <summary>Stroked part of a port symbol in the Mecha Break style (SVG path data, 16x16).</summary>
    private static Geometry? PortIconLines(string? letter) => letter switch
    {
        "A" => Geometry.Parse("M4.8 3 L7.9 8.7 L4.8 14.5 M9 3 L12.4 8.7 L9 14.5"),
        "B" => Geometry.Parse("M2 8.5 L5 3.3 H11 L14 8.5 L11 13.7 H5 Z"),
        "X" => Geometry.Parse("M3.6 3 L12.4 14.5 M12.4 3 L3.6 14.5"),
        _ => null,
    };

    /// <summary>Filled part of a port symbol in the Mecha Break style (SVG path data, 16x16).</summary>
    private static Geometry? PortIconFill(string? letter) => letter switch
    {
        "B" => Geometry.Parse("M3.8 9.6 H9 L12.2 4.8 L13.2 8.5 L11.4 12.6 H5.2 Z"),
        "Y" => Geometry.Parse("M1.5 3 L5.2 4.8 H10.8 L14.5 3 L8 14.5 Z"),
        _ => null,
    };

    /// <summary>Symbol or letter shown on a connection chip.</summary>
    private string PortName(string? letter) => letter switch
    {
        null => Loc.T("port_on_keyboard_cap"),
        "A" when UseMechaBreakPorts => "»",
        "B" when UseMechaBreakPorts => "◆",
        "Y" when UseMechaBreakPorts => "▾",
        _ => letter,
    };

    /// <summary>Full label used in tooltips and details, keeping the A/B/X/Y reference.</summary>
    private string PortDetail(string? letter) => letter is null
        ? Loc.T("on_keyboard")
        : UseMechaBreakPorts
            ? Loc.F("port_detail_mecha", PortName(letter), letter)
            : Loc.F("port_detail_letter", letter);

    /// <summary>The ten Super Buttons, in device order, offered when the event cannot be identified.</summary>
    public IReadOnlyList<MappingRowViewModel> AllSuperButtons
        => _allMappings.Where(row => row.IsSuperButton).ToList();

    /// <summary>True while the block asks which Super Button the user pressed.</summary>
    [ObservableProperty]
    private bool _isAskingSuperButton;

    /// <summary>Prompt shown with the ten buttons when the event cannot be identified.</summary>
    [ObservableProperty]
    private string _superButtonQuestion = Loc.T("super_question");

    public ObservableCollection<MacroSlotViewModel> Macros { get; } = new();
    [ObservableProperty]
    private DeviceInfo? _selectedDevice;

    /// <summary>The key (or Super Button) shown in the side panel of the Mapeamento tab.</summary>
    [ObservableProperty]
    private MappingRowViewModel? _selectedKey;

    /// <summary>
    /// True when the window is too narrow for the side-by-side layout: the selected-key panel leaves
    /// the right column and joins the tab's own scroll region, under the board and the Super Buttons,
    /// instead of keeping 320 px of an 880 px window — which would shrink the key labels past reading
    /// size and, if the panel took the height instead, squeeze the board to a strip.
    /// </summary>
    [ObservableProperty]
    private bool _isNarrowLayout;

    /// <summary>Column span of the board column (the full width when stacked).</summary>
    public int ContentColumnSpan => IsNarrowLayout ? 2 : 1;

    /// <summary>Board column margin: a gutter for the scrollbar beside, none when stacked.</summary>
    public Thickness ContentMargin => IsNarrowLayout
        ? new Thickness(0, 0, 0, 10)
        : new Thickness(0, 0, 14, 0);

    partial void OnIsNarrowLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(ContentColumnSpan));
        OnPropertyChanged(nameof(ContentMargin));
    }

    [ObservableProperty]
    private string _statusMessage = Loc.T("st_initial");

    /// <summary>Severity of <see cref="StatusMessage"/>; drives the glyph and color of the bar.</summary>
    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Index of the visible tab; bound two-way so Alt+1..5 can jump between tabs.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private byte _specialFeature;

    [ObservableProperty]
    private byte _voice = 2;

    /// <summary><c>volume_lv</c> of the connected Retro 87 "X" (family Xbox), 1–5 in the official app.</summary>
    [ObservableProperty]
    private byte _xboxVolumeLevel;

    /// <summary><c>fast_setting</c> of the connected Retro 87 "X": the Quick Setup toggles as bits.</summary>
    [ObservableProperty]
    private byte _xboxFastSetting;

    /// <summary>"Disable Windows key" — bit <c>0x1</c> of <c>fast_setting</c>.</summary>
    public bool XboxDisableWindowsKey
    {
        get => (XboxFastSetting & XboxWire.FastSetting.DisableWindowsKey) != 0;
        set => SetFastSettingBit(XboxWire.FastSetting.DisableWindowsKey, value);
    }

    /// <summary>"Disable Alt + Tab" — bit <c>0x2</c> of <c>fast_setting</c>.</summary>
    public bool XboxDisableAltTab
    {
        get => (XboxFastSetting & XboxWire.FastSetting.DisableAltTab) != 0;
        set => SetFastSettingBit(XboxWire.FastSetting.DisableAltTab, value);
    }

    /// <summary>"Disable Alt + F4" — bit <c>0x4</c> of <c>fast_setting</c>.</summary>
    public bool XboxDisableAltF4
    {
        get => (XboxFastSetting & XboxWire.FastSetting.DisableAltF4) != 0;
        set => SetFastSettingBit(XboxWire.FastSetting.DisableAltF4, value);
    }

    /// <summary>Flips one Quick Setup bit, leaving every other bit of the byte as the device had it.</summary>
    private void SetFastSettingBit(byte mask, bool on)
    {
        byte next = on ? (byte)(XboxFastSetting | mask) : (byte)(XboxFastSetting & ~mask);
        if (next != XboxFastSetting) XboxFastSetting = next;
    }

    partial void OnXboxFastSettingChanged(byte value)
    {
        OnPropertyChanged(nameof(XboxDisableWindowsKey));
        OnPropertyChanged(nameof(XboxDisableAltTab));
        OnPropertyChanged(nameof(XboxDisableAltF4));
        RefreshChanges();
    }

    partial void OnProfileNameChanged(string value) => RefreshChanges();

    partial void OnSpecialFeatureChanged(byte value) => RefreshChanges();

    partial void OnXboxVolumeLevelChanged(byte value) => RefreshChanges();

    [ObservableProperty]
    private string? _updateMessage;

    /// <summary>Severity of the firmware notice (it is not always good news).</summary>
    [ObservableProperty]
    private StatusSeverity _updateSeverity = StatusSeverity.Info;

    [ObservableProperty]
    private bool _updateAvailable;

    /// <summary>Folder the local profiles are kept in (shown in the UI).</summary>
    public string ProfilesDirectory => _profiles.RootDirectory;

    /// <summary>Folder the pre-write backups are kept in (shown in the UI).</summary>
    public string BackupsDirectory => _backups.RootDirectory;

    /// <summary>True when a device operation may run: connected and not already busy.</summary>
    public bool CanUseDevice => IsConnected && !IsBusy;

    /// <summary>True when the connected session supports writes at all.</summary>
    public bool CanWriteDevice => CanUseDevice && (_protocol?.CanWrite ?? false);

    /// <summary>
    /// True when Salvar is offered: the session accepts writes and the screen differs from what the
    /// keyboard holds. The permission to write is the confirmation in the save dialog.
    /// </summary>
    public bool CanSave => CanWriteDevice && HasChanges;

    /// <summary>True when the connected session is read-only.</summary>
    public bool IsReadOnlySession => IsConnected && _protocol is { CanWrite: false };

    /// <summary>True when the connected keyboard speaks the Xbox family (Retro 87 "X").</summary>
    /// <remarks>
    /// The Config tab shows one card per family: <c>special feature</c>/<c>voice</c> belong to the
    /// JP family, while <c>volume_lv</c>/<c>fnLock</c>/colour theme belong to the Xbox one. The
    /// flags follow the session the factory created, so the wrong card is never offered.
    /// </remarks>
    public bool IsXboxFamily => _protocol is XboxJpProtocol;

    /// <summary>True when the connected keyboard speaks the JP family.</summary>
    public bool IsJpFamily => _protocol is JpProtocol;

    /// <summary>True while no family is known yet (nothing connected): the Config tab asks to connect.</summary>
    public bool IsFamilyUnknown => !IsXboxFamily && !IsJpFamily;

    /// <summary>Connection state as text (the UI must never show a raw boolean).</summary>
    public string ConnectionText => IsConnected ? Loc.T("connected") : Loc.T("disconnected");

    /// <summary>Firmware version read from the keyboard, for the "About the keyboard" tab.</summary>
    public string FirmwareVersionText => _protocol is { DeviceVersion: > 0f } p
        ? p.DeviceVersion.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
        : "—";

    /// <summary>Lets the screen tests show a keyboard that reports its battery, without HID.</summary>
    internal void ShowBatteryFrom(IJpProtocol protocol)
    {
        _protocol = protocol;
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(BatteryFillWidth));
        OnPropertyChanged(nameof(BatteryBrush));
        OnPropertyChanged(nameof(BatteryAccessibleName));
    }

    /// <summary>Battery level read when connecting, shown in the header next to the keyboard name.</summary>
    public string BatteryText => _protocol is { BatteryPercent: >= 0 } p ? $"{p.BatteryPercent}%" : "";

    /// <summary>Width in pixels of the fill inside the battery icon (0 to 23).</summary>
    public double BatteryFillWidth => _protocol is { BatteryPercent: >= 0 } p ? Math.Clamp(p.BatteryPercent, 0, 100) * 23 / 100.0 : 0;

    /// <summary>Green from 40%, amber from 15%, red below.</summary>
    public IBrush BatteryBrush => _protocol?.BatteryPercent switch
    {
        >= 40 => BrushFor("Ok"),
        >= 15 => BrushFor("Warn"),
        _ => BrushFor("Danger"),
    };

    /// <summary>Spoken form for screen readers, since the icon is drawn.</summary>
    public string BatteryAccessibleName => _protocol is { BatteryPercent: >= 0 } p ? Loc.F("battery_pct", p.BatteryPercent) : "";

    /// <summary>False when the family does not report the battery (JP on Linux).</summary>
    public bool HasBattery => _protocol is { BatteryPercent: >= 0 };

    /// <summary>Which drawn mark the status bar shows; one shape per severity, never a font glyph.</summary>
    public bool StatusIsInfo => StatusSeverity == StatusSeverity.Info;

    /// <inheritdoc cref="StatusIsInfo"/>
    public bool StatusIsSuccess => StatusSeverity == StatusSeverity.Success;

    /// <inheritdoc cref="StatusIsInfo"/>
    public bool StatusIsWarning => StatusSeverity == StatusSeverity.Warning;

    /// <inheritdoc cref="StatusIsInfo"/>
    public bool StatusIsError => StatusSeverity == StatusSeverity.Error;

    /// <summary>Color of the status glyph.</summary>
    public IBrush StatusGlyphBrush => StatusSeverity switch
    {
        StatusSeverity.Success => BrushFor("Ok"),
        StatusSeverity.Warning => BrushFor("Warn"),
        StatusSeverity.Error => BrushFor("Danger"),
        _ => BrushFor("FgMuted"),
    };

    /// <summary>Color of the status text: neutral for information, colored for warnings and errors.</summary>
    public IBrush StatusTextBrush => StatusSeverity switch
    {
        StatusSeverity.Warning => BrushFor("Warn"),
        StatusSeverity.Error => BrushFor("Danger"),
        _ => BrushFor("Fg"),
    };

    /// <summary>Color of the firmware notice: green when up to date, amber when it needs attention.</summary>
    public IBrush UpdateBrush => UpdateSeverity switch
    {
        StatusSeverity.Success => BrushFor("Ok"),
        StatusSeverity.Warning => BrushFor("Warn"),
        StatusSeverity.Error => BrushFor("Danger"),
        _ => BrushFor("FgDim"),
    };

    private static IBrush BrushFor(string key) => Palette.For(key);

    /// <summary>Number of board keys whose target differs from the default.</summary>
    public int ModifiedMappingCount => _allMappings.Count(m => m.IsModified);

    /// <summary>True once a configuration has been read (or loaded) and there are rows to show.</summary>
    public bool HasMappings => _allMappings.Count > 0;

    /// <summary>True before anything was read: the mapping tab shows an invitation instead of an empty list.</summary>
    public bool IsMappingEmpty => !HasMappings;

    /// <summary>True before anything was read: the macro tab shows an invitation instead of nothing.</summary>
    public bool IsMacrosEmpty => Macros.Count == 0;

    /// <summary>Line above the keyboard: how much of the device differs from the factory targets.</summary>
    public string MappingSummary => _allMappings.Count == 0
        ? Loc.T("nothing_read")
        : Loc.F("mapping_summary", KeyboardLayout.FirstExtraPosition, ModifiedMappingCount, SuperButtons.Count);

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseDevice));
        OnPropertyChanged(nameof(CanWriteDevice));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsReadOnlySession));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(FirmwareVersionText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(BatteryFillWidth));
        OnPropertyChanged(nameof(BatteryBrush));
        OnPropertyChanged(nameof(BatteryAccessibleName));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseDevice));
        OnPropertyChanged(nameof(CanWriteDevice));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnStatusSeverityChanged(StatusSeverity value)
    {
        OnPropertyChanged(nameof(StatusIsInfo));
        OnPropertyChanged(nameof(StatusIsSuccess));
        OnPropertyChanged(nameof(StatusIsWarning));
        OnPropertyChanged(nameof(StatusIsError));
        OnPropertyChanged(nameof(StatusGlyphBrush));
        OnPropertyChanged(nameof(StatusTextBrush));
    }

    partial void OnUpdateSeverityChanged(StatusSeverity value) => OnPropertyChanged(nameof(UpdateBrush));

    /// <summary>Sets the status line together with its severity, so it never lies about a failure.</summary>
    private void SetStatus(StatusSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
    }

    /// <summary>Wires window-lifecycle handling (called from App).</summary>
    public void Attach(Window window)
    {
        _window = window;
        _monitor.Start();
        window.Closed += (_, _) =>
        {
            Dispose();

            // Leave right after releasing the keyboard. On Linux, Avalonia's D-Bus connection is torn
            // down after the dispatcher stopped, and that fails on a thread-pool thread with an
            // unhandled TaskCanceledException (a stack trace in the terminal, a crash exit code).
            if (OperatingSystem.IsLinux()) Environment.Exit(0);
        };
    }

    partial void OnSelectedDeviceChanged(DeviceInfo? value)
    {
        if (value is null) return;
        _monitor.Select(value);
    }

    /// <summary>HID paths of the keyboard the current session was opened on (what a restart takes away).</summary>
    private HashSet<string> _connectedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The keyboard to reconnect to after it dropped the connection by itself; null when none.</summary>
    private DeviceInfo? _reconnectTarget;

    private bool _reconnecting;

    // ---------------------------------------------------------------- firmware notice

    /// <summary>
    /// Only <b>notifies</b> when the server has a newer firmware. It never downloads
    /// or flashes anything (that is done with the official Windows tool).
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (_protocol is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }

        IsBusy = true;
        UpdateAvailable = false;
        UpdateMessage = null;
        SetStatus(StatusSeverity.Info, Loc.T("st_checking"));
        try
        {
            int firmwareType = SelectedDevice?.Pid == KnownDevices.Retro87Wireless
                ? FirmwareUpdateChecker.FirmwareTypeRetro24G
                : FirmwareUpdateChecker.FirmwareTypeRetroUsb;

            var result = await _updates.CheckAsync(firmwareType, _protocol.DeviceVersion);
            if (result is null)
            {
                SetStatus(StatusSeverity.Warning, Loc.T("st_update_nonet"));
            }
            else if (result.UpdateAvailable)
            {
                UpdateAvailable = true;
                UpdateSeverity = StatusSeverity.Warning;
                string text = Loc.F("st_update_available", result.ServerVersion ?? "?", result.DeviceVersion ?? "?");
                UpdateMessage = text;
                SetStatus(StatusSeverity.Warning, text);
            }
            else
            {
                SetStatus(StatusSeverity.Success, Loc.F("st_firmware_ok", result.DeviceVersion ?? "?"));
            }
        }
        catch (Exception ex)
        {
            SetStatus(StatusSeverity.Error, Loc.F("st_update_failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Dispose()
    {
        _superQuestionTimer?.Stop();
        _rawResolveTimer?.Stop();
        Disconnect();
        _monitor.Dispose();
    }
}
