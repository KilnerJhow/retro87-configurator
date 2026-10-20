// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Protocol;

/// <summary>
/// Device-level protocol of the 8BitDo Retro keyboard family ("JP"), implemented over
/// <see cref="IKeyboardTransport"/> so that it runs on Linux without a vendor library.
/// </summary>
/// <remarks>
/// <para>
/// The frame constants are documented in <see cref="JpWire"/>. The high-level call order
/// follows the read and write flows of the official application.
/// </para>
/// <para>
/// Commands cover version/battery, profile name, mapping read and write, macro read and
/// write, special feature and voice.
/// </para>
/// </remarks>
public sealed class JpProtocol : IJpProtocol
{
    private readonly IKeyboardTransport _transport;
    private readonly object _gate = new();

    public JpProtocol(IKeyboardTransport transport) => _transport = transport;

    public bool IsOpen => _transport.IsOpen;

    /// <summary>The JP family supports writing (guarded by the UI).</summary>
    public bool CanWrite => true;

    /// <summary>
    /// Always 0: this family exposes no HID command that reports its firmware version, and
    /// this port does not shell out the way the original software does.
    /// </summary>
    /// <remarks>
    /// On Windows the version is obtained by shelling out to a bundled firmware tool that
    /// speaks to the device over a named pipe. That path is Windows-only and
    /// firmware-adjacent, so this app reports 0 here and
    /// <see cref="Retro87.Services.FirmwareUpdateChecker"/> notifies without a device version.
    /// </remarks>
    public float DeviceVersion => 0f;

    /// <summary>No battery read on Linux for this family (the official tool does it on Windows).</summary>
    public int BatteryPercent => -1;

    // ---------------------------------------------------------------- lifecycle

    public bool Open()
    {
        lock (_gate)
        {
            if (!_transport.Open()) return false;

            // The read flow starts by selecting the profile with report 0xFF and
            // ignores the result, so a failure here is not fatal.
            SwitchReport(0xFF);
            return true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _transport.Close();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _transport.Dispose();
        }
    }

    // ---------------------------------------------------------------- primitives

    /// <summary>Sends one keyboard frame: report id <see cref="JpWire.ReportIdOut"/> + 32 payload bytes.</summary>
    private bool WritePayload(byte[] payload)
    {
        var frame = new byte[JpWire.FrameLength];
        frame[0] = JpWire.ReportIdOut;
        Array.Copy(payload, 0, frame, 1, Math.Min(payload.Length, JpWire.PayloadLength));
        return _transport.Write(frame) > 0;
    }

    /// <summary>
    /// Reads one keyboard frame, dropping the report id exactly like <c>_ReadHidJP@8</c>
    /// does (<c>resp[i] == rawReport[i + 1]</c>). Returns false when nothing was read.
    /// </summary>
    private bool ReadPayload(out byte[] resp)
    {
        int length = Math.Max(JpWire.FrameLength, _transport.MaxInputReportLength);
        var raw = new byte[length];
        resp = new byte[JpWire.PayloadLength];

        int read = _transport.Read(raw);
        if (read <= 0) return false;

        int copy = Math.Min(JpWire.PayloadLength, read - 1);
        if (copy > 0) Array.Copy(raw, 1, resp, 0, copy);
        return true;
    }

    private static byte[] BuildPayload(byte command, params byte[] args)
    {
        var payload = new byte[JpWire.PayloadLength];
        payload[0] = command;
        int copy = Math.Min(args.Length, JpWire.PayloadLength - 1);
        if (copy > 0) Array.Copy(args, 0, payload, 1, copy);
        return payload;
    }

    /// <summary>
    /// Sends a command and polls for its reply, re-sending when a read fails, exactly like
    /// the native Get* exports (30 reads, <see cref="JpWire.ReadGapMs"/> apart).
    /// </summary>
    private bool Query(byte command, byte[] args, int gapMs, out byte[] resp)
    {
        byte[] payload = BuildPayload(command, args);
        WritePayload(payload);

        for (int attempt = 0; attempt < JpWire.MaxReadAttempts; attempt++)
        {
            Thread.Sleep(gapMs);
            if (!ReadPayload(out resp))
            {
                WritePayload(payload);
                continue;
            }
            if (resp[0] == command) return true;
        }

        resp = new byte[JpWire.PayloadLength];
        return false;
    }

    /// <summary>
    /// Sends one write frame and waits for the <c>0xE4 0x08</c> acknowledgement, exactly the
    /// loop the native Set*/write* exports run (<paramref name="attempts"/> reads,
    /// <paramref name="gapMs"/> apart). When <paramref name="resendOnBadReply"/> is set the
    /// frame is re-sent after a reply that is not an acknowledgement, like
    /// <c>writeName</c>/<c>writeMacroName</c>/<c>writeMacroJP</c> do.
    /// </summary>
    private bool SendAndWaitAck(byte[] payload, int gapMs, int attempts, bool resendOnBadReply = false)
    {
        WritePayload(payload);
        return WaitForAck(gapMs, attempts, resendOnBadReply ? payload : null);
    }

    /// <summary>
    /// Writes every chunked frame and then waits for a single acknowledgement, which is what
    /// the native chunked writers do. The last frame is the one they re-send on a bad reply.
    /// </summary>
    private bool SendFramesAndWaitAck(IReadOnlyList<byte[]> frames, int gapMs, int attempts, bool resendOnBadReply)
    {
        foreach (byte[] frame in frames) WritePayload(frame);
        return WaitForAck(gapMs, attempts, resendOnBadReply ? frames[frames.Count - 1] : null);
    }

    private bool WaitForAck(int gapMs, int attempts, byte[]? resendFrame)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Thread.Sleep(gapMs);
            if (!ReadPayload(out byte[] resp)) continue;

            if (resp[0] == JpWire.AckFirst && resp[1] == JpWire.AckSecond) return true;

            if (resendFrame is not null)
            {
                Thread.Sleep(gapMs);
                WritePayload(resendFrame);
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- simple commands

    /// <summary>Mirrors <c>_SwitchReport@4</c> (command 0x76): selects the active profile.</summary>
    public bool SwitchReport(byte report)
        => SendAndWaitAck(BuildPayload(JpWire.Cmd.SwitchReport, report), JpWire.ReadGapMs, JpWire.AckAttempts);

    /// <summary>Mirrors <c>_SwitchReportExtra@4</c> (command 0x7B). Not used by v1.</summary>
    public bool SwitchReportExtra(byte report)
        => SendAndWaitAck(BuildPayload(JpWire.Cmd.SwitchReportExtra, report), JpWire.ReadGapMs, JpWire.AckAttempts);

    /// <summary>Mirrors <c>_SetFeature@4</c> (command 0x78) — the "special feature" byte.</summary>
    public bool SetFeature(byte feature)
        => SendAndWaitAck(BuildPayload(JpWire.Cmd.SetFeature, feature), JpWire.ReadGapMs, JpWire.AckAttempts);

    /// <summary>Mirrors <c>_setVoice@4</c> (command 0x79); the native code waits 100 ms per read.</summary>
    public bool SetVoice(byte voice)
        => SendAndWaitAck(BuildPayload(JpWire.Cmd.SetVoice, voice), JpWire.VoiceGapMs, JpWire.VoiceAckAttempts);

    /// <summary>
    /// Mirrors <c>_GetBattery@4</c> (command 0x06): the reply's first 16 payload bytes.
    /// </summary>
    public bool GetBattery(out byte[] battery)
    {
        battery = new byte[16];
        if (!Query(JpWire.Cmd.GetBattery, Array.Empty<byte>(), JpWire.ReadGapMs, out byte[] resp)) return false;
        Array.Copy(resp, 0, battery, 0, Math.Min(battery.Length, resp.Length));
        return true;
    }

    // ---------------------------------------------------------------- profile name

    /// <summary>
    /// Mirrors <c>_GetProfileName@4</c> (command 0x80): 29 + 29 + 3 payload bytes taken from
    /// <c>resp[3..]</c>, with <c>resp[2] == 1</c> meaning "another chunk follows".
    /// </summary>
    public bool GetProfileName(out byte[] name)
    {
        name = new byte[JpWire.NameLength];
        int offset = 0;

        for (int chunk = 0; chunk < 3; chunk++)
        {
            if (!Query(JpWire.Cmd.GetProfileName, Array.Empty<byte>(), JpWire.ReadGapMs, out byte[] resp)) return false;

            int copy = chunk == 2 ? 3 : 29;
            Array.Copy(resp, 3, name, offset, Math.Min(copy, resp.Length - 3));
            offset += copy;

            if (chunk == 2 || resp[2] != 1) break;
        }
        return true;
    }

    public bool WakeRawSignal(string profileName) => WriteProfileName(profileName);

    /// <summary>
    /// Mirrors <c>writeName</c> (command 0x70): 61 UTF-16 bytes in chunks of 29 with the
    /// fields <c>[0x70, length, more, 29 data]</c>. The official software does not write the
    /// profile name for this family, so it is read-only there; this is kept because the frame
    /// is verified and a future firmware may accept it.
    /// </summary>
    public bool WriteProfileName(string name)
    {
        int length = Math.Min(DeviceNameCodec.WireLength(name), JpWire.NameLength);

        var source = new byte[3 * JpWire.ProfileNameChunk];
        Array.Copy(DeviceNameCodec.Encode(name), source, DeviceNameCodec.NameBytes);

        var frames = new List<byte[]>();

        byte[] first = BuildPayload(JpWire.Cmd.WriteProfileName);
        first[1] = (byte)(length < 30 ? length : JpWire.ProfileNameChunk);
        first[2] = (byte)(length >= 30 ? 1 : 0);
        Array.Copy(source, 0, first, 3, JpWire.ProfileNameChunk);
        frames.Add(first);

        if (length > 29)
        {
            byte[] second = BuildPayload(JpWire.Cmd.WriteProfileName);
            second[1] = (byte)(length < 59 ? length - 29 : JpWire.ProfileNameChunk);
            second[2] = (byte)(length >= 59 ? 1 : 0);
            Array.Copy(source, 29, second, 3, JpWire.ProfileNameChunk);
            frames.Add(second);
        }

        if (length > 58)
        {
            byte[] third = BuildPayload(JpWire.Cmd.WriteProfileName);
            third[1] = (byte)(length - 58);
            third[2] = 0;
            Array.Copy(source, 58, third, 3, JpWire.ProfileNameChunk);
            frames.Add(third);
        }

        return SendFramesAndWaitAck(frames, JpWire.ReadGapMs, JpWire.AckAttempts, resendOnBadReply: false);
    }

    // ---------------------------------------------------------------- mappings

    /// <summary>
    /// Mirrors <c>_GetMapping@4</c> (command 0x81): up to 192 payload bytes in chunks of 30
    /// taken from <c>resp[1..30]</c>, <c>resp[31] != 0</c> meaning "another chunk follows".
    /// The caller's array holds 191 bytes, and every even offset is a physical key id with a
    /// custom mapping (a zero id terminates the list).
    /// </summary>
    public bool GetMapping(out byte[] mapping)
    {
        mapping = new byte[191];
        int offset = 0;

        while (offset < mapping.Length)
        {
            if (!Query(JpWire.Cmd.GetMapping, Array.Empty<byte>(), JpWire.MappingGapMs, out byte[] resp)) return false;

            int copy = Math.Min(30, mapping.Length - offset);
            Array.Copy(resp, 1, mapping, offset, copy);
            offset += 30;

            if (resp[31] == 0) return true;
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>_GetMappingValue@8</c> (command 0x83): 30 payload bytes from
    /// <c>resp[1..30]</c>. The mapping value the original decodes is
    /// <c>value[2..6]</c> — see <see cref="MappingPayloadCodec"/>.
    /// </summary>
    public bool GetMappingValue(byte keyId, out byte[] value)
    {
        value = new byte[33];
        if (!Query(JpWire.Cmd.GetMappingValue, new[] { keyId }, JpWire.ReadGapMs, out byte[] resp)) return false;
        Array.Copy(resp, 1, value, 0, Math.Min(30, resp.Length - 1));
        return true;
    }

    /// <summary>Mirrors <c>_SetMapping@8</c> (keyboard remap, transport selector 0x07).</summary>
    public bool SetMapping(byte keyId, byte[] value) => SendMappingEntry(JpWire.MappingKindKeyboard, keyId, value);

    /// <summary>Mirrors <c>_SetShortCut@8</c> (transport selector 0x0C).</summary>
    public bool SetShortCut(byte keyId, byte[] value) => SendMappingEntry(JpWire.MappingKindShortcut, keyId, value);

    /// <summary>Mirrors <c>_SetMouse@8</c> (transport selector 0x01, five value bytes).</summary>
    public bool SetMouse(byte keyId, byte[] value) => SendMappingEntry(JpWire.MappingKindMouse, keyId, value);

    private bool SendMappingEntry(byte kind, byte keyId, byte[] value)
    {
        var payload = new byte[JpWire.PayloadLength];
        Array.Copy(JpWire.MappingPrelude, payload, JpWire.MappingPrelude.Length);
        payload[7] = keyId;
        payload[8] = kind;

        int needed = kind == JpWire.MappingKindMouse
            ? JpWire.MappingValueLengthMouse
            : JpWire.MappingValueLengthKeyboard;
        Array.Copy(value, 0, payload, 9, Math.Min(needed, value.Length));

        // The mapping classifier: the transport and the read timing depend on which kind of
        // target the id is.
        return kind switch
        {
            JpWire.MappingKindMouse => SendAndWaitAck(payload, JpWire.ReadGapMs, JpWire.AckAttempts),
            JpWire.MappingKindKeyboard => SendAndWaitAck(payload, JpWire.MappingGapMs, JpWire.MappingAckAttempts),
            _ => SendAndWaitAck(payload, JpWire.MappingGapMs, JpWire.AckAttempts),
        };
    }

    /// <summary>
    /// Mirrors <c>the mapping classifier</c>: converts the target mapping id into its
    /// payload and sends it over the transport the mapping classifier selects.
    /// </summary>
    public bool SetMappingEntry(byte keyId, byte targetId)
    {
        byte[] value = MappingPayloadCodec.EncodePayload(targetId);
        return MappingTables.TransportFor(targetId) switch
        {
            KeyboardTransport.Mouse => SetMouse(keyId, value),
            KeyboardTransport.Shortcut => SetShortCut(keyId, value),
            _ => SetMapping(keyId, value),
        };
    }

    // ---------------------------------------------------------------- macros

    /// <summary>
    /// Mirrors <c>_GetMacro@4</c> (command 0x82): 32 payload bytes — the macro host key ids at
    /// offsets 0, 4, 8, …, terminated by a zero id (<c>resp[29] != 0</c> means a second chunk
    /// of 4 bytes follows).
    /// </summary>
    public bool GetMacro(out byte[] macroKeys)
    {
        macroKeys = new byte[32];

        if (!Query(JpWire.Cmd.GetMacro, Array.Empty<byte>(), JpWire.ReadGapMs, out byte[] resp)) return false;
        Array.Copy(resp, 1, macroKeys, 0, Math.Min(28, resp.Length - 1));

        if (resp[29] != 0)
        {
            if (!Query(JpWire.Cmd.GetMacro, Array.Empty<byte>(), JpWire.ReadGapMs, out byte[] resp2)) return false;
            Array.Copy(resp2, 1, macroKeys, 28, Math.Min(4, resp2.Length - 1));
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>_GetMacroName@8</c> (command 0x84): 61 name bytes in chunks of 28 taken from
    /// <c>resp[4..31]</c>, with <c>resp[3] != 0</c> meaning "another chunk follows".
    /// </summary>
    public bool GetMacroName(byte keyId, out byte[] name)
    {
        name = new byte[DeviceNameCodec.NameBytes];
        int offset = 0;

        for (int chunk = 0; chunk < 3; chunk++)
        {
            if (!Query(JpWire.Cmd.GetMacroName, new[] { keyId }, JpWire.ReadGapMs, out byte[] resp)) return false;

            int copy = Math.Min(28, name.Length - offset);
            if (copy > 0) Array.Copy(resp, 4, name, offset, Math.Min(copy, resp.Length - 4));
            offset += 28;

            if (resp[3] == 0) break;
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>_GetMacroValue@8</c> (command 0x86): the 609-byte macro record, in chunks of
    /// 26 bytes taken from <c>resp[3..28]</c>, with <c>resp[2] == 0</c> meaning "done".
    /// </summary>
    public bool GetMacroValue(byte keyId, out byte[] record)
    {
        record = new byte[JpWire.MacroRecordLength];
        int offset = 0;

        while (offset < record.Length)
        {
            if (!Query(JpWire.Cmd.GetMacroValue, new[] { keyId }, JpWire.ReadGapMs, out byte[] resp)) return false;

            int copy = Math.Min(26, record.Length - offset);
            if (copy > 0) Array.Copy(resp, 3, record, offset, Math.Min(copy, resp.Length - 3));
            offset += 26;

            if (resp[2] == 0) break;
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>writeMacroName</c> (command 0x74): the 61-byte UTF-16 name in chunks of 28
    /// with the fields <c>[0x74, key, length, more, 28 data]</c>. The length is the byte index
    /// of the field's first zero code unit, computed exactly like
    /// <c>the macro writer</c> does.
    /// </summary>
    /// <remarks>
    /// Chunk boundaries: the first chunk carries <c>min(length, 0x1C)</c> bytes, the second
    /// <c>min(length, 0x1C)</c> more, and the third the remaining <c>length - 0x38</c>.
    /// </remarks>
    public bool WriteMacroName(byte keyId, byte[] nameField)
    {
        int length = MacroNameWireLength(nameField);

        var source = new byte[3 * JpWire.MacroNameChunk];
        Array.Copy(nameField, source, Math.Min(nameField.Length, source.Length));

        var frames = new List<byte[]>();

        byte[] first = BuildPayload(JpWire.Cmd.WriteMacroName, keyId);
        first[2] = (byte)(length > 0x1C ? 0x1C : length);
        first[3] = (byte)(length > 0x1C ? 1 : 0);
        Array.Copy(source, 0, first, 4, JpWire.MacroNameChunk);
        frames.Add(first);

        if (length > 0x1C)
        {
            byte[] second = BuildPayload(JpWire.Cmd.WriteMacroName, keyId);
            second[2] = (byte)(length > 0x38 ? 0x1C : length - 0x1C);
            second[3] = (byte)(length > 0x38 ? 1 : 0);
            Array.Copy(source, 0x1C, second, 4, JpWire.MacroNameChunk);
            frames.Add(second);
        }

        if (length > 0x38)
        {
            byte[] third = BuildPayload(JpWire.Cmd.WriteMacroName, keyId);
            third[2] = (byte)(length - 0x38);
            third[3] = 0;
            Array.Copy(source, 0x38, third, 4, JpWire.MacroNameChunk);
            frames.Add(third);
        }

        return SendFramesAndWaitAck(frames, JpWire.MacroWriteGapMs, JpWire.MacroWriteAckAttempts, resendOnBadReply: true);
    }

    /// <summary>
    /// Writes a macro (command 0x76). <paramref name="data"/> is the macro body: three bytes
    /// per step, optionally followed by the four-byte delay entry <c>[0x0F, msLo, msHi, 0]</c>
    /// that the macro writer appends. <paramref name="cycles"/> is the loop count, 0xFF meaning
    /// "loop forever".
    /// </summary>
    /// <remarks>
    /// The record layout is <c>[0x01, cycles, 0x20|0, stepCount, steps…]</c>, where the first
    /// chunk carries the four header bytes and the continuation chunks are placed at the
    /// record offset <c>cursor + 4</c>.
    /// <para>
    /// The first chunk carries one byte short of the full body, so the byte at record offset
    /// 24 is the frame's zero padding. That is harmless whenever the byte at body offset 20 is
    /// the zero third byte of a key step, which is the case for every macro with seven or more
    /// steps; it only drops the delay's high byte for a six-step macro. This app writes the
    /// 21st byte instead, which keeps the record contiguous and matches the device byte for
    /// byte everywhere else.
    /// </para>
    /// </remarks>
    public bool WriteMacro(byte keyId, byte[] data, byte cycles)
        => SendFramesAndWaitAck(
            BuildMacroFrames(keyId, data, cycles),
            JpWire.MacroWriteGapMs,
            JpWire.MacroWriteAckAttempts,
            resendOnBadReply: true);

    /// <summary>
    /// Builds the chunk frames of <c>writeMacroJP</c>. Split out from
    /// <see cref="WriteMacro"/> so the chunk arithmetic can be verified without hardware.
    /// </summary>
    internal static List<byte[]> BuildMacroFrames(byte keyId, byte[] data, byte cycles)
    {
        int length = data.Length;
        var frames = new List<byte[]>();

        byte[] first = BuildPayload(JpWire.Cmd.WriteMacro, keyId);
        first[6] = JpWire.MacroRecordMarker;
        if (cycles == JpWire.MacroCyclesInfinite) first[8] = JpWire.MacroInfiniteFlag;
        else first[7] = cycles;
        first[9] = (byte)(length / JpWire.MacroStepBytes);

        if (length < 0x16)
        {
            first[5] = 0x1A;
            Array.Copy(data, 0, first, 10, Math.Min(length, JpWire.MacroPayloadLengthLong));
            frames.Add(first);
        }
        else
        {
            first[2] = 1;                       // "another chunk follows"
            first[5] = 0x19;                    // length field of the header chunk
            Array.Copy(data, 0, first, 10, JpWire.MacroPayloadLengthLong);
            frames.Add(first);

            int cursor = JpWire.MacroPayloadLengthLong;   // 21, as in the native code
            while (cursor < length)
            {
                byte[] chunk = BuildPayload(JpWire.Cmd.WriteMacro, keyId);

                if (length - cursor <= JpWire.MacroChunkLength)   // native: remaining < 0x19
                {
                    chunk[2] = 0;
                    chunk[5] = (byte)(length - cursor);
                    WriteOffset(chunk, cursor + JpWire.MacroHeaderLength);
                    Array.Copy(data, cursor, chunk, 6, length - cursor);
                    cursor = length;
                }
                else
                {
                    chunk[2] = 1;
                    chunk[5] = JpWire.MacroChunkLength;
                    WriteOffset(chunk, cursor + JpWire.MacroHeaderLength);
                    Array.Copy(data, cursor, chunk, 6, JpWire.MacroChunkLength);
                    cursor += JpWire.MacroChunkLength;
                }
                frames.Add(chunk);
            }
        }

        return frames;
    }

    private static void WriteOffset(byte[] frame, int recordOffset)
    {
        frame[3] = (byte)(recordOffset & 0xFF);
        frame[4] = (byte)((recordOffset >> 8) & 0xFF);
    }

    /// <summary>
    /// Mirrors <c>the macro writer</c>: writes one macro slot — the name first, then the
    /// step body. Slots without a name are skipped, exactly as in the original.
    /// </summary>
    public bool WriteMacroSlot(MacroSlot slot)
    {
        if (string.IsNullOrEmpty(slot.Name)) return true;

        if (!WriteMacroName(slot.HostKey, DeviceNameCodec.Encode(slot.Name))) return false;

        return WriteMacro(slot.HostKey, BuildMacroBody(slot.Actions, slot.IntervalMs), slot.Cycles);
    }

    /// <summary>
    /// Builds the macro body written by <c>writeMacroJP</c>: three bytes per step, plus the
    /// four-byte delay entry <c>[0x0F, msLo, msHi, 0]</c> that the original
    /// the macro writer appends when the slot has a delay or more than one step.
    /// </summary>
    internal static byte[] BuildMacroBody(IReadOnlyList<MacroAction> actions, ushort intervalMs)
    {
        var steps = new List<byte>();
        int stepCount = 0;
        foreach (MacroAction action in actions)
        {
            if (action.Kind == MacroActionKind.Null) break;
            steps.AddRange(action.Encode());
            stepCount++;
        }

        byte[] data = steps.ToArray();
        if (intervalMs > 0 || stepCount > 1)
        {
            // The original appends four bytes and writes only three of them.
            var padded = new byte[data.Length + 4];
            Array.Copy(data, padded, data.Length);
            padded[data.Length] = JpWire.MacroStepTime;
            padded[data.Length + 1] = (byte)(intervalMs & 0xFF);
            padded[data.Length + 2] = (byte)((intervalMs >> 8) & 0xFF);
            data = padded;
        }
        return data;
    }

    /// <summary>
    /// Mirrors the 16-bit zero-pair scan of <c>the macro writer</c>: the byte index of
    /// the first zero code unit, or 0 when the field is completely filled.
    /// </summary>
    private static int MacroNameWireLength(byte[] nameField)
    {
        int length = 0;
        for (int i = 0; i < nameField.Length / 2; i++)
        {
            if (i != 30 && nameField[i * 2] == 0 && nameField[i * 2 + 1] == 0)
            {
                length = i * 2;
                break;
            }
        }
        return length;
    }

    // ---------------------------------------------------------------- special feature / voice

    /// <summary>Mirrors <c>_GetSpecial@4</c> (command 0x88); 28 payload bytes from <c>resp[1..]</c>.</summary>
    public bool GetSpecial(out byte[] special)
    {
        special = new byte[32];
        if (!Query(JpWire.Cmd.GetSpecial, Array.Empty<byte>(), JpWire.ReadGapMs, out byte[] resp)) return false;
        Array.Copy(resp, 1, special, 0, Math.Min(28, resp.Length - 1));
        return true;
    }

    /// <summary>Mirrors <c>_GetVoice@4</c> (command 0x89); 28 payload bytes from <c>resp[1..]</c>.</summary>
    public bool GetVoice(out byte[] voice)
    {
        voice = new byte[32];
        if (!Query(JpWire.Cmd.GetVoice, Array.Empty<byte>(), JpWire.ReadGapMs, out byte[] resp)) return false;
        Array.Copy(resp, 1, voice, 0, Math.Min(28, resp.Length - 1));
        return true;
    }

    // ---------------------------------------------------------------- whole configuration

    /// <summary>
    /// Reads the whole configuration, in the order <c>the read flow</c> uses:
    /// profile name, mappings (per key), macros (per slot), special feature and voice.
    /// Board keys without a custom mapping keep their
    /// <see cref="KeyboardLayout.DefaultMappingId"/> — the original also starts from
    /// the board key table and never reads the key-to-id assignment from the device.
    /// </summary>
    /// <remarks>
    /// Unlike the original, a failing sub-read does not abort the whole operation: the caller
    /// gets everything that could be read, which is more useful for a status display.
    /// </remarks>
    public KeyboardConfig ReadConfig()
    {
        lock (_gate)
        {
            var config = new KeyboardConfig();
            Array.Copy(KeyboardLayout.DefaultMappingId, config.BoardKeys, KeyboardLayout.PositionCount);

            SwitchReport(0xFF);

            if (GetProfileName(out byte[] name))
                config.ProfileName = DeviceNameCodec.Decode(name);

            if (GetMapping(out byte[] table))
            {
                for (int index = 0; index + 1 < table.Length; index += 2)
                {
                    byte keyId = table[index];
                    if (keyId == 0) break;
                    if (!GetMappingValue(keyId, out byte[] value)) break;

                    int position = KeyboardLayout.PositionOfKeyId(keyId);
                    if (position < 0) continue;

                    // getMapping's input is button[i] = value[i + 1]: button[0] is the transport
                    // byte (7 keyboard / 12 shortcut / 1 mouse) and button[1..5] the payload.
                    config.BoardKeys[position] = MappingPayloadCodec.DecodeId(
                        value[1],
                        new ReadOnlySpan<byte>(value, 2, MappingPayloadCodec.PayloadLength));
                }
            }

            if (GetMacro(out byte[] macroKeys))
            {
                int slot = 0;
                for (int i = 0; i < macroKeys.Length && slot < JpWire.MacroSlotCount; i += 4)
                {
                    byte keyId = macroKeys[i];
                    if (keyId == 0) break;
                    if (!GetMacroName(keyId, out byte[] macroName)) break;
                    if (!GetMacroValue(keyId, out byte[] record)) break;

                    DecodeMacroSlot(config.Macros[slot], keyId, macroName, record);
                    slot++;
                }
            }

            if (GetSpecial(out byte[] special)) config.SpecialFeature = special[0];
            if (GetVoice(out byte[] voice)) config.Voice = voice[0];

            return config;
        }
    }

    /// <summary>
    /// Writes the whole configuration, in the order <c>the write flow</c> uses:
    /// special feature, mappings, voice, then the eight macro slots.
    /// </summary>
    /// <param name="config">Configuration to write.</param>
    /// <param name="force">
    /// When false (the original behaviour) only board keys whose target differs from their
    /// default are sent, so resetting a key that the device currently remaps has no effect;
    /// set it to true to write all 95 positions.
    /// </param>
    public bool WriteConfig(KeyboardConfig config, bool force = false)
    {
        lock (_gate)
        {
            if (config.SpecialFeature != 0 && !SetFeature(config.SpecialFeature)) return false;

            for (int position = 0; position < JpWire.BoardPositionCount; position++)
            {
                byte target = config.BoardKeys[position];
                if (!force && target == KeyboardLayout.DefaultMappingId[position]) continue;
                if (!SetMappingEntry(KeyboardLayout.PositionKeyId[position], target)) return false;
            }

            if (config.Voice != 2 && !SetVoice(config.Voice)) return false;

            for (int slot = 0; slot < JpWire.MacroSlotCount; slot++)
            {
                if (!WriteMacroSlot(config.Macros[slot])) return false;
            }
            return true;
        }
    }

    internal static void DecodeMacroSlot(MacroSlot slot, byte keyId, byte[] nameField, byte[] record)
    {
        slot.HostKey = keyId;
        slot.Name = DeviceNameCodec.Decode(nameField);
        slot.Actions.Clear();

        if (record.Length < JpWire.MacroHeaderLength + JpWire.MacroStepBytes) return;

        slot.Cycles = record[2] == JpWire.MacroInfiniteFlag ? JpWire.MacroCyclesInfinite : record[1];

        int count = record[3];
        if (count <= 0) return;

        // The macro reader: the step entries start at index 4, and the
        // last one is the slot's delay when it is a 0x0F entry.
        int lastEntry = count * JpWire.MacroStepBytes + 1;   // == 4 + 3 * (count - 1)
        bool lastIsTime = lastEntry + 2 < record.Length && record[lastEntry] == JpWire.MacroStepTime;
        if (lastIsTime)
            slot.IntervalMs = (ushort)(record[lastEntry + 1] | (record[lastEntry + 2] << 8));

        for (int i = 0; i < count; i++)
        {
            if (lastIsTime && i == count - 1) break;

            int offset = JpWire.MacroHeaderLength + i * JpWire.MacroStepBytes;
            if (offset + 2 >= record.Length) break;
            slot.Actions.Add(DecodeStep(record, offset));
        }
    }

    private static MacroAction DecodeStep(byte[] record, int offset)
    {
        byte op = record[offset];
        byte value = record[offset + 1];

        return op switch
        {
            JpWire.MacroStepTime => new MacroAction
            {
                Kind = MacroActionKind.Time,
                DelayMs = (ushort)(value | (record[offset + 2] << 8)),
                Op = op,
            },
            0x81 or 0x83 => new MacroAction { Kind = MacroActionKind.KeyDown, Usage = value, Op = op },
            0x01 or 0x03 => new MacroAction { Kind = MacroActionKind.KeyUp, Usage = value, Op = op },
            _ => new MacroAction { Kind = MacroActionKind.Null, Usage = value, Op = op },
        };
    }
}
