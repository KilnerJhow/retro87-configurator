// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Retro87.Models;
using Retro87.ViewModels;

namespace Retro87.Views;

/// <summary>
/// Lays the keys of the drawn keyboard out from their own geometry
/// (<see cref="KeyboardGeometry"/>), in key units. It is the <c>ItemsPanel</c> of the Mapeamento
/// keyboard, so the board has one size to scale and no per-key margins to keep in sync.
/// </summary>
/// <remarks>
/// The panel reads the item's view-model from each child's <see cref="StyledElement.DataContext"/>
/// (the <c>ContentPresenter</c> an <c>ItemsControl</c> creates), because the item carries the box:
/// a key that is not in <see cref="KeyboardGeometry"/> simply gets no box and is not arranged.
/// </remarks>
public sealed class KeyboardPanel : Panel
{
    /// <summary>Size of one key unit, in pixels. The whole board scales from this number.</summary>
    public static readonly StyledProperty<double> UnitProperty =
        AvaloniaProperty.Register<KeyboardPanel, double>(nameof(Unit), 46);

    /// <summary>Gap kept between a key's box and the drawn keycap, in pixels.</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<KeyboardPanel, double>(nameof(Gap), 2);

    public double Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Unit chosen by the last measure (the width the board actually got).</summary>
    private double _unit = 46;

    protected override Size MeasureOverride(Size availableSize)
    {
        // The unit follows the width the board actually got (and never exceeds Unit), so the keys
        // stretch with the window while the labels keep their pixel size: no Viewbox, no shrinking
        // lettering.
        double unit = Unit;
        if (availableSize.Width > 0 && !double.IsInfinity(availableSize.Width))
            unit = Math.Min(Unit, Math.Max(24, availableSize.Width / KeyboardGeometry.Columns));
        _unit = unit;

        foreach (Control child in Children)
        {
            if (child.DataContext is MappingRowViewModel { Box.Width: > 0 } key)
                child.Measure(new Size(key.Box.Width * unit, unit));
            else
                child.Measure(new Size(0, 0));
        }

        return new Size(KeyboardGeometry.Columns * unit, KeyboardGeometry.Rows * unit);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double unit = _unit;
        double gap = Gap;

        foreach (Control child in Children)
        {
            if (child.DataContext is not MappingRowViewModel { Box.Width: > 0 } key)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            double x = key.Box.X * unit + gap;
            double y = KeyboardGeometry.RowOffset(key.Box.Row) * unit + gap;
            double width = key.Box.Width * unit - gap * 2;
            double height = unit - gap * 2;
            child.Arrange(new Rect(x, y, width, height));
        }

        return finalSize;
    }
}
