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
using Retro87.Localization;
using Retro87.Models;
using Retro87.Protocol;
using Retro87.Services;

namespace Retro87.ViewModels;

/// <summary>One selectable macro step kind, with the Portuguese label shown in the picker.</summary>
public sealed record MacroKindOption(MacroActionKind Kind, string Label)
{
    /// <summary>What a screen reader announces for the picker row.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// One editable macro step. It keeps the raw <c>Op</c> read from the device, so an untouched
/// step is written back byte for byte; editing the kind or the key derives the common
/// <c>0x81/0x83</c> (down) and <c>0x01/0x03</c> (up) pair again.
/// </summary>
/// <remarks>
/// Whether a key uses the modifier form (<c>0x83</c>/<c>0x03</c>) is inferred from the eight
/// modifier ids (<see cref="MappingIds.LCTRL_MAP"/>..<see cref="MappingIds.RWIN_MAP"/>, 100..107).
/// This is an area the port still has to confirm against real hardware; a step read from the
/// device is never rewritten just because the inference disagrees.
/// </remarks>
public partial class MacroStepViewModel : ObservableObject
{
    /// <summary>Kinds the editor offers (Null, the record terminator, is never edited).</summary>
    public static IReadOnlyList<MacroKindOption> KindOptions => new[]
    {
        new MacroKindOption(MacroActionKind.KeyDown, Loc.T("macro_press")),
        new MacroKindOption(MacroActionKind.KeyUp, Loc.T("macro_release")),
        new MacroKindOption(MacroActionKind.Time, Loc.T("macro_wait")),
    };

    private readonly MacroSlotViewModel _owner;
    private byte _op;

    public MacroStepViewModel(MacroSlotViewModel owner, MacroAction action, MappingIdSpace? idSpace = null)
    {
        _owner = owner;
        _idSpace = idSpace ?? MappingIdSpace.Jp;
        _selectedKind = KindOptions.FirstOrDefault(o => o.Kind == action.Kind) ?? KindOptions[0];
        _usage = action.Usage;
        _delayMs = action.DelayMs;
        _op = action.Op;
        _selectedTarget = _idSpace.Find(action.Usage);
    }

    private readonly MappingIdSpace _idSpace;

    /// <summary>Targets of the connected family, used to pick the key of a KeyDown/KeyUp step.</summary>
    public IReadOnlyList<MappingTarget> Targets => _idSpace.Targets;

    [ObservableProperty]
    private MacroKindOption _selectedKind;

    [ObservableProperty]
    private byte _usage;

    [ObservableProperty]
    private int _delayMs;

    [ObservableProperty]
    private MappingTarget? _selectedTarget;

    public MacroActionKind Kind => SelectedKind.Kind;

    public bool IsKeyStep => Kind is MacroActionKind.KeyDown or MacroActionKind.KeyUp;

    public bool IsDelayStep => Kind == MacroActionKind.Time;

    partial void OnSelectedKindChanged(MacroKindOption value)
    {
        RecomputeOp();
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsKeyStep));
        OnPropertyChanged(nameof(IsDelayStep));
    }

    partial void OnSelectedTargetChanged(MappingTarget? value)
    {
        if (value is not null) Usage = value.Id;
    }

    partial void OnUsageChanged(byte value)
    {
        if (SelectedTarget?.Id != value) SelectedTarget = _idSpace.Find(value);
        RecomputeOp();
    }

    [RelayCommand] private void MoveUp() => _owner.MoveStep(this, -1);
    [RelayCommand] private void MoveDown() => _owner.MoveStep(this, +1);
    [RelayCommand] private void Remove() => _owner.RemoveStep(this);

    /// <summary>Snapshot of the step for writing; the delay is clamped to the wire range.</summary>
    public MacroAction ToAction() => new()
    {
        Kind = Kind,
        Usage = Usage,
        DelayMs = (ushort)Math.Clamp(DelayMs, 0, ushort.MaxValue),
        Op = _op,
    };

    private void RecomputeOp() => _op = Kind switch
    {
        MacroActionKind.KeyDown => IsModifierUsage(Usage) ? (byte)0x83 : (byte)0x81,
        MacroActionKind.KeyUp => IsModifierUsage(Usage) ? (byte)0x03 : (byte)0x01,
        _ => 0,
    };

    private static bool IsModifierUsage(byte usage)
        => usage >= MappingIds.LCTRL_MAP && usage <= MappingIds.RWIN_MAP;   // 100..107
}
