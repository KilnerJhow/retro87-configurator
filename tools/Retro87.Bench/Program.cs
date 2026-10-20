// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;

namespace Retro87;

/// <summary>
/// Entry point of the bench tool: the hardware-free self-test and the probes/tests that talk to a
/// real keyboard. The app itself ships none of this.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "--help" or "-h"))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        bool planOnly = Has("--plan");

        int IntOption(string name, int fallback)
        {
            int at = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int parsed) ? parsed : fallback;
        }

        if (Has("--selftest")) return SelfTest.Run(Console.Out) ? 0 : 1;
        if (Has("--xbox-probe")) return Protocol.XboxHardwareProbe.Run(Console.Out);
        if (Has("--xbox-input-probe")) return Protocol.XboxInputProbe.Run(Console.Out, Math.Clamp(IntOption("--seconds", 20), 3, 120));
        if (Has("--xbox-super-listen")) return Protocol.XboxSuperListenProbe.Run(Console.Out, Math.Clamp(IntOption("--seconds", 30), 3, 120));
        if (Has("--xbox-write-test")) return Protocol.XboxWriteTest.Run(Console.Out, planOnly);
        if (Has("--xbox-super-test")) return Protocol.XboxSuperTest.Run(Console.Out, planOnly);
        if (Has("--xbox-macro-test")) return Protocol.XboxMacroTest.Run(Console.Out, planOnly);
        if (Has("--xbox-config-test")) return Protocol.XboxConfigTest.Run(Console.Out, planOnly);
        if (Has("--xbox-media-test")) return Protocol.XboxMediaTest.Run(Console.Out, planOnly);
        if (Has("--xbox-ui-config-test")) return Protocol.XboxUiConfigTest.Run(Console.Out, planOnly);
        if (Has("--xbox-link-test")) return Protocol.XboxLinkLossTest.Run(Console.Out, IntOption("--gap", 30), IntOption("--insist", 0));

        if (Has("--xbox-wake"))
        {
            if (Has("--watch"))
            {
                int minutes = IntOption("--watch", 10);
                return Protocol.XboxWakeTest.Watch(Console.Out, minutes > 0 ? minutes : 10);
            }
            return Protocol.XboxWakeTest.Run(Console.Out, planOnly, Has("--force"));
        }

        int restore = Array.FindIndex(args, a => string.Equals(a, "--xbox-restore", StringComparison.OrdinalIgnoreCase));
        if (restore >= 0)
        {
            if (restore + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: Retro87.Bench --xbox-restore <file.bin>");
                return 1;
            }
            return Protocol.XboxWriteTest.Restore(Console.Out, args[restore + 1]);
        }

        Console.Error.WriteLine($"Unknown option: {args[0]}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Retro87.Bench — self-test and bench probes for the 8BitDo Retro 87 / Retro 87 "X"

Usage (dotnet run --project tools/Retro87.Bench -- <option>):
  --selftest                       checks models/protocol without hardware

Bench probes (read-only with --plan; writing only with explicit authorization):
  --xbox-probe
  --xbox-input-probe [--seconds N]
  --xbox-super-listen [--seconds N]
  --xbox-wake [--plan] [--force] [--watch N]
  --xbox-link-test [--gap S] [--insist N]
  --xbox-write-test [--plan]
  --xbox-super-test [--plan]
  --xbox-macro-test [--plan]
  --xbox-config-test [--plan]
  --xbox-media-test [--plan]
  --xbox-ui-config-test [--plan]
  --xbox-restore <file.bin>

Golden rule: reading is free; writing needs explicit permission and a backup.
""");
    }
}
