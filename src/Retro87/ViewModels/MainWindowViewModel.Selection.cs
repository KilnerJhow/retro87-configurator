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

public partial class MainWindowViewModel
{
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
}
