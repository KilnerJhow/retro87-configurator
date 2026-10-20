// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>
/// Reversible write test for the ten Super Buttons of the Retro 87 "X".
/// </summary>
/// <remarks>
/// <para>
/// The ten Super Buttons share the same <c>keyCode</c> (<c>0xF3</c>) on the wire, so they can only
/// be addressed by record index (<see cref="SuperButtons.WireIndices"/>). This probe writes each
/// one to a harmless keyboard target, reads the record back, restores the original bytes and reads
/// it back again. At the end it also exercises the same edit through <c>WriteConfig</c> — the path
/// the UI's Save button uses — on Super Button 1.
/// </para>
/// <para>
/// Run as <c>Retro87 --xbox-super-test --plan</c> it only reads: it opens the keyboard, saves a
/// backup and prints the records and frames, then exits without writing. The real run takes its
/// own backup and must end with the 1532-byte configuration identical to that backup.
/// </para>
/// </remarks>
internal static class XboxSuperTest
{
    /// <summary>
    /// Harmless targets used in rotation. None of them is a Super Button record or a media id,
    /// so the test never changes what a button does for longer than the round trip.
    /// </summary>
    private static readonly (byte KeyCode, string Name)[] Harmless =
    {
        (0x48, "Pause"), (0x68, "F13"), (0x69, "F14"), (0x6A, "F15"), (0x6B, "F16"),
        (0x47, "Scroll Lock"), (0x49, "Insert"), (0x4A, "Home"), (0x4B, "Page Up"),
    };

    public static int Run(TextWriter output, bool planOnly)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        // Same selection rule as the other probes: several HID collections of the same product
        // exist and only the vendor one answers the version command.
        XboxJpProtocol? protocol = null;
        DeviceInfo? device = null;
        string? collectionPath = null;
        foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;

            var attempt = new XboxJpProtocol(new HidSharpTransport(hid)) { Trace = null };
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

        XboxConfig? again = protocol.ReadXboxConfig();
        int instability = again is null ? -1 : CountDiffs(config.Raw, again.Raw);
        output.WriteLine(instability switch
        {
            0 => "Stable read (two identical reads).",
            < 0 => "Could not re-read to check stability.",
            _ => $"WARNING: two consecutive reads differ in {instability} byte(s) — another program " +
                 "(the official software on Windows?) is writing to the keyboard right now.",
        });
        if (instability != 0)
        {
            output.WriteLine("ABORTING: without a stable read I will not write to the device.");
            return 1;
        }

        var originals = new byte[SuperButtons.Count][];
        var remapped = new byte[SuperButtons.Count][];
        var targets = new (byte KeyCode, string Name)[SuperButtons.Count];

        for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal++)
        {
            int index = SuperButtons.WireIndexOf(ordinal);
            int offset = XboxWire.MappingBase + index * XboxWire.MappingSize;
            byte[] original = config.Raw.AsSpan(offset, XboxWire.MappingSize).ToArray();
            (byte keyCode, string name) = PickTarget(original);
            byte[] changed = XboxFromKeyboardConfig.BuildRecord(
                SuperButtons.WireKeyCode, keyCode, XboxMappingType.KeyCode, original.AsSpan(1, 3));

            originals[ordinal] = original;
            remapped[ordinal] = changed;
            targets[ordinal] = (keyCode, name);

            output.WriteLine(
                $"[{ordinal,2}] {SuperButtons.NameOf(ordinal)} ({SuperButtons.WireNames[ordinal]}, " +
                $"index {index,3}, offset {offset,5}): now {Convert.ToHexString(original)} -> " +
                $"{Convert.ToHexString(changed)}  ({name})");
            if (planOnly)
            {
                output.WriteLine($"     frame: {Convert.ToHexString(XboxJpProtocol.BuildRequest(
                    XboxWire.Cmd.Write, 0, (uint)offset, changed))}");
            }
        }

        // What the UI's Save button would plan for the same edit (Super Button 1).
        int uiPosition = SuperButtons.PositionOf(0);
        KeyboardConfig ui = protocol.ReadConfig();
        byte uiBefore = ui.BoardKeys[uiPosition];
        byte uiTarget = targets[0].KeyCode;
        ui.BoardKeys[uiPosition] = uiTarget;
        XboxWritePlan uiPlan = XboxFromKeyboardConfig.Plan(ui, config);
        output.WriteLine();
        output.WriteLine($"UI plan (WriteConfig) for Super Button 1 -> {targets[0].Name}: " +
                         $"{uiPlan.Mappings.Count} record(s), {uiPlan.Macros.Count} macro(s), " +
                         $"avisos={uiPlan.Warnings.Count}");

        if (planOnly)
        {
            output.WriteLine();
            output.WriteLine("PLAN MODE: nothing was written to the keyboard. Run without --plan to execute the test.");
            return 0;
        }

        // ---------------------------------------------------------------- record-level writes
        for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal++)
        {
            int index = SuperButtons.WireIndexOf(ordinal);
            int offset = XboxWire.MappingBase + index * XboxWire.MappingSize;
            output.WriteLine();
            output.WriteLine($"Step {ordinal + 1}/10 — {SuperButtons.NameOf(ordinal)} " +
                             $"({SuperButtons.WireNames[ordinal]}, index {index}) -> {targets[ordinal].Name}");

            if (!protocol.WriteMapping(index, remapped[ordinal]))
                return Fail(output, protocol, index, originals[ordinal], "the write failed");
            if (!Same(protocol, offset, remapped[ordinal], out string step))
                return Fail(output, protocol, index, originals[ordinal], step);
            output.WriteLine($"  ok: {Convert.ToHexString(remapped[ordinal])}");

            if (!protocol.WriteMapping(index, originals[ordinal]))
                return Fail(output, protocol, index, originals[ordinal], "the revert failed");
            if (!Same(protocol, offset, originals[ordinal], out step))
                return Fail(output, protocol, index, originals[ordinal], step);
            output.WriteLine("  ok: restored");
        }

        // ---------------------------------------------------------------- UI path (WriteConfig)
        output.WriteLine();
        output.WriteLine("Final step — UI path (WriteConfig) for Super Button 1");
        protocol.TargetKinds = null;
        if (!protocol.WriteConfig(ui))
        {
            foreach (string warning in protocol.LastWriteWarnings) output.WriteLine($"      ! {warning}");
            return Fail(output, protocol, SuperButtons.WireIndexOf(0), originals[0], "WriteConfig falhou");
        }
        if (!Same(protocol, XboxWire.MappingBase + SuperButtons.WireIndexOf(0) * XboxWire.MappingSize,
                  remapped[0], out string uiStep))
            return Fail(output, protocol, SuperButtons.WireIndexOf(0), originals[0], uiStep);
        output.WriteLine("  ok: the remap reached the device through the same path as the Save button");

        ui.BoardKeys[uiPosition] = uiBefore;
        if (!protocol.WriteConfig(ui, force: true))
            return Fail(output, protocol, SuperButtons.WireIndexOf(0), originals[0], "WriteConfig (reverter) falhou");
        if (!Same(protocol, XboxWire.MappingBase + SuperButtons.WireIndexOf(0) * XboxWire.MappingSize,
                  originals[0], out uiStep))
            return Fail(output, protocol, SuperButtons.WireIndexOf(0), originals[0], uiStep);
        output.WriteLine("  ok: restored");

        // ---------------------------------------------------------------- final comparison
        XboxConfig? after = protocol.ReadXboxConfig();
        if (after is null) return Fail(output, protocol, -1, Array.Empty<byte>(), "could not re-read the configuration");
        int diffs = CountDiffs(config.Raw, after.Raw);
        output.WriteLine();
        output.WriteLine(diffs == 0
            ? $"RESULT: the 10 Super Buttons were written and reverted; the device is " +
              $"IDENTICAL to the backup ({config.Raw.Length} bytes, 0 differences)."
            : $"RESULT: {diffs} byte(s) differ from the backup — restore with the file {backup}");
        output.WriteLine($"Backup at {backup}");
        return diffs == 0 ? 0 : 2;
    }

    /// <summary>First harmless target that is not already the record's mapping.</summary>
    private static (byte KeyCode, string Name) PickTarget(byte[] record)
    {
        uint current = (uint)(record[4] | record[5] << 8 | record[6] << 16 | record[7] << 24);
        foreach ((byte keyCode, string name) in Harmless)
            if (keyCode != current) return (keyCode, name);
        return Harmless[0];
    }

    private static int CountDiffs(byte[] a, byte[] b)
    {
        int diffs = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != b[i]) diffs++;
        return diffs;
    }

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

    private static int Fail(TextWriter output, XboxJpProtocol protocol, int index, byte[] original, string what)
    {
        output.WriteLine($"  FAILED: {what}.");
        if (index >= 0 && original.Length == XboxWire.MappingSize)
        {
            output.WriteLine("  trying to restore the original record...");
            if (protocol.WriteMapping(index, original))
                output.WriteLine("  restore sent; check with --xbox-probe or restore the backup.");
            else
                output.WriteLine("  the restore also failed; use the backup saved before the test.");
        }
        output.WriteLine("  The backup was saved before writing: you can restore from it if anything looks wrong.");
        return 3;
    }
}
