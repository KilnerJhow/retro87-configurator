// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>
/// Exercises the <b>wake</b> handshake on the real keyboard: the <c>cmd 0x08 sub 0</c> frame the
/// official app sends first (<c>_ReportXbox(0)</c>) and that the port had never sent on its own.
/// </summary>
/// <remarks>
/// <para>
/// This one is a <b>control frame</b>, not a configuration write: nothing in the 1532-byte
/// configuration is touched. It exists because the Retro 87 "X" goes idle and stops answering, and
/// the app needs a way to nudge it back (the dongle with no keyboard powered answers
/// <c>02 04 C0 …</c>, the error the port now fails fast on).
/// </para>
/// <para>
/// Run as <c>Retro87 --xbox-wake --plan</c> it only prints the frame; without <c>--plan</c> it
/// sends it and reports the device's answers (add <c>--force</c> to send the handshake even to an
/// already-awake keyboard, to observe the ACK).
/// </para>
/// </remarks>
internal static class XboxWakeTest
{
    /// <summary>
    /// Watches the keyboard for the idle <b>sleep</b>: pings it every 30 s while holding the device
    /// open (the condition the app lives in) and, when it stops answering, sends the wake and
    /// reports whether it came back. Exits on the first recovery, or when <paramref name="minutes"/>
    /// elapse without it sleeping.
    /// </summary>
    public static int Watch(TextWriter output, int minutes)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        XboxJpProtocol? protocol = null;
        foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;
            var attempt = new XboxJpProtocol(new HidSharpTransport(hid)) { Trace = null, WakeOnOpen = false };
            if (!attempt.Open() || !attempt.Ping()) { attempt.Dispose(); continue; }
            protocol = attempt;
            break;
        }
        if (protocol is null)
        {
            output.WriteLine("No Retro 87 \"X\" collection answered the ping; nothing to observe.");
            return 1;
        }

        using (protocol)
        {
            output.WriteLine($"  t=0s  answered (firmware {protocol.FirmwareVersion:0.00})");
            output.WriteLine($"  waiting {minutes} min in silence (nothing is sent, like the app idle)…");
            Thread.Sleep(minutes * 60_000);

            output.WriteLine($"  t={minutes}min  sondando…");
            if (protocol.Ping())
            {
                output.WriteLine("  answered: the keyboard did NOT sleep in that time; nothing to recover.");
                return 0;
            }

            output.WriteLine($"  NO ANSWER ({protocol.LastError ?? "timeout"}) — sending the wake (cmd 0x08 sub 0)");
            bool woken = protocol.Wake();
            bool back = woken && protocol.Ping();
            output.WriteLine(back
                ? "  RECOVERED by the wake."
                : $"  the wake did not bring the device back (wake acknowledged: {woken}).");

            XboxConfig? config = back ? protocol.ReadXboxConfig() : null;
            output.WriteLine(config is null
                ? "  full read: failed"
                : $"  full read: ok (profile \"{config.ProfileName}\", {config.Raw.Length} bytes)");
            return back ? 0 : 2;
        }
    }

    public static int Run(TextWriter output, bool planOnly, bool force = false)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);

        var candidates = monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList();
        if (candidates.Count == 0)
        {
            output.WriteLine("No Retro 87 \"X\" (PID 0x2028/0x202E) found.");
            return 1;
        }

        byte[] frame = XboxJpProtocol.BuildRequest(XboxWire.Cmd.Session, XboxWire.SessionSubtype.Open, 0,
            ReadOnlySpan<byte>.Empty, 0);
        output.WriteLine($"Wake frame (cmd 0x08 sub 0, no data): {Convert.ToHexString(frame)}");
        if (planOnly)
        {
            output.WriteLine("PLAN MODE: nothing was sent to the keyboard. Run without --plan to send the wake.");
            return 0;
        }

        // A sleeping keyboard never answers the version command, so the usual "which collection
        // answers?" selection cannot be used here: try the collections and, on each, send the wake
        // and see if it comes alive.
        XboxJpProtocol? protocol = null;
        DeviceInfo? device = null;
        string? collectionPath = null;
        bool awakeBeforeAny = false;
        foreach (DeviceInfo candidate in candidates)
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;

            var attempt = new XboxJpProtocol(new HidSharpTransport(hid))
            {
                Trace = line => output.WriteLine($"      · {line}"),
            };
            if (!attempt.Open(probeVersion: false))
            {
                attempt.Dispose();
                continue;
            }

            bool awakeBefore = attempt.Ping();
            awakeBeforeAny |= awakeBefore;
            output.WriteLine($"  {candidate.DevicePath}");
            output.WriteLine($"    before the wake: {(awakeBefore ? "the keyboard already answered (it was not asleep)" : "no answer (asleep or absent)")}");

            if (!awakeBefore || force)
            {
                // --force sends the handshake even to an awake keyboard, to observe the device's
                // answer (the same frame the write batches already use).
                bool woken = attempt.Wake();
                output.WriteLine($"    wake (cmd 0x08 sub 0){(force ? " [forced]" : "")}: " +
                                 $"{(woken ? "acknowledged" : "no answer")}");
                if (!woken && !awakeBefore)
                {
                    output.WriteLine("      (no answer to the wake itself — keyboard off or the dongle has no keyboard)");
                    attempt.Dispose();
                    continue;
                }
            }

            if (attempt.Ping())
            {
                protocol = attempt;
                device = candidate;
                collectionPath = candidate.DevicePath;
                output.WriteLine($"    after: firmware {attempt.FirmwareVersion:0.00}, battery {attempt.BatteryPercent}%");
                break;
            }
            attempt.Dispose();
        }

        if (protocol is null || device is null || collectionPath is null)
        {
            output.WriteLine();
            output.WriteLine(awakeBeforeAny
                ? "The keyboard answered the initial ping, but I did not keep a usable collection."
                : "NO collection answered the wake. Check that the keyboard is on (or that the dongle has a keyboard).");
            return 1;
        }

        using (protocol)
        {
            output.WriteLine($"Keyboard: \"{device.ProductName}\"  firmware {protocol.FirmwareVersion:0.00}  " +
                             $"battery {protocol.BatteryPercent}%");
            output.WriteLine($"Collection used: {collectionPath}");

            output.WriteLine();
            output.WriteLine("Full read after the wake (proves the device is talking again):");
            XboxConfig? config = protocol.ReadXboxConfig();
            if (config is null)
            {
                output.WriteLine($"  FAILED: {(protocol.LastError ?? "no answer")}");
                return 2;
            }
            output.WriteLine($"  ok: perfil \"{config.ProfileName}\", {config.Mappings.Count(m => m.Type != XboxMappingType.None)} " +
                             "non-NONE mapping(s), 1532 bytes read");
            output.WriteLine();
            output.WriteLine("RESULT: the wake is accepted by the device and reading works afterwards.");
            return 0;
        }
    }
}
