// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using System;
using System.Linq;
using System.Reflection;

namespace Retro87;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // "Retro87 --version" prints the build version and exits.
        if (args.Any(a => a is "--version" or "-v"))
        {
            Console.WriteLine($"Retro87 {VersionText()}");
            Environment.ExitCode = 0;
            return;
        }

        // "Retro87 --help" lists the command surface and exits.
        if (args.Any(a => a is "--help" or "-h"))
        {
            PrintHelp();
            Environment.ExitCode = 0;
            return;
        }

        // "Retro87 --report [file.toml]" writes a read-only diagnostic report.
        int report = Array.FindIndex(args, a => string.Equals(a, "--report", StringComparison.OrdinalIgnoreCase));
        if (report >= 0)
        {
            string? path = report + 1 < args.Length && !args[report + 1].StartsWith("-", StringComparison.Ordinal)
                ? args[report + 1]
                : null;
            Environment.ExitCode = Services.DiagnosticReport.Run(Console.Out, path);
            return;
        }

        // "Retro87 --make-icon [path.ico]" generates the app icon (SkiaSharp), with a PNG preview.
        if (args.Any(a => string.Equals(a, "--make-icon", StringComparison.OrdinalIgnoreCase)))
        {
            int iconArg = Array.FindIndex(args, a => string.Equals(a, "--make-icon", StringComparison.OrdinalIgnoreCase));
            string ico = iconArg >= 0 && iconArg + 1 < args.Length
                ? args[iconArg + 1]
                : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "app.ico");
            ico = Path.GetFullPath(ico);
            string png = Path.ChangeExtension(ico, ".png");
            Brand.AppIcon.WriteIco(ico, png);
            Console.WriteLine($"icon: {ico}\npreview: {png}");
            Environment.ExitCode = 0;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static string VersionText()
    {
        string? version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version.Split('+')[0];
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Retro87 — configurator for the 8BitDo Retro 87 / Retro 87 "X"

Usage:
  Retro87                          opens the window
  Retro87 --version                shows the version
  Retro87 --help                   shows this help
  Retro87 --report [file.toml]     writes a diagnostic report (read-only)
  Retro87 --make-icon [app.ico]    generates the app icon

Self-test and hardware probes: tools/Retro87.Bench.
Golden rule: reading is free; writing needs the lock in the UI and a backup.
This program never downloads or writes firmware.
""");
    }
}
