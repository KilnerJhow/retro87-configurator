// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Retro87.Localization;

/// <summary>XAML text from the translation table: <c>Text="{loc:T tab_mapping}"</c>.</summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
