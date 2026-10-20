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
    // ---------------------------------------------------------------- unsaved changes

    /// <summary>What differs between the screen and the last configuration read from the keyboard.</summary>
    public ObservableCollection<ChangeItem> Changes { get; } = new();

    /// <summary>True when the screen holds edits the keyboard does not have yet.</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>Text next to Salvar: how many edits are waiting.</summary>
    /// <summary>Line of the save dialog: how much is about to be written.</summary>
    public string SaveDialogSummary => Loc.F("save_summary", ChangesSummary);

    public string ChangesSummary => Changes.Count == 1 ? Loc.T("change_one") : Loc.F("change_many", Changes.Count);

    /// <summary>True while the save dialog (list of changes + confirmation) is on screen.</summary>
    [ObservableProperty]
    private bool _isSaveDialogOpen;

    private bool _scanSuspended;

    /// <summary>Rebuilds <see cref="Changes"/>; cheap enough (95 keys, 8 macros) to run on every edit.</summary>
    private void RefreshChanges()
    {
        if (_scanSuspended) return;

        Changes.Clear();
        foreach (ChangeItem item in BuildChanges())
            Changes.Add(item);

        if (Changes.Count == 0) IsSaveDialogOpen = false;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangesSummary));
        OnPropertyChanged(nameof(SaveDialogSummary));
        OnPropertyChanged(nameof(CanSave));
    }

    private List<ChangeItem> BuildChanges()
    {
        var list = new List<ChangeItem>();
        if (_loadedConfig is not { } old) return list;

        if (ProfileName != old.ProfileName)
            list.Add(new ChangeItem(Loc.T("change_profile_name"), Quote(old.ProfileName), Quote(ProfileName)));

        for (int i = 0; i < _allMappings.Count && i < old.BoardKeys.Length; i++)
        {
            MappingRowViewModel row = _allMappings[i];
            if (!row.IsEditedSinceRead) continue;
            string label = row.IsSuperButton ? Loc.F("change_super", row.WireName ?? string.Empty) : Loc.F("panel_title_key", row.KeyName);
            list.Add(new ChangeItem(label, row.LoadedTarget.Label, row.MappingName));
        }

        if (IsJpFamily && SpecialFeature != old.SpecialFeature)
            list.Add(new ChangeItem("Special feature", old.SpecialFeature.ToString(), SpecialFeature.ToString()));

        if (IsXboxFamily)
        {
            if (XboxVolumeLevel != old.XboxVolumeLevel)
                list.Add(new ChangeItem("Volume", old.XboxVolumeLevel.ToString(), XboxVolumeLevel.ToString()));
            AddQuickSetup(list, old, XboxWire.FastSetting.DisableWindowsKey, Loc.T("qs_win"));
            AddQuickSetup(list, old, XboxWire.FastSetting.DisableAltTab, Loc.T("qs_alttab"));
            AddQuickSetup(list, old, XboxWire.FastSetting.DisableAltF4, Loc.T("qs_altf4"));
        }

        for (int i = 0; i < Macros.Count && i < old.Macros.Length; i++)
        {
            MacroSlot was = old.Macros[i];
            MacroSlotViewModel now = Macros[i];
            string slot = Loc.F("macro_n", i + 1);

            if (now.Name != was.Name)
                list.Add(new ChangeItem(Loc.F("change_macro_name", slot), MacroNameText(was.Name), MacroNameText(now.Name)));
            if (now.Cycles != was.Cycles)
                list.Add(new ChangeItem(Loc.F("change_macro_cycles", slot), was.Cycles.ToString(), now.Cycles.ToString()));
            if (now.IntervalMs != was.IntervalMs)
                list.Add(new ChangeItem(Loc.F("change_macro_interval", slot), $"{was.IntervalMs} ms", $"{now.IntervalMs} ms"));

            var oldSteps = was.Actions.Where(a => a.Kind != MacroActionKind.Null).ToList();
            var newSteps = now.Steps.Select(s => s.ToAction()).ToList();
            if (!oldSteps.SequenceEqual(newSteps, MacroStepComparer.Instance))
            {
                list.Add(new ChangeItem(Loc.F("change_macro_steps", slot), StepCount(oldSteps.Count),
                    StepCount(newSteps.Count) + (oldSteps.Count == newSteps.Count ? Loc.T("steps_edited") : "")));
            }
        }

        return list;
    }

    private void AddQuickSetup(List<ChangeItem> list, KeyboardConfig old, byte mask, string label)
    {
        bool was = (old.XboxFastSetting & mask) != 0;
        bool now = (XboxFastSetting & mask) != 0;
        if (was != now) list.Add(new ChangeItem(label, was ? Loc.T("yes") : Loc.T("no"), now ? Loc.T("yes") : Loc.T("no")));
    }

    private static string Quote(string name) => name.Length == 0 ? Loc.T("empty") : $"“{name}”";

    private static string MacroNameText(string name) => name.Length == 0 ? Loc.T("empty") : name;

    private static string StepCount(int n) => n == 1 ? Loc.T("step_one") : Loc.F("step_many", n);

    /// <summary>Watches one macro slot (and its steps, now and later) so edits reach <see cref="Changes"/>.</summary>
    private void WatchMacro(MacroSlotViewModel slot)
    {
        slot.PropertyChanged += (_, _) => RefreshChanges();
        foreach (MacroStepViewModel step in slot.Steps)
            step.PropertyChanged += (_, _) => RefreshChanges();
        slot.Steps.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (MacroStepViewModel step in e.NewItems)
                    step.PropertyChanged += (_, _) => RefreshChanges();
            RefreshChanges();
        };
    }

    /// <summary>Opens the list of changes; the confirmation inside it is the permission to write.</summary>
    [RelayCommand]
    private void OpenSaveDialog()
    {
        if (!CanSave) return;
        SaveDialogNote = string.Empty;
        IsSaveDialogOpen = true;
    }

    [RelayCommand]
    private void CancelSaveDialog()
    {
        IsSaveDialogOpen = false;
        SaveDialogNote = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmSaveAsync()
    {
        IsSaveDialogOpen = false;
        await WriteConfigAsync();
    }

    private sealed class MacroStepComparer : IEqualityComparer<MacroAction>
    {
        public static MacroStepComparer Instance { get; } = new();

        public bool Equals(MacroAction? a, MacroAction? b) =>
            a is not null && b is not null && a.Kind == b.Kind &&
            (a.Kind == MacroActionKind.Time ? a.DelayMs == b.DelayMs : a.Usage == b.Usage);

        public int GetHashCode(MacroAction a) => HashCode.Combine(a.Kind, a.Usage, a.DelayMs);
    }
}
