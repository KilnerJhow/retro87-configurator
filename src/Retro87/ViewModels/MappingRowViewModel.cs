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

/// <summary>
/// One row of the mapping tab: a board key and the target it is remapped to.
/// </summary>
public partial class MappingRowViewModel : ObservableObject
{
    private readonly Action<MappingRowViewModel>? _onSelect;

    public MappingRowViewModel(int position, string keyName, byte mappingId, byte defaultMappingId,
        MappingIdSpace? idSpace = null, KeyBox box = default, Action<MappingRowViewModel>? onSelect = null,
        MappingTargetKind? readKind = null)
    {
        Position = position;
        KeyName = keyName;
        DefaultMappingId = defaultMappingId;
        _idSpace = idSpace ?? MappingIdSpace.Jp;
        _mappingId = mappingId;
        _selectedTarget = _idSpace.Find(mappingId, readKind);
        LoadedTarget = _selectedTarget;
        // The field is set directly (no change handler runs), so the "type" dropdown must be filled
        // here too — otherwise the side panel shows an empty Alvo until the user picks something.
        _selectedCategory = Categories.FirstOrDefault(c => c.Category == _selectedTarget.Category);
        Box = box;
        _onSelect = onSelect;
    }

    private readonly MappingIdSpace _idSpace;

    /// <summary>Where the key sits on the drawn keyboard, in key units.</summary>
    public KeyBox Box { get; }

    /// <summary>True while this key is the one shown in the side panel.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Ordinal in <see cref="SuperButtons"/> when this row is a Super Button, or null.</summary>
    public int? SuperOrdinal { get; init; }

    /// <summary>Wire name of a Super Button ("KA"…"K8"), null for a board key.</summary>
    public string? WireName { get; init; }

    /// <summary>Interface ("A"…"Y") of a Super Button, null for a board key or a built-in button.</summary>
    public string? InterfaceName { get; init; }

    /// <summary>True for the ten Super Buttons, which are drawn outside the keyboard.</summary>
    public bool IsSuperButton => SuperOrdinal is not null;

    /// <summary>
    /// Second line of the side panel: where this row lives in the device. The connection is called
    /// "connection" here — the word the screen uses — while the model and the protocol docs still call it
    /// "interface" (A, B, X, Y; see <see cref="SuperButtons.InterfaceNames"/>).
    /// </summary>
    public string Detail => (IsSuperButton
        ? Loc.F("detail_super", InterfaceName ?? Loc.T("on_keyboard"),
            SuperButtons.WireIndexOf(SuperOrdinal!.Value), WireName ?? string.Empty)
        : Loc.F("detail_board", Position)) + ReferenceNote;

    /// <summary>
    /// " · default: L" once the key sends something else, so the side panel can say which key it is
    /// (the title) apart from what it sends now (the drop-downs). A Super Button has no factory
    /// table, so its reference is what the keyboard held when it was read.
    /// </summary>
    private string ReferenceNote => IsSuperButton
        ? IsEditedSinceRead ? Loc.F("ref_read", LoadedTarget.Label) : string.Empty
        : IsModified ? Loc.F("ref_default", DefaultName) : string.Empty;

    /// <summary>Title of the side panel: the physical key ("Key L"), never the target it sends.</summary>
    public string PanelTitle => IsSuperButton ? KeyName : Loc.F("panel_title_key", KeyName);

    /// <summary>Text printed on the keycap: the wire name ("KA", "KB") for the keyboard's own Super Buttons.</summary>
    public string KeyCapLabel => WireName ?? KeyName;

    /// <summary>Hover text of the key: what it does today.</summary>
    public string MappingTooltip => $"{KeyName}: {MappingName}";

    /// <summary>Name a screen reader announces for the key.</summary>
    public string AutomationName => $"{KeyName}, {MappingName}";

    /// <summary>
    /// Keycap fill: Muted Amber while selected — the same amber the armed lock uses, so the two
    /// "you are here" states of the app read alike.
    /// </summary>
    public IBrush KeyBackground => Palette.For(IsPressed ? "OkSoft" : IsSelected ? "AccentSoft" : "Surface2");

    /// <summary>Keycap border: amber for an edited or selected key, the quiet line otherwise.</summary>
    public IBrush KeyBorder => Palette.For(IsPressed ? "Ok" : IsModified || IsSelected ? "Accent" : "Line");

    /// <summary>Asks the window to show this key in the side panel.</summary>
    [RelayCommand]
    private void Select() => _onSelect?.Invoke(this);

    /// <summary>0-based position in the board key array (0..94).</summary>
    public int Position { get; }

    /// <summary>Label of the physical key at this position.</summary>
    public string KeyName { get; }

    /// <summary>Target the key maps to out of the box (see <see cref="KeyboardLayout.DefaultMappingId"/>).</summary>
    public byte DefaultMappingId { get; }

    /// <summary>Every selectable target of the connected family, ordered by category.</summary>
    public IReadOnlyList<MappingTarget> Targets => _idSpace.Targets;

    [ObservableProperty]
    private byte _mappingId;

    private MappingTarget? _selectedTarget;

    /// <summary>
    /// The chosen target. A null write is refused and the current value is announced again: the
    /// side panel's drop-downs write "nothing" back whenever they rebuild their list (when the type
    /// changes or the panel moves to another key), and taking that literally erased the target and
    /// then fell back to the first target of the type — editing keys the user never touched.
    /// </summary>
    public MappingTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref _selectedTarget, value))
                OnSelectedTargetChanged(value);
        }
    }

    /// <summary>The "type" dropdown of the official software: only the types this family offers.</summary>
    public IReadOnlyList<CategoryOption> Categories => _idSpace.Categories;

    private CategoryOption? _selectedCategory;

    /// <summary>The "type" dropdown's choice; a null write is refused for the reason given at <see cref="SelectedTarget"/>.</summary>
    public CategoryOption? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref _selectedCategory, value))
                OnSelectedCategoryChanged(value);
        }
    }

    /// <summary>The "subtype" dropdown: the targets of the chosen type, in the official order.</summary>
    /// <remarks>The list is shared by every key of the family (see <see cref="MappingIdSpace.Categories"/>).</remarks>
    public IReadOnlyList<MappingTarget> SubTargets
        => SelectedCategory is { } chosen ? _idSpace.SubTargetsOf(chosen.Category) : Array.Empty<MappingTarget>();

    /// <summary>"Disable" has no subtype, so the second dropdown disappears.</summary>
    public bool HasSubtype => SelectedCategory is { Category: not TargetCategory.Disable };

    private void OnSelectedCategoryChanged(CategoryOption? value)
    {
        OnPropertyChanged(nameof(SubTargets));
        OnPropertyChanged(nameof(HasSubtype));
        if (value is null || SelectedTarget?.Category == value.Category) return;

        // Picking another type lands on its first subtype, like the official software.
        SelectedTarget = value.Category == TargetCategory.Disable
            ? Targets.First(t => t.Category == TargetCategory.Disable)
            : Targets.Where(t => t.Category == value.Category).OrderBy(t => t.Order).First();
    }

    /// <summary>The target the device had when the screen was filled (same id, the category it reported).</summary>
    public MappingTarget LoadedTarget { get; }

    /// <summary>
    /// True when the target differs from what was read, counting a change of category on a shared id
    /// (Left Alt to NoVoice) — which <see cref="IsModified"/>, a comparison of ids, cannot see.
    /// </summary>
    public bool IsEditedSinceRead => SelectedTarget is { } t && !MappingIdSpace.SameTarget(t, LoadedTarget);

    /// <summary>Name of the chosen target; the id alone is ambiguous in the Xbox family.</summary>
    public string MappingName => SelectedTarget?.Label ?? TargetLabels.Of(_idSpace.Describe(MappingId));

    /// <summary>Name of the default target, used as the tooltip of the reset button.</summary>
    public string DefaultName => TargetLabels.Of(_idSpace.Describe(DefaultMappingId));

    /// <summary>
    /// Label of the side panel's reset button. For the ten Super Buttons the reference is not a
    /// factory table — the device's own default depends on what is plugged in — so the button
    /// promises only what it can keep: the value the keyboard had when it was last read.
    /// </summary>
    public string RestoreLabel => IsSuperButton ? Loc.T("restore_read") : Loc.T("restore_default");

    /// <summary>Tooltip of the reset button, naming the reference it really uses.</summary>
    public string RestoreTooltip => IsSuperButton
        ? Loc.F("restore_tip_super", DefaultName)
        : Loc.F("restore_tip_key", DefaultName);

    /// <summary>
    /// Second line of a Super Button in the cut-out: mist over the bench graphite, and silver over the
    /// amber of a selected button — mist over muted amber is 4.2:1 and fails AA at 10 px.
    /// </summary>
    public IBrush MappingBrush => Palette.For(IsSelected ? "FgDim" : "FgMuted");

    /// <summary>True when the key no longer maps to its default target.</summary>
    public bool IsModified => MappingId != DefaultMappingId;

    /// <summary>True when the button has a target; the round button shows "+" otherwise.</summary>
    public bool HasTarget => MappingId != 0;

    public bool HasNoTarget => MappingId == 0;

    /// <summary>Round button fill: green while held, amber when it has a target, flat when empty.</summary>
    public IBrush DiscFill => Palette.For(IsPressed ? "Ok" : HasTarget ? "Accent" : "Surface2");

    /// <summary>Round button ring: green while held, white while selected, quiet otherwise.</summary>
    public IBrush DiscRing => Palette.For(IsPressed ? "Ok" : IsSelected ? "Fg" : "Line");

    private void NotifyDiscChanged()
    {
        OnPropertyChanged(nameof(DiscFill));
        OnPropertyChanged(nameof(DiscRing));
    }

    private void OnSelectedTargetChanged(MappingTarget? value)
    {
        if (value is null) return;
        MappingId = value.Id;
        if (SelectedCategory?.Category != value.Category)
            SelectedCategory = Categories.First(c => c.Category == value.Category);

        // The id may not have moved (Left Alt and NoVoice share 0xE2), so the id handler did not run.
        OnPropertyChanged(nameof(MappingName));
        OnPropertyChanged(nameof(MappingTooltip));
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(IsEditedSinceRead));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Detail));
    }

    partial void OnMappingIdChanged(byte value)
    {
        if (SelectedTarget?.Id != value) SelectedTarget = _idSpace.Find(value);
        OnPropertyChanged(nameof(MappingName));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsEditedSinceRead));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(MappingTooltip));
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(KeyBorder));
        OnPropertyChanged(nameof(HasTarget));
        OnPropertyChanged(nameof(HasNoTarget));
        NotifyDiscChanged();
    }

    /// <summary>True for a moment after the device reports this key's target (see "Last").</summary>
    [ObservableProperty]
    private bool _isPressed;

    partial void OnIsPressedChanged(bool value)
    {
        OnPropertyChanged(nameof(KeyBackground));
        OnPropertyChanged(nameof(KeyBorder));
        NotifyDiscChanged();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(KeyBackground));
        OnPropertyChanged(nameof(KeyBorder));
        OnPropertyChanged(nameof(MappingBrush));
        NotifyDiscChanged();
    }

    /// <summary>Puts the key back to its default target (local edit; not written to the device).</summary>
    [RelayCommand]
    public void RestoreDefault() => MappingId = DefaultMappingId;
}
