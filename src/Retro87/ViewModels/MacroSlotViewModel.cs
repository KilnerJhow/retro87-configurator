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

/// <summary>One of the eight macro slots, with its editable step list.</summary>
/// <remarks>
/// A slot the keyboard reported as empty stays <b>collapsed</b> in a single row ("Slot N — empty")
/// with one button to open the editor: eight full forms for macros that do not exist were the
/// biggest block of noise in the app. A slot with a name or steps opens by itself, and a slot that
/// is still empty can be collapsed again.
/// </remarks>
public partial class MacroSlotViewModel : ObservableObject
{
    private readonly MappingIdSpace _idSpace;

    public MacroSlotViewModel(int index, MappingIdSpace? idSpace = null)
    {
        Index = index;
        _idSpace = idSpace ?? MappingIdSpace.Jp;
        Steps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StepCount));
            OnPropertyChanged(nameof(StepsText));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanCollapse));
        };
    }

    public int Index { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>True while this slot shows the full editor instead of the collapsed row.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Number of repetitions; the wire byte is clamped to 0..255 on write (0xFF = infinite).</summary>
    [ObservableProperty]
    private int _cycles = 1;

    /// <summary>Slot interval in milliseconds; the wire value is clamped to 0..65535 on write.</summary>
    [ObservableProperty]
    private int _intervalMs;

    /// <summary>Editable steps, in write order.</summary>
    public ObservableCollection<MacroStepViewModel> Steps { get; } = new();

    public int StepCount => Steps.Count;

    public string SlotLabel => Loc.F("slot_n", Index);

    public string CreateName => Loc.F("slot_create", Index);

    public string CollapseName => Loc.F("slot_collapse", Index);

    public string StepsText => Loc.F("steps_n", Steps.Count);

    public bool IsEmpty => string.IsNullOrEmpty(Name);

    /// <summary>True when the collapsed row is the one shown.</summary>
    public bool IsCollapsed => !IsEditing;

    /// <summary>True while the slot has neither a name nor steps, so collapsing it again loses nothing.</summary>
    public bool CanCollapse => IsEmpty && Steps.Count == 0;

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanCollapse));
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsCollapsed));

    /// <summary>Opens the editor of an empty slot ("Criar macro").</summary>
    [RelayCommand] private void Edit() => IsEditing = true;

    /// <summary>Puts the editor away again; only offered while the slot is still empty.</summary>
    [RelayCommand] private void Collapse() => IsEditing = false;

    [RelayCommand] private void AddKeyDown() => AddStep(MacroActionKind.KeyDown);
    [RelayCommand] private void AddKeyUp() => AddStep(MacroActionKind.KeyUp);
    [RelayCommand] private void AddDelay() => AddStep(MacroActionKind.Time);

    private void AddStep(MacroActionKind kind)
    {
        var action = new MacroAction
        {
            Kind = kind,
            Usage = kind == MacroActionKind.Time ? (byte)0 : MappingIds.A_MAP,
            DelayMs = kind == MacroActionKind.Time ? (ushort)50 : (ushort)0,
        };
        Steps.Add(new MacroStepViewModel(this, action, _idSpace));
    }

    /// <summary>Moves a step by <paramref name="delta"/> positions; no-op when out of range.</summary>
    public void MoveStep(MacroStepViewModel step, int delta)
    {
        int from = Steps.IndexOf(step);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= Steps.Count) return;
        Steps.Move(from, to);
    }

    public void RemoveStep(MacroStepViewModel step) => Steps.Remove(step);
}
