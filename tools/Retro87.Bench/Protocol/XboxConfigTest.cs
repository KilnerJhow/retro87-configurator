// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>Everything the configuration write test would send, and what it must put back.</summary>
/// <param name="OriginalName">Profile name the keyboard reports now.</param>
/// <param name="TestName">Temporary name the test writes.</param>
/// <param name="OriginalNameField">The 32 bytes of the name field as read (UTF-16BE).</param>
/// <param name="TestNameField">The 32 bytes the test writes for <see cref="TestName"/>.</param>
/// <param name="FieldOffset">Offset of the loose byte under test (<c>volume_lv@0x5C9</c>).</param>
/// <param name="OriginalField">Value of that byte now.</param>
/// <param name="TestField">Temporary value the test writes.</param>
internal sealed record XboxConfigTestPlan(
    string OriginalName, string TestName, byte[] OriginalNameField, byte[] TestNameField,
    int FieldOffset, byte OriginalField, byte TestField)
{
    /// <summary>Suggested value for <see cref="TestField"/> given what the device holds.</summary>
    /// <remarks>
    /// <c>volume_lv</c> is the byte the official app was captured flipping: it wrote <c>0x02</c>
    /// over the <c>0x01</c> the device held and put <c>0x01</c> back. Keeping the test inside
    /// <c>1..2</c> stays on the values the app itself used.
    /// </remarks>
    public static byte AlternateField(byte current) => current == 2 ? (byte)1 : (byte)2;
}

/// <summary>
/// Hardware write test for the two configuration writes the C1/C2 did not cover: the
/// <b>profile name</b> (<c>cmd 0x01</c>, 32 bytes UTF-16BE at offset 4) and a <b>loose byte</b>
/// (<c>volume_lv@0x5C9</c>) — the byte the official app was captured flipping 1↔2. Reversible,
/// with a backup taken first.
/// </summary>
/// <remarks>
/// <para>
/// Run as <c>Retro87 --xbox-config-test --plan</c> it only <b>reads</b>: it saves a backup, prints
/// the name field and the byte as they are, and the exact frames a real run would send. That is
/// the step the project rule asks for before touching the device.
/// </para>
/// <para>
/// The real run, in order: (1) writes the loose byte with its <b>own value</b> (a no-op, proving the
/// 1-byte write is accepted without changing anything); (2) writes the temporary value and checks it
/// came back; (3) writes a temporary profile name and checks it decoded back; (4) <b>reverts</b> both
/// to the bytes read at the start; (5) compares the whole 1532-byte configuration with the backup.
/// </para>
/// </remarks>
internal static class XboxConfigTest
{
    /// <summary>Temporary profile name; short, ASCII and obviously not the user's.</summary>
    private const string TestName = "RT87 TEST";

    /// <summary>Loose byte exercised: <c>volume_lv</c> (documented and captured).</summary>
    private const int TestFieldOffset = XboxWire.VolumeLevelOffset;

    public static int Run(TextWriter output, bool planOnly)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        // Same selection rule as the read-only probe and the other write tests: several HID
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
    /// Builds the test against a configuration already read. Pure: it decides the temporary name
    /// and byte and never touches the device, so the selftest can drive it with the fake.
    /// </summary>
    internal static bool TryBuildPlan(XboxConfig config, out XboxConfigTestPlan plan, out string error)
    {
        plan = null!;
        error = string.Empty;

        if (config.Raw.Length < XboxWire.ProfileNameOffset + XboxWire.ProfileNameBytes)
        {
            error = "the configuration read is too short to hold the profile name.";
            return false;
        }
        if (config.Raw.Length < TestFieldOffset + 1)
        {
            error = "the configuration read is too short to hold the byte at 0x5C9.";
            return false;
        }

        string originalName = config.ProfileName;
        string testName = originalName == TestName ? "RT87 TST" : TestName;
        byte[] originalField = config.Raw.AsSpan(XboxWire.ProfileNameOffset, XboxWire.ProfileNameBytes).ToArray();

        plan = new XboxConfigTestPlan(
            originalName, testName, originalField,
            XboxFromKeyboardConfig.EncodeNameBigEndian(testName, XboxWire.ProfileNameBytes),
            TestFieldOffset, config.Raw[TestFieldOffset], XboxConfigTestPlan.AlternateField(config.Raw[TestFieldOffset]));
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

        if (!TryBuildPlan(config, out XboxConfigTestPlan plan, out string error))
        {
            output.WriteLine($"ABORTANDO: {error}");
            output.WriteLine("Nothing was written. The backup above is still valid.");
            return 1;
        }

        output.WriteLine();
        output.WriteLine($"Profile name: now \"{plan.OriginalName}\" → test \"{plan.TestName}\"");
        output.WriteLine($"  field (32 B, offset {XboxWire.ProfileNameOffset}): {Hex(plan.OriginalNameField)}");
        output.WriteLine($"  vira                                            : {Hex(plan.TestNameField)}");
        output.WriteLine($"Byte solto: {TestFieldOffset} (0x{TestFieldOffset:X}, volume_lv)  " +
                         $"hoje 0x{plan.OriginalField:X2} → teste 0x{plan.TestField:X2}");

        output.WriteLine();
        output.WriteLine("Frames a run would send (the batch sits between cmd 0x08 sub 0 and sub 1):");
        output.WriteLine($"  cmd 0x08 sub 0 (open)  : {Hex(Frame(XboxWire.Cmd.Session, XboxWire.SessionSubtype.Open, 0, ReadOnlySpan<byte>.Empty))}");
        output.WriteLine($"  cmd 0x01 (volume no-op): {Hex(Frame(XboxWire.Cmd.Write, 0, TestFieldOffset, new[] { plan.OriginalField }))}");
        output.WriteLine($"  cmd 0x01 (volume)     : {Hex(Frame(XboxWire.Cmd.Write, 0, TestFieldOffset, new[] { plan.TestField }))}");
        output.WriteLine($"  cmd 0x01 (name)       : {Hex(Frame(XboxWire.Cmd.Write, 0, XboxWire.ProfileNameOffset, plan.TestNameField))}");
        output.WriteLine("  (undo) cmd 0x01 puts the original name (32 B) and the original byte back.");

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

        bool volumeApplied = false, nameApplied = false;
        string why = string.Empty;

        output.WriteLine($"Step 2 — cmd 0x01 writing byte {TestFieldOffset} with its own value (no-op)");
        if (protocol.WriteField(TestFieldOffset, plan.OriginalField) &&
            RegionIs(protocol, TestFieldOffset, new[] { plan.OriginalField }, out why))
        {
            output.WriteLine($"  ok: the device confirms and returns 0x{plan.OriginalField:X2}");
        }
        else
        {
            output.WriteLine($"  FAILED: {why}");
        }

        output.WriteLine($"Step 3 — cmd 0x01 writing 0x{plan.TestField:X2} at {TestFieldOffset} (volume_lv)");
        if (protocol.WriteField(TestFieldOffset, plan.TestField) &&
            RegionIs(protocol, TestFieldOffset, new[] { plan.TestField }, out why))
        {
            volumeApplied = true;
            output.WriteLine($"  ok: re-read returns 0x{plan.TestField:X2}");
        }
        else
        {
            output.WriteLine($"  FAILED: {why}");
        }

        output.WriteLine($"Step 4 — cmd 0x01 writing the name \"{plan.TestName}\" (32 B UTF-16BE at offset 4)");
        if (protocol.WriteProfileName(plan.TestName) &&
            RegionIs(protocol, XboxWire.ProfileNameOffset, plan.TestNameField, out why))
        {
            nameApplied = true;
            output.WriteLine($"  ok: re-read returns {Hex(plan.TestNameField)}");
        }
        else
        {
            output.WriteLine($"  FAILED: {why}");
        }

        bool readPathOk = false;
        if (volumeApplied || nameApplied)
        {
            output.WriteLine("Step 5 — re-reading the configuration (the app sees what was written)");
            XboxConfig? afterWrite = protocol.ReadXboxConfig();
            readPathOk = afterWrite is not null && DescribeApplied(afterWrite, plan, output);
        }

        // ---------------------------------------------------------------- revert (always)
        output.WriteLine();
        output.WriteLine("Step 6 — undoing");
        string whyName = string.Empty, whyField = string.Empty;
        bool nameReverted = protocol.WriteProfileName(plan.OriginalName) &&
                            NameIs(protocol, plan.OriginalName, out whyName);
        bool fieldReverted = protocol.WriteField(TestFieldOffset, plan.OriginalField) &&
                             RegionIs(protocol, TestFieldOffset, new[] { plan.OriginalField }, out whyField);
        protocol.SendSession(XboxWire.SessionSubtype.Close);
        output.WriteLine(nameReverted ? "  ok: the name is back to the original" : $"  FAILED: {whyName}");
        output.WriteLine(fieldReverted ? "  ok: the byte is back to the original" : $"  FAILED: {whyField}");

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

        bool ok = volumeApplied && nameApplied && nameReverted && fieldReverted && diffs == 0;
        output.WriteLine();
        output.WriteLine(ok
            ? "RESULT: profile name (cmd 0x01, 32 B) and loose byte validated on the device and undone."
            : "RESULT: something was not as expected — see the lines above.");
        if ((volumeApplied || nameApplied) && !readPathOk)
            output.WriteLine("NOTE: the writes were confirmed by the region, but re-reading the " +
                             "configuration did not show them as expected.");
        output.WriteLine($"Backup at {backup}");
        return ok ? 0 : 2;
    }

    /// <summary>
    /// Prints what the ordinary read (<see cref="XboxJpProtocol.ReadXboxConfig"/>) sees after the
    /// writes: the temporary name and the temporary byte.
    /// </summary>
    private static bool DescribeApplied(XboxConfig after, XboxConfigTestPlan plan, TextWriter output)
    {
        bool nameOk = after.ProfileName == plan.TestName;
        output.WriteLine(nameOk
            ? $"  ok: the profile reads back as \"{after.ProfileName}\""
            : $"  FAILED: the profile reads as \"{after.ProfileName}\", expected \"{plan.TestName}\"");
        bool fieldOk = after.Raw.Length > plan.FieldOffset && after.Raw[plan.FieldOffset] == plan.TestField;
        output.WriteLine(fieldOk
            ? $"  ok: byte {plan.FieldOffset} reads back as 0x{plan.TestField:X2}"
            : $"  FAILED: byte {plan.FieldOffset} reads as 0x{(after.Raw.Length > plan.FieldOffset ? after.Raw[plan.FieldOffset] : 0):X2}, " +
              $"esperava 0x{plan.TestField:X2}");
        return nameOk && fieldOk;
    }

    /// <summary>Reads a region of the configuration back and compares it with what we wrote.</summary>
    private static bool RegionIs(XboxJpProtocol protocol, int offset, byte[] expected, out string message)
    {
        message = string.Empty;
        if (!protocol.ReadRegion((uint)offset, expected.Length, out byte[] actual) ||
            !actual.AsSpan().SequenceEqual(expected))
        {
            message = $"the device returned {Hex(actual)}, expected {Hex(expected)}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Checks the profile name by <b>decoding</b> it (the field is NUL padded, so comparing the
    /// decoded string is what matters).
    /// </summary>
    private static bool NameIs(XboxJpProtocol protocol, string expected, out string message)
    {
        message = string.Empty;
        if (!protocol.ReadRegion(XboxWire.ProfileNameOffset, XboxWire.ProfileNameBytes, out byte[] field))
        {
            message = "could not re-read the name field";
            return false;
        }
        string actual = XboxJpProtocol.DecodeProfileName(field, 0, XboxWire.ProfileNameBytes);
        if (actual != expected)
        {
            message = $"the name read is \"{actual}\", expected \"{expected}\"";
            return false;
        }
        return true;
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
