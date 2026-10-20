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

        // "Retro87 --selftest" verifies the ported model/protocol layer without any hardware.
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTest.Run(Console.Out) ? 0 : 1;
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

        // "Retro87 --xbox-input-probe" reads the input reports of every HID collection (read-only).
        if (args.Any(a => string.Equals(a, "--xbox-input-probe", StringComparison.OrdinalIgnoreCase)))
        {
            int secondsArg = Array.FindIndex(args, a => string.Equals(a, "--seconds", StringComparison.OrdinalIgnoreCase));
            int seconds = secondsArg >= 0 && secondsArg + 1 < args.Length && int.TryParse(args[secondsArg + 1], out int parsed)
                ? Math.Clamp(parsed, 3, 120)
                : 20;
            Environment.ExitCode = Protocol.XboxInputProbe.Run(Console.Out, seconds);
            return;
        }

        // "Retro87 --xbox-super-listen" identifies Super Button presses from the HID reports (read-only).
        if (args.Any(a => string.Equals(a, "--xbox-super-listen", StringComparison.OrdinalIgnoreCase)))
        {
            int secondsArg = Array.FindIndex(args, a => string.Equals(a, "--seconds", StringComparison.OrdinalIgnoreCase));
            int seconds = secondsArg >= 0 && secondsArg + 1 < args.Length && int.TryParse(args[secondsArg + 1], out int parsed)
                ? Math.Clamp(parsed, 3, 120)
                : 30;
            Environment.ExitCode = Protocol.XboxSuperListenProbe.Run(Console.Out, seconds);
            return;
        }

        // "Retro87 --xbox-probe" reads the Retro 87 X (family Xbox) over HID, read-only.
        if (args.Any(a => string.Equals(a, "--xbox-probe", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = Protocol.XboxHardwareProbe.Run(Console.Out);
            return;
        }

        // "Retro87 --xbox-write-test" exercises the write path on the real keyboard (reversible).
        // With --plan it only reads, saves a backup and prints the frames it would send.
        if (args.Any(a => string.Equals(a, "--xbox-write-test", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            Environment.ExitCode = Protocol.XboxWriteTest.Run(Console.Out, planOnly);
            return;
        }

        // "Retro87 --xbox-super-test" exercises the ten Super Button records and the WriteConfig
        // path on the real keyboard (reversible). With --plan it only reads and prints the frames.
        if (args.Any(a => string.Equals(a, "--xbox-super-test", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            Environment.ExitCode = Protocol.XboxSuperTest.Run(Console.Out, planOnly);
            return;
        }

        // "Retro87 --xbox-macro-test" exercises cmd 0x05/0x06 on the real keyboard (reversible).
        // With --plan it only reads, saves a backup and prints the frames it would send.
        if (args.Any(a => string.Equals(a, "--xbox-macro-test", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            Environment.ExitCode = Protocol.XboxMacroTest.Run(Console.Out, planOnly);
            return;
        }

        // "Retro87 --xbox-config-test" exercises the profile name and a loose byte on the real
        // keyboard (reversible). With --plan it only reads, saves a backup and prints the frames.
        if (args.Any(a => string.Equals(a, "--xbox-config-test", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            Environment.ExitCode = Protocol.XboxConfigTest.Run(Console.Out, planOnly);
            return;
        }

        // "Retro87 --xbox-media-test" exercises a media (MKEY) and a mouse (MOUSE) target on the
        // real keyboard (reversible). With --plan it only reads and prints the frames.
        if (args.Any(a => string.Equals(a, "--xbox-media-test", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            Environment.ExitCode = Protocol.XboxMediaTest.Run(Console.Out, planOnly);
            return;
        }

        // "Retro87 --xbox-ui-config-test" exercises the Config tab fields (volume_lv and the
        // fast_setting Quick Setup bits) through WriteConfig on the real keyboard (reversible).
        // With --plan it only reads and prints the frames.
        if (args.Any(a => string.Equals(a, "--xbox-ui-config-test", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            Environment.ExitCode = Protocol.XboxUiConfigTest.Run(Console.Out, planOnly);
            return;
        }

        // "Retro87 --xbox-link-test" measures what the keyboard does when the 2.4 G dongle is
        // pulled out and put back (the link-loss sleep), probing before and after the wake frame.
        if (args.Any(a => string.Equals(a, "--xbox-link-test", StringComparison.OrdinalIgnoreCase)))
        {
            int gapIndex = Array.FindIndex(args, a => string.Equals(a, "--gap", StringComparison.OrdinalIgnoreCase));
            int gap = gapIndex >= 0 && gapIndex + 1 < args.Length && int.TryParse(args[gapIndex + 1], out int parsed)
                ? parsed
                : 30;
            int insistIndex = Array.FindIndex(args, a => string.Equals(a, "--insist", StringComparison.OrdinalIgnoreCase));
            int insist = insistIndex >= 0 && insistIndex + 1 < args.Length && int.TryParse(args[insistIndex + 1], out int parsedInsist)
                ? parsedInsist
                : 0;
            Environment.ExitCode = Protocol.XboxLinkLossTest.Run(Console.Out, gap, insist);
            return;
        }

        // "Retro87 --xbox-wake" sends the cmd 0x08 sub 0 wake handshake (a control frame) and
        // reports whether the keyboard came alive. With --plan it only prints the frame.
        if (args.Any(a => string.Equals(a, "--xbox-wake", StringComparison.OrdinalIgnoreCase)))
        {
            bool planOnly = args.Any(a => string.Equals(a, "--plan", StringComparison.OrdinalIgnoreCase));
            bool force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
            int watch = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
            if (watch >= 0)
            {
                int minutes = watch + 1 < args.Length && int.TryParse(args[watch + 1], out int parsed) && parsed > 0
                    ? parsed
                    : 10;
                Environment.ExitCode = Protocol.XboxWakeTest.Watch(Console.Out, minutes);
                return;
            }
            Environment.ExitCode = Protocol.XboxWakeTest.Run(Console.Out, planOnly, force);
            return;
        }

        // "Retro87 --xbox-restore <file.bin>" writes a whole 1532-byte configuration back.
        int restore = Array.FindIndex(args, a => string.Equals(a, "--xbox-restore", StringComparison.OrdinalIgnoreCase));
        if (restore >= 0)
        {
            if (restore + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: Retro87 --xbox-restore <file.bin>");
                Environment.ExitCode = 1;
                return;
            }
            Environment.ExitCode = Protocol.XboxWriteTest.Restore(Console.Out, args[restore + 1]);
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
  Retro87 --selftest               checks models/protocol without hardware
  Retro87 --report [file.toml]     writes a diagnostic report (read-only)
  Retro87 --make-icon [app.ico]    generates the app icon

Bench probes (read-only with --plan; writing only with explicit authorization):
  Retro87 --xbox-probe
  Retro87 --xbox-input-probe [--seconds N]
  Retro87 --xbox-super-listen [--seconds N]
  Retro87 --xbox-wake [--plan] [--force] [--watch N]
  Retro87 --xbox-link-test [--gap S] [--insist N]
  Retro87 --xbox-write-test [--plan]
  Retro87 --xbox-super-test [--plan]
  Retro87 --xbox-macro-test [--plan]
  Retro87 --xbox-config-test [--plan]
  Retro87 --xbox-media-test [--plan]
  Retro87 --xbox-ui-config-test [--plan]
  Retro87 --xbox-restore <file.bin>

Golden rule: reading is free; writing needs the lock in the UI and a backup.
This program never downloads or writes firmware.
""");
    }
}
