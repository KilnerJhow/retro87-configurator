// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Retro87.Protocol;
using Avalonia.VisualTree;

namespace Retro87.Tests.Screens;

/// <summary>
/// Screens without a keyboard: opens the window with fake data and saves PNGs in <c>RETRO87_SHOTS</c>
/// (default: <c>tests/Retro87.Tests/bin/shots</c>). Agents read the PNGs; the tests check what can be measured.
/// </summary>
public sealed class ScreenshotTests
{
    private static string Dir => Environment.GetEnvironmentVariable("RETRO87_SHOTS")
                                 ?? Path.Combine(AppContext.BaseDirectory, "shots");

    private static string Shot(string name, string language) => Path.Combine(Dir, $"{name}.{language}.png");

    [AvaloniaTheory]
    [InlineData("pt")]
    [InlineData("en")]
    public void Every_tab_renders(string language)
    {
        using var screen = new ScreenHarness(language);
        string[] tabs = { "mapping", "macros", "settings", "profiles", "about" };
        for (int i = 0; i < tabs.Length; i++)
        {
            screen.SelectTab(i);
            string path = Shot($"tab-{i + 1}-{tabs[i]}", language);
            screen.Save(path);
            Assert.True(new FileInfo(path).Length > 5_000, $"{path} looks empty");
        }
    }

    [AvaloniaTheory]
    [InlineData("pt", "A")]
    [InlineData("en", "A")]
    [InlineData("en", "Esc")]
    [InlineData("en", "Right")]
    [InlineData("pt", "Space")]
    public void Balloon_opens_next_to_the_key_and_stays_inside_the_window(string language, string keyName)
    {
        using var screen = new ScreenHarness(language);
        screen.Select(screen.Key(keyName));

        Popup popup = screen.Window.FindControl<Popup>("KeyPopup")!;
        Assert.True(popup.IsOpen, "the popover did not open");
        screen.Save(Shot($"popover-{keyName}", language));

        Control content = (Control)popup.Child!;
        Rect box = new(content.TranslatePoint(default, screen.Window) ?? default, content.Bounds.Size);
        Assert.True(box.Left >= -1 && box.Right <= screen.Window.Bounds.Width + 1,
            $"popover outside the window horizontally: {box}");
        Assert.True(box.Top >= -1 && box.Bottom <= screen.Window.Bounds.Height + 1,
            $"popover outside the window vertically: {box}");
    }

    [AvaloniaFact]
    public void Edited_key_has_no_amber_dot()
    {
        using var screen = new ScreenHarness("pt");
        var key = screen.Key("A");
        key.SelectedTarget = key.Targets.First(t => t.Id != key.SelectedTarget?.Id);
        screen.Settle();
        Assert.True(key.IsModified);
        screen.Save(Shot("key-modified", "pt"));
    }

    [AvaloniaTheory]
    [InlineData("pt", 100, true)]
    [InlineData("pt", 30, true)]
    [InlineData("en", 10, true)]
    [InlineData("en", 87, true)]
    public void Battery_shows_in_the_header_with_the_level(string language, int percent, bool visible)
    {
        using var screen = new ScreenHarness(language);
        var protocol = new XboxJpProtocol(new FakeXboxDevice { BatteryPercent = (byte)percent });
        Assert.True(protocol.ReadVersion());
        screen.ViewModel.ShowBatteryFrom(protocol);
        screen.Settle();

        Assert.Equal(visible, screen.ViewModel.HasBattery);
        Assert.Equal($"{percent}%", screen.ViewModel.BatteryText);
        Assert.InRange(screen.ViewModel.BatteryFillWidth, 0, 23);
        screen.Save(Shot($"battery-{percent}", language));
    }

    [AvaloniaFact]
    public void Minimum_window_size_renders()
    {
        using var screen = new ScreenHarness("pt", 880, 600);
        screen.Save(Shot("min-window", "pt"));
    }
}
