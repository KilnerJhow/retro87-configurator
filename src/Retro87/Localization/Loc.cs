// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.Globalization;

namespace Retro87.Localization;

/// <summary>One language the app can show: the code stored on disk and the name shown in the picker.</summary>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Looks up the text of the interface in the current language. Views bind to <c>Item[key]</c>, so
/// switching the language refreshes every label without reopening the window. A key missing from
/// the current language falls back to Portuguese, and then to the key itself.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("pt", "Português"),
        new("en", "English"),
    ];

    private const string Fallback = "pt";

    private string _code = Fallback;
    private CultureInfo _culture = CultureInfo.GetCultureInfo(Fallback);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed, once the bindings were told to refresh.</summary>
    public event Action? LanguageChanged;

    public string Code => _code;

    public CultureInfo Culture => _culture;

    public string this[string key] => Lookup(key);

    public static string T(string key) => Instance.Lookup(key);

    public static string F(string key, params object[] args) =>
        string.Format(Instance._culture, Instance.Lookup(key), args);

    /// <summary>A text in a given language, whatever the current one is.</summary>
    public static string In(string code, string key)
    {
        if (!Strings.Table.TryGetValue(key, out var pair)) return key;
        return Pick(code) == "en" && pair.En.Length > 0 ? pair.En : pair.Pt;
    }

    /// <summary>Picks a supported language from a culture name such as <c>pt-BR</c>; English otherwise.</summary>
    public static string Pick(string? cultureName)
    {
        string two = (cultureName ?? string.Empty).Split('-', '_')[0].ToLowerInvariant();
        foreach (var option in Languages)
            if (option.Code == two) return option.Code;
        return "en";
    }

    public void SetLanguage(string code)
    {
        code = Pick(code);
        if (code == _code) return;
        _code = code;
        _culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = _culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        LanguageChanged?.Invoke();
    }

    private string Lookup(string key)
    {
        if (!Strings.Table.TryGetValue(key, out var pair)) return key;
        string text = _code == "en" ? pair.En : pair.Pt;
        return text.Length > 0 ? text : pair.Pt;
    }
}

