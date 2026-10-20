// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Retro87.ViewModels;

namespace Retro87.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _repositioning;

    public MainWindow()
    {
        InitializeComponent();
        Mapping.KeyPopup.Closed += OnKeyPopupClosed;

        // Tunnel so a Super Button mapped to a shortcut (Ctrl+L, F5, …) is captured by the
        // listener before the window's KeyBindings act on it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => HookViewModel();
        HookViewModel();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.TryCaptureSuperButton(e.Key))
            e.Handled = true;
    }

    private void HookViewModel()
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedKey))
            Dispatcher.UIThread.Post(ShowKeyPopup, DispatcherPriority.Loaded);
    }

    /// <summary>Opens the editor balloon next to the selected key, or closes it when nothing is selected.</summary>
    private void ShowKeyPopup()
    {
        var selected = _viewModel?.SelectedKey;
        _repositioning = true;
        try
        {
            Mapping.KeyPopup.IsOpen = false;
            if (selected is null) return;

            // Keys and the Super Button pair live in different lists; anything else (the choice chips of
            // the identification question) falls back to the board card so the balloon still appears.
            Control? target = Mapping.KeysItems.ContainerFromItem(selected)
                              ?? Mapping.PairItems.ContainerFromItem(selected)
                              ?? Mapping.BoardCard;
            Mapping.KeyPopup.PlacementTarget = target;
            PlaceInsideWindow(target);
            Mapping.KeyPopup.IsOpen = true;
        }
        finally
        {
            _repositioning = false;
        }
    }

    /// <summary>
    /// A popup is its own OS window, so the platform lets it hang outside the app. Choose above or below
    /// the key by where there is room, and slide it sideways to stay inside the window.
    /// </summary>
    private void PlaceInsideWindow(Control target)
    {
        const double margin = 8;
        var child = Mapping.KeyPopup.Child;
        if (child is null || target.TranslatePoint(new Point(0, 0), this) is not { } origin) return;

        child.Measure(Size.Infinity);
        Size size = child.DesiredSize;
        double below = Bounds.Height - (origin.Y + target.Bounds.Height);
        double above = origin.Y;
        Mapping.KeyPopup.Placement = below >= size.Height + margin || below >= above
            ? PlacementMode.Bottom
            : PlacementMode.Top;

        double centered = origin.X + target.Bounds.Width / 2 - size.Width / 2;
        double left = Math.Clamp(centered, margin, Math.Max(margin, Bounds.Width - size.Width - margin));
        Mapping.KeyPopup.HorizontalOffset = left - centered;
    }

    /// <summary>Clicking outside the balloon (or Esc) closes it and unselects the key.</summary>
    private void OnKeyPopupClosed(object? sender, System.EventArgs e)
    {
        if (!_repositioning && _viewModel?.SelectedKey is not null)
            _viewModel.ClearSelectionCommand.Execute(null);
    }
}
