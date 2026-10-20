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
