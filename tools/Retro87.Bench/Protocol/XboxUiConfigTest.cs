// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>Everything the UI-config write test would send, and what it must put back.</summary>
/// <param name="OriginalVolume">Value of <c>volume_lv@0x5C9</c> as read.</param>
/// <param name="TestVolume">Temporary value the test writes (inside the official app's 1..5).</param>
/// <param name="OriginalFastSetting">Value of <c>fast_setting@0x5C8</c> as read.</param>
/// <param name="TestFastSetting">Temporary value: the original with the "Disable Alt + F4" bit toggled.</param>
internal sealed record XboxUiConfigTestPlan(
    byte OriginalVolume, byte TestVolume, byte OriginalFastSetting, byte TestFastSetting)
{
    /// <summary>
    /// Temporary volume: 3 unless the device already holds 3, in which case 2 — both inside the
    /// <b>1..5</b> range of the official app's Volume slider.
    /// </summary>
    public static byte AlternateVolume(byte current) => current == 3 ? (byte)2 : (byte)3;

    /// <summary>Toggles the "Disable Alt + F4" bit, leaving every other bit as the device had it.</summary>
    public static byte Toggle(byte fastSetting)
        => (byte)(fastSetting ^ XboxWire.FastSetting.DisableAltF4);

    /// <summary>Human-readable reading of the Quick Setup bits.</summary>
    public static string Describe(byte fastSetting)
    {
        var on = new List<string>(3);
        if ((fastSetting & XboxWire.FastSetting.DisableWindowsKey) != 0) on.Add("Windows key disabled");
        if ((fastSetting & XboxWire.FastSetting.DisableAltTab) != 0) on.Add("Alt + Tab disabled");
        if ((fastSetting & XboxWire.FastSetting.DisableAltF4) != 0) on.Add("Alt + F4 disabled");
        string known = on.Count == 0 ? "nothing disabled" : string.Join(", ", on);
        byte other = (byte)(fastSetting & ~(XboxWire.FastSetting.DisableWindowsKey
                                            | XboxWire.FastSetting.DisableAltTab
                                            | XboxWire.FastSetting.DisableAltF4));
        return other == 0 ? known : $"{known}; bits altos: 0x{other:X2}";
    }
}

/// <summary>
/// Hardware write test for the two configuration fields the <b>Config tab</b> exposes on the
/// Retro 87 "X" — the <b>volume</b> (<c>volume_lv@0x5C9</c>, 1–5) and the <b>Quick Setup</b> bits
/// (<c>fast_setting@0x5C8</c>) — written through the <b>UI path</b>
/// (<see cref="XboxJpProtocol.WriteConfig"/>), which is what the *Save* button does. Reversible,
/// with a backup taken first.
/// </summary>
/// <remarks>
/// <para>
/// Run as <c>Retro87 --xbox-ui-config-test --plan</c> it only <b>reads</b>: it saves a backup,
/// prints the current values and the exact frames a real run would send, then exits without
/// writing.
/// </para>
/// <para>
/// The real run, in order: (1) the volume is written through the UI path and checked by reading
/// the byte back and by the projection the tab shows; (2) the same for the <c>fast_setting</c>
/// byte with one Quick Setup bit toggled; (3) <b>both are reverted</b> to the bytes read at the
/// start, again through the UI path; (4) the whole 1532-byte configuration is compared with the
/// backup and the projection must show the original values.
/// </para>
/// <para>
/// The bytes go with a <b>single <c>cmd 0x01</c></b> each — the primitive validated on the device
/// in C3 — and only the field the test changed is sent, because the plan is built by diffing
/// against the configuration just read (this also proves the diff path of the tab works).
/// </para>
/// </remarks>
internal static class XboxUiConfigTest
{
    public static int Run(TextWriter output, bool planOnly)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        // Same selection rule as the other write tests: only the vendor collection answers.
        // The wake handshake is on here because the keyboard may now be on the 2.4 G dongle, the
        // setup the documented sleep was observed with.
        XboxJpProtocol? protocol = null;
        DeviceInfo? device = null;
        string? collectionPath = null;
        foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;

            var attempt = new XboxJpProtocol(new HidSharpTransport(hid))
            {
                Trace = null,
                WakeOnOpen = true,
            };
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
            output.WriteLine(device.Pid == XboxWire.PidUsb
                ? "Transport: USB cable (0x2028), with the wake handshake on."
                : "Transport: 2.4 G dongle (0x202E), with the wake handshake on — it is what the " +
                  "device sleeps.");
            return RunOnOpenDevice(output, protocol, device, planOnly);
        }
    }

    /// <summary>
    /// Builds the test against a configuration already read. Pure: it only decides the temporary
    /// values, so the selftest can drive it with the fake.
    /// </summary>
    internal static bool TryBuildPlan(XboxConfig config, out XboxUiConfigTestPlan plan, out string error)
    {
        plan = null!;
        error = string.Empty;

        if (config.Raw.Length < XboxWire.VolumeLevelOffset + 1 ||
            config.Raw.Length < XboxWire.FastSettingOffset + 1)
        {
            error = "the configuration read is too short to hold the volume (0x5C9) and the fast_setting (0x5C8).";
            return false;
        }

        byte volume = config.Raw[XboxWire.VolumeLevelOffset];
        byte fastSetting = config.Raw[XboxWire.FastSettingOffset];
        plan = new XboxUiConfigTestPlan(volume, XboxUiConfigTestPlan.AlternateVolume(volume),
            fastSetting, XboxUiConfigTestPlan.Toggle(fastSetting));
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

        if (!TryBuildPlan(config, out XboxUiConfigTestPlan plan, out string error))
        {
            output.WriteLine($"ABORTANDO: {error}");
            output.WriteLine("Nothing was written. The backup above is still valid.");
            return 1;
        }

        output.WriteLine();
        output.WriteLine($"Volume   — volume_lv@{XboxWire.VolumeLevelOffset} (0x{XboxWire.VolumeLevelOffset:X}): " +
                         $"hoje 0x{plan.OriginalVolume:X2} ({plan.OriginalVolume}) → teste 0x{plan.TestVolume:X2} ({plan.TestVolume})");
        output.WriteLine($"Quick Setup — fast_setting@{XboxWire.FastSettingOffset} (0x{XboxWire.FastSettingOffset:X}): " +
                         $"hoje 0x{plan.OriginalFastSetting:X2} ({XboxUiConfigTestPlan.Describe(plan.OriginalFastSetting)})");
        output.WriteLine($"              becomes 0x{plan.TestFastSetting:X2} ({XboxUiConfigTestPlan.Describe(plan.TestFastSetting)})");
        output.WriteLine($"The UI shows: volume {config.VolumeLevel}, quick setup " +
                         $"{XboxUiConfigTestPlan.Describe(config.FastSetting)}");

        output.WriteLine();
        output.WriteLine("Frames a run would send (the batch sits between cmd 0x08 sub 0 and sub 1):");
        output.WriteLine($"  cmd 0x08 sub 0 (open)  : {Hex(Frame(XboxWire.Cmd.Session, XboxWire.SessionSubtype.Open, 0, ReadOnlySpan<byte>.Empty))}");
        output.WriteLine($"  cmd 0x01 (volume)     : {Hex(Frame(XboxWire.Cmd.Write, 0, XboxWire.VolumeLevelOffset, new[] { plan.TestVolume }))}");
        output.WriteLine($"  cmd 0x01 (quick setup): {Hex(Frame(XboxWire.Cmd.Write, 0, XboxWire.FastSettingOffset, new[] { plan.TestFastSetting }))}");
        output.WriteLine("  (undo) cmd 0x01 puts the original volume and fast_setting back.");

        if (planOnly)
        {
            output.WriteLine();
            output.WriteLine("PLAN MODE: nothing was written to the keyboard. Run without --plan to execute the test.");
            return 0;
        }

        // ---------------------------------------------------------------- writes (each verified)
        bool volumeOk = ApplyVolumeThroughUi(protocol,
            XboxWire.VolumeLevelOffset, plan.TestVolume, "Step 1", output);
        bool quickOk = ApplyFastSettingThroughUi(protocol,
            XboxWire.FastSettingOffset, plan.TestFastSetting, "Step 2", output);

        // ---------------------------------------------------------------- revert (always)
        output.WriteLine();
        output.WriteLine("Step 3 — undoing through the UI path (original volume and fast_setting)");
        bool volumeReverted = ApplyVolumeThroughUi(protocol,
            XboxWire.VolumeLevelOffset, plan.OriginalVolume, "volume", output);
        bool quickReverted = ApplyFastSettingThroughUi(protocol,
            XboxWire.FastSettingOffset, plan.OriginalFastSetting, "quick setup", output);

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
        bool projectionOk = after.VolumeLevel == plan.OriginalVolume &&
                            after.FastSetting == plan.OriginalFastSetting;
        output.WriteLine(projectionOk
            ? $"  ok: a UI mostraria volume {after.VolumeLevel} e quick setup " +
              $"({XboxUiConfigTestPlan.Describe(after.FastSetting)}), as at the start"
            : $"  FAILED: a UI mostraria volume {after.VolumeLevel} e fast_setting " +
              $"0x{after.FastSetting:X2}, expected {plan.OriginalVolume} e 0x{plan.OriginalFastSetting:X2}");

        bool ok = volumeOk && quickOk && volumeReverted && quickReverted && diffs == 0 && projectionOk;
        output.WriteLine();
        output.WriteLine(ok
            ? "RESULT: volume and Quick Setup written through the UI path (Config tab) and undone."
            : "RESULT: something was not as expected — see the lines above.");
        output.WriteLine($"Backup at {backup}");
        return ok ? 0 : 2;
    }

    /// <summary>Writes the volume through <see cref="XboxJpProtocol.WriteConfig"/> and checks the byte and the projection.</summary>
    private static bool ApplyVolumeThroughUi(XboxJpProtocol protocol, int offset, byte value,
        string label, TextWriter output)
        => ApplyThroughUi(protocol, offset, value, label, output, (ui, v) => ui.XboxVolumeLevel = v);

    /// <summary>Writes <c>fast_setting</c> through <see cref="XboxJpProtocol.WriteConfig"/> and checks the byte and the projection.</summary>
    private static bool ApplyFastSettingThroughUi(XboxJpProtocol protocol, int offset, byte value,
        string label, TextWriter output)
        => ApplyThroughUi(protocol, offset, value, label, output, (ui, v) => ui.XboxFastSetting = v);

    private static bool ApplyThroughUi(XboxJpProtocol protocol, int offset, byte value,
        string label, TextWriter output, Action<KeyboardConfig, byte> set)
    {
        string field = offset == XboxWire.VolumeLevelOffset ? "volume" : "fast_setting";
        output.WriteLine();
        output.WriteLine($"{label} — cmd 0x01 writing {field} = 0x{value:X2} through the UI path (WriteConfig)");

        KeyboardConfig ui = protocol.ReadConfig();
        set(ui, value);
        if (!protocol.WriteConfig(ui))
        {
            foreach (string warning in protocol.LastWriteWarnings) output.WriteLine($"      ! {warning}");
            output.WriteLine("  FAILED: WriteConfig did not confirm");
            return false;
        }

        if (!ByteIs(protocol, offset, value, out string why))
        {
            output.WriteLine($"  FAILED: {why}");
            return false;
        }
        output.WriteLine($"  ok: the device returns 0x{value:X2} at {offset} (0x{offset:X})");

        XboxConfig? read = protocol.ReadXboxConfig();
        if (read is null)
        {
            output.WriteLine("  FAILED: could not re-read the configuration");
            return false;
        }
        KeyboardConfig projected = XboxToKeyboardConfig.Convert(read);
        byte shown = offset == XboxWire.VolumeLevelOffset ? projected.XboxVolumeLevel : projected.XboxFastSetting;
        bool projectionOk = shown == value;
        output.WriteLine(projectionOk
            ? $"  ok: the configuration read shows {field} = 0x{value:X2}"
            : $"  FAILED: the configuration read shows {field} = 0x{shown:X2}, expected 0x{value:X2}");
        return projectionOk;
    }

    /// <summary>Reads one configuration byte back and compares it with what we meant to write.</summary>
    private static bool ByteIs(XboxJpProtocol protocol, int offset, byte expected, out string message)
    {
        message = string.Empty;
        if (!protocol.ReadRegion((uint)offset, 1, out byte[] actual) || actual.Length != 1 || actual[0] != expected)
        {
            message = $"the device returned 0x{(actual.Length > 0 ? actual[0] : 0):X2}, expected 0x{expected:X2}";
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
