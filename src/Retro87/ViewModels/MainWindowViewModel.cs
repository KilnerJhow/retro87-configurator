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

/// <summary>One selectable macro step kind, with the Portuguese label shown in the picker.</summary>
public sealed record MacroKindOption(MacroActionKind Kind, string Label)
{
    /// <summary>What a screen reader announces for the picker row.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// One connection of the Super Buttons block: the two Super Buttons that live behind it. "On the keyboard"
/// holds the keyboard's own pair; A, B, X and Y hold the pair of each connection (see
/// <see cref="SuperButtons"/>). The pair is only shown when the connection is the active one, which is
/// why the block is read as two steps instead of ten equal buttons.
/// </summary>
public partial class SuperConnectionViewModel : ObservableObject
{
    private readonly Action<SuperConnectionViewModel>? _onSelect;

    /// <summary>
    /// The port symbol (» hexagon X ▼) as SVG paths in a 16x16 box, copied from the official
    /// software's artwork, or null to show <see cref="Name"/> as text. Drawn instead of typed so the
    /// four symbols share one size. <see cref="IconLines"/> is stroked, <see cref="IconFill"/> filled.
    /// </summary>
    public Geometry? Icon => IconLines ?? IconFill;

    public Geometry? IconLines { get; init; }

    public Geometry? IconFill { get; init; }

    public SuperConnectionViewModel(string name, string caption, string wireNames,
        IReadOnlyList<MappingRowViewModel> buttons, Action<SuperConnectionViewModel>? onSelect = null)
    {
        Name = name;
        Caption = caption;
        WireNames = wireNames;
        Buttons = buttons;
        _onSelect = onSelect;

        foreach (MappingRowViewModel button in buttons)
            button.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MappingRowViewModel.IsModified))
                    OnPropertyChanged(nameof(HasModified));
                else if (e.PropertyName == nameof(MappingRowViewModel.IsPressed))
                    NotifyJackChanged();
            };
    }

    private bool IsAnyPressed => Buttons.Any(button => button.IsPressed);

    /// <summary>Round port fill: green while a button behind it is held, amber veil while it is the open connection.</summary>
    public IBrush JackFill => Palette.For(IsAnyPressed ? "OkSoft" : IsActive ? "AccentSoft" : "Surface2");

    /// <summary>Round port ring, same states as <see cref="JackFill"/>.</summary>
    public IBrush JackRing => Palette.For(IsAnyPressed ? "Ok" : IsActive ? "Accent" : "Line");

    /// <summary>Ring width: thicker while the port is lit.</summary>
    public double JackRingWidth => IsAnyPressed || IsActive ? 3 : 2;

    /// <summary>Dot under the port for the first button of the pair.</summary>
    public IBrush FirstDot => DotBrush(Buttons[0]);

    /// <summary>Dot under the port for the second button of the pair.</summary>
    public IBrush SecondDot => DotBrush(Buttons[1]);

    private IBrush DotBrush(MappingRowViewModel button)
        => Palette.For(button.IsPressed ? "Ok" : IsActive ? "Accent" : "Line");

    private void NotifyJackChanged()
    {
        OnPropertyChanged(nameof(JackFill));
        OnPropertyChanged(nameof(JackRing));
        OnPropertyChanged(nameof(JackRingWidth));
        OnPropertyChanged(nameof(FirstDot));
        OnPropertyChanged(nameof(SecondDot));
    }

    /// <summary>Chip label: "On the keyboard", "A", "B", "X", "Y".</summary>
    public string Name { get; }

    /// <summary>How the connection is said inside a sentence: "of the keyboard", "of connection A".</summary>
    public string Caption { get; }

    /// <summary>The pair's wire names in mono ("KA · KB"), the device's own names for them.</summary>
    public string WireNames { get; }

    /// <summary>The two Super Buttons of this connection, in device order.</summary>
    public IReadOnlyList<MappingRowViewModel> Buttons { get; }

    /// <summary>True while this connection feeds the pair shown in the callout.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>True when either button of the pair was edited since the read (the amber marker).</summary>
    public bool HasModified => Buttons.Any(button => button.IsModified);

    /// <summary>Hover text of the chip: it names what clicking it shows.</summary>
    public string Tooltip => Loc.F("chip_tip", Caption);

    /// <summary>Name a screen reader announces for the chip.</summary>
    public string AutomationName => Loc.F("chip_name", Caption);

    /// <summary>Chip fill: the active connection lights up with the same accent as a pressed/selected key.</summary>
    public IBrush ChipBackground => IsActive ? Palette.For("AccentSoft") : Brushes.Transparent;

    /// <summary>Chip outline: the accent for the active connection, the veiled line for the rest.</summary>
    public IBrush ChipBorder => IsActive ? Palette.For("Accent") : Palette.For("LineSoft");

    /// <summary>Connection name in the chip: cold paper when active, mist when quiet.</summary>
    public IBrush ChipNameBrush => IsActive ? Palette.For("Fg") : Palette.For("FgMuted");

    /// <summary>Asks the window to show this connection's pair in the callout.</summary>
    [RelayCommand]
    private void Select() => _onSelect?.Invoke(this);

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ChipBackground));
        OnPropertyChanged(nameof(ChipBorder));
        OnPropertyChanged(nameof(ChipNameBrush));
        NotifyJackChanged();
    }
}

/// <summary>
/// How loud a status line is. The status bar is the only channel the app has for feedback, so the
/// severity decides its color and glyph: a failure must not look like "configuration read".
/// </summary>
public enum StatusSeverity
{
    Info,
    Success,
    Warning,
    Error,
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

/// <summary>
/// Main window view-model. Owns the device monitor, the protocol instance, the local profile
/// store and the read-only firmware notification service.
/// </summary>
/// <summary>One line of the save dialog: what changes, the value the keyboard has and the one it will get.</summary>
public sealed record ChangeItem(string Label, string Before, string After);

/// <summary>One row of the Perfis tab: a profile file saved on this computer.</summary>
/// <remarks>
/// It carries its own commands: the row's menu is a flyout, and a flyout is outside the list's
/// visual tree, so it reaches the window's view-model more reliably through the row it belongs to.
/// </remarks>
public sealed class SavedProfileItem
{
    public SavedProfileItem(string fileName, string name, string info, string savedText, bool sameNameOnKeyboard,
        Action<SavedProfileItem> activate, Action<SavedProfileItem> rename, Action<SavedProfileItem> copy,
        Action<SavedProfileItem> delete, Action showFolder)
    {
        FileName = fileName;
        Name = name;
        Info = info;
        SavedText = savedText;
        SameNameOnKeyboard = sameNameOnKeyboard;
        ActivateCommand = new RelayCommand(() => activate(this));
        RenameCommand = new RelayCommand(() => rename(this));
        CopyCommand = new RelayCommand(() => copy(this));
        DeleteCommand = new RelayCommand(() => delete(this));
        ShowFolderCommand = new RelayCommand(showFolder);
    }

    public string FileName { get; }
    public string Name { get; }
    public string Info { get; }
    public string SavedText { get; }
    public bool SameNameOnKeyboard { get; }
    public System.Windows.Input.ICommand ActivateCommand { get; }
    public System.Windows.Input.ICommand RenameCommand { get; }
    public System.Windows.Input.ICommand CopyCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand ShowFolderCommand { get; }
}

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

    private void UpdateDevices(IReadOnlyList<DeviceInfo> collections)
    {
        // The dongle restarts its USB connection some seconds after a write that changed something
        // (seen on the hardware, 2026-10-20). Every collection path of the open session then
        // disappears, the protocol handle is dead and nothing says so: notice it here and reconnect.
        bool connectionLost = IsConnected && SelectedDevice is not null && _connectedPaths.Count > 0 &&
                              !collections.Any(c => _connectedPaths.Contains(c.DevicePath));
        DeviceInfo? lostDevice = connectionLost ? SelectedDevice : null;
        if (lostDevice is not null)
        {
            bool hadChanges = HasChanges;
            Disconnect();
            _reconnectTarget = lostDevice;
            SetStatus(StatusSeverity.Warning, hadChanges
                ? Loc.T("st_conn_reset_discard")
                : Loc.T("st_conn_reset"));
        }

        // One keyboard exposes several HID collections (mouse, consumer, keyboard, vendor); the
        // picker shows one row per physical unit instead of one per collection.
        IReadOnlyList<DeviceInfo> devices = DeviceIdentity.Distinct(collections);

        Devices.Clear();
        foreach (var d in devices)
            Devices.Add(d);

        // The instances are rebuilt on every poll: keep the selection by physical identity.
        if (SelectedDevice is null || !devices.Any(d => DeviceIdentity.SamePhysicalDevice(d, SelectedDevice)))
            SelectedDevice = devices.FirstOrDefault();

        if (devices.Count == 0)
        {
            SetStatus(StatusSeverity.Info,
                Loc.T("st_none_found"));
        }
        else if (!IsConnected && _reconnectTarget is null)
        {
            SetStatus(StatusSeverity.Info, devices.Count == 1
                ? Loc.T("st_found_one")
                : Loc.F("st_found_many", devices.Count));
        }

        if (_reconnectTarget is { } target && !_reconnecting)
            _ = ReconnectAsync(target);
    }

    /// <summary>
    /// Connects again to a keyboard that restarted its link, waiting for it to come back: the
    /// collections show up before the device answers, so a failed try is repeated a few times.
    /// </summary>
    private async Task ReconnectAsync(DeviceInfo target)
    {
        _reconnecting = true;
        try
        {
            for (int attempt = 0; attempt < 8 && !IsConnected && _reconnectTarget is not null; attempt++)
            {
                DeviceInfo? present = Devices.FirstOrDefault(d => DeviceIdentity.SamePhysicalDevice(d, target));
                if (present is not null && !IsBusy)
                {
                    SelectedDevice = present;
                    await ConnectAsync();
                    if (IsConnected) break;
                }
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            if (!IsConnected && _reconnectTarget is not null)
            {
                SetStatus(StatusSeverity.Warning,
                    Loc.T("st_reconnect_failed"));
            }
        }
        finally
        {
            _reconnectTarget = null;
            _reconnecting = false;
        }
    }

    [RelayCommand]
    private void RefreshDevices() => UpdateDevices(_monitor.Devices);

    /// <summary>
    /// Alt+1..5 — goes to the tab whose zero-based index the key binding passes. Living in the
    /// view-model (and not in a code-behind handler) keeps the shortcut list in one place with the
    /// commands it triggers.
    /// </summary>
    [RelayCommand]
    private void GoToTab(string? index)
    {
        if (int.TryParse(index, out int tab) && tab >= 0 && tab < DeviceTabCount)
            SelectedTabIndex = tab;
    }

    /// <summary>Number of tabs in the window (Mapping, Macros, Settings, Profiles, About the keyboard).</summary>
    public const int DeviceTabCount = 5;

    /// <summary>Closes the session and goes back to the keyboard list.</summary>
    [RelayCommand]
    private void DisconnectDevice()
    {
        if (IsBusy) return;
        Disconnect();
        RefreshDevices();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice is null)
        {
            SetStatus(StatusSeverity.Warning, Loc.T("st_no_selection"));
            return;
        }
        if (IsBusy) return;

        DeviceInfo device = SelectedDevice;
        IsBusy = true;
        SetStatus(StatusSeverity.Info, Loc.F("st_connecting", device.ProductName));
        try
        {
            Disconnect();

            // Opening the HID interface and reading the version can take a moment on Linux
            // (hidraw enumeration and the first command); keep it off the UI thread so the
            // window keeps repainting and the progress bar stays alive.
            _protocol = await Task.Run(() => _factory.Create(device));
            IsConnected = _protocol is not null;
            _connectedPaths = IsConnected
                ? _monitor.Devices.Where(d => DeviceIdentity.SamePhysicalDevice(d, device))
                    .Select(d => d.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _idSpace = _protocol is XboxJpProtocol ? MappingIdSpace.Xbox : MappingIdSpace.Jp;
            NotifyFamilyChanged();
            if (IsConnected)
                StartInputListening(device);
            if (!IsConnected)
            {
                SetStatus(StatusSeverity.Error,
                    Loc.T("st_hid_failed"));
            }
            else if (_protocol!.CanWrite)
            {
                SetStatus(StatusSeverity.Success,
                    Loc.F("st_connected", device.ProductName));
            }
            else
            {
                SetStatus(StatusSeverity.Info,
                    Loc.F("st_connected_ro", device.ProductName));
            }
        }
        catch (Exception ex)
        {
            _protocol = null;
            IsConnected = false;
            NotifyFamilyChanged();
            SetStatus(StatusSeverity.Error, Loc.F("st_connect_failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }

        // The picker is the whole "connect" gesture: the screen opens already filled.
        if (IsConnected)
            await ReadConfigAsync();
    }

    /// <summary>
    /// What to tell the user when an Xbox session reads nothing. The advice is the one measured on
    /// the hardware (2026-10-18): when the keyboard loses the dongle (or the PC goes off) it goes to
    /// sleep, and then <b>only a key press brings it back</b> — the wake frame (<c>cmd 0x08 sub 0</c>)
    /// was tried once and 19 times in a row without an answer, and the app has no way to press a key.
    /// </summary>
    private static string DescribeUnresponsive(XboxJpProtocol xbox)
    {
        var failure = xbox.LastFailure;
        string cause = failure switch
        {
            XboxFailure.DeviceError => Loc.T("unresp_device_error"),
            XboxFailure.NoAnswer or XboxFailure.None => Loc.T("unresp_no_answer"),
            _ => xbox.LastError ?? Loc.T("unresp_no_answer"),
        };
        return Loc.F(failure == XboxFailure.DeviceError ? "unresp_asleep" : "unresp_other", cause);
    }

    private void Disconnect()
    {
        StopInputListening();
        _protocol?.Dispose();
        _protocol = null;
        IsConnected = false;
        _loadedConfig = null;
        RefreshChanges();
        NotifyFamilyChanged();
    }

    /// <summary>Raises the family flags after the protocol (and therefore the family) changed.</summary>
    private void NotifyFamilyChanged()
    {
        OnPropertyChanged(nameof(ProfileNameMaxLength));
        OnPropertyChanged(nameof(IsXboxFamily));
        OnPropertyChanged(nameof(IsJpFamily));
        OnPropertyChanged(nameof(IsFamilyUnknown));
        OnPropertyChanged(nameof(CanSave));
    }


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

    /// <summary>
    /// Turns on capture mode: the next key/media event is matched against the Super Button
    /// mappings and the block jumps to the button that owns it. Escape cancels.
    /// </summary>
    [RelayCommand]
    private async Task ListenSuperButtonAsync()
    {
        if (IsListeningForSuperButton)
        {
            IsListeningForSuperButton = false;
            IsAskingSuperButton = false;
            SetStatus(StatusSeverity.Info, Loc.T("st_capture_cancelled"));
            return;
        }
        if (IsMappingEmpty)
        {
            SetStatus(StatusSeverity.Warning, Loc.T("st_read_first_listen"));
            return;
        }

        if (!await WakeRawSuperButtonSignalAsync()) return;

        IsAskingSuperButton = false;
        IsListeningForSuperButton = true;
        SetStatus(StatusSeverity.Info,
            Loc.T("st_press_super"));
        StartSuperQuestionTimer();
    }

    /// <summary>
    /// Rewrites the current profile name unchanged — a harmless write the dongle needs to start
    /// streaming its raw vendor report (how a port with no mapped target still gets identified).
    /// Without this, that report only flows while the official 8BitDo app is open: picking a
    /// profile there does the same write as a side effect. Runs once per "Ouvir Super Button"
    /// click, not per press.
    /// </summary>
    private async Task<bool> WakeRawSuperButtonSignalAsync()
    {
        if (_protocol is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return false; }
        if (!CanWriteDevice)
        {
            SetStatus(StatusSeverity.Warning,
                Loc.T("st_capture_unavailable"));
            return false;
        }
        IJpProtocol protocol = _protocol;
        string name = _loadedConfig?.ProfileName ?? string.Empty;
        IsBusy = true;
        SetStatus(StatusSeverity.Info,
            Loc.T("st_waking"));
        try
        {
            bool ok = await Task.Run(() => protocol.WakeRawSignal(name));
            if (!ok)
                SetStatus(StatusSeverity.Error, Loc.T("st_wake_failed"));
            return ok;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// After a few seconds without a key event, asks which button was pressed. This covers
    /// media/mouse-mapped buttons, whose events may be consumed by the desktop before the window
    /// sees them.
    /// </summary>
    private void StartSuperQuestionTimer()
    {
        _superQuestionTimer?.Stop();
        _superQuestionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _superQuestionTimer.Tick += (_, _) =>
        {
            _superQuestionTimer?.Stop();
            if (!IsListeningForSuperButton) return;

            IsListeningForSuperButton = false;
            SuperButtonChoices = AllSuperButtons;
            SuperButtonQuestion = Loc.T("super_question_missed");
            IsAskingSuperButton = true;
            SetStatus(StatusSeverity.Info, Loc.T("st_pick_super"));
        };
        _superQuestionTimer.Start();
    }

    /// <summary>Closes the “which Super Button?” question without selecting anything.</summary>
    [RelayCommand]
    private void CancelSuperButtonQuestion()
    {
        IsAskingSuperButton = false;
        IsListeningForSuperButton = false;
        SetStatus(StatusSeverity.Info, Loc.T("st_question_cancelled"));
    }

    /// <summary>
    /// Handles a key while capture mode is on. Returns true when the event belongs to the capture
    /// (either it identified a button or it is a key the capture should consume).
    /// </summary>
    /// <remarks>
    /// This is the fallback path used when the HID listener could not open the keyboard
    /// collection. When it is running, the same press also arrives from
    /// <see cref="OnInputPressed"/>; the handler is idempotent, so the duplicate is harmless.
    /// </remarks>
    public bool TryCaptureSuperButton(Key key)
    {
        if (!IsListeningForSuperButton) return false;

        if (key == Key.Escape)
        {
            IsListeningForSuperButton = false;
            SetStatus(StatusSeverity.Info, Loc.T("st_capture_cancelled"));
            return true;
        }

        if (!SuperButtonCapture.TryMap(key, out byte usage, out MappingTargetKind kind))
            return false;

        HandleSuperButtonPress(usage, kind, SuperButtonCapture.Describe(key));
        return true;
    }

    /// <summary>Receives a press from the HID listener (reader thread) and moves it to the UI thread.</summary>
    private void OnInputPressed(object? sender, HidInputPress press)
        => Dispatcher.UIThread.Post(() => HandleSuperButtonPress(press.Code, press.Kind, keyLabel: null));

    /// <summary>Receives a raw Super Button press/release (reader thread) and moves it to the UI thread.</summary>
    private void OnRawPortPressed(object? sender, SuperButtonRawPress press)
        => Dispatcher.UIThread.Post(() => HandleRawPortPress(press));

    /// <summary>
    /// Keeps the pressed button lit for exactly as long as it is physically held — the vendor
    /// signal says so directly, unlike the mapped-id path's fixed-duration flash — and, for a
    /// press, arms a short wait for a mapped-id event that might explain the same physical event.
    /// If none arrives, <see cref="ResolveRawPortPress"/> handles it alone: this button has no
    /// mapped target, the case <see cref="HandleSuperButtonPress"/> alone cannot identify.
    /// </summary>
    private void HandleRawPortPress(SuperButtonRawPress press)
    {
        if (IsMappingEmpty) return;

        int ordinal = SuperButtons.OrdinalOf(press.Interface, press.PairIndex);
        MappingRowViewModel? row = _allMappings.FirstOrDefault(r => r.SuperOrdinal == ordinal);

        if (!press.IsPressed)
        {
            if (row is not null)
            {
                row.IsPressed = false;
                _heldRawRows.Remove(row);
            }
            if (_pendingRawPress is { } pending && pending.Interface == press.Interface
                && pending.PairIndex == press.PairIndex)
                _pendingRawPress = null;   // released before the wait below ran its course
            return;
        }

        if (row is not null)
        {
            row.IsPressed = true;
            _heldRawRows.Add(row);
        }

        _pendingRawPress = press;
        _pendingRawPressTick = Environment.TickCount64;

        _rawResolveTimer?.Stop();
        _rawResolveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RawPortResolveDelayMs) };
        _rawResolveTimer.Tick += (_, _) =>
        {
            _rawResolveTimer?.Stop();
            if (_pendingRawPress == press) ResolveRawPortPress(press);
        };
        _rawResolveTimer.Start();
    }

    /// <summary>
    /// A raw Super Button press that no mapped-id event explained within the wait: it has no
    /// target, which is exactly why <see cref="HandleSuperButtonPress"/> never ran for it. Unlike
    /// the mapped path, the vendor signal already says exactly which button — nothing to ask.
    /// </summary>
    private void ResolveRawPortPress(SuperButtonRawPress press)
    {
        _pendingRawPress = null;

        int ordinal = SuperButtons.OrdinalOf(press.Interface, press.PairIndex);
        MappingRowViewModel? row = _allMappings.FirstOrDefault(r => r.SuperOrdinal == ordinal);
        if (row is null) return;

        bool identifying = IsListeningForSuperButton || IsAskingSuperButton;
        LastSuperButtonText = Loc.F("last_none", DescribeButton(row));
        SelectKey(row);
        if (!identifying) return;

        IsAskingSuperButton = false;
        SetStatus(StatusSeverity.Success,
            Loc.F("identified_none", row.KeyName, row.InterfaceName ?? Loc.T("on_keyboard")));
    }

    /// <summary>
    /// Resolves a decoded press against the ten Super Buttons and either selects it or asks which
    /// of the matching ports was pressed. Runs on the UI thread.
    /// </summary>
    private void HandleSuperButtonPress(byte code, MappingTargetKind kind, string? keyLabel)
    {
        if (IsMappingEmpty) return;

        // The vendor signal for the same physical press, if it arrived first: use it to tell apart
        // two Super Buttons sharing a target, then drop it — a mapped press explains the event, so
        // ResolveRawPortPress must not ask about it again as "no target".
        SuperButtonRawPress? rawPress = Environment.TickCount64 - _pendingRawPressTick < 300 ? _pendingRawPress : null;
        _pendingRawPress = null;
        _rawResolveTimer?.Stop();

        byte mappingId = _idSpace.IdForUsage(code, kind);
        string description = DescribePress(code, mappingId, kind, keyLabel);
        bool identifying = IsListeningForSuperButton || IsAskingSuperButton;

        List<MappingRowViewModel> pressed = _allMappings
            .Where(row => row.MappingId == mappingId && KindMatches(row.SelectedTarget?.Kind, kind))
            .ToList();
        FlashPressed(pressed);
        // The "Last" line belongs to the Super Buttons block: an ordinary key still lights up on the
        // board, but it never writes here (and never wipes the last Super Button shown).
        List<MappingRowViewModel> pressedSuper = pressed.Where(row => row.IsSuperButton).ToList();
        if (!identifying && pressedSuper.Count > 0)
            LastSuperButtonText = Loc.F("last_one", string.Join(" " + Loc.T("or_word") + " ", pressedSuper.Select(DescribeButton)));

        List<MappingRowViewModel> matches = pressed.Where(row => row.IsSuperButton).ToList();

        if (matches.Count == 1)
        {
            ApplySuperButtonPress(matches[0], identifying);
            return;
        }

        if (matches.Count == 0)
        {
            // A normal key that no Super Button uses: only worth mentioning while identifying.
            if (!identifying) return;

            SuperButtonChoices = AllSuperButtons;
            SuperButtonQuestion =
                Loc.F("super_no_match", description);
            IsListeningForSuperButton = false;
            IsAskingSuperButton = true;
            SetStatus(StatusSeverity.Info, Loc.T("st_pick_super"));
            return;
        }

        // More than one Super Button shares the target (two ports on Play/Pause, for example): the
        // HID report alone says what was pressed, not which physical button produced it — but the
        // vendor signal (if it arrived for the same press) already names the exact button.
        if (rawPress is { } raw)
        {
            int rawOrdinal = SuperButtons.OrdinalOf(raw.Interface, raw.PairIndex);
            MappingRowViewModel? resolved = matches.FirstOrDefault(row => row.SuperOrdinal == rawOrdinal);
            if (resolved is not null)
            {
                ApplySuperButtonPress(resolved, identifying);
                return;
            }
        }

        string ports = string.Join(" " + Loc.T("or_word") + " ", matches.Select(DescribeButton));
        LastSuperButtonText = Loc.F("last_one", ports);
        if (!identifying) return;

        SuperButtonChoices = matches;
        SuperButtonQuestion =
            Loc.F("super_multi", description, matches.Count);
        IsListeningForSuperButton = false;
        IsAskingSuperButton = true;
        SetStatus(StatusSeverity.Info, Loc.T("st_pick_port"));
    }

    /// <summary>
    /// Lights every key and Super Button that owns the reported target for a moment, so a press
    /// shows where it came from even when nothing is being identified.
    /// </summary>
    private void FlashPressed(IReadOnlyList<MappingRowViewModel> rows)
    {
        _pressedTimer?.Stop();
        foreach (MappingRowViewModel row in _allMappings.Where(r => r.IsPressed && !_heldRawRows.Contains(r)))
            row.IsPressed = false;
        foreach (MappingRowViewModel row in rows) row.IsPressed = true;
        if (rows.Count == 0) return;

        _pressedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _pressedTimer.Tick += (_, _) =>
        {
            _pressedTimer?.Stop();
            foreach (MappingRowViewModel row in rows.Where(r => !_heldRawRows.Contains(r))) row.IsPressed = false;
        };
        _pressedTimer.Start();
    }

    /// <summary>Selects the button, opens its port and reports the result.</summary>
    private void ApplySuperButtonPress(MappingRowViewModel row, bool identifying)
    {
        LastSuperButtonText = Loc.F("last_one", DescribeButton(row));
        SelectKey(row);
        if (!identifying) return;

        IsAskingSuperButton = false;
        SetStatus(StatusSeverity.Success,
            Loc.F("identified", row.KeyName, row.InterfaceName ?? Loc.T("on_keyboard"), row.MappingName));
    }

    /// <summary>Port and name of a button, e.g. "porta ▾ (Y) · Super Button 9 (Play)".</summary>
    private static string DescribeButton(MappingRowViewModel row)
    {
        string port = row.InterfaceName ?? Loc.T("on_keyboard");
        return $"{port} · {row.KeyName} ({row.MappingName})";
    }

    /// <summary>Human-readable press: the key label, or the id name decoded for the family.</summary>
    private string DescribePress(byte code, byte mappingId, MappingTargetKind kind, string? keyLabel)
    {
        string label = keyLabel ?? _idSpace.Describe(mappingId);
        string origin = kind switch
        {
            MappingTargetKind.Media or MappingTargetKind.Shortcut => Loc.T("origin_media"),
            MappingTargetKind.Mouse => Loc.T("origin_mouse"),
            _ => string.Empty,
        };
        return origin.Length == 0 ? label : $"{label} ({origin})";
    }

    /// <summary>
    /// The JP family classifies media targets as <see cref="MappingTargetKind.Shortcut"/> while the
    /// HID/shared model calls them media; both names mean the same row here.
    /// </summary>
    private static bool KindMatches(MappingTargetKind? rowKind, MappingTargetKind pressKind)
    {
        if (rowKind is null) return false;

        static MappingTargetKind Normalize(MappingTargetKind kind)
            => kind == MappingTargetKind.Shortcut ? MappingTargetKind.Media : kind;

        return Normalize(rowKind.Value) == Normalize(pressKind);
    }

    /// <summary>Starts reading the keyboard's input collections for the selected device.</summary>
    private void StartInputListening(DeviceInfo device)
    {
        StopInputListening();

        var collections = _monitor.Devices
            .Where(d => DeviceIdentity.SamePhysicalDevice(d, device))
            .Select(d => _monitor.FindHidDevice(d.DevicePath))
            .Where(hid => hid is not null)
            .Cast<HidDevice>()
            .ToList();

        if (collections.Count == 0) return;

        var listener = new SuperButtonInputListener(collections);
        listener.Pressed += OnInputPressed;
        listener.RawPortPressed += OnRawPortPressed;
        if (!listener.Start())
        {
            listener.Pressed -= OnInputPressed;
            listener.RawPortPressed -= OnRawPortPressed;
            listener.Dispose();
            return;
        }

        _inputListener = listener;
    }

    /// <summary>Stops the HID listener (disconnect, window close, or a new connection).</summary>
    private void StopInputListening()
    {
        if (_inputListener is null) return;
        _inputListener.Pressed -= OnInputPressed;
        _inputListener.RawPortPressed -= OnRawPortPressed;
        _inputListener.Dispose();
        _inputListener = null;

        // The device is gone: any button the raw signal left lit would otherwise never clear.
        foreach (MappingRowViewModel row in _heldRawRows) row.IsPressed = false;
        _heldRawRows.Clear();
        _pendingRawPress = null;
        _rawResolveTimer?.Stop();
    }

    /// <summary>Shows a key (or Super Button) in the side panel of the Mapeamento tab.</summary>
    private void SelectKey(MappingRowViewModel? key)
    {
        if (key is null || ReferenceEquals(key, SelectedKey)) return;
        if (SelectedKey is not null) SelectedKey.IsSelected = false;
        SelectedKey = key;
        key.IsSelected = true;

        // Picking a Super Button is also step 1: its connection becomes the one on screen.
        if (key.IsSuperButton)
        {
            // KA and KB are drawn on the keyboard itself, so they never open a pair below the ports.
            SuperConnectionViewModel? owner = SuperConnections.FirstOrDefault(c => c.Buttons.Contains(key));
            SetActiveConnection(ReferenceEquals(owner, KeyboardConnection) ? null : owner);
            IsAskingSuperButton = false;
            IsListeningForSuperButton = false;
        }
    }

    /// <summary>
    /// Step 1 of the Super Buttons block: the connection's pair comes on screen and the frame moves
    /// to it. Selecting the connection is not selecting a button — the side panel drops a Super
    /// Button of another connection, which would otherwise stay "selecionado" out of sight.
    /// </summary>
    private void ChooseConnection(SuperConnectionViewModel connection)
    {
        SetActiveConnection(connection);
        if (SelectedKey is { IsSuperButton: true } selected && !connection.Buttons.Contains(selected))
            ClearSelection();
    }

    /// <summary>Moves the active connection and keeps the chips' "you are here" state in step.</summary>
    private void SetActiveConnection(SuperConnectionViewModel? connection)
    {
        if (ReferenceEquals(ActiveConnection, connection)) return;
        if (ActiveConnection is not null) ActiveConnection.IsActive = false;
        ActiveConnection = connection;
        if (connection is not null) connection.IsActive = true;
    }

    /// <summary>Closes the side panel of the Mapeamento tab (the X of the panel).</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        if (SelectedKey is not null) SelectedKey.IsSelected = false;
        SelectedKey = null;
    }

    /// <summary>Fills the keyboard (85 board keys) and the Super Button block from <see cref="_allMappings"/>.</summary>
    private void RebuildKeyViews()
    {
        Keys.Clear();
        SuperConnections.Clear();
        LastSuperButtonText = string.Empty;

        var board = _allMappings.Where(row => !row.IsSuperButton).ToList();
        foreach (MappingRowViewModel row in board)
            Keys.Add(row);

        var super = _allMappings.Where(row => row.IsSuperButton).ToList();
        foreach (MappingRowViewModel row in super.Where(row => row.Box.Width > 0))
            Keys.Add(row);
        for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal += 2)
            SuperConnections.Add(BuildConnection(ordinal, super));

        SuperPorts.Clear();
        foreach (SuperConnectionViewModel connection in SuperConnections.Skip(1))
            SuperPorts.Add(connection);
        KeyboardConnection = SuperConnections.FirstOrDefault();

        // A fresh read starts at step 1: nothing chosen, no pair on screen, and the frame asks which
        // connection.
        SetActiveConnection(null);

        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(HasMappings));
        OnPropertyChanged(nameof(IsMappingEmpty));
        OnPropertyChanged(nameof(AllSuperButtons));
    }

    /// <summary>
    /// Builds the connection that owns the Super Buttons at <paramref name="firstOrdinal"/> and the
    /// next one: ordinals 0/1 are the keyboard's own pair, 2/3 the pair of connection A, and so on
    /// (see <see cref="SuperButtons.InterfaceOf"/>).
    /// </summary>
    private SuperConnectionViewModel BuildConnection(int firstOrdinal, IReadOnlyList<MappingRowViewModel> super)
    {
        MappingRowViewModel left = super.First(row => row.SuperOrdinal == firstOrdinal);
        MappingRowViewModel right = super.First(row => row.SuperOrdinal == firstOrdinal + 1);

        string? connection = SuperButtons.InterfaceOf(firstOrdinal);
        string name = PortName(connection);
        string caption = connection is null ? Loc.T("caption_keyboard") : Loc.F("caption_of", PortDetail(connection));

        return new SuperConnectionViewModel(name, caption, $"{left.WireName} · {right.WireName}",
            new[] { left, right }, ChooseConnection)
        {
            IconLines = UseMechaBreakPorts ? PortIconLines(connection) : null,
            IconFill = UseMechaBreakPorts ? PortIconFill(connection) : null,
        };
    }

    /// <summary>Puts every key back to its default target (local edit; written on Save).</summary>
    [RelayCommand]
    private void RestoreAllMappings()
    {
        foreach (MappingRowViewModel row in _allMappings)
            row.RestoreDefault();
        SetStatus(StatusSeverity.Info,
            Loc.T("st_restored_all"));
    }

    /// <summary>Deep copy, so the snapshot stays put while the UI keeps being edited.</summary>
    private static KeyboardConfig CloneConfig(KeyboardConfig source)
    {
        var copy = new KeyboardConfig
        {
            ProfileName = source.ProfileName,
            SpecialFeature = source.SpecialFeature,
            Voice = source.Voice,
            XboxVolumeLevel = source.XboxVolumeLevel,
            XboxFastSetting = source.XboxFastSetting,
        };

        Array.Copy(source.BoardKeys, copy.BoardKeys, copy.BoardKeys.Length);
        Array.Copy(source.BoardKinds, copy.BoardKinds, copy.BoardKinds.Length);

        for (int i = 0; i < source.Macros.Length; i++)
        {
            MacroSlot from = source.Macros[i];
            MacroSlot to = copy.Macros[i];
            to.HostKey = from.HostKey;
            to.Name = from.Name;
            to.Cycles = from.Cycles;
            to.IntervalMs = from.IntervalMs;
            to.Actions.AddRange(from.Actions);
        }

        return copy;
    }

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
