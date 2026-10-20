// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>One media or mouse target the test writes (id, display name, wire type and UI kind).</summary>
internal sealed record XboxMediaTarget(byte Id, string Name, XboxMappingType Type, MappingTargetKind Kind);

/// <summary>Everything the media/mouse write test would send, and what it must put back.</summary>
/// <param name="MappingIndex">Index of the mapping record of the test key.</param>
/// <param name="KeyCode">Physical key code of the test key.</param>
/// <param name="KeyName">Human-readable name of the test key.</param>
/// <param name="Position">Position of the key in the UI layout (the UI path needs it).</param>
/// <param name="OriginalRecord">The 12 bytes of the key's record before the test.</param>
/// <param name="Padding">The unused <c>+1..+3</c> bytes of the record (kept verbatim).</param>
/// <param name="Media">The media (<c>MKEY</c>) target to write.</param>
/// <param name="Mouse">The mouse (<c>MOUSE</c>) target to write.</param>
internal sealed record XboxMediaTestPlan(
    int MappingIndex, byte KeyCode, string KeyName, int Position,
    byte[] OriginalRecord, byte[] Padding,
    XboxMediaTarget Media, XboxMediaTarget Mouse)
{
    /// <summary>The 12 bytes that make the test key a mapping to <paramref name="target"/>.</summary>
    public byte[] RecordFor(XboxMediaTarget target)
        => XboxFromKeyboardConfig.BuildRecord(KeyCode, target.Id, target.Type, Padding);
}

/// <summary>
/// Hardware write test for <b>media</b> (<c>MKEY</c>) and <b>mouse</b> (<c>MOUSE</c>) targets on
/// the Retro 87 "X" — the two target categories the port had never written, and the ones the id
/// table exists for. Reversible, with a backup taken first.
/// </summary>
/// <remarks>
/// <para>
/// Run as <c>Retro87 --xbox-media-test --plan</c> it only <b>reads</b>: it saves a backup, prints
/// the record and the exact frames a real run would send, then exits without writing.
/// </para>
/// <para>
/// The real run, in order: (1) a <b>no-op</b> write of the key's own record (<c>cmd 0x01</c>,
/// proving nothing changes); (2) a remap to a media target through the <b>UI path</b>
/// (<see cref="XboxJpProtocol.WriteConfig"/> with the target category), verified by reading the
/// record back; (3) the same for a mouse target; (4) the <b>revert</b> to the original record;
/// (5) a comparison of the whole configuration with the backup.
/// </para>
/// <para>
/// The targets are chosen from the ported id table with <b>no ambiguity</b>: media prefers
/// <c>Play</c> (<c>0xCD</c>, the id the official app was captured writing) and mouse prefers
/// <c>Left_Click</c> (<c>0xE8</c>); if either were missing the test falls back to the first
/// unambiguous id of that type. Ids the device uses with two meanings (<c>0xE9</c>, <c>0xEA</c>,
/// <c>0x6F</c>, <c>0x70</c>, <c>0xE2</c>) are deliberately <b>not</b> used here.
/// </para>
/// </remarks>
internal static class XboxMediaTest
{
    /// <summary>
    /// Keys nobody depends on, in the order the test prefers them. Unlike the other tests this one
    /// also needs the key to have a position in the UI layout, because it writes through
    /// <see cref="XboxJpProtocol.WriteConfig"/>.
    /// </summary>
    private static readonly (byte KeyCode, string Name)[] Harmless =
    {
        (0x47, "Scroll Lock"), (0x48, "Pause"), (0x46, "Print Screen"), (0x49, "Insert"),
        (0x4A, "Home"), (0x68, "F13"), (0x69, "F14"), (0x6A, "F15"), (0x6B, "F16"),
    };

    /// <summary>Media id the test prefers (<c>Play</c>), proven on the wire by the first capture.</summary>
    private const byte PreferredMediaId = 0xCD;

    /// <summary>Mouse id the test prefers (<c>Left_Click</c>), the first unambiguous mouse button.</summary>
    private const byte PreferredMouseId = 0xE8;

    public static int Run(TextWriter output, bool planOnly)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        // Same selection rule as the other write tests: only the vendor collection answers.
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

    /// <summary>
    /// Builds the test against a configuration already read. Pure: it chooses the key and the
    /// targets and never touches the device, so the selftest can drive it with the fake.
    /// </summary>
    internal static bool TryBuildPlan(XboxConfig config, out XboxMediaTestPlan plan, out string error)
    {
        plan = null!;
        error = string.Empty;

        int index = -1;
        int position = -1;
        byte keyCode = 0;
        string keyName = string.Empty;
        byte[]? original = null;
        foreach ((byte candidate, string name) in Harmless)
        {
            if (config.Mappings.Count(m => m.KeyCode == candidate) != 1) continue;
            int candidateIndex = Array.FindIndex(config.Mappings, m => m.KeyCode == candidate);
            if (candidateIndex < 0) continue;
            if (config.Mappings[candidateIndex].Mapping != candidate) continue;   // only keys still at their default

            int layoutPosition = KeyboardLayout.PositionOfKeyId(candidate);
            if (layoutPosition < 0) continue;   // the UI path cannot address it

            int offset = XboxWire.MappingBase + candidateIndex * XboxWire.MappingSize;
            if (config.Raw.Length < offset + XboxWire.MappingSize) continue;

            index = candidateIndex;
            position = layoutPosition;
            keyCode = candidate;
            keyName = name;
            original = config.Raw.AsSpan(offset, XboxWire.MappingSize).ToArray();
            break;
        }
        if (index < 0 || original is null)
        {
            error = "could not find a 'harmless' key in the default state that also has a position in the " +
                    "UI table (Scroll Lock, Pause, …).";
            return false;
        }

        XboxMediaTarget? media = FindTarget(PreferredMediaId, XboxMappingType.MKey);
        XboxMediaTarget? mouse = FindTarget(PreferredMouseId, XboxMappingType.Mouse);
        if (media is null || mouse is null)
        {
            error = "the id table has no unambiguous media and/or mouse target.";
            return false;
        }

        plan = new XboxMediaTestPlan(index, keyCode, keyName, position, original,
            original.AsSpan(1, 3).ToArray(), media, mouse);
        return true;
    }

    /// <summary>
    /// The <paramref name="preferred"/> id when it is that exact type, or the first unambiguous id
    /// of the type otherwise (id order), or null when the table has none.
    /// </summary>
    private static XboxMediaTarget? FindTarget(byte preferred, XboxMappingType type)
    {
        if (XboxIdTable.TryGet(preferred, out XboxIdEntry entry) && entry.Type == type)
            return new XboxMediaTarget(entry.Id, entry.Name, entry.Type, entry.Kind);

        foreach (XboxIdEntry candidate in XboxIdTable.All.OrderBy(e => e.Id))
        {
            if (candidate.Type != type) continue;
            if (XboxIdTable.IsAmbiguous(candidate.Id)) continue;
            return new XboxMediaTarget(candidate.Id, candidate.Name, candidate.Type, candidate.Kind);
        }
        return null;
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
            output.WriteLine("Close the official software (and the 8BitDo panel) and run again.");
            return 1;
        }

        if (!TryBuildPlan(config, out XboxMediaTestPlan plan, out string error))
        {
            output.WriteLine($"ABORTANDO: {error}");
            output.WriteLine("Nothing was written. The backup above is still valid.");
            return 1;
        }

        int mappingOffset = XboxWire.MappingBase + plan.MappingIndex * XboxWire.MappingSize;
        output.WriteLine();
        output.WriteLine($"Test key: {plan.KeyName} (keyCode 0x{plan.KeyCode:X2}), record index " +
                         $"{plan.MappingIndex}, offset {mappingOffset} (0x{mappingOffset:X}), UI position {plan.Position}");
        output.WriteLine($"  now on the device       : {Hex(plan.OriginalRecord)}");
        output.WriteLine($"  becomes media ({plan.Media.Name} 0x{plan.Media.Id:X2}, type {plan.Media.Type}) : " +
                         $"{Hex(plan.RecordFor(plan.Media))}");
        output.WriteLine($"  becomes mouse ({plan.Mouse.Name} 0x{plan.Mouse.Id:X2}, type {plan.Mouse.Type}) : " +
                         $"{Hex(plan.RecordFor(plan.Mouse))}");

        output.WriteLine();
        output.WriteLine("Frames a run would send (media and mouse go through the UI path, with the cmd 0x08 pair):");
        output.WriteLine($"  cmd 0x01 (no-op) : {Hex(Frame(mappingOffset, plan.OriginalRecord))}");
        output.WriteLine($"  cmd 0x01 (media) : {Hex(Frame(mappingOffset, plan.RecordFor(plan.Media)))}");
        output.WriteLine($"  cmd 0x01 (mouse) : {Hex(Frame(mappingOffset, plan.RecordFor(plan.Mouse)))}");
        output.WriteLine($"  (undo) cmd 0x01 puts the original record back ({Hex(plan.OriginalRecord)}).");

        if (planOnly)
        {
            output.WriteLine();
            output.WriteLine("PLAN MODE: nothing was written to the keyboard. Run without --plan to execute the test.");
            return 0;
        }

        // ---------------------------------------------------------------- writes (each verified)
        output.WriteLine();
        output.WriteLine($"Step 1 — cmd 0x01 writing the record back as it is (no-op)");
        string whyNoOp = string.Empty, whyRevert = string.Empty;
        bool noOpOk = protocol.WriteMapping(plan.MappingIndex, plan.OriginalRecord) &&
                      RecordIs(protocol, mappingOffset, plan.OriginalRecord, out whyNoOp);
        output.WriteLine(noOpOk ? "  ok: the device confirms and returns the same bytes" : $"  FAILED: {whyNoOp}");

        bool mediaOk = ApplyThroughUi(protocol, plan, plan.Media, output);
        bool mouseOk = ApplyThroughUi(protocol, plan, plan.Mouse, output);

        // ---------------------------------------------------------------- revert (always)
        output.WriteLine();
        output.WriteLine("Step 4 — undoing (cmd 0x01 puts the original record back)");
        bool reverted = protocol.WriteMapping(plan.MappingIndex, plan.OriginalRecord) &&
                        RecordIs(protocol, mappingOffset, plan.OriginalRecord, out whyRevert);
        output.WriteLine(reverted ? "  ok: the record is back to the original" : $"  FAILED: {whyRevert}");

        // ---------------------------------------------------------------- final comparison
        output.WriteLine();
        output.WriteLine("Final check");
        XboxConfig? after = protocol.ReadXboxConfig();
        if (after is null)
        {
            output.WriteLine("  FAILED: could not re-read the configuration.");
            return 3;
        }
        int diffs = CountDiffs(config.Raw, after.Raw);
        output.WriteLine(diffs == 0
            ? $"  configuration: IDENTICAL to the backup ({config.Raw.Length} bytes, 0 differences)"
            : $"  configuration: {diffs} byte(s) differ from the backup — restore with the file {backup}");

        bool ok = noOpOk && mediaOk && mouseOk && reverted && diffs == 0;
        output.WriteLine();
        output.WriteLine(ok
            ? "RESULT: media (MKEY) and mouse (MOUSE) targets validated on the device through the UI path and undone."
            : "RESULT: something was not as expected — see the lines above.");
        output.WriteLine($"Backup at {backup}");
        return ok ? 0 : 2;
    }

    /// <summary>
    /// Remaps the test key to <paramref name="target"/> through the UI path
    /// (<see cref="XboxJpProtocol.WriteConfig"/>, with the target category) and checks the record
    /// and the projections the UI would show.
    /// </summary>
    private static bool ApplyThroughUi(XboxJpProtocol protocol, XboxMediaTestPlan plan,
        XboxMediaTarget target, TextWriter output)
    {
        int mappingOffset = XboxWire.MappingBase + plan.MappingIndex * XboxWire.MappingSize;
        byte[] expected = plan.RecordFor(target);

        output.WriteLine();
        output.WriteLine($"Step — cmd 0x01 assigning {plan.KeyName} to {target.Name} " +
                         $"(0x{target.Id:X2}, type {target.Type}) through the UI path (WriteConfig)");
        KeyboardConfig ui = protocol.ReadConfig();
        ui.BoardKeys[plan.Position] = target.Id;
        protocol.TargetKinds = new Dictionary<int, MappingTargetKind> { [plan.Position] = target.Kind };
        try
        {
            if (!protocol.WriteConfig(ui))
            {
                foreach (string warning in protocol.LastWriteWarnings) output.WriteLine($"      ! {warning}");
                output.WriteLine("  FAILED: WriteConfig did not confirm");
                return false;
            }
        }
        finally
        {
            protocol.TargetKinds = null;
        }

        if (!RecordIs(protocol, mappingOffset, expected, out string why))
        {
            output.WriteLine($"  FAILED: {why}");
            return false;
        }
        output.WriteLine($"  ok: the record reached the device: {Hex(expected)}");

        XboxConfig? read = protocol.ReadXboxConfig();
        if (read is null)
        {
            output.WriteLine("  FAILED: could not re-read the configuration");
            return false;
        }
        XboxMapping record = read.Mappings[plan.MappingIndex];
        bool typeOk = record.Type == target.Type && record.MappingId == target.Id;
        output.WriteLine(typeOk
            ? $"  ok: releitura = type {record.Type}, mapping 0x{record.MappingId:X2} ({record.TargetName})"
            : $"  FAILED: releitura = type {record.Type}, mapping 0x{record.MappingId:X2}, " +
              $"esperava type {target.Type}, 0x{target.Id:X2}");

        KeyboardConfig projected = XboxToKeyboardConfig.Convert(read);
        bool projectionOk = projected.BoardKeys[plan.Position] == target.Id;
        output.WriteLine(projectionOk
            ? $"  ok: the UI would show {target.Name} on that key"
            : $"  FAILED: a UI mostraria 0x{projected.BoardKeys[plan.Position]:X2}, esperava 0x{target.Id:X2}");

        return typeOk && projectionOk;
    }

    /// <summary>Reads a 12-byte mapping record back and compares it with what we meant to write.</summary>
    private static bool RecordIs(XboxJpProtocol protocol, int offset, byte[] expected, out string message)
    {
        message = string.Empty;
        if (!protocol.ReadRegion((uint)offset, XboxWire.MappingSize, out byte[] actual) ||
            !actual.AsSpan().SequenceEqual(expected))
        {
            message = $"the device returned {Hex(actual)}, expected {Hex(expected)}";
            return false;
        }
        return true;
    }

    private static byte[] Frame(int offset, byte[] record)
        => XboxJpProtocol.BuildRequest(XboxWire.Cmd.Write, 0, (uint)offset, record);

    private static string Hex(byte[] bytes) => bytes.Length == 0 ? "(empty)" : Convert.ToHexString(bytes);

    private static int CountDiffs(byte[] a, byte[] b)
    {
        int diffs = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != b[i]) diffs++;
        return diffs;
    }
}
