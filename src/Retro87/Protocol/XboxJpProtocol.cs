// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;
using Retro87.Models;

namespace Retro87.Protocol;

/// <summary>
/// Protocol of the 8BitDo Retro 87 "X" (family <b>Xbox</b>), implemented over
/// <see cref="IKeyboardTransport"/> so it runs on Linux without a vendor library.
/// </summary>
/// <remarks>
/// <para>
/// Documented from interoperability analysis and validated by read-only probes on the real
/// keyboard, then by two USB captures of the official app — see <c>docs/PROTOCOL.md</c>.
/// The framing is in <see cref="XboxWire"/>: 64-byte reports, request report id <c>0x81</c>,
/// reply report id <c>0x02</c>, and a <b>10-byte</b> reply header (data from byte 10).
/// </para>
/// <para>
/// <b>Writing.</b> <see cref="WriteConfig"/> mirrors what the official app was captured doing:
/// <c>cmd 0x01</c> for the 12-byte mapping records (at <c>0x28 + i·12</c>), the profile name and
/// single-byte fields; <c>cmd 0x05</c> followed by <c>cmd 0x06</c> for the macro slots; and the
/// <c>cmd 0x08</c> session bracket around a batch of writes. The project rule still stands:
/// writing to the user's keyboard needs explicit permission, so the UI gates it behind its own
/// switch and takes a backup of the configuration first.
/// </para>
/// </remarks>
public sealed class XboxJpProtocol : IJpProtocol, IWriteDiagnostics
{
    private readonly IKeyboardTransport _transport;
    private readonly object _gate = new();

    /// <summary>Configuration last read, used as the baseline for diffing and for backups.</summary>
    private XboxConfig? _baseline;

    public XboxJpProtocol(IKeyboardTransport transport) => _transport = transport;

    public bool IsOpen => _transport.IsOpen;

    /// <summary>
    /// True: this layer can write (mapping records, profile name and macro slots). The UI is
    /// what asks the user for permission — see <c>MainWindowViewModel.ConfirmSaveAsync</c>.
    /// </summary>
    public bool CanWrite => true;

    /// <summary>
    /// Raw 1532 bytes of the configuration last read from the device, or null when nothing was
    /// read in this session. The UI saves this as a backup before writing.
    /// </summary>
    public byte[]? LastRawConfig => _baseline?.Raw;

    /// <summary>
    /// When true (default) a write batch is wrapped in the <c>cmd 0x08</c> open/close pair the
    /// official app was captured sending. It is a control frame with no data, but it has not
    /// been verified against the keyboard alone, so it can be turned off in one place.
    /// </summary>
    public bool UseSessionBrackets { get; set; } = true;

    /// <summary>
    /// When true, <see cref="Open"/> sends the wake handshake (<see cref="Wake"/>) before the
    /// version command. Off by default so the read-only tools stay <b>read-only by construction</b>;
    /// the app turns it on (see <c>JpProtocolFactory</c>) and so do the hardware write tests.
    /// </summary>
    public bool WakeOnOpen { get; set; }

    /// <summary>
    /// Message of the last command that gave up (a device error such as <c>0xC0</c>, or a timeout),
    /// or null. The UI can use it to tell "asleep / no keyboard on the dongle" from "protocol bug".
    /// </summary>
    public string? LastError
    {
        get => _lastError;
        private set
        {
            _lastError = value;
            LastFailure = Classify(value);
        }
    }

    private string? _lastError;

    /// <summary>
    /// The kind of failure behind <see cref="LastError"/>, so the UI can show a localized message
    /// instead of the English diagnostic text.
    /// </summary>
    public XboxFailure LastFailure { get; private set; }

    private static XboxFailure Classify(string? message) =>
        string.IsNullOrEmpty(message) ? XboxFailure.None
        : message.Contains("device error") ? XboxFailure.DeviceError
        : message.Contains("no answer") ? XboxFailure.NoAnswer
        : XboxFailure.Other;

    /// <summary>Macro slots of this family (the reference reads/writes ten; the UI model has eight).</summary>
    public const int MacroSlotCount = 10;

    /// <summary>Notes from the last <see cref="WriteConfig"/> about edits that produced no write.</summary>
    public IReadOnlyList<string> LastWriteWarnings { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Optional category per board position, as the UI picker knows it. It is what lets the plan
    /// tell apart the ids the device shares between keyboard and media/mouse (0xE2, 0xE9, 0xEA,
    /// 0x6F, 0x70). The view-model fills it before writing; without it those ids are refused
    /// with a warning instead of guessed.
    /// </summary>
    public IReadOnlyDictionary<int, MappingTargetKind>? TargetKinds { get; set; }

    /// <summary>
    /// Optional diagnostic hook: one line per reply (or per failed attempt). Used by the hardware
    /// test to show whether the device answered the first frame or only after retries.
    /// </summary>
    public Action<string>? Trace { get; set; }

    /// <summary>Firmware version from the version command (0 when unknown).</summary>
    public float DeviceVersion => FirmwareVersion;

    /// <summary>Firmware version from the last <see cref="ReadVersion"/> (0 when unknown).</summary>
    public float FirmwareVersion { get; private set; }

    /// <summary>Battery percent from the last <see cref="ReadVersion"/> (-1 when unknown).</summary>
    public int BatteryPercent { get; private set; } = -1;

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Opens the transport, sends the wake handshake when asked and probes the version.</summary>
    public bool Open() => Open(probeVersion: true);

    /// <param name="probeVersion">
    /// When true the version command runs inside <see cref="Open"/>, which is how the factory tells
    /// the vendor collection from its siblings. A tool that expects a sleeping keyboard passes
    /// false so it can send the wake handshake before spending time on retries.
    /// </param>
    public bool Open(bool probeVersion)
    {
        lock (_gate)
        {
            if (!_transport.Open()) return false;
            // The native app calls _ReportXbox(0) (cmd 0x08 sub 0) before everything — the wake
            // handshake the device needs after it goes idle (or the dongle answers 02 04 C0 … when
            // no keyboard is powered). It is a control frame, not a configuration write, but the
            // read-only probe deliberately leaves it off (WakeOnOpen default false).
            if (WakeOnOpen) Wake();
            if (probeVersion) ReadVersion();
            return true;
        }
    }

    /// <summary>
    /// Sends the wake/select handshake the official app sends first (<c>_ReportXbox(0)</c>, the
    /// <c>cmd 0x08 sub 0</c> frame), so a keyboard that went idle answers again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One send and one wait — a control frame, not a retried operation: the point is to nudge the
    /// device, and sending it 30 times to a collection that will never answer (the factory tries
    /// the HID siblings in turn) would only make opening slow. Returns whether it was
    /// acknowledged.
    /// </para>
    /// <para>
    /// <b>What it does not do (measured 2026-10-18):</b> the keyboard goes to sleep when it
    /// <b>loses the link</b> (dongle pulled, PC off) and in that state it does not hear the dongle at
    /// all — the dongle answers <c>02 04 C0 …</c> and this frame gets no ACK, whether it is sent once
    /// or 19 times in a row. <b>Only a key press brings it back.</b> The frame is kept because it is
    /// what the official app sends and it costs nothing, but the UI must tell the user to press a
    /// key rather than rely on it.
    /// </para>
    /// </remarks>
    public bool Wake()
    {
        lock (_gate)
        {
            bool ok = QueryOnce(XboxWire.Cmd.Session, XboxWire.SessionSubtype.Open, 0,
                ReadOnlySpan<byte>.Empty, null, out _, out string why);
            Trace?.Invoke(ok ? "wake: cmd 0x08 sub 0 acknowledged" : $"wake: no answer to cmd 0x08 sub 0 ({why})");
            return ok;
        }
    }

    public void Close()
    {
        lock (_gate) _transport.Close();
    }

    public void Dispose()
    {
        lock (_gate) _transport.Dispose();
    }

    /// <summary>
    /// Writes the configuration the UI edited, in the order the official app was captured
    /// using: <c>cmd 0x08</c> open → mapping records (<c>cmd 0x01</c>) → macro slots
    /// (<c>cmd 0x05</c> + <c>0x06</c>) → profile name (<c>cmd 0x01</c>) → <c>cmd 0x08</c> close.
    /// </summary>
    /// <param name="config">Configuration coming from the UI.</param>
    /// <param name="force">
    /// False (default) sends only what differs from the configuration last read from the device,
    /// so untouched keys and macros are not rewritten; true rewrites every known board position
    /// and every non-empty macro slot.
    /// </param>
    /// <remarks>
    /// Needs a baseline: if nothing has been read in this session, one read is done first
    /// (read-only), because the UI model does not carry the record indices, the unused
    /// <c>+1..+3</c> bytes or the fields the UI does not show.
    /// </remarks>
    public bool WriteConfig(KeyboardConfig config, bool force = false)
    {
        lock (_gate)
        {
            if (!IsOpen) return false;

            XboxConfig? baseline = _baseline ?? ReadXboxConfig();
            if (baseline is null) return false;

            XboxWritePlan plan = XboxFromKeyboardConfig.Plan(config, baseline, force, TargetKinds);
            LastWriteWarnings = plan.Warnings;
            if (plan.IsEmpty) return true;

            if (UseSessionBrackets && !SendSession(XboxWire.SessionSubtype.Open)) return false;
            try
            {
                foreach (XboxMappingChange change in plan.Mappings)
                {
                    if (!WriteMapping(change.Index, change.Record)) return false;
                }

                // The mapping record that binds a macro slot is part of plan.Mappings; here only
                // the slot contents (info + steps) are sent.
                foreach (XboxMacroChange macro in plan.Macros)
                {
                    if (!WriteMacroRegion(macro.SlotBase, macro.Info, macro.Steps, macro.StepCount)) return false;
                }

                if (plan.ProfileName is { } name && !WriteProfileName(name)) return false;

                // Single-byte fields (volume_lv, fnLock, colour theme): the cmd 0x01 primitive
                // validated on the device in C3 (docs/PROTOCOL.md).
                foreach (XboxFieldChange field in plan.Fields)
                {
                    if (!WriteField(field.Offset, field.Value)) return false;
                }
                return true;
            }
            finally
            {
                if (UseSessionBrackets) SendSession(XboxWire.SessionSubtype.Close);
            }
        }
    }

    // ---------------------------------------------------------------- writing

    /// <summary>Writes <paramref name="data"/> at <paramref name="offset"/> (cmd <c>0x01</c>, 53-byte chunks).</summary>
    public bool WriteRegion(uint offset, ReadOnlySpan<byte> data)
    {
        lock (_gate) return WriteRegionCore(XboxWire.Cmd.Write, offset, data);
    }

    /// <summary>Writes one 12-byte mapping record at <c>0x28 + index·12</c>.</summary>
    public bool WriteMapping(int index, ReadOnlySpan<byte> record)
    {
        if (index < 0 || index >= XboxWire.MappingCount || record.Length != XboxWire.MappingSize) return false;
        return WriteRegion((uint)(XboxWire.MappingBase + index * XboxWire.MappingSize), record);
    }

    /// <summary>Writes a single configuration byte (<c>enable</c>, <c>fnLock</c>, <c>fast_setting</c>, <c>volume_lv</c>, colour theme).</summary>
    public bool WriteField(int offset, byte value)
    {
        var single = new[] { value };
        return WriteRegion((uint)offset, single);
    }

    /// <summary>Writes the 32-byte profile name field (UTF-16 <b>big-endian</b>) at offset 4.</summary>
    public bool WriteProfileName(string name)
        => WriteRegion(XboxWire.ProfileNameOffset,
            XboxFromKeyboardConfig.EncodeNameBigEndian(name, XboxWire.ProfileNameBytes));

    /// <summary>
    /// Sends one macro slot: <c>cmd 0x05</c> (start; the offset is the slot base), then
    /// <c>cmd 0x06</c> with the 48-byte info at the base and the steps from
    /// <c>base + 0x40</c> — exactly the frames and chunking captured from the official app.
    /// </summary>
    public bool WriteMacroRegion(uint slotBase, ReadOnlySpan<byte> info, ReadOnlySpan<byte> steps, int stepCount)
    {
        lock (_gate)
        {
            if (info.Length != XboxWire.MacroInfoSize) return false;

            if (!Query(XboxWire.Cmd.MacroStart, 0, slotBase, ReadOnlySpan<byte>.Empty, 0, out _))
                return false;
            if (!WriteRegionCore(XboxWire.Cmd.MacroData, slotBase, info))
                return false;

            int bytes = stepCount * XboxWire.MacroStepSize;
            if (bytes <= 0) return true;
            if (steps.Length < bytes) return false;
            return WriteRegionCore(XboxWire.Cmd.MacroData, slotBase + XboxWire.MacroDataOffset, steps[..bytes]);
        }
    }

    public bool WakeRawSignal(string profileName)
    {
        lock (_gate)
        {
            if (!IsOpen || !SendSession(XboxWire.SessionSubtype.Open)) return false;
            try { return WriteProfileName(profileName); }
            finally { SendSession(XboxWire.SessionSubtype.Close); }
        }
    }

    /// <summary>Sends the <c>cmd 0x08</c> session frame (<paramref name="subtype"/> 0 = open, 1 = close).</summary>
    public bool SendSession(byte subtype)
    {
        lock (_gate) return Query(XboxWire.Cmd.Session, subtype, 0, ReadOnlySpan<byte>.Empty, 0, out _);
    }

    /// <summary>Chunked write helper shared by <see cref="WriteRegion"/> and the macro data phase.</summary>
    private bool WriteRegionCore(byte cmd, uint offset, ReadOnlySpan<byte> data)
    {
        for (int position = 0; position < data.Length;)
        {
            int chunk = Math.Min(data.Length - position, XboxWire.MaxChunk);
            if (!Query(cmd, 0, offset + (uint)position, data.Slice(position, chunk), chunk, out _))
                return false;
            position += chunk;
        }
        return true;
    }

    // ---------------------------------------------------------------- framing

    /// <summary>
    /// Builds one 64-byte request frame: <c>[0]=0x81 [1]=0x04 [2]=cmd [3]=subtype [4]=len
    /// [5]=checksum [6..9]=offset LE [10..]=data</c>.
    /// </summary>
    internal static byte[] BuildRequest(byte cmd, byte subtype, uint offset, ReadOnlySpan<byte> data, int? length = null)
    {
        var frame = new byte[XboxWire.FrameLength];
        frame[0] = XboxWire.ReportIdOut;
        frame[1] = XboxWire.Marker;
        frame[2] = cmd;
        frame[3] = subtype;
        int len = length ?? data.Length;
        frame[4] = (byte)len;
        frame[5] = Checksum(data);
        frame[6] = (byte)(offset & 0xFF);
        frame[7] = (byte)((offset >> 8) & 0xFF);
        frame[8] = (byte)((offset >> 16) & 0xFF);
        frame[9] = (byte)((offset >> 24) & 0xFF);
        int copy = Math.Min(data.Length, XboxWire.FrameLength - XboxWire.RequestHeaderLength);
        data.Slice(0, copy).CopyTo(frame.AsSpan(XboxWire.RequestHeaderLength));
        return frame;
    }

    /// <summary>
    /// True when <paramref name="reply"/> is a well-formed reply to <paramref name="cmd"/>:
    /// <c>[0]==0x02 [1]==0x04 [2]==0x03 [3]==cmd</c>.
    /// </summary>
    internal static bool IsReplyFor(byte cmd, ReadOnlySpan<byte> reply)
        => reply.Length >= XboxWire.ReplyHeaderLength
           && reply[0] == XboxWire.ReportIdIn
           && reply[1] == XboxWire.Marker
           && reply[2] == XboxWire.ReplyConst
           && reply[3] == cmd;

    /// <summary>
    /// True when the reply is really the answer to <b>this</b> request: same command, and the
    /// echoed offset (and checksum, when the request carried data) match.
    /// </summary>
    /// <remarks>
    /// The device echoes the request's checksum at <c>reply[5]</c> and its offset at
    /// <c>reply[6..9]</c> (confirmed in both captures — the document used to call those bytes
    /// "reserved/zeroed"). Without this check a <b>late reply</b> to the previous chunk is
    /// happily consumed as the answer to the next one, which silently shifts the whole
    /// configuration — exactly what happened in the first hardware write test (C1).
    /// </remarks>
    internal static bool IsReplyTo(byte cmd, uint offset, ReadOnlySpan<byte> requestData, ReadOnlySpan<byte> reply)
    {
        if (!IsReplyFor(cmd, reply)) return false;
        if (ReadU32(reply, XboxWire.ReplyOffset) != offset) return false;
        if (requestData.Length > 0 && reply[XboxWire.ReplyChecksum] != Checksum(requestData)) return false;
        return true;
    }

    /// <summary>
    /// True when the report is the device's <b>error/terminator</b> reply (<c>02 04 C0 …</c>):
    /// what the dongle answers with no keyboard powered on, and what a device that will not answer
    /// produces. Retrying cannot help, so callers fail fast on it.
    /// </summary>
    internal static bool IsErrorReply(ReadOnlySpan<byte> reply)
        => reply.Length >= XboxWire.ReplyHeaderLength
           && reply[0] == XboxWire.ReportIdIn
           && reply[1] == XboxWire.Marker
           && reply[2] == XboxWire.ReplyError;

    /// <summary>Checksum of a request payload: the sum of its bytes (mod 256).</summary>
    internal static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte checksum = 0;
        for (int i = 0; i < data.Length; i++) checksum += data[i];
        return checksum;
    }

    private bool SendRequest(byte cmd, byte subtype, uint offset, ReadOnlySpan<byte> data, int? length = null)
        => _transport.Write(BuildRequest(cmd, subtype, offset, data, length)) > 0;

    private bool TryReadReply(byte cmd, uint offset, ReadOnlySpan<byte> requestData, out byte[] reply, out string why)
    {
        int length = Math.Max(XboxWire.FrameLength, _transport.MaxInputReportLength);
        var raw = new byte[length];
        reply = Array.Empty<byte>();
        why = string.Empty;
        int read = _transport.Read(raw);
        if (read < XboxWire.ReplyHeaderLength)
        {
            why = read <= 0 ? "no answer (timeout)" : $"short reply ({read} byte(s))";
            return false;
        }
        if (IsErrorReply(raw.AsSpan(0, read)))
        {
            why = "device error (0xC0: asleep or no keyboard on the dongle)";
            return false;
        }
        if (!IsReplyTo(cmd, offset, requestData, raw.AsSpan(0, read)))
        {
            why = $"reply to another request (cmd=0x{raw[3]:X2} ck=0x{raw[XboxWire.ReplyChecksum]:X2} " +
                  $"off={ReadU32(raw, XboxWire.ReplyOffset)} count={raw[4]})";
            return false;
        }
        reply = raw;
        return true;
    }

    /// <summary>
    /// Sends a request and waits for the reply that belongs to it.
    /// </summary>
    /// <remarks>
    /// The device's input endpoint is a HID input report: it <b>keeps repeating the last
    /// report</b> until it changes (that is why the idle keyboard streams its <c>0x09</c> status
    /// report, and why a read can hand us the previous command's answer). So the loop sends the
    /// request once and then <b>keeps reading</b> until a reply with the matching command,
    /// offset and checksum shows up, ignoring the repeats; only when nothing matches within
    /// <see cref="XboxWire.ReplyWaitMs"/> does it send the request again. Without this, a
    /// repeated report is consumed as if it were the answer to the next chunk and the whole
    /// configuration shifts by one chunk (found the hard way in the first hardware test).
    /// </remarks>
    private bool Query(byte cmd, byte subtype, uint offset, ReadOnlySpan<byte> data, int? length, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        LastError = null;
        for (int attempt = 0; attempt < XboxWire.MaxAttempts; attempt++)
        {
            if (!SendRequest(cmd, subtype, offset, data, length))
            {
                Trace?.Invoke($"cmd 0x{cmd:X2} sub {subtype} off {offset}: failed to send (attempt {attempt + 1})");
                continue;
            }

            int repeats = 0, timeouts = 0;
            bool errorReply = false;
            long start = Environment.TickCount64;
            long deadline = start + XboxWire.ReplyWaitMs;
            while (Environment.TickCount64 < deadline)
            {
                if (!TryReadReply(cmd, offset, data, out byte[] reply, out string why))
                {
                    if (why.StartsWith("no answer")) timeouts++;
                    else repeats++;
                    if (why.StartsWith("device error"))
                    {
                        // The device said "no" (0xC0). Retrying cannot change that, so give up at
                        // once and leave a diagnosis for the caller.
                        errorReply = true;
                        LastError = why;
                        break;
                    }
                    continue;
                }

                Trace?.Invoke($"cmd 0x{cmd:X2} sub {subtype} off {offset}: answered cmd=0x{reply[3]:X2} " +
                              $"count={reply[4]} ck=0x{reply[XboxWire.ReplyChecksum]:X2} " +
                              $"off={ReadU32(reply, XboxWire.ReplyOffset)} (attempt {attempt + 1}, " +
                              $"{repeats} repeated, {timeouts} timeout(s), {Environment.TickCount64 - start} ms)");
                int count = reply[4];
                int available = Math.Max(0, Math.Min(count, reply.Length - XboxWire.ReplyHeaderLength));
                payload = reply.AsSpan(XboxWire.ReplyHeaderLength, available).ToArray();
                return true;
            }
            if (errorReply)
            {
                Trace?.Invoke($"cmd 0x{cmd:X2} sub {subtype} off {offset}: the device answered with an error (0xC0); " +
                              "not retrying (asleep or no keyboard on the dongle)");
                break;
            }
            Trace?.Invoke($"cmd 0x{cmd:X2} sub {subtype} off {offset}: no matching report in " +
                          $"{XboxWire.ReplyWaitMs} ms ({repeats} repeated, {timeouts} timeout(s)); " +
                          $"resending (attempt {attempt + 1})");
        }
        LastError ??= $"cmd 0x{cmd:X2}: no answer after {XboxWire.MaxAttempts} attempts";
        Trace?.Invoke($"cmd 0x{cmd:X2} sub {subtype} off {offset}: GAVE UP — {LastError}");
        return false;
    }

    /// <summary>
    /// Sends one control frame and waits <b>once</b> for its reply, without re-sending. Used for
    /// the wake handshake and the one-shot ping, where repeating has no value.
    /// </summary>
    /// <param name="why">
    /// Most informative reason no reply matched: the device's <c>0xC0</c> error when it said no,
    /// otherwise the last thing that went wrong (silence, a foreign reply).
    /// </param>
    private bool QueryOnce(byte cmd, byte subtype, uint offset, ReadOnlySpan<byte> data, int? length,
        out byte[] payload, out string why)
    {
        payload = Array.Empty<byte>();
        why = string.Empty;
        if (!SendRequest(cmd, subtype, offset, data, length))
        {
            why = "failed to send the request";
            return false;
        }

        string lastWhy = string.Empty;
        string errorWhy = string.Empty;
        long deadline = Environment.TickCount64 + XboxWire.ReplyWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!TryReadReply(cmd, offset, data, out byte[] reply, out string reason))
            {
                // Keep the device's own "no" over a later timeout: it is the diagnosis the caller
                // cares about (asleep / no keyboard on the dongle).
                if (reason.StartsWith("device error") && errorWhy.Length == 0) errorWhy = reason;
                lastWhy = reason;
                continue;
            }
            int count = reply[4];
            int available = Math.Max(0, Math.Min(count, reply.Length - XboxWire.ReplyHeaderLength));
            payload = reply.AsSpan(XboxWire.ReplyHeaderLength, available).ToArray();
            return true;
        }

        why = errorWhy.Length > 0 ? errorWhy
            : lastWhy.Length > 0 ? lastWhy
            : "no answer (timeout)";
        return false;
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Reads the firmware version + battery (cmd <c>0x0A</c>), with the usual retries.</summary>
    public bool ReadVersion()
    {
        lock (_gate)
        {
            if (!Query(XboxWire.Cmd.Version, 0, 0, ReadOnlySpan<byte>.Empty, null, out byte[] data) || data.Length < 3)
                return false;

            ApplyVersion(data);
            return true;
        }
    }

    /// <summary>
    /// One-shot version request: tells whether the device answers <b>right now</b>, without the
    /// retries of <see cref="ReadVersion"/>. Used by the wake tool to report the state before and
    /// after the handshake (and to tell "asleep" from "no keyboard on the dongle").
    /// </summary>
    public bool Ping() => Ping(out _);

    /// <param name="detail">
    /// Why the one-shot got no answer (<c>0xC0</c> from the dongle, silence, a foreign reply), or
    /// empty when it answered. Lets a caller tell "the device said no" from "nobody is there".
    /// </param>
    /// <inheritdoc cref="Ping()"/>
    public bool Ping(out string detail)
    {
        lock (_gate)
        {
            if (!QueryOnce(XboxWire.Cmd.Version, 0, 0, ReadOnlySpan<byte>.Empty, null, out byte[] data, out detail) ||
                data.Length < 3)
            {
                LastError = detail;
                return false;
            }

            ApplyVersion(data);
            detail = string.Empty;
            return true;
        }
    }

    private void ApplyVersion(byte[] data)
    {
        BatteryPercent = data[0];
        int raw = data[1] | (data[2] << 8);
        FirmwareVersion = raw / 100f;
    }

    /// <summary>Reads the whole 1532-byte configuration (cmd <c>0x02</c>, chunked by 53 bytes).</summary>
    public XboxConfig? ReadXboxConfig()
    {
        lock (_gate)
        {
            if (!ReadRegion(0, XboxWire.ConfigSize, out byte[] raw) || raw.Length != XboxWire.ConfigSize)
            {
                // A keyboard that went idle between operations stops answering (and the dongle
                // says 02 04 C0 … with no keyboard on). When the caller opted into the wake
                // handshake, wake it and try the read once more before giving up.
                if (!WakeOnOpen || !Wake()) return null;
                if (!ReadRegion(0, XboxWire.ConfigSize, out raw) || raw.Length != XboxWire.ConfigSize)
                    return null;
                Trace?.Invoke("read: worked after the wake");
            }

            var cfg = new XboxConfig
            {
                Raw = raw,
                Flag = ReadU32(raw, XboxWire.FlagOffset),
                ProfileName = DecodeProfileName(raw, XboxWire.ProfileNameOffset, XboxWire.ProfileNameBytes),
                Enable = raw[XboxWire.EnableOffset],
                FnLock = raw[XboxWire.FnLockOffset],
                FastSetting = raw[XboxWire.FastSettingOffset],
                VolumeLevel = raw[XboxWire.VolumeLevelOffset],
                CurrentColorTheme = raw[XboxWire.CurrentColorThemeOffset],
                LedsSleep = ReadU32(raw, XboxWire.LedsSleepOffset),
                BatteryPercent = BatteryPercent,
                FirmwareVersion = FirmwareVersion,
            };

            for (int i = 0; i < XboxWire.MappingCount; i++)
            {
                int o = XboxWire.MappingBase + i * XboxWire.MappingSize;
                cfg.Mappings[i] = new XboxMapping
                {
                    KeyCode = raw[o],
                    Mapping = ReadU32(raw, o + 4),
                    Type = (XboxMappingType)ReadU32(raw, o + 8),
                };
            }

            // Macros: the original only reads the slots a MACRO mapping points at.
            foreach (XboxMapping mapping in cfg.Mappings)
            {
                if (mapping.Type != XboxMappingType.Macro || mapping.Mapping == 0) continue;
                int slot = (int)(mapping.Mapping / XboxWire.MacroSlotStride);
                if (slot < 0 || slot >= cfg.Macros.Length || cfg.Macros[slot] is not null) continue;
                cfg.Macros[slot] = ReadMacro(mapping.Mapping, mapping.KeyCode);
            }

            // Remember what the device holds: the write path diffs against this and the UI
            // backs it up before any write.
            _baseline = cfg;
            return cfg;
        }
    }

    /// <summary>
    /// Interface read: projections into the JP-shaped <see cref="KeyboardConfig"/> the UI
    /// edits (see <see cref="XboxToKeyboardConfig"/>). Returns an empty config on failure.
    /// </summary>
    public KeyboardConfig ReadConfig()
    {
        lock (_gate)
        {
            XboxConfig? xbox = ReadXboxConfig();
            return xbox is null ? new KeyboardConfig() : XboxToKeyboardConfig.Convert(xbox);
        }
    }

    /// <summary>Reads <paramref name="length"/> bytes of the configuration starting at <paramref name="offset"/>.</summary>
    public bool ReadRegion(uint offset, int length, out byte[] data)
        => ReadRegionCore(XboxWire.Cmd.Read, offset, length, out data);

    /// <summary>
    /// Reads <paramref name="length"/> bytes of the <b>macro</b> address space (<c>cmd 0x07</c>)
    /// starting at <paramref name="offset"/> — the same space <see cref="ReadMacro"/> uses. It is
    /// separate from the configuration region: <c>offset 0</c> here is slot 0's info record, not
    /// the profile header.
    /// </summary>
    public bool ReadMacroRegion(uint offset, int length, out byte[] data)
        => ReadRegionCore(XboxWire.Cmd.MacroRead, offset, length, out data);

    private bool ReadRegionCore(byte cmd, uint offset, int length, out byte[] data)
    {
        data = Array.Empty<byte>();
        var blob = new byte[length];
        int position = 0;
        while (position < length)
        {
            int remaining = length - position;
            int chunk = Math.Min(remaining, XboxWire.MaxChunk);
            if (!Query(cmd, 0, offset + (uint)position, ReadOnlySpan<byte>.Empty, chunk, out byte[] payload))
                return false;
            if (payload.Length == 0) return false;
            int copy = Math.Min(payload.Length, remaining);
            Array.Copy(payload, 0, blob, position, copy);
            position += copy;
        }
        data = blob;
        return true;
    }

    // ---------------------------------------------------------------- macros (cmd 0x07, read-only)

    /// <summary>
    /// Reads and decodes one macro slot referenced by a <c>MACRO</c> mapping. The mapping's
    /// <c>mapping</c> field is the slot's device offset; slot index is <c>offset / 4096</c>,
    /// the info record is 48 bytes at that offset and the steps start 64 bytes in
    /// (see <c>the macro reader</c>).
    /// </summary>
    public XboxMacroSlot? ReadMacro(uint offset, byte hostKeyCode)
    {
        if (!ReadRegionCore(XboxWire.Cmd.MacroRead, offset, XboxWire.MacroInfoSize, out byte[] info) ||
            info.Length != XboxWire.MacroInfoSize)
            return null;

        var slot = new XboxMacroSlot
        {
            Slot = (int)(offset / XboxWire.MacroSlotStride),
            HostKeyCode = hostKeyCode,
            Flag = ReadU32(info, XboxWire.MacroInfo.Flag),
            MacroKeyCode = info[XboxWire.MacroInfo.KeyCode],
            Name = DecodeProfileName(info, XboxWire.MacroInfo.Name, XboxWire.MacroInfo.NameBytes),
            MaxStep = ReadU16(info, XboxWire.MacroInfo.MaxStep),
            Interval = ReadU16(info, XboxWire.MacroInfo.Interval),
            Repeat = info[XboxWire.MacroInfo.Repeat],
            RepeatType = info[XboxWire.MacroInfo.RepeatType],
            UnifyTime = ReadU16(info, XboxWire.MacroInfo.UnifyTime),
            RawInfo = info,
        };

        if (slot.MaxStep is > 0 and <= XboxWire.MacroMaxSteps)
        {
            int bytes = slot.MaxStep * XboxWire.MacroStepSize;
            if (ReadRegionCore(XboxWire.Cmd.MacroRead, offset + XboxWire.MacroDataOffset, bytes, out byte[] steps) &&
                steps.Length == bytes)
            {
                for (int i = 0; i < slot.MaxStep; i++)
                {
                    int o = i * XboxWire.MacroStepSize;
                    slot.Steps.Add(new XboxMacroStep
                    {
                        KeyState = steps[o],
                        KeyCode = steps[o + 1],
                        Timer = ReadU16(steps, o + 2),
                    });
                }
            }
        }
        return slot;
    }

    // ---------------------------------------------------------------- parsing helpers

    internal static uint ReadU32(ReadOnlySpan<byte> buffer, int offset)
        => (uint)(buffer[offset]
                  | (buffer[offset + 1] << 8)
                  | (buffer[offset + 2] << 16)
                  | (buffer[offset + 3] << 24));

    internal static ushort ReadU16(ReadOnlySpan<byte> buffer, int offset)
        => (ushort)(buffer[offset] | (buffer[offset + 1] << 8));

    /// <summary>
    /// Decodes the profile/macro name. The Xbox on-wire field is <b>UTF-16 big-endian</b>
    /// (observed <c>00 53 00 6b 00 69 …</c> = "Skipult"), unlike the JP family which is
    /// little-endian. Stops at the first zero code unit, or at <c>0xFFFF</c> (erased flash).
    /// </summary>
    internal static string DecodeProfileName(ReadOnlySpan<byte> buffer, int offset, int bytes)
    {
        var sb = new StringBuilder(bytes / 2);
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            int unit = (buffer[offset + i] << 8) | buffer[offset + i + 1];
            if (unit == 0 || unit == 0xFFFF) break;
            sb.Append((char)unit);
        }
        return sb.ToString();
    }
}
