// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Media;

namespace Retro87.ViewModels;

/// <summary>
/// Brushes read from the application resources by token name — the only tie the view-models have to
/// the palette (see DESIGN.md for the token roles).
/// </summary>
internal static class Palette
{
    /// <summary>Brush of a token ("Accent", "Surface2", "Danger"…), or grey when it is missing.</summary>
    public static IBrush For(string key)
        => Application.Current is { } app
           && app.TryGetResource(key, null, out object? value)
           && value is IBrush brush
            ? brush
            : Brushes.Gray;
}
