// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>One macro slot as read by <c>cmd 0x07</c> before the test (info + its step bytes).</summary>
/// <param name="Slot">Slot index the probe belongs to.</param>
/// <param name="Base">Device offset of the slot (<c>slot · 0x1000</c>).</param>
/// <param name="Info">The 48 info bytes, or empty when the device did not answer.</param>
/// <param name="Steps">The step bytes read from <c>Base + 0x40</c> (12 bytes at least when empty).</param>
internal sealed record XboxMacroSlotProbe(int Slot, uint Base, byte[] Info, byte[] Steps)
{
    /// <summary>True when the info record came back whole.</summary>
    public bool Readable => Info.Length == XboxWire.MacroInfoSize;

    /// <summary>Step count stored in the info record (<c>macroMaxStep</c>).</summary>
    public ushort MaxStep => Readable ? (ushort)(Info[XboxWire.MacroInfo.MaxStep] | (Info[XboxWire.MacroInfo.MaxStep + 1] << 8)) : (ushort)0;

    /// <summary>
    /// True when the slot carries no macro: the range the original app accepts is
    /// <c>1..1008</c>, and erased flash reads <c>0xFFFF</c> (doc §6).
    /// </summary>
    public bool IsEmpty => Readable && (MaxStep == 0 || MaxStep > XboxWire.MacroMaxSteps);

    /// <summary>Macro name decoded from the info record (UTF-16 big-endian).</summary>
    public string Name => Readable
        ? XboxJpProtocol.DecodeProfileName(Info, XboxWire.MacroInfo.Name, XboxWire.MacroInfo.NameBytes)
        : string.Empty;
}

/// <summary>Everything the macro hardware test would send, and what it must put back.</summary>
/// <param name="MappingIndex">Index of the mapping record whose key becomes the macro host.</param>
/// <param name="KeyCode">Physical key code of the host key.</param>
/// <param name="KeyName">Human-readable name of the host key.</param>
/// <param name="OriginalRecord">The 12 bytes of the host key's record before the test.</param>
/// <param name="MacroRecord">The 12 bytes making the host key a <c>MACRO</c> mapping.</param>
/// <param name="Slot">Macro slot the test uses.</param>
/// <param name="SlotBase">Device offset of that slot.</param>
/// <param name="OriginalInfo">The slot's 48 info bytes before the test.</param>
/// <param name="OriginalSteps">The slot's step bytes before the test.</param>
/// <param name="Info">The 48 info bytes to write.</param>
/// <param name="Steps">The step bytes to write (12 bytes: press A, 100 ms, release A).</param>
internal sealed record XboxMacroTestPlan(
    int MappingIndex, byte KeyCode, string KeyName,
    byte[] OriginalRecord, byte[] MacroRecord,
    int Slot, uint SlotBase,
    byte[] OriginalInfo, byte[] OriginalSteps,
    byte[] Info, byte[] Steps)
{
    /// <summary>Number of steps the plan writes.</summary>
    public int StepCount => Steps.Length / XboxWire.MacroStepSize;
}

/// <summary>
/// Hardware write test for a <b>macro</b> on the Retro 87 "X" (family Xbox): the first time
/// <c>cmd 0x05</c>/<c>0x06</c> are exercised on the real keyboard, <b>reversible</b> and with the
/// configuration backed up first.
/// </summary>
/// <remarks>
/// <para>
/// Run as <c>Retro87 --xbox-macro-test --plan</c> it only <b>reads</b>: it probes every macro slot
/// with <c>cmd 0x07</c>, builds the exact frames a real run would send and prints them, then exits
/// without writing. That is the step the project rule asks for before touching the device.
/// </para>
/// <para>
/// The real run, in order: (1) binds a <b>harmless key</b> (Scroll Lock, Pause, …) to a
/// <b>MACRO</b> mapping pointing at an <b>empty slot ≥ 1</b>, which is also the first hardware
/// evidence for the <c>mapping = slot × 0x1000</c> rule (only slot 0 was observed before);
/// (2) writes the macro (<c>cmd 0x05</c> start, <c>cmd 0x06</c> info at the base and the steps from
/// <c>base + 0x40</c>); (3) verifies it by reading the raw slot back with <c>cmd 0x07</c> and through
/// <c>ReadXboxConfig</c>; (4) <b>reverts</b> the mapping and writes the slot's original bytes back;
/// (5) compares the whole 1532-byte configuration with the backup and every macro slot with its
/// probe.
/// </para>
/// <para>
/// Slot 0 is left alone on purpose (on the reference unit it holds the official app's 83-step
/// "Test" macro), but every slot is probed and compared before and after, and any slot that
/// changed is restored byte for byte.
/// </para>
/// </remarks>
internal static class XboxMacroTest
{
    /// <summary>Slot the test prefers; slot 0 holds the official app's macro on the reference unit.</summary>
    private const int PreferredSlot = 1;

    /// <summary>Name written into the test slot (short; the field is 32 bytes of UTF-16BE).</summary>
    private const string TestMacroName = "RT87T";

    /// <summary>Delay of the middle step, in ms.</summary>
    private const ushort TestDelayMs = 100;

    /// <summary>Slots probed with <c>cmd 0x07</c> (the device model carries 10).</summary>
    private const int SlotCount = 10;

    /// <summary>
    /// Keys nobody depends on, in the order the test prefers them: Scroll Lock, Pause, Print
    /// Screen, Insert, Home and the F13..F16 block.
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

        // Same selection rule as the read-only probe and the mapping write test: several HID
        // collections of the same product exist and only the vendor one answers.
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
    /// Builds the test against a configuration already read and the macro slots probed with
    /// <c>cmd 0x07</c>. Pure: it decides the host key, the empty slot and the exact bytes, and
    /// never touches the device (so the selftest can drive it with the fake).
    /// </summary>
    internal static bool TryBuildPlan(XboxConfig config, IReadOnlyList<XboxMacroSlotProbe> probes,
        out XboxMacroTestPlan plan, out string error)
    {
        plan = null!;
        error = string.Empty;

        // ---------------------------------------------------------------- the harmless host key
        int index = -1;
        byte keyCode = 0;
        string keyName = string.Empty;
        byte[]? original = null;
        foreach ((byte candidate, string name) in Harmless)
        {
            if (config.Mappings.Count(m => m.KeyCode == candidate) != 1) continue;
            int position = Array.FindIndex(config.Mappings, m => m.KeyCode == candidate);
            if (position < 0) continue;
            if (config.Mappings[position].Mapping != candidate) continue;   // only keys still at their default
            int offset = XboxWire.MappingBase + position * XboxWire.MappingSize;
            if (config.Raw.Length < offset + XboxWire.MappingSize) continue;
            index = position;
            keyCode = candidate;
            keyName = name;
            original = config.Raw.AsSpan(offset, XboxWire.MappingSize).ToArray();
            break;
        }
        if (index < 0 || original is null)
        {
            error = "could not find a 'harmless' key in the default state (Scroll Lock, Pause, …).";
            return false;
        }

        // ---------------------------------------------------------------- an empty slot >= 1
        XboxMacroSlotProbe? chosen = null;
        foreach (XboxMacroSlotProbe probe in probes
                     .Where(p => p.Slot >= 1)
                     .OrderBy(p => p.Slot == PreferredSlot ? 0 : 1)
                     .ThenBy(p => p.Slot))
        {
            if (!probe.Readable) continue;          // no answer: never write where we cannot read back
            if (!probe.IsEmpty) continue;           // slot already carries a macro
            if (probe.Steps.Length < 12) continue;  // cannot promise a byte-for-byte restore
            chosen = probe;
            break;
        }
        if (chosen is null)
        {
            error = "no empty, readable macro slot (slot ≥ 1) to use; nothing was written.";
            return false;
        }

        // ---------------------------------------------------------------- the macro to write
        var steps = new List<XboxMacroStep>
        {
            new() { KeyState = 1, KeyCode = MappingIds.A_MAP },          // press A
            new() { KeyState = 3, Timer = TestDelayMs },                 // wait 100 ms
            new() { KeyState = 2, KeyCode = MappingIds.A_MAP },          // release A
        };
        byte[] stepBytes = XboxFromKeyboardConfig.BuildStepBytes(steps);
        var slotData = new MacroSlot { HostKey = keyCode, Name = TestMacroName, Cycles = 1, IntervalMs = 0 };
        byte[] info = XboxFromKeyboardConfig.BuildMacroInfo(slotData, keyCode, steps.Count, null);
        byte[] record = XboxFromKeyboardConfig.BuildRecord(keyCode, chosen.Base, XboxMappingType.Macro,
            original.AsSpan(1, 3));

        plan = new XboxMacroTestPlan(index, keyCode, keyName, original, record,
            chosen.Slot, chosen.Base, chosen.Info, chosen.Steps, info, stepBytes);
        return true;
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

        List<XboxMacroSlotProbe> probes = ProbeSlots(protocol, output);

        if (!TryBuildPlan(config, probes, out XboxMacroTestPlan plan, out string error))
        {
            output.WriteLine($"ABORTANDO: {error}");
            output.WriteLine("Nothing was written. The backup above is still valid.");
            return 1;
        }

        int mappingOffset = XboxWire.MappingBase + plan.MappingIndex * XboxWire.MappingSize;
        output.WriteLine();
        output.WriteLine($"Test key: {plan.KeyName} (keyCode 0x{plan.KeyCode:X2}), record index " +
                         $"{plan.MappingIndex}, offset {mappingOffset} (0x{mappingOffset:X})");
        output.WriteLine($"  now on the device  : {Hex(plan.OriginalRecord)}");
        output.WriteLine($"  becomes MACRO mapping: {Hex(plan.MacroRecord)}");
        output.WriteLine($"Macro slot: [{plan.Slot}] base 0x{plan.SlotBase:X} " +
                         "(tests the rule mapping = slot × 0x1000, so far seen only on slot 0)");
        output.WriteLine($"  info   (48 B) : {Hex(plan.Info)}");
        output.WriteLine($"  steps ({plan.StepCount})     : {Hex(plan.Steps)}   " +
                         "(1 = press A, 3 = wait 100 ms, 2 = release A)");
        output.WriteLine($"  info original      : {Hex(plan.OriginalInfo)}");
        output.WriteLine($"  original steps     : {Hex(plan.OriginalSteps)}");

        output.WriteLine();
        output.WriteLine("Frames a run would send (the batch sits between cmd 0x08 sub 0 and sub 1):");
        output.WriteLine($"  cmd 0x08 sub 0 (open)  : {Hex(Frame(XboxWire.Cmd.Session, XboxWire.SessionSubtype.Open, 0, ReadOnlySpan<byte>.Empty))}");
        output.WriteLine($"  cmd 0x01 (mapeia)     : {Hex(Frame(XboxWire.Cmd.Write, 0, (uint)mappingOffset, plan.MacroRecord))}");
        output.WriteLine($"  cmd 0x05 (start)      : {Hex(Frame(XboxWire.Cmd.MacroStart, 0, plan.SlotBase, ReadOnlySpan<byte>.Empty))}");
        output.WriteLine($"  cmd 0x06 (info)       : {Hex(Frame(XboxWire.Cmd.MacroData, 0, plan.SlotBase, plan.Info))}");
        output.WriteLine($"  cmd 0x06 (steps)      : {Hex(Frame(XboxWire.Cmd.MacroData, 0, plan.SlotBase + XboxWire.MacroDataOffset, plan.Steps))}");
        output.WriteLine("  (undo) cmd 0x01 puts the original record back; cmd 0x06 puts the slot's original bytes back.");

        if (planOnly)
        {
            output.WriteLine();
            output.WriteLine("PLAN MODE: nothing was written to the keyboard. Run without --plan to execute the test.");
            return 0;
        }

        // ---------------------------------------------------------------- writes (each verified)
        output.WriteLine();
        output.WriteLine("Step 1 — cmd 0x08 opens the session");
        if (!protocol.SendSession(XboxWire.SessionSubtype.Open))
        {
            output.WriteLine("  FAILED: the handshake did not answer; nothing was written.");
            return 3;
        }
        output.WriteLine("  ok");

        bool applied = false;
        string why1 = string.Empty, why2 = string.Empty;
        output.WriteLine($"Step 2 — cmd 0x01 assigning {plan.KeyName} to slot {plan.Slot} (MACRO)");
        if (protocol.WriteMapping(plan.MappingIndex, plan.MacroRecord) &&
            RecordIs(protocol, mappingOffset, plan.MacroRecord, out why1))
        {
            output.WriteLine($"  ok: {Hex(plan.MacroRecord)}");
            output.WriteLine("Step 3 — cmd 0x05 + 0x06 writing the macro (info + steps)");
            if (protocol.WriteMacroRegion(plan.SlotBase, plan.Info, plan.Steps, plan.StepCount) &&
                MacroRegionIs(protocol, plan.SlotBase, plan.Info, plan.Steps, out why2))
            {
                output.WriteLine($"  ok: info and {plan.StepCount} steps returned by the device, byte for byte");
                applied = true;
            }
            else
            {
                output.WriteLine($"  FAILED: {why2}");
            }
        }
        else
        {
            output.WriteLine($"  FAILED: {why1}");
        }

        bool readPathOk = false;
        if (applied)
        {
            output.WriteLine("Step 4 — re-reading the configuration (the key must point to the macro)");
            XboxConfig? afterWrite = protocol.ReadXboxConfig();
            readPathOk = afterWrite is not null && DescribeApplied(afterWrite, plan, output);
        }

        // ---------------------------------------------------------------- revert (always)
        output.WriteLine();
        output.WriteLine("Step 5 — undoing");
        string why3 = string.Empty, why4 = string.Empty;
        bool mappingReverted = protocol.WriteMapping(plan.MappingIndex, plan.OriginalRecord) &&
                               RecordIs(protocol, mappingOffset, plan.OriginalRecord, out why3);
        bool macroReverted = protocol.WriteMacroRegion(plan.SlotBase, plan.OriginalInfo, plan.OriginalSteps,
                                 plan.OriginalSteps.Length / XboxWire.MacroStepSize) &&
                             MacroRegionIs(protocol, plan.SlotBase, plan.OriginalInfo, plan.OriginalSteps, out why4);
        protocol.SendSession(XboxWire.SessionSubtype.Close);
        output.WriteLine(mappingReverted ? "  ok: the key's record is back to the original" : $"  FAILED: {why3}");
        output.WriteLine(macroReverted ? "  ok: the slot is back to the original bytes" : $"  FAILED: {why4}");

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

        bool slotsRestored = CompareAndRestoreSlots(protocol, probes, output);

        bool ok = applied && mappingReverted && macroReverted && diffs == 0 && slotsRestored;
        output.WriteLine();
        output.WriteLine(ok
            ? "RESULT: macro write validated on the device (cmd 0x05/0x06) and everything undone."
            : "RESULT: something was not as expected — see the lines above.");
        if (applied && !readPathOk)
            output.WriteLine("NOTE: the macro was confirmed by cmd 0x07, but re-reading the configuration " +
                             "did not show it as expected (possible defect in reading it through the config).");
        output.WriteLine($"Backup at {backup}");
        return ok ? 0 : 2;
    }

    /// <summary>
    /// Probes every macro slot with <c>cmd 0x07</c> and prints what each one holds. <b>Read-only</b>,
    /// so the selftest (and a human before a write) can see the whole macro region first.
    /// </summary>
    internal static List<XboxMacroSlotProbe> ProbeSlots(XboxJpProtocol protocol, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Macro slots (cmd 0x07, read-only):");
        var probes = new List<XboxMacroSlotProbe>();
        for (int slot = 0; slot < SlotCount; slot++)
        {
            uint baseOffset = XboxWire.MacroSlotBase(slot);
            bool gotInfo = protocol.ReadMacroRegion(baseOffset, XboxWire.MacroInfoSize, out byte[] info);
            int stepBytes = 12;
            if (gotInfo && info.Length == XboxWire.MacroInfoSize)
            {
                ushort maxStep = (ushort)(info[XboxWire.MacroInfo.MaxStep] | (info[XboxWire.MacroInfo.MaxStep + 1] << 8));
                if (maxStep is >= 1 and <= XboxWire.MacroMaxSteps) stepBytes = maxStep * XboxWire.MacroStepSize;
            }
            bool gotSteps = protocol.ReadMacroRegion(baseOffset + XboxWire.MacroDataOffset, stepBytes, out byte[] steps);

            var probe = new XboxMacroSlotProbe(slot, baseOffset,
                gotInfo ? info : Array.Empty<byte>(), gotSteps ? steps : Array.Empty<byte>());
            probes.Add(probe);

            string status = !probe.Readable
                ? "no answer (not used)"
                : probe.IsEmpty
                    ? "apagado (livre)"
                    : $"maxStep={probe.MaxStep} name=\"{probe.Name}\" steps read={probe.Steps.Length / XboxWire.MacroStepSize}";
            output.WriteLine($"  [{slot}] base=0x{probe.Base:X4}  {status}");
        }
        return probes;
    }

    /// <summary>True when the device returns the macro info and steps the test just sent.</summary>
    private static bool MacroRegionIs(XboxJpProtocol protocol, uint baseOffset,
        byte[] expectedInfo, byte[] expectedSteps, out string message)
    {
        message = string.Empty;
        if (!protocol.ReadMacroRegion(baseOffset, XboxWire.MacroInfoSize, out byte[] info) ||
            !info.AsSpan().SequenceEqual(expectedInfo))
        {
            message = $"the returned info ({Hex(info)}) is not the one sent ({Hex(expectedInfo)})";
            return false;
        }
        if (expectedSteps.Length > 0 &&
            (!protocol.ReadMacroRegion(baseOffset + XboxWire.MacroDataOffset, expectedSteps.Length, out byte[] steps) ||
             !steps.AsSpan().SequenceEqual(expectedSteps)))
        {
            message = $"the returned steps ({Hex(steps)}) are not the ones sent ({Hex(expectedSteps)})";
            return false;
        }
        return true;
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

    /// <summary>
    /// Prints what the ordinary read (<see cref="XboxJpProtocol.ReadXboxConfig"/>) sees after the
    /// write: the key as a <c>MACRO</c> mapping and the slot with its name, count and steps.
    /// </summary>
    private static bool DescribeApplied(XboxConfig after, XboxMacroTestPlan plan, TextWriter output)
    {
        bool ok = true;

        XboxMapping record = after.Mappings[plan.MappingIndex];
        bool mappingOk = record.Type == XboxMappingType.Macro && record.Mapping == plan.SlotBase;
        output.WriteLine(mappingOk
            ? $"  ok: record {plan.MappingIndex} = type MACRO, mapping 0x{record.Mapping:X}"
            : $"  FAILED: record {plan.MappingIndex} = type {record.Type}, mapping 0x{record.Mapping:X}");
        ok &= mappingOk;

        XboxMacroSlot? slot = after.Macros[plan.Slot];
        bool slotOk = slot is { MaxStep: 3, Repeat: 1, Interval: 0 }
                      && slot.Name == TestMacroName
                      && slot.Steps.Count == 3
                      && slot.Steps[0] is { KeyState: 1, KeyCode: MappingIds.A_MAP }
                      && slot.Steps[1].IsDelay && slot.Steps[1].Timer == TestDelayMs
                      && slot.Steps[2] is { KeyState: 2, KeyCode: MappingIds.A_MAP };
        output.WriteLine(slotOk
            ? $"  ok: slot {plan.Slot} read back (\"{slot!.Name}\", {slot.MaxStep} steps, " +
              "1=press A, 3=wait, 2=release A)"
            : "  FAILED: the slot did not come back through the configuration read as expected" +
              (slot is null ? " (came back null)" : $" (name=\"{slot.Name}\", maxStep={slot.MaxStep}, steps={slot.Steps.Count})"));
        ok &= slotOk;

        return ok;
    }

    /// <summary>
    /// Compares every macro slot with its pre-test probe and restores any that changed
    /// unexpectedly (the host slot should be back, and no other slot should have moved).
    /// </summary>
    private static bool CompareAndRestoreSlots(XboxJpProtocol protocol,
        IReadOnlyList<XboxMacroSlotProbe> probes, TextWriter output)
    {
        bool allSame = true;
        foreach (XboxMacroSlotProbe before in probes)
        {
            if (!before.Readable) continue;

            bool same = protocol.ReadMacroRegion(before.Base, XboxWire.MacroInfoSize, out byte[] info) &&
                        info.AsSpan().SequenceEqual(before.Info) &&
                        (before.Steps.Length == 0 ||
                         (protocol.ReadMacroRegion(before.Base + XboxWire.MacroDataOffset, before.Steps.Length, out byte[] steps) &&
                          steps.AsSpan().SequenceEqual(before.Steps)));
            if (same) continue;

            allSame = false;
            output.WriteLine($"  WARNING: slot [{before.Slot}] did not return to its original state; restoring.");
            int stepCount = before.Steps.Length / XboxWire.MacroStepSize;
            bool restored = protocol.WriteMacroRegion(before.Base, before.Info, before.Steps, stepCount) &&
                            protocol.ReadMacroRegion(before.Base, XboxWire.MacroInfoSize, out byte[] check) &&
                            check.AsSpan().SequenceEqual(before.Info);
            output.WriteLine(restored
                ? $"  ok: slot [{before.Slot}] restored"
                : $"  FAILED: could not restore slot [{before.Slot}]");
        }
        if (allSame) output.WriteLine("  macro slots: all in their original state");
        return allSame;
    }

    private static byte[] Frame(byte cmd, byte subtype, uint offset, ReadOnlySpan<byte> data)
        => XboxJpProtocol.BuildRequest(cmd, subtype, offset, data, data.Length == 0 ? 0 : null);

    private static string Hex(byte[] bytes) => bytes.Length == 0 ? "(empty)" : Convert.ToHexString(bytes);

    private static int CountDiffs(byte[] a, byte[] b)
    {
        int diffs = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != b[i]) diffs++;
        return diffs;
    }
}
