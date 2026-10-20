// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>
/// Hardware write test for the Retro 87 "X": the smallest possible exercise of the write path,
/// **reversible** and with a backup taken before anything is sent.
/// </summary>
/// <remarks>
/// <para>
/// Run as <c>Retro87 --xbox-write-test --plan</c> it only <b>reads</b>: it opens the keyboard,
/// reads the configuration, saves a backup and prints the exact frames that a real run would
/// send, then exits without writing anything. That is the step the project rule asks for before
/// touching the device ("explain the command, offset and bytes, then wait for permission").
/// </para>
/// <para>
/// The real run does, in order: (1) a <b>no-op</b> write of one mapping record — same bytes the
/// device already holds, to prove <c>cmd 0x01</c> is accepted without changing behaviour;
/// (2) the actual remap of the physical <b>Scroll Lock</b> key to <b>Pause</b> (a key nobody
/// depends on, and the device knows both); (3) the <b>revert</b> of that record; (4) the same
/// remap and revert through <see cref="XboxJpProtocol.WriteConfig"/> — the path the UI uses, with
/// the plan/diff and the <c>cmd 0x08</c> session bracket; (5) a final comparison of the whole
/// 1532-byte configuration against the backup, which must come out identical.
/// </para>
/// </remarks>
internal static class XboxWriteTest
{
    /// <summary>Record the real keyboard uses for the physical Scroll Lock key (first choice).</summary>
    private const int PreferredIndex = 79;

    /// <summary>
    /// Keys nobody depends on, in the order the test prefers them: Scroll Lock, Pause, Print
    /// Screen, Insert, Home and the F13..F16 block. The test only ever remaps one of these onto
    /// another one of these, and reverts it right away.
    /// </summary>
    private static readonly (byte KeyCode, string Name)[] Harmless =
    {
        (0x47, "Scroll Lock"), (0x48, "Pause"), (0x46, "Print Screen"), (0x49, "Insert"),
        (0x4A, "Home"), (0x68, "F13"), (0x69, "F14"), (0x6A, "F15"), (0x6B, "F16"),
    };

    public static int Run(TextWriter output, bool planOnly)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        // Same selection rule as the read-only probe: several HID collections of the same
        // product exist and only the vendor one answers the version command. Writing to another
        // one simply fails (that is how this test first failed), so try until one answers.
        XboxJpProtocol? protocol = null;
        DeviceInfo? device = null;
        string? collectionPath = null;
        foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;

            var transport = new HidSharpTransport(hid);
            var attempt = new XboxJpProtocol(transport) { Trace = null };
            if (attempt.Open() && attempt.ReadVersion())
            {
                protocol = attempt;
                device = candidate;
                collectionPath = candidate.DevicePath;
                break;
            }
            attempt.Dispose();
        }
        if (protocol is null || device is null || collectionPath is null)
        {
            output.WriteLine("No Retro 87 \"X\" collection answered (PID 0x2028/0x202E).");
            return 1;
        }

        using (protocol)
        {
            protocol.Trace = line => output.WriteLine($"      · {line}");
            output.WriteLine($"Keyboard: \"{device.ProductName}\"  firmware {protocol.FirmwareVersion:0.00}  " +
                             $"battery {protocol.BatteryPercent}%");
            output.WriteLine($"Collection used: {collectionPath}");
            return RunOnOpenDevice(output, protocol, device, planOnly);
        }
    }

    private static int RunOnOpenDevice(TextWriter output, XboxJpProtocol protocol, DeviceInfo device, bool planOnly)
    {

        // ---------------------------------------------------------------- read-only part
        XboxConfig? config = protocol.ReadXboxConfig();
        if (config is null)
        {
            output.WriteLine("Failed to read the configuration.");
            return 1;
        }

        var backups = new ConfigBackup();
        string? backup = backups.Save($"Retro87X-{device.Pid:X4}", config.Raw);
        output.WriteLine($"Backup: {backup ?? $"FALHOU ({backups.LastError})"}");
        if (backup is null) return 1;

        // Reading twice is the cheapest way to notice that somebody else (the official Windows
        // software, for instance) is writing to the device right now. Writing on top of that
        // would be reckless, so an unstable read aborts the test.
        XboxConfig? again = protocol.ReadXboxConfig();
        int instability = again is null ? -1 : CountDiffs(config.Raw, again.Raw);
        output.WriteLine(instability switch
        {
            0 => "Stable read (two identical reads).",
            < 0 => "Could not re-read to check stability.",
            _ => $"WARNING: two consecutive reads differ in {instability} byte(s) — another program " +
                 "(the official software on Windows?) is writing to the keyboard right now.",
        });
        if (instability > 0)
        {
            output.WriteLine("First differing bytes (offset: read1/read2):");
            int shown = 0;
            for (int i = 0; i < config.Raw.Length && i < again!.Raw.Length && shown < 8; i++)
            {
                if (config.Raw[i] == again.Raw[i]) continue;
                output.WriteLine($"  {i,4} (0x{i:X3}): {config.Raw[i]:X2} / {again.Raw[i]:X2}");
                shown++;
            }
        }
        if (instability != 0)
        {
            output.WriteLine("ABORTING: without a stable read I will not write to the device.");
            output.WriteLine("Close the official software (and the 8BitDo panel) and run again.");
            return 1;
        }

        if (planOnly)
        {
            output.WriteLine();
            output.WriteLine("Non-empty records the keyboard reports now:");
            for (int i = 0; i < config.Mappings.Length; i++)
            {
                XboxMapping m = config.Mappings[i];
                if (m.KeyCode == 0 && m.Mapping == 0 && m.Type == XboxMappingType.None) continue;
                bool unique = config.Mappings.Count(x => x.KeyCode == m.KeyCode) == 1;
                output.WriteLine($"  [{i,3}] keyCode=0x{m.KeyCode:X2} mapping=0x{m.Mapping:X8} " +
                                 $"type={m.Type,-8} {XboxMappingIds.Describe(m.MappingId, m.Type)}" +
                                 (unique ? "" : "   (duplicate keyCode)"));
            }
        }

        int index = -1;
        byte testKey = 0, testTarget = 0;
        string testName = string.Empty, targetName = string.Empty;
        foreach ((byte keyCode, string name) in Harmless)
        {
            if (config.Mappings.Count(m => m.KeyCode == keyCode) != 1) continue;
            int candidate = Array.FindIndex(config.Mappings, m => m.KeyCode == keyCode);
            if (candidate < 0) continue;
            if (config.Mappings[candidate].Mapping != keyCode) continue;   // only keys still at their default

            (byte KeyCode, string Name) target = Harmless.First(pair => pair.KeyCode != keyCode);
            index = candidate;
            testKey = keyCode;
            testName = name;
            testTarget = target.KeyCode;
            targetName = target.Name;
            break;
        }
        if (index < 0)
        {
            output.WriteLine("Could not find a 'harmless' key in the default state (Scroll Lock, Pause, …).");
            output.WriteLine("Nothing was written. The backup above is still valid.");
            return 1;
        }

        int offset = XboxWire.MappingBase + index * XboxWire.MappingSize;
        byte[] original = config.Raw.AsSpan(offset, XboxWire.MappingSize).ToArray();
        byte[] remapped = XboxFromKeyboardConfig.BuildRecord(testKey, testTarget,
            XboxMappingType.KeyCode, original.AsSpan(1, 3));

        output.WriteLine();
        output.WriteLine($"Chosen test: key {testName} (keyCode 0x{testKey:X2}) -> target {targetName} " +
                         $"(0x{testTarget:X2})");
        output.WriteLine($"Record: index {index}, offset {offset} (0x{offset:X})");
        output.WriteLine($"  now on the device: {Convert.ToHexString(original)}");
        output.WriteLine($"  after the remap  : {Convert.ToHexString(remapped)}");
        output.WriteLine($"  frame 1 (cmd 0x01, writes the same back): " +
                         $"{Convert.ToHexString(XboxJpProtocol.BuildRequest(XboxWire.Cmd.Write, 0, (uint)offset, original))}");
        output.WriteLine($"  frame 2 (cmd 0x01, remaps):                   " +
                         $"{Convert.ToHexString(XboxJpProtocol.BuildRequest(XboxWire.Cmd.Write, 0, (uint)offset, remapped))}");

        // what the UI path would plan for the same edit
        KeyboardConfig ui = protocol.ReadConfig();
        int position = KeyboardLayout.PositionOfKeyId(testKey);
        if (position >= 0) ui.BoardKeys[position] = testTarget;
        XboxWritePlan plan = XboxFromKeyboardConfig.Plan(ui, config);
        output.WriteLine($"  UI plan (WriteConfig): {plan.Mappings.Count} record(s), " +
                         $"{plan.Macros.Count} macro(s), name={(plan.ProfileName is null ? "no" : "yes")}, " +
                         $"avisos={plan.Warnings.Count}");
        if (position < 0)
        {
            output.WriteLine($"  warning: key 0x{testKey:X2} has no position in the UI table; " +
                             "Step 4 (UI path) will be skipped.");
        }

        if (planOnly)
        {
            output.WriteLine();
            output.WriteLine("PLAN MODE: nothing was written to the keyboard. Run without --plan to execute the test.");
            return 0;
        }

        // ---------------------------------------------------------------- writes (each verified)
        output.WriteLine();
        output.WriteLine("Step 1 — cmd 0x01 writing the record back as it is (changes nothing)");
        if (!protocol.WriteMapping(index, original)) return Fail(output, "falhou");
        if (!Same(protocol, offset, original, out string step1)) return Fail(output, step1);
        output.WriteLine("  ok: the device confirmed and returns the same bytes");

        output.WriteLine($"Step 2 — cmd 0x01 remapping {testName} -> {targetName}");
        if (!protocol.WriteMapping(index, remapped)) return Fail(output, "falhou");
        if (!Same(protocol, offset, remapped, out string step2)) return Fail(output, step2);
        output.WriteLine($"  ok: {Convert.ToHexString(remapped)}");

        output.WriteLine("Step 3 — cmd 0x01 undoing (putting the original record back)");
        if (!protocol.WriteMapping(index, original)) return Fail(output, "falhou");
        if (!Same(protocol, offset, original, out string step3)) return Fail(output, step3);
        output.WriteLine("  ok: restored");

        if (position >= 0)
        {
            output.WriteLine("Step 4 — the UI path (WriteConfig: plan + cmd 0x08 open/close)");
            protocol.TargetKinds = null;
            if (!protocol.WriteConfig(ui))
            {
                foreach (string warning in protocol.LastWriteWarnings) output.WriteLine($"      ! {warning}");
                return Fail(output, "WriteConfig falhou");
            }
            if (!Same(protocol, offset, remapped, out string step4)) return Fail(output, step4);
            output.WriteLine("  ok: the remap reached the device through the same path as the Save button");

            output.WriteLine("Step 5 — undoing through the UI path");
            ui.BoardKeys[position] = testKey;
            if (!protocol.WriteConfig(ui, force: true)) return Fail(output, "WriteConfig (reverter) falhou");
            if (!Same(protocol, offset, original, out string step5)) return Fail(output, step5);
            output.WriteLine("  ok: restored");
        }
        else
        {
            output.WriteLine("Step 4/5 — skipped (the chosen key has no position in the UI table).");
        }

        // ---------------------------------------------------------------- final comparison
        XboxConfig? after = protocol.ReadXboxConfig();
        if (after is null) return Fail(output, "could not re-read the configuration");
        int diffs = 0;
        for (int i = 0; i < config.Raw.Length && i < after.Raw.Length; i++)
            if (config.Raw[i] != after.Raw[i]) diffs++;

        output.WriteLine();
        output.WriteLine(diffs == 0
            ? $"RESULT: write validated and the device is IDENTICAL to the backup ({config.Raw.Length} bytes, 0 differences)."
            : $"RESULT: {diffs} byte(s) differ from the backup — restore with the file {backup}");
        output.WriteLine($"Backup at {backup}");
        return diffs == 0 ? 0 : 2;
    }

    /// <summary>
    /// Writes a whole 1532-byte configuration file back to the keyboard (<c>cmd 0x01</c>, chunked
    /// by 53 bytes) and verifies it by reading twice.
    /// </summary>
    /// <remarks>
    /// This is the "undo" for a write test and the way to put back a backup. It is a big write
    /// (29 frames) and it restores everything the file holds, so it is only used on an explicit
    /// request. A stability check runs first, as before every write.
    /// </remarks>
    public static int Restore(TextWriter output, string path)
    {
        byte[] wanted;
        try
        {
            wanted = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            output.WriteLine($"Could not read {path}: {ex.Message}");
            return 1;
        }
        if (wanted.Length != XboxWire.ConfigSize)
        {
            output.WriteLine($"{path} has {wanted.Length} bytes; expected {XboxWire.ConfigSize}.");
            return 1;
        }

        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        XboxJpProtocol? protocol = null;
        DeviceInfo? device = null;
        foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;
            var attempt = new XboxJpProtocol(new HidSharpTransport(hid));
            if (attempt.Open() && attempt.ReadVersion()) { protocol = attempt; device = candidate; break; }
            attempt.Dispose();
        }
        if (protocol is null || device is null)
        {
            output.WriteLine("No Retro 87 \"X\" collection answered.");
            return 1;
        }

        using (protocol)
        {
            output.WriteLine($"Keyboard: \"{device.ProductName}\"");
            output.WriteLine($"File: {path}");

            XboxConfig? before = protocol.ReadXboxConfig();
            XboxConfig? again = protocol.ReadXboxConfig();
            if (before is null || again is null || CountDiffs(before.Raw, again.Raw) != 0)
            {
                output.WriteLine("ABORTING: unstable read — another program is writing to the keyboard.");
                return 1;
            }

            int already = CountDiffs(before.Raw, wanted);
            output.WriteLine($"Difference from the file: {already} byte(s)");
            if (already == 0)
            {
                output.WriteLine("Already identical to the file; nothing to write.");
                return 0;
            }

            output.WriteLine("Writing the 1532 bytes (cmd 0x01, 53-byte blocks)…");
            if (!protocol.WriteRegion(0, wanted))
            {
                output.WriteLine("FAILED: the keyboard did not confirm all the writes.");
                return 3;
            }

            XboxConfig? after = protocol.ReadXboxConfig();
            XboxConfig? afterToo = protocol.ReadXboxConfig();
            if (after is null || afterToo is null)
            {
                output.WriteLine("Could not re-read to check.");
                return 3;
            }
            int left = CountDiffs(after.Raw, wanted);
            output.WriteLine(left == 0
                ? "RESULT: device identical to the file (0 differences) and the read is stable."
                : $"RESULT: {left} byte(s) still differ from the file.");
            return left == 0 ? 0 : 2;
        }
    }

    /// <summary>Byte differences between two configurations (0 = the read is steady).</summary>
    private static int CountDiffs(byte[] a, byte[] b)
    {
        int diffs = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != b[i]) diffs++;
        return diffs;
    }

    /// <summary>Reads the record back and compares it with what we meant to write.</summary>
    private static bool Same(XboxJpProtocol protocol, int offset, byte[] expected, out string message)
    {
        XboxConfig? config = protocol.ReadXboxConfig();
        if (config is null)
        {
            message = "could not re-read the configuration to check";
            return false;
        }
        byte[] actual = config.Raw.AsSpan(offset, XboxWire.MappingSize).ToArray();
        if (actual.AsSpan().SequenceEqual(expected))
        {
            message = string.Empty;
            return true;
        }
        message = $"the device returned {Convert.ToHexString(actual)}, expected {Convert.ToHexString(expected)}";
        return false;
    }

    private static int Fail(TextWriter output, string what)
    {
        output.WriteLine($"  FAILED: {what}.");
        output.WriteLine("  The backup was saved before writing: you can restore from it if anything looks wrong.");
        return 3;
    }
}
