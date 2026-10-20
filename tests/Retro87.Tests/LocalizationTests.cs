// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.RegularExpressions;
using Retro87.Localization;
using Xunit;

namespace Retro87.Tests;

public sealed class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[:,][^}]*)?\}");

    [Fact]
    public void Every_text_has_an_english_translation_with_the_same_placeholders()
    {
        foreach (var (key, pair) in Strings.Table)
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.En), $"{key}: missing English");
            Assert.Equal(Slots(pair.Pt), Slots(pair.En));
        }
    }

    [Fact]
    public void Every_key_used_in_xaml_and_code_exists_in_the_table()
    {
        string src = SourceDirectory();
        var used = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs"))
                                 && !f.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{loc:T (\w+)\}")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Loc\.[TF]\(""(\w+)""")) used.Add(m.Groups[1].Value);
        }

        Assert.NotEmpty(used);
        // Keys ending in "_" are prefixes completed in code (see the test below).
        Assert.All(used.Where(key => !key.EndsWith('_')), key => Assert.True(Strings.Table.ContainsKey(key), $"missing string: {key}"));
    }

    [Fact]
    public void Every_target_category_and_media_target_has_a_plain_label()
    {
        foreach (var category in Enum.GetValues<Retro87.Models.TargetCategory>())
            Assert.True(Strings.Table.ContainsKey("category_" + category), category.ToString());

        Assert.NotEqual("NoVoice", Retro87.Models.TargetLabels.Of("NoVoice"));
        Assert.Equal("Esc", Retro87.Models.TargetLabels.Of("Esc"));
    }

    [Fact]
    public void Language_follows_the_culture_name_and_falls_back_to_english()
    {
        Assert.Equal("pt", Loc.Pick("pt-BR"));
        Assert.Equal("en", Loc.Pick("en-US"));
        Assert.Equal("en", Loc.Pick("de-DE"));
        Assert.Equal("en", Loc.Pick(null));
    }

    private static string Slots(string text) =>
        string.Join(",", Placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s));

    private static string SourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Retro87");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/Retro87 not found above the test binaries.");
    }
}
