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
    // ---------------------------------------------------------------- local profiles

    /// <summary>Profiles saved on this computer for the connected keyboard's family (newest first).</summary>
    public ObservableCollection<SavedProfileItem> SavedProfiles { get; } = new();

    /// <summary>Name of the profile the keyboard holds right now (empty when it has none).</summary>
    public string CurrentProfileName => _loadedConfig?.ProfileName ?? string.Empty;

    public bool HasDeviceProfile => CurrentProfileName.Length > 0;

    public bool HasNoDeviceProfile => !HasDeviceProfile;

    /// <summary>One line about what the keyboard's profile holds ("12 keys changed · 2 macros").</summary>
    public string CurrentProfileSummary => _loadedConfig is null ? string.Empty : Summarize(_loadedConfig);

    /// <summary>True when the saved file with the keyboard profile's name holds something else.</summary>
    [ObservableProperty]
    private bool _currentDiffers;

    /// <summary>True when the keyboard has a profile that is not saved on this computer.</summary>
    [ObservableProperty]
    private bool _currentHasNoCopy;

    /// <summary>True when no profile was saved on this computer yet.</summary>
    public bool IsProfileListEmpty => SavedProfiles.Count == 0;

    public string SavedProfilesCount => SavedProfiles.Count.ToString();

    /// <summary>Text under the save dialog title when it was opened by activating a profile.</summary>
    [ObservableProperty]
    private string _saveDialogNote = string.Empty;

    /// <summary>
    /// Host keys of the macros of the profile being activated. The keyboard's own record has the
    /// ones it holds now; a profile brings its own, and the screen has no field for them.
    /// </summary>
    private byte[]? _hostKeyOverride;

    private static bool SameFamily(ushort a, ushort b)
        => a == b || (KnownDevices.IsXbox(a) && KnownDevices.IsXbox(b));

    /// <summary>"12 keys changed · 2 macros": what a profile changes from the factory state.</summary>
    private string Summarize(KeyboardConfig config)
    {
        int keys = 0;
        for (int i = 0; i < config.BoardKeys.Length && i < _idSpace.DefaultTargets.Count; i++)
            if (SuperButtons.OrdinalOfPosition(i) < 0 && config.BoardKeys[i] != _idSpace.DefaultTargets[i]) keys++;

        int macros = config.Macros.Count(m => m.Name.Length > 0 || m.Actions.Any(a => a.Kind != MacroActionKind.Null));
        return $"{(keys == 1 ? Loc.T("sum_key_one") : Loc.F("sum_key_many", keys))} · " +
               $"{(macros == 1 ? Loc.T("sum_macro_one") : Loc.F("sum_macro_many", macros))}";
    }

    /// <summary>What makes two configurations "the same profile" (the keys' categories are left out on purpose: old files lack them).</summary>
    private static string Signature(KeyboardConfig config)
    {
        var text = new System.Text.StringBuilder();
        text.Append(Convert.ToHexString(config.BoardKeys)).Append('|')
            .Append(config.SpecialFeature).Append(',').Append(config.Voice).Append(',')
            .Append(config.XboxVolumeLevel).Append(',').Append(config.XboxFastSetting);
        foreach (MacroSlot slot in config.Macros)
        {
            text.Append('|').Append(slot.Name).Append(',').Append(slot.Cycles).Append(',').Append(slot.IntervalMs);
            foreach (MacroAction action in slot.Actions.Where(a => a.Kind != MacroActionKind.Null))
                text.Append(';').Append((int)action.Kind).Append(':').Append(action.Usage).Append(':').Append(action.DelayMs);
        }
        return text.ToString();
    }

    private void ReloadProfiles()
    {
        SavedProfiles.Clear();
        string current = CurrentProfileName;
        string? currentSignature = _loadedConfig is null ? null : Signature(_loadedConfig);
        bool currentFileFound = false, differs = false;

        foreach (ProfileInfo info in _profiles.List())
        {
            if (SelectedDevice is { } device && !SameFamily(info.Pid, device.Pid)) continue;

            KeyboardConfig? config = _profiles.Load(info.FileName);
            bool sameName = current.Length > 0 && string.Equals(info.ProfileName, current, StringComparison.OrdinalIgnoreCase);
            if (sameName && config is not null && currentSignature is not null)
            {
                currentFileFound = true;
                differs |= Signature(config) != currentSignature;
            }

            SavedProfiles.Add(new SavedProfileItem(info.FileName, info.ProfileName,
                config is null ? Loc.T("unreadable_file") : Summarize(config),
                info.SavedAtUtc.ToLocalTime().ToString("g", Loc.Instance.Culture), sameName,
                ActivateProfile, RenameProfile, CopyProfile, DeleteProfile, ShowProfilesFolder));
        }

        CurrentDiffers = differs;
        CurrentHasNoCopy = current.Length > 0 && !currentFileFound;
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(HasDeviceProfile));
        OnPropertyChanged(nameof(HasNoDeviceProfile));
        OnPropertyChanged(nameof(CurrentProfileSummary));
        OnPropertyChanged(nameof(IsProfileListEmpty));
        OnPropertyChanged(nameof(SavedProfilesCount));
    }

    /// <summary>Goes to the Mapeamento tab, where the profile on the keyboard is edited.</summary>
    [RelayCommand]
    private void EditCurrentProfile() => SelectedTabIndex = 0;

    /// <summary>Saves what the keyboard holds now as a file on this computer. Never touches the device.</summary>
    [RelayCommand]
    private void SaveCurrentCopy()
    {
        if (SelectedDevice is null || _loadedConfig is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }
        if (!HasDeviceProfile) { SetStatus(StatusSeverity.Warning, Loc.T("st_no_profile")); return; }

        if (!_profiles.Save(SelectedDevice, CloneConfig(_loadedConfig)))
        {
            SetStatus(StatusSeverity.Error, Loc.F("st_profile_save_failed", _profiles.LastError ?? string.Empty));
            return;
        }

        ReloadProfiles();
        SetStatus(StatusSeverity.Success, Loc.F("st_profile_saved", CurrentProfileName));
    }

    /// <summary>
    /// Brings a saved profile to the screen over what the keyboard holds, then opens the save dialog:
    /// the dialog lists exactly what changes and its confirmation is the permission to write.
    /// </summary>
    private void ActivateProfile(SavedProfileItem? item)
    {
        if (item is null) return;
        if (_protocol is null || _loadedConfig is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }
        if (!CanWriteDevice) { SetStatus(StatusSeverity.Warning, Loc.T("st_write_unavailable")); return; }

        KeyboardConfig? profile = _profiles.Load(item.FileName);
        if (profile is null)
        {
            SetStatus(StatusSeverity.Error, Loc.F("st_profile_read_failed", _profiles.LastError ?? string.Empty));
            return;
        }

        string note = Loc.F("note_activating", item.Name);
        if (HasChanges) note += Loc.T("note_discarded");

        // Keep what the keyboard holds when it is not saved anywhere. Not when the file being
        // activated is that very profile: saving first would overwrite it before it is applied.
        if (HasDeviceProfile && (CurrentHasNoCopy || CurrentDiffers) && !item.SameNameOnKeyboard
            && SelectedDevice is { } device && _profiles.Save(device, CloneConfig(_loadedConfig)))
            note += Loc.F("note_kept_copy", CurrentProfileName);

        ApplyProfileOverDevice(profile);
        ReloadProfiles();
        OfferSaveOfProfile(note);
    }

    /// <summary>Puts every key, macro and setting back to the factory state, then opens the save dialog.</summary>
    [RelayCommand]
    private void ClearKeyboard()
    {
        if (_protocol is null || _loadedConfig is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }
        if (!CanWriteDevice) { SetStatus(StatusSeverity.Warning, Loc.T("st_write_unavailable")); return; }

        string note = Loc.T("note_clearing");
        if (HasChanges) note += Loc.T("note_discarded");
        ApplyProfileOverDevice(BlankConfig());
        OfferSaveOfProfile(note);
    }

    private void OfferSaveOfProfile(string note)
    {
        if (Changes.Count == 0)
        {
            SetStatus(StatusSeverity.Info, Loc.T("st_already_clear"));
            return;
        }

        SaveDialogNote = note;
        IsSaveDialogOpen = true;
    }

    /// <summary>Factory state for the keys and macros; the ten Super Buttons and the quick settings stay as the keyboard has them.</summary>
    private KeyboardConfig BlankConfig()
    {
        KeyboardConfig blank = CloneConfig(_loadedConfig!);
        blank.ProfileName = string.Empty;
        var fresh = new KeyboardConfig();
        for (int i = 0; i < blank.BoardKeys.Length && i < _idSpace.DefaultTargets.Count; i++)
        {
            if (SuperButtons.OrdinalOfPosition(i) >= 0) continue;
            blank.BoardKeys[i] = _idSpace.DefaultTargets[i];
            blank.BoardKinds[i] = null;
        }

        for (int i = 0; i < blank.Macros.Length; i++)
        {
            MacroSlot slot = blank.Macros[i];
            slot.HostKey = 0;
            slot.Name = string.Empty;
            slot.Cycles = fresh.Macros[i].Cycles;
            slot.IntervalMs = fresh.Macros[i].IntervalMs;
            slot.Actions.Clear();
        }

        return blank;
    }

    /// <summary>
    /// Rebuilds the screen from what the keyboard holds and then applies <paramref name="profile"/>
    /// on top as ordinary edits, so the list of changes is the difference between the two.
    /// </summary>
    private void ApplyProfileOverDevice(KeyboardConfig profile)
    {
        ApplyToUi(_loadedConfig!);

        _scanSuspended = true;
        try
        {
            ProfileName = profile.ProfileName;
            SpecialFeature = profile.SpecialFeature;
            Voice = profile.Voice;
            if (profile.XboxVolumeLevel != 0) XboxVolumeLevel = profile.XboxVolumeLevel;
            XboxFastSetting = profile.XboxFastSetting;

            for (int i = 0; i < _allMappings.Count && i < profile.BoardKeys.Length; i++)
                _allMappings[i].SelectedTarget = _idSpace.Find(profile.BoardKeys[i], profile.BoardKinds[i]);

            for (int i = 0; i < Macros.Count && i < profile.Macros.Length; i++)
            {
                MacroSlot from = profile.Macros[i];
                MacroSlotViewModel slot = Macros[i];
                slot.Name = from.Name;
                slot.Cycles = from.Cycles;
                slot.IntervalMs = from.IntervalMs;
                slot.Steps.Clear();
                foreach (MacroAction action in from.Actions)
                {
                    if (action.Kind == MacroActionKind.Null) break;
                    slot.Steps.Add(new MacroStepViewModel(slot, action, _idSpace));
                }

                slot.IsEditing = !slot.IsEmpty || slot.Steps.Count > 0;
            }

            _hostKeyOverride = profile.Macros.Select(m => m.HostKey).ToArray();
        }
        finally
        {
            _scanSuspended = false;
            RefreshChanges();
        }
    }

    // ---- name dialog (new / copy / rename / delete share one dialog)

    private enum NameDialogMode { None, New, Copy, RenameFile, RenameCurrent, Delete }

    private NameDialogMode _nameMode;
    private SavedProfileItem? _nameTarget;

    [ObservableProperty]
    private bool _isNameDialogOpen;

    [ObservableProperty]
    private string _nameDialogTitle = string.Empty;

    [ObservableProperty]
    private string _nameDialogText = string.Empty;

    [ObservableProperty]
    private string _nameDialogConfirm = "OK";

    [ObservableProperty]
    private string _nameDialogInput = string.Empty;

    [ObservableProperty]
    private string _nameDialogError = string.Empty;

    /// <summary>The "start from what is on the keyboard / blank" choice, shown only for a new profile.</summary>
    [ObservableProperty]
    private bool _nameDialogShowSource;

    [ObservableProperty]
    private bool _newFromKeyboard = true;

    [ObservableProperty]
    private bool _nameDialogHasInput = true;

    /// <summary>True for the delete confirmation, whose button is drawn as destructive.</summary>
    [ObservableProperty]
    private bool _nameDialogDestructive;

    /// <summary>The keyboard stores the name in 32 bytes of UTF-16 (16 characters); the JP family has room for 30.</summary>
    public int ProfileNameMaxLength => IsXboxFamily ? 16 : 30;

    public bool IsAnyDialogOpen => IsSaveDialogOpen || IsNameDialogOpen;

    partial void OnIsSaveDialogOpenChanged(bool value) => OnPropertyChanged(nameof(IsAnyDialogOpen));

    partial void OnIsNameDialogOpenChanged(bool value) => OnPropertyChanged(nameof(IsAnyDialogOpen));

    partial void OnNewFromKeyboardChanged(bool value) => OnPropertyChanged(nameof(NewFromBlank));

    public bool NewFromBlank
    {
        get => !NewFromKeyboard;
        set => NewFromKeyboard = !value;
    }

    private void OpenNameDialog(NameDialogMode mode, string title, string text, string confirm,
        string input = "", SavedProfileItem? target = null, bool hasInput = true, bool destructive = false)
    {
        _nameMode = mode;
        _nameTarget = target;
        NameDialogTitle = title;
        NameDialogText = text;
        NameDialogConfirm = confirm;
        NameDialogInput = input;
        NameDialogError = string.Empty;
        NameDialogHasInput = hasInput;
        NameDialogDestructive = destructive;
        NameDialogShowSource = mode == NameDialogMode.New;
        NewFromKeyboard = true;
        IsNameDialogOpen = true;
    }

    [RelayCommand]
    private void NewProfile()
    {
        if (_loadedConfig is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return; }
        OpenNameDialog(NameDialogMode.New, Loc.T("dlg_new_title"),
            Loc.T("dlg_new_text"), Loc.T("dlg_create"));
    }

    private void CopyProfile(SavedProfileItem? item)
    {
        if (item is null) return;
        OpenNameDialog(NameDialogMode.Copy, Loc.F("dlg_copy_title", item.Name),
            Loc.T("dlg_copy_text"), Loc.T("dlg_copy"), item.Name + " 2", item);
    }

    private void RenameProfile(SavedProfileItem? item)
    {
        if (item is null) return;
        OpenNameDialog(NameDialogMode.RenameFile, Loc.F("dlg_rename_title", item.Name),
            Loc.T("dlg_rename_file_text"), Loc.T("dlg_rename"), item.Name, item);
    }

    [RelayCommand]
    private void RenameCurrentProfile()
    {
        if (_loadedConfig is null) return;
        OpenNameDialog(NameDialogMode.RenameCurrent, Loc.F("dlg_rename_title", CurrentProfileName),
            Loc.T("dlg_rename_current_text"), Loc.T("dlg_rename"), CurrentProfileName);
    }

    private void DeleteProfile(SavedProfileItem? item)
    {
        if (item is null) return;
        OpenNameDialog(NameDialogMode.Delete, Loc.F("dlg_delete_title", item.Name),
            Loc.T("dlg_delete_text"), Loc.T("dlg_delete"),
            target: item, hasInput: false, destructive: true);
    }

    [RelayCommand]
    private void CancelNameDialog() => IsNameDialogOpen = false;

    [RelayCommand]
    private void ConfirmNameDialog()
    {
        string name = NameDialogInput.Trim();
        if (NameDialogHasInput)
        {
            if (name.Length == 0) { NameDialogError = Loc.T("err_name_empty"); return; }
            if (name.Length > ProfileNameMaxLength)
            {
                NameDialogError = Loc.F("err_name_long", ProfileNameMaxLength);
                return;
            }

            bool taken = _profiles.Exists(name) && !(_nameMode == NameDialogMode.RenameFile
                && ProfileStore.FileNameFor(name).Equals(_nameTarget?.FileName, StringComparison.OrdinalIgnoreCase));
            if (_nameMode != NameDialogMode.RenameCurrent && taken)
            {
                NameDialogError = Loc.T("err_name_taken");
                return;
            }
        }

        switch (_nameMode)
        {
            case NameDialogMode.New:
                if (SelectedDevice is null || _loadedConfig is null) return;
                KeyboardConfig config = NewFromKeyboard ? CloneConfig(_loadedConfig) : BlankConfig();
                config.ProfileName = name;
                if (!_profiles.Save(SelectedDevice, config))
                {
                    NameDialogError = Loc.F("err_create_failed", _profiles.LastError ?? string.Empty);
                    return;
                }

                SetStatus(StatusSeverity.Success, Loc.F("st_profile_created", name));
                break;

            case NameDialogMode.Copy:
            case NameDialogMode.RenameFile:
                bool keep = _nameMode == NameDialogMode.Copy;
                if (_nameTarget is null || _profiles.Duplicate(_nameTarget.FileName, name, keep) is null)
                {
                    NameDialogError = Loc.F("err_failed", _profiles.LastError ?? string.Empty);
                    return;
                }

                SetStatus(StatusSeverity.Success, keep ? Loc.F("st_profile_copied", name) : Loc.F("st_profile_renamed", name));
                break;

            case NameDialogMode.RenameCurrent:
                ProfileName = name;
                SetStatus(StatusSeverity.Info, Loc.F("st_name_changed", name));
                break;

            case NameDialogMode.Delete:
                if (_nameTarget is null || !_profiles.Delete(_nameTarget.FileName))
                {
                    NameDialogError = Loc.F("err_delete_failed", _profiles.LastError ?? string.Empty);
                    return;
                }

                SetStatus(StatusSeverity.Info, Loc.F("st_profile_deleted", _nameTarget.Name));
                break;
        }

        IsNameDialogOpen = false;
        ReloadProfiles();
    }

    /// <summary>Opens the folder holding the saved profiles in the system file manager.</summary>
    private void ShowProfilesFolder()
    {
        try
        {
            Directory.CreateDirectory(_profiles.RootDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _profiles.RootDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SetStatus(StatusSeverity.Warning, Loc.F("st_folder_failed", ex.Message, _profiles.RootDirectory));
        }
    }
}
