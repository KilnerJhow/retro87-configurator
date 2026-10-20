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
