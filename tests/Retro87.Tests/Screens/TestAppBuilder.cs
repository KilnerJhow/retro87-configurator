// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Headless;
using Retro87;
using Retro87.Tests.Screens;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Retro87.Tests.Screens;

/// <summary>Starts the real App (styles, resources, font) on a display-less platform, with Skia rendering.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.WithoutMainWindow = true;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}
