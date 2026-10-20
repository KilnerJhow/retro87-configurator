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

public partial class MainWindowViewModel
{
    // ---------------------------------------------------------------- device configuration

    [RelayCommand]
    private async Task ReadConfigAsync()
    {
        if (_protocol is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }
        if (IsBusy) return;

        var protocol = _protocol;
        IsBusy = true;
        SetStatus(StatusSeverity.Info, Loc.T("st_reading"));
        try
        {
            KeyboardConfig config = await Task.Run(() => protocol.ReadConfig());
            if (!ReferenceEquals(_protocol, protocol)) return;

            // The Xbox family goes idle and stops answering (the dongle even says so with 0xC0).
            // Distinguish that from a protocol bug instead of drawing an empty configuration.
            if (protocol is XboxJpProtocol { LastRawConfig: null } xbox)
            {
                SetStatus(StatusSeverity.Warning, DescribeUnresponsive(xbox));
                return;
            }
            ApplyToUi(config);
            SetStatus(StatusSeverity.Success, Loc.T("st_read_ok"));
        }
        catch (Exception ex)
        {
            SetStatus(StatusSeverity.Error, Loc.F("st_read_failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WriteConfigAsync()
    {
        if (_protocol is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }
        if (IsBusy) return;

        if (!_protocol.CanWrite)
        {
            SetStatus(StatusSeverity.Warning, Loc.T("st_write_unavailable_ro"));
            return;
        }

        // Project rule: writing to the keyboard needs explicit permission. It is the confirmation
        // in the save dialog (the only caller), and a backup of what the device holds is mandatory.
        // Never write a screen that was not filled from the device (or a profile): the editable
        // state would be defaults, and the diff against the device would happily write them —
        // clearing mappings and fields the user never saw.
        if (_loadedConfig is null)
        {
            SetStatus(StatusSeverity.Warning,
                Loc.T("st_read_before_write"));
            return;
        }

        var protocol = _protocol;
        IsBusy = true;
        _writing = true;
        SetStatus(StatusSeverity.Info, Loc.T("st_writing"));
        try
        {
            string? backup = SaveBackupBeforeWrite();
            if (protocol is IWriteDiagnostics { LastRawConfig: { Length: > 0 } } && backup is null)
            {
                SetStatus(StatusSeverity.Error,
                    Loc.F("st_backup_failed", _backups.LastError ?? string.Empty));
                return;
            }

            // The Xbox ids are shared between categories, so hand the picker's category per key
            // to the protocol: it is what tells a media id from the key id with the same value.
            if (protocol is XboxJpProtocol xbox) xbox.TargetKinds = BuildTargetKinds();
            KeyboardConfig snapshot = SnapshotFromUi();

            // The whole write (plus the read-back) can take seconds; running it on the UI thread
            // would freeze the window for that whole time.
            KeyboardConfig? readBack = await Task.Run(() =>
            {
                if (!protocol.WriteConfig(snapshot)) return null;
                // Read back so the UI (and the protocol's diff baseline) reflect what the device
                // actually holds now instead of what we hoped we wrote.
                return protocol.ReadConfig();
            });
            // The keyboard may restart its link while (or just after) it applies the write: the
            // session was replaced by the reconnection, so what the screen shows after reconnecting
            // is what the keyboard really holds. Do not call that a write failure.
            if (!ReferenceEquals(_protocol, protocol)) return;
            if (readBack is null)
            {
                SetStatus(StatusSeverity.Error,
                    Loc.T("st_write_unconfirmed"));
                return;
            }

            string warnings = CollectWriteWarnings();
            ApplyToUi(readBack);

            SetStatus(warnings.Length == 0 ? StatusSeverity.Success : StatusSeverity.Warning,
                backup is null
                    ? Loc.T("st_written") + warnings
                    : Loc.F("st_written_backup", Path.GetFileName(backup)) + warnings);
        }
        catch (Exception ex)
        {
            SetStatus(StatusSeverity.Error, Loc.F("st_write_failed", ex.Message));
        }
        finally
        {
            _writing = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Saves the raw configuration the device reported before a write. Returns the path, or null
    /// when the session does not expose a raw configuration (the JP family keeps none) —
    /// in that case the write still proceeds, since the JP path predates this safety net.
    /// </summary>
    private string? SaveBackupBeforeWrite()
    {
        if (_protocol is not IWriteDiagnostics { LastRawConfig: { Length: > 0 } raw }) return null;

        var device = SelectedDevice;
        string name = device is null ? "retro87" : $"{device.ProductName}-{device.Pid:X4}";
        return _backups.Save(name, raw);
    }

    /// <summary>Appends whatever the protocol could not turn into writes (e.g. a macro with no host key).</summary>
    private string CollectWriteWarnings()
    {
        if (_protocol is not IWriteDiagnostics { LastWriteWarnings: { Count: > 0 } warnings }) return string.Empty;
        return Loc.T("warning_prefix") + string.Join(" ", warnings);
    }

    /// <summary>Category of each board position as picked in the UI (Xbox ids share values).</summary>
    private IReadOnlyDictionary<int, MappingTargetKind> BuildTargetKinds()
    {
        var kinds = new Dictionary<int, MappingTargetKind>(_allMappings.Count);
        for (int i = 0; i < _allMappings.Count && i < KeyboardLayout.PositionCount; i++)
            kinds[i] = _allMappings[i].SelectedTarget?.Kind ?? MappingTargetKind.Keyboard;
        return kinds;
    }

    /// <summary>Pushes a configuration into the editable UI state.</summary>
    internal void ApplyToUi(KeyboardConfig config)
    {
        _scanSuspended = true;
        _hostKeyOverride = null;
        try { ApplyToUiCore(config); }
        finally
        {
            _scanSuspended = false;
            RefreshChanges();
            ReloadProfiles();
        }
    }

    private void ApplyToUiCore(KeyboardConfig config)
    {
        _loadedConfig = config;
        ProfileName = config.ProfileName;
        SpecialFeature = config.SpecialFeature;
        Voice = config.Voice;
        XboxVolumeLevel = config.XboxVolumeLevel;
        XboxFastSetting = config.XboxFastSetting;

        _allMappings.Clear();
        for (int i = 0; i < config.BoardKeys.Length && i < KeyboardLayout.PositionCount; i++)
        {
            int ordinal = SuperButtons.OrdinalOfPosition(i);
            // The ten Super Buttons have no factory table in this port (the device's defaults depend
            // on the accessory), so their reference is the value just read: the amber marker then
            // means "edited since this read", which is what the screen is for.
            byte reference = ordinal >= 0 ? config.BoardKeys[i] : _idSpace.DefaultTargets[i];
            _allMappings.Add(new MappingRowViewModel(
                i, KeyboardLayout.PositionNames[i], config.BoardKeys[i], reference,
                _idSpace, ordinal >= 0 ? KeyboardGeometry.SuperBoxOf(ordinal) : KeyboardGeometry.BoxOf(i), SelectKey,
                config.BoardKinds[i])
            {
                SuperOrdinal = ordinal >= 0 ? ordinal : null,
                WireName = ordinal >= 0 ? SuperButtons.WireNames[ordinal] : null,
                InterfaceName = ordinal >= 0 ? PortDetail(SuperButtons.InterfaceOf(ordinal)) : null,
            });
        }
        foreach (MappingRowViewModel row in _allMappings)
            row.PropertyChanged += OnMappingRowChanged;

        SelectedKey = null;
        RebuildKeyViews();

        Macros.Clear();
        for (int i = 0; i < config.Macros.Length; i++)
        {
            MacroSlot slot = config.Macros[i];
            var slotVm = new MacroSlotViewModel(i, _idSpace)
            {
                Name = slot.Name,
                Cycles = slot.Cycles,
                IntervalMs = slot.IntervalMs,
            };

            foreach (MacroAction action in slot.Actions)
            {
                if (action.Kind == MacroActionKind.Null) break;   // terminator, never edited
                slotVm.Steps.Add(new MacroStepViewModel(slotVm, action, _idSpace));
            }

            // A slot the device reported with a name or steps opens straight away; the empty ones
            // stay collapsed in one row each.
            slotVm.IsEditing = !slotVm.IsEmpty || slotVm.Steps.Count > 0;
            WatchMacro(slotVm);

            Macros.Add(slotVm);
        }

        OnPropertyChanged(nameof(IsMacrosEmpty));
    }

    /// <summary>
    /// Full configuration built from the editable state: a copy of the last loaded
    /// configuration with every field the UI owns applied on top.
    /// </summary>
    private KeyboardConfig SnapshotFromUi()
    {
        var config = _loadedConfig is null ? new KeyboardConfig() : CloneConfig(_loadedConfig);

        config.ProfileName = ProfileName;
        config.SpecialFeature = SpecialFeature;
        config.Voice = Voice;
        config.XboxVolumeLevel = XboxVolumeLevel;
        config.XboxFastSetting = XboxFastSetting;

        // Iterate the full list, not the filtered view, so hiding rows never drops edits.
        for (int i = 0; i < _allMappings.Count && i < config.BoardKeys.Length; i++)
            config.BoardKeys[i] = _allMappings[i].MappingId;

        for (int i = 0; i < Macros.Count && i < config.Macros.Length; i++)
        {
            MacroSlot slot = config.Macros[i];
            MacroSlotViewModel slotVm = Macros[i];

            slot.Name = slotVm.Name;
            slot.Cycles = (byte)Math.Clamp(slotVm.Cycles, 0, byte.MaxValue);
            slot.IntervalMs = (ushort)Math.Clamp(slotVm.IntervalMs, 0, ushort.MaxValue);

            slot.Actions.Clear();
            foreach (MacroStepViewModel step in slotVm.Steps)
                slot.Actions.Add(step.ToAction());
            if (_hostKeyOverride is { } hosts && i < hosts.Length) slot.HostKey = hosts[i];
        }

        return config;
    }

    private void OnMappingRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MappingRowViewModel.IsModified)) return;
        OnPropertyChanged(nameof(MappingSummary));
        RefreshChanges();
    }
}
