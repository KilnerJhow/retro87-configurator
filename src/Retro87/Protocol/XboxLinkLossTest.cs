// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>What happened to the keyboard across one dongle unplug/replug.</summary>
/// <param name="Before">State right after the dongle came back, before any wake frame.</param>
/// <param name="BeforeDetail">The probe's own reason when it did not answer.</param>
/// <param name="WakeAttempts">How many wake frames were sent (0 when the probe already answered).</param>
/// <param name="WakeAcknowledged">Whether any <c>cmd 0x08 sub 0</c> frame was acknowledged.</param>
/// <param name="After">State after the wake attempts.</param>
/// <param name="ConfigUnchanged">
/// Whether the configuration still matches the state read before the unplug: <c>true</c> verified
/// identical, <c>false</c> verified different, <c>null</c> <b>not verifiable</b> (the keyboard never
/// answered, so nothing was read).
/// </param>
internal sealed record XboxLinkOutcome(
    XboxProbeState Before, string BeforeDetail,
    int WakeAttempts, bool WakeAcknowledged, XboxProbeState After, bool? ConfigUnchanged)
{
    /// <summary>True when the keyboard answered again after the wake attempt.</summary>
    public bool Recovered => After == XboxProbeState.Awake;

    /// <summary>True when the keyboard was already answering when the dongle came back.</summary>
    public bool NeverSlept => Before == XboxProbeState.Awake;
}

/// <summary>
/// Measures what the keyboard does when it <b>loses the dongle</b>: the user unplugs the 2.4 G
/// dongle, the keyboard's link drops (the LEDs go out), and when the dongle is plugged back the
/// tool probes it <b>immediately</b> — before any key press — and then tries the wake handshake
/// (<c>cmd 0x08 sub 0</c>), the frame the app sends when it opens the device.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: the user observed that turning the PC off turns the keyboard off. The dongle is
/// USB-powered, so it cannot send a shutdown command (there is none in the DLL for this family
/// either); what it can do is <b>stop existing</b>. Unplugging the dongle reproduces exactly that
/// (a yanked dongle certainly sent nothing), which is why this is the cheap, deterministic way to
/// put the keyboard to sleep — no 15-minute wait.
/// </para>
/// <para>
/// Timeline it reports, all measured from the device leaving the bus: how long the dongle stayed
/// out (<c>--gap</c>, default 30 s), how long the keyboard took to answer after the replug, the
/// probe state before the wake, whether the wake was acknowledged, the probe state after it, and
/// whether the 1532-byte configuration still matches what was read before the unplug.
/// </para>
/// <para>
/// <b>Read-only by construction:</b> the only frames it ever sends are the version probe
/// (<c>cmd 0x0A</c>) and the wake (<c>cmd 0x08 sub 0</c>) — both control frames. It never writes
/// configuration. The one thing it asks of the user is physical: pull the dongle out and put it
/// back when told to, <b>without touching the keyboard</b>.
/// </para>
/// </remarks>
internal static class XboxLinkLossTest
{
    /// <summary>How often the presence of the dongle is checked while waiting (it must react fast).</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>How long to wait for the user to pull the dongle, and for them to put it back.</summary>
    private static readonly TimeSpan UnplugTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait for the keyboard to be awake at the start: it may still be asleep from a
    /// previous unplug, and only a key press brings it back (measured: the wake frame does not).
    /// </summary>
    private static readonly TimeSpan WaitForWakeTimeout = TimeSpan.FromMinutes(10);

    /// <param name="insistSeconds">
    /// How long to keep insisting on the wake when the first one fails (0 = send it once).
    /// </param>
    public static int Run(TextWriter output, int gapSeconds = 30, int insistSeconds = 0)
    {
        if (gapSeconds < 0) gapSeconds = 0;
        if (insistSeconds < 0) insistSeconds = 0;

        // A fast poll: the probe has to happen as soon as the dongle is back, before a key press.
        using var monitor = new DeviceMonitor(PollInterval);
        monitor.Start();
        Thread.Sleep(500);

        // The keyboard may still be asleep from a previous unplug: wait for the user to press a key
        // instead of aborting, and report how long that took (it is evidence of the wake mechanism).
        if (!TryFindAwakeVendor(monitor, line => output.WriteLine("  " + line), WaitForWakeTimeout,
                out XboxJpProtocol? found, out DeviceInfo? foundDevice,
                out string vendorPath, out XboxConfig? baseline, out string why, out long wakeWaitMs))
        {
            output.WriteLine($"Could not start: {why}");
            return 1;
        }
        if (wakeWaitMs > 0)
            output.WriteLine($"  (the keyboard woke {wakeWaitMs / 1000.0:0.0}s after I started waiting)");
        XboxJpProtocol protocol = found!;
        DeviceInfo device = foundDevice!;
        byte[]? baselineRaw = baseline?.Raw;

        using (protocol)
        {
            output.WriteLine($"Keyboard: \"{device.ProductName}\"  firmware {protocol.FirmwareVersion:0.00}  " +
                             $"battery {protocol.BatteryPercent}%");
            output.WriteLine($"Vendor collection: {vendorPath}");
            output.WriteLine($"Estado inicial: {XboxProbe.Describe(XboxProbeState.Awake)}" +
                             (baseline is null ? " (could not read the configuration)" : $" — {baseline.Raw.Length} bytes read"));
        }

        output.WriteLine();
        output.WriteLine($"Unplug the dongle from USB NOW (the keyboard LEDs should go out). " +
                         $"I wait for it to disappear, leave it out for {gapSeconds} s and tell you to plug it back in.");
        output.WriteLine("Do not touch the keyboard until I say it is over.");

        // ---------------------------------------------------------------- the physical gap
        long start = Environment.TickCount64;
        if (!WaitForUnplug(monitor, vendorPath, out long goneAt))
        {
            output.WriteLine($"The dongle did not disappear within {UnplugTimeout.TotalSeconds:0} s — nothing was measured.");
            return 1;
        }
        output.WriteLine($"  t+{(goneAt - start) / 1000.0:0.0}s  the dongle disappeared (here the LEDs go out and the keyboard goes to sleep).");

        long replugDue = goneAt + gapSeconds * 1000L;
        while (Environment.TickCount64 < replugDue) Thread.Sleep(100);
        output.WriteLine($"  t+{(Environment.TickCount64 - start) / 1000.0:0.0}s  you can PLUG the dongle back in now.");

        if (!WaitForReplug(monitor, out long backAt))
        {
            output.WriteLine($"The dongle did not come back within {UnplugTimeout.TotalSeconds:0} s — nothing was measured.");
            return 1;
        }
        output.WriteLine($"  t+{(backAt - start) / 1000.0:0.0}s  the dongle is back; probing immediately (without touching the keyboard).");

        // ---------------------------------------------------------------- probe + wake
        XboxLinkOutcome outcome = Measure(monitor, vendorPath, baselineRaw,
            line => output.WriteLine("      " + line), insistSeconds);

        output.WriteLine();
        output.WriteLine("Link loss result:");
        output.WriteLine($"  before the wake: {XboxProbe.Describe(outcome.Before)}" +
                         (outcome.BeforeDetail.Length == 0 ? string.Empty : $" — {outcome.BeforeDetail}"));
        if (!outcome.NeverSlept)
        {
            output.WriteLine($"  wake          : {outcome.WakeAttempts} enviado(s); " +
                             $"{(outcome.WakeAcknowledged ? "some were acknowledged" : "none acknowledged")}");
            output.WriteLine($"  after the wake : {XboxProbe.Describe(outcome.After)}");
            output.WriteLine($"  configuration : {outcome.ConfigUnchanged switch
            {
                true => "identical to the previous state (checked by re-reading)",
                false => "CHANGED — see above",
                null => "NOT checked: the keyboard did not answer, so nothing could be read",
            }}");
        }
        output.WriteLine();
        long total = Environment.TickCount64 - start;
        output.WriteLine("RESULTADO: " + Summarize(outcome));
        output.WriteLine($"Total time: {total / 1000.0:0.0}s (dongle out for {gapSeconds} s).");
        return outcome.NeverSlept || outcome.Recovered ? 0 : 2;
    }

    /// <summary>
    /// The one-line verdict for an outcome. Pure, so the selftest can pin the three cases:
    /// never slept, slept and the wake recovered it, slept and the wake did not.
    /// </summary>
    internal static string Summarize(XboxLinkOutcome outcome)
        => outcome.NeverSlept
            ? "the keyboard did NOT sleep on link loss — it answered again by itself when the dongle " +
              "came back (so sleep with the PC off has another cause: time, not the link)."
            : outcome.Recovered
                ? "link loss leaves the keyboard asleep and the wake (cmd 0x08 sub 0) recovers it."
                : "link loss leaves the keyboard asleep and the wake did NOT recover it (the command is not enough).";

    /// <summary>
    /// Probes the keyboard on the remembered collection and, when it is not answering, sends the
    /// wake frame and probes again — reading the configuration back to prove nothing changed.
    /// </summary>
    /// <remarks>
    /// Pure with respect to the physical timeline (it takes an already-present dongle), so the
    /// selftest drives it with <see cref="FakeXboxDevice"/>. The collection is looked up by the
    /// remembered path first; when Windows handed the dongle a different path on the replug, the
    /// vendor collection (the one that reports 64-byte output reports) is used instead.
    /// </remarks>
    internal static XboxLinkOutcome Measure(DeviceMonitor monitor, string vendorPath, byte[]? baseline,
        Action<string> log, int insistSeconds = 0)
    {
        var hid = monitor.FindHidDevice(vendorPath) ?? VendorCandidate(monitor);
        if (hid is null)
        {
            log("the collection is not back on the bus");
            return new XboxLinkOutcome(XboxProbeState.NoAnswer, "the collection is not back", 0, false,
                XboxProbeState.NoAnswer, null);
        }

        using var protocol = new XboxJpProtocol(new HidSharpTransport(hid)) { WakeOnOpen = false };
        if (!protocol.Open(probeVersion: false))
        {
            log("could not open the collection");
            return new XboxLinkOutcome(XboxProbeState.NoAnswer, "did not open", 0, false, XboxProbeState.NoAnswer, null);
        }
        return MeasureOpen(protocol, baseline, log, insistSeconds);
    }

    /// <summary>
    /// The Xbox collection to talk to when the remembered path is gone: the vendor one (64-byte
    /// output reports, as the read-only probe shows), or the first candidate as a last resort.
    /// </summary>
    private static HidSharp.HidDevice? VendorCandidate(DeviceMonitor monitor)
    {
        HidSharp.HidDevice? first = null;
        foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(candidate.DevicePath);
            if (hid is null) continue;
            first ??= hid;
            try
            {
                if (hid.GetMaxOutputReportLength() == XboxWire.FrameLength) return hid;
            }
            catch (Exception)
            {
                // A collection whose descriptors cannot be read is simply not the vendor one.
            }
        }
        return first;
    }

    /// <summary>The probe/wake/verify sequence, against an already open session.</summary>
    /// <param name="insistSeconds">
    /// When &gt; 0 and the first wake did not bring the device back, keep sending the wake every
    /// 500 ms for this long. A wireless keyboard duty-cycles its receiver, so a single frame may be
    /// sent while it is not listening — insisting is what tells "the frame never lands" from
    /// "the timing was wrong".
    /// </param>
    internal static XboxLinkOutcome MeasureOpen(XboxJpProtocol protocol, byte[]? baseline,
        Action<string> log, int insistSeconds = 0)
    {
        bool awakeBefore = protocol.Ping(out string why);
        XboxProbeState before = XboxProbe.Classify(awakeBefore, why);
        if (awakeBefore)
        {
            log("the keyboard already answered the probe (it was not asleep)");
            return new XboxLinkOutcome(before, string.Empty, 0, false, before, true);
        }
        // Keep the first reason: the probe after the wake overwrites the out parameter, and the
        // diagnosis of the state the dongle came back in is what the caller reports.
        string beforeDetail = why;
        log($"probe without an answer: {beforeDetail}");

        bool woke = protocol.Wake();
        int attempts = 1;
        bool awakeAfter = false;
        if (woke) awakeAfter = protocol.Ping(out why);
        else log("the wake was not acknowledged");

        if (!woke && !awakeAfter && insistSeconds > 0)
        {
            log($"retrying the wake for up to {insistSeconds}s (every 500 ms)…");
            long deadline = Environment.TickCount64 + insistSeconds * 1000L;
            long startInsist = Environment.TickCount64;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(500);
                attempts++;
                woke |= protocol.Wake();
                if (protocol.Ping(out why))
                {
                    awakeAfter = true;
                    log($"the keyboard answered after {attempts} wake(s), " +
                        $"{(Environment.TickCount64 - startInsist) / 1000.0:0.0}s insistindo");
                    break;
                }
            }
            if (!awakeAfter) log($"the keyboard did not answer even after {attempts} wake(s)");
        }

        XboxProbeState after = XboxProbe.Classify(awakeAfter, why);
        bool? unchanged = null;
        if (awakeAfter)
        {
            XboxConfig? config = protocol.ReadXboxConfig();
            if (config is null)
            {
                log("the full read failed after the wake");
            }
            else if (baseline is not null)
            {
                int diffs = CountDiffs(baseline, config.Raw);
                unchanged = diffs == 0;
                log($"full read: {config.Raw.Length} bytes, {diffs} difference(s) vs. before unplugging the dongle");
            }
        }
        return new XboxLinkOutcome(before, beforeDetail, attempts, woke, after, unchanged);
    }

    /// <summary>Waits for the dongle (the remembered collection) to leave the bus.</summary>
    private static bool WaitForUnplug(DeviceMonitor monitor, string vendorPath, out long atTick)
    {
        long deadline = Environment.TickCount64 + (long)UnplugTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (monitor.FindHidDevice(vendorPath) is null)
            {
                atTick = Environment.TickCount64;
                return true;
            }
            Thread.Sleep(PollInterval);
        }
        atTick = Environment.TickCount64;
        return false;
    }

    /// <summary>
    /// Waits for the dongle to be on the bus again, by its <b>PID</b> rather than by the path: a
    /// replug can land on a new instance id (or another port), and the measurement must not be
    /// delayed by that.
    /// </summary>
    private static bool WaitForReplug(DeviceMonitor monitor, out long atTick)
    {
        long deadline = Environment.TickCount64 + (long)UnplugTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (monitor.Devices.Any(d => d.Pid == XboxWire.PidDongle || d.Pid == XboxWire.PidUsb))
            {
                atTick = Environment.TickCount64;
                return true;
            }
            Thread.Sleep(PollInterval);
        }
        atTick = Environment.TickCount64;
        return false;
    }

    /// <summary>
    /// Picks the collection that answers, and remembers its path for the return. When the keyboard is
    /// asleep (or the dongle is not plugged), it <b>waits</b> for the user to wake it, saying what is
    /// missing — and reports how long that took through <paramref name="waitedMs"/>.
    /// </summary>
    private static bool TryFindAwakeVendor(DeviceMonitor monitor, Action<string> log, TimeSpan timeout,
        out XboxJpProtocol? protocol, out DeviceInfo? device, out string vendorPath, out XboxConfig? config,
        out string why, out long waitedMs)
    {
        protocol = null;
        device = null;
        vendorPath = string.Empty;
        config = null;
        why = "no Retro 87 \"X\" collection answered";
        waitedMs = 0;

        long start = Environment.TickCount64;
        long deadline = start + (long)timeout.TotalMilliseconds;
        bool announcedNoKeyboard = false;
        bool announcedNoDongle = false;

        while (true)
        {
            foreach (DeviceInfo candidate in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
            {
                var hid = monitor.FindHidDevice(candidate.DevicePath);
                if (hid is null) continue;

                var attempt = new XboxJpProtocol(new HidSharpTransport(hid)) { WakeOnOpen = false };
                if (!attempt.Open(probeVersion: false) || !attempt.Ping())
                {
                    attempt.Dispose();
                    continue;
                }
                protocol = attempt;
                device = candidate;
                vendorPath = candidate.DevicePath;
                config = attempt.ReadXboxConfig();
                waitedMs = Environment.TickCount64 - start;
                return true;
            }

            if (Environment.TickCount64 >= deadline) return false;

            bool anyXbox = monitor.Devices.Any(d => KnownDevices.IsXbox(d.Pid));
            if (!anyXbox && !announcedNoDongle)
            {
                log("no dongle on the bus: plug in the Retro 87 \"X\" dongle.");
                announcedNoDongle = true;
            }
            else if (anyXbox && !announcedNoKeyboard)
            {
                log("the dongle is on the bus, but the keyboard does not answer: " +
                    "press a key on the keyboard to wake it.");
                announcedNoKeyboard = true;
            }
            Thread.Sleep(1000);
        }
    }

    private static int CountDiffs(byte[] a, byte[] b)
    {
        int diffs = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != b[i]) diffs++;
        return diffs + Math.Abs(a.Length - b.Length);
    }
}
