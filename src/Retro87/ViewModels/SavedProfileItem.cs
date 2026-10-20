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
