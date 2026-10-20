// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Localization;

/// <summary>
/// Remembers the chosen language in <c>&lt;ApplicationData&gt;/Retro87/language.txt</c>. Without a
/// saved choice the app follows the operating system language.
/// </summary>
public sealed class LanguagePreference
{
    private readonly string _path;

    public LanguagePreference(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Retro87", "language.txt");
    }

    public string Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string saved = File.ReadAllText(_path).Trim();
                if (saved.Length > 0) return Loc.Pick(saved);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Loc.Pick(System.Globalization.CultureInfo.CurrentUICulture.Name);
    }

    public void Save(string code)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, code);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
