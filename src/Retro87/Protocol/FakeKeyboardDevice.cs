// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Protocol;

/// <summary>
/// The keyboard side of the JP wire protocol, modelled so <see cref="JpProtocol"/> can be
/// driven end to end without hardware (see the "protocol dry run" section of
/// <c>Retro87 --selftest</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a model built from the same frame documentation the port itself is written from
/// (<see cref="JpWire"/> and the reply offsets in <see cref="JpProtocol"/>), <b>not</b> a
/// capture of a real device. It can therefore prove that the port is <i>self-consistent</i>
/// — a configuration written to it reads back equal, frames have the documented shape, the
/// retry loops recover from lost frames — but it cannot prove that the real keyboard behaves
/// the same way. The hardware session still has to confirm the reply offsets, the
/// acknowledgement behaviour and the <c>writeMacroJP</c> deviation.
/// </para>
/// <para>
/// Every reply is a report of <see cref="ReportLength"/> bytes: byte 0 is the input report id
/// (never inspected by the port, exactly as <c>_ReadHidJP@8</c> drops it) and bytes 1..32 are
/// the 32-byte payload the port sees as <c>resp</c>.
/// </para>
/// <para>
/// Knobs such as <see cref="LoseNextRequests"/> and <see cref="NeverAcknowledgeWrites"/>
/// simulate a bus that drops a frame or a device that stays silent, which is what the port's
/// retry loops exist for.
/// </para>
/// </remarks>
internal sealed class FakeKeyboardDevice : IKeyboardTransport
{
    private readonly Dictionary<byte, byte[]> _mappingValues = new();      // key id -> 5 value bytes
    private readonly Dictionary<byte, byte> _mappingTransports = new();    // key id -> transport byte
    private readonly Dictionary<byte, byte[]> _macroNames = new();         // key id -> 61 name bytes
    private readonly Dictionary<byte, byte[]> _macroRecords = new();       // key id -> 609 record bytes
    private readonly Queue<byte[]> _inbox = new();

    private byte[] _profileName = new byte[DeviceNameCodec.NameBytes];
    private int _profileNameLength = -1;                                   // -1 = derive from the field
    private readonly Dictionary<byte, int> _macroNameLengths = new();
    private bool _open;

    private byte _command = byte.MaxValue;   // current command, to reset the chunk counters
    private int _chunk;                      // chunk index of consecutive identical read requests
    private int _writeChunk;                 // chunk index of consecutive identical write frames

    // ---------------------------------------------------------------- test knobs

    /// <summary>Drops the next N frames "on the way to the device" (no reply, no state change).</summary>
    public int LoseNextRequests { get; set; }

    /// <summary>Answers the next N writes with a report that is not the <c>E4 08</c> acknowledgement.</summary>
    public int NakNextWrites { get; set; }

    /// <summary>Never acknowledges a write (the port's write paths must then report failure).</summary>
    public bool NeverAcknowledgeWrites { get; set; }

    /// <summary>Replies to the next query with an unrelated report before the real one.</summary>
    public bool SendStaleReplyFirst { get; set; }

    /// <summary>Input report id this "device" stamps on replies (the port must not care).</summary>
    public byte InputReportId { get; set; } = JpWire.ReportIdOut;

    /// <summary>Reply report length; a real HID collection reports 33 or 64.</summary>
    public int ReportLength { get; set; } = JpWire.FrameLength;

    /// <summary>Set when the protocol used a feature report, which this family never does.</summary>
    public bool FeatureReportsUsed { get; private set; }

    // ---------------------------------------------------------------- observed traffic

    /// <summary>Every payload the device received (frame minus the report id).</summary>
    public List<byte[]> ReceivedPayloads { get; } = new();

    /// <summary>Frames the model did not understand — must stay empty.</summary>
    public List<string> Unexpected { get; } = new();

    // ---------------------------------------------------------------- device state

    public byte Special { get; set; }
    public byte Voice { get; set; } = 2;
    public byte SelectedProfile { get; private set; } = 0xFF;

    /// <summary>Reads the stored profile name field.</summary>
    public string ProfileName => DeviceNameCodec.Decode(_profileName);

    /// <summary>Sets the stored profile name the way the device holds it (61-byte field).</summary>
    public void SetProfileName(string name)
    {
        _profileName = DeviceNameCodec.Encode(name);
        _profileNameLength = Math.Min(DeviceNameCodec.WireLength(name), JpWire.NameLength);
    }

    /// <summary>Key ids the device currently has a custom mapping for.</summary>
    public IReadOnlyCollection<byte> MappedKeyIds => _mappingValues.Keys;

    /// <summary>Key ids the device currently has a macro for.</summary>
    public IReadOnlyCollection<byte> MacroKeyIds => _macroRecords.Keys;

    /// <summary>Stores one custom mapping exactly as the device would after receiving it.</summary>
    public void StoreMapping(byte keyId, byte targetId)
    {
        _mappingTransports[keyId] = MappingPayloadCodec.TransportByteOf(targetId);
        _mappingValues[keyId] = MappingPayloadCodec.EncodePayload(targetId);
    }

    /// <summary>
    /// Preloads a macro slot the way the device stores it: header
    /// <c>[marker, cycles, flags, count]</c>, three bytes per step, then the delay entry
    /// <c>[0x0F, msLo, msHi, 0]</c> when the slot has an interval or more than one step.
    /// </summary>
    public void StoreMacro(byte keyId, string name, byte cycles, ushort intervalMs, IReadOnlyList<MacroAction> actions)
    {
        _macroNames[keyId] = DeviceNameCodec.Encode(name);
        _macroNameLengths[keyId] = Math.Min(DeviceNameCodec.WireLength(name), DeviceNameCodec.NameBytes);

        var steps = new List<byte>();
        foreach (MacroAction action in actions)
        {
            if (action.Kind == MacroActionKind.Null) break;
            steps.AddRange(action.Encode());
        }

        int count = steps.Count / JpWire.MacroStepBytes;
        bool withDelay = intervalMs > 0 || count > 1;
        if (withDelay) count++;

        var record = new byte[JpWire.MacroRecordLength];
        record[0] = JpWire.MacroRecordMarker;
        if (cycles == JpWire.MacroCyclesInfinite) record[2] = JpWire.MacroInfiniteFlag;
        else record[1] = cycles;
        record[3] = (byte)count;

        steps.CopyTo(record, JpWire.MacroHeaderLength);
        if (withDelay)
        {
            int offset = JpWire.MacroHeaderLength + steps.Count;
            record[offset] = JpWire.MacroStepTime;
            record[offset + 1] = (byte)(intervalMs & 0xFF);
            record[offset + 2] = (byte)((intervalMs >> 8) & 0xFF);
        }

        _macroRecords[keyId] = record;
    }

    /// <summary>The 609-byte record the device holds for a key (all zeros when it has none).</summary>
    public byte[] MacroRecordOf(byte keyId)
        => _macroRecords.TryGetValue(keyId, out byte[]? record) ? record : new byte[JpWire.MacroRecordLength];

    /// <summary>The 5 mapping value bytes the device holds for a key (all zeros when unmapped).</summary>
    public byte[] MappingValueOf(byte keyId)
        => _mappingValues.TryGetValue(keyId, out byte[]? value) ? value : new byte[MappingPayloadCodec.PayloadLength];

    /// <summary>The transport byte the device holds for a key (0 when unmapped).</summary>
    public byte MappingTransportOf(byte keyId)
        => _mappingTransports.TryGetValue(keyId, out byte transport) ? transport : (byte)0;

    /// <summary>Payloads received since the last <see cref="ClearReceived"/>.</summary>
    public void ClearReceived() => ReceivedPayloads.Clear();

    // ---------------------------------------------------------------- IKeyboardTransport

    public bool IsOpen => _open;

    public int MaxFeatureReportLength => 64;
    public int MaxInputReportLength => ReportLength;
    public int MaxOutputReportLength => JpWire.FrameLength;

    public bool Open() => _open = true;

    public void Close() => _open = false;

    public int SetFeatureReport(byte[] buffer)
    {
        FeatureReportsUsed = true;
        Unexpected.Add("SetFeatureReport");
        return 0;
    }

    public int GetFeatureReport(byte[] buffer)
    {
        FeatureReportsUsed = true;
        Unexpected.Add("GetFeatureReport");
        return 0;
    }

    public int Write(byte[] buffer)
    {
        if (buffer.Length < JpWire.FrameLength || buffer[0] != JpWire.ReportIdOut)
        {
            Unexpected.Add($"output frame of {buffer.Length} bytes with report id 0x{(buffer.Length > 0 ? buffer[0] : 0):x2}");
            return 0;
        }

        var payload = new byte[JpWire.PayloadLength];
        Array.Copy(buffer, 1, payload, 0, JpWire.PayloadLength);
        ReceivedPayloads.Add(payload);

        Handle(payload);
        return buffer.Length;
    }

    public int Read(byte[] buffer)
    {
        if (_inbox.Count == 0) return 0;                  // nothing arrived before the read timeout
        if (buffer.Length < JpWire.FrameLength) return 0;

        byte[] payload = _inbox.Dequeue();
        int length = Math.Min(ReportLength, buffer.Length);
        buffer[0] = InputReportId;
        Array.Copy(payload, 0, buffer, 1, Math.Min(payload.Length, length - 1));
        return length;
    }

    public void Dispose() => Close();

    // ---------------------------------------------------------------- frame handling

    private void Handle(byte[] payload)
    {
        // A lost frame never reaches the device, so it must not advance any chunk counter
        // either: the port re-sends the same request and gets the same chunk.
        if (LoseNextRequests > 0)
        {
            LoseNextRequests--;
            return;
        }

        byte command = payload[0];
        if (command != _command)
        {
            _command = command;
            _chunk = 0;
            _writeChunk = 0;
        }

        // The mapping writers carry the fixed prelude as their command bytes rather than a
        // _Set* export value (see JpWire.MappingPrelude), so they are matched before the switch.
        if (command == JpWire.MappingPrelude[0])
        {
            HandleMappingWrite(payload);
            return;
        }

        switch (command)
        {
            case JpWire.Cmd.GetBattery:                        // 0x06
                Reply(command, p => p[1] = 100);
                break;

            case JpWire.Cmd.GetProfileName:                    // 0x80
                HandleGetProfileName(command);
                break;

            case JpWire.Cmd.GetMapping:                        // 0x81
                HandleGetMapping(command);
                break;

            case JpWire.Cmd.GetMappingValue:                   // 0x83
                HandleGetMappingValue(payload);
                break;

            case JpWire.Cmd.GetMacro:                          // 0x82
                HandleGetMacro(command);
                break;

            case JpWire.Cmd.GetMacroName:                      // 0x84
                HandleGetMacroName(payload);
                break;

            case JpWire.Cmd.GetMacroValue:                     // 0x86
                HandleGetMacroValue(payload);
                break;

            case JpWire.Cmd.GetSpecial:                        // 0x88
                Reply(command, p => p[1] = Special);
                break;

            case JpWire.Cmd.GetVoice:                          // 0x89
                Reply(command, p => p[1] = Voice);
                break;

            case JpWire.Cmd.SwitchReport:                      // 0x76: profile switch or writeMacroJP
                HandleSwitchReportOrMacro(payload);
                break;

            case JpWire.Cmd.SwitchReportExtra:                 // 0x7B
                SelectedProfile = payload[1];
                Ack();
                break;

            case JpWire.Cmd.WriteProfileName:                  // 0x70
                HandleWriteProfileName(payload);
                break;

            case JpWire.Cmd.WriteMacroName:                    // 0x74
                HandleWriteMacroName(payload);
                break;

            case JpWire.Cmd.SetFeature:                        // 0x78
                Special = payload[1];
                Ack();
                break;

            case JpWire.Cmd.SetVoice:                          // 0x79
                Voice = payload[1];
                Ack();
                break;

            default:
                Unexpected.Add($"command 0x{command:x2}");
                break;
        }
    }

    /// <summary>
    /// <c>_GetProfileName@4</c>: the port takes 29 + 29 + 3 bytes from <c>resp[3..]</c> and
    /// reads <c>resp[2] == 1</c> as "another chunk follows" (see <see cref="JpProtocol.GetProfileName"/>).
    /// </summary>
    private void HandleGetProfileName(byte command)
    {
        int wire = ProfileNameWireLength;
        int chunk = _chunk++;

        Reply(command, p =>
        {
            p[1] = (byte)Math.Min(byte.MaxValue, wire);
            p[2] = (byte)(chunk < 2 && wire > 29 * (chunk + 1) ? 1 : 0);
            CopyClamped(_profileName, chunk * JpWire.ProfileNameChunk, p, 3, JpWire.ProfileNameChunk);
        });
    }

    /// <summary>
    /// <c>_GetMapping@4</c>: 30 bytes from <c>resp[1..30]</c>, <c>resp[31] == 1</c> meaning
    /// another chunk follows. Every pair is <c>[key id, 0]</c>, a zero key id ends the list
    /// (see <see cref="JpProtocol.GetMapping"/>).
    /// </summary>
    private void HandleGetMapping(byte command)
    {
        byte[] keys = MappedKeyIds.OrderBy(k => k).ToArray();
        var table = new byte[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++) table[i * 2] = keys[i];

        const int perChunk = 30 / 2;                     // 15 pairs per chunk
        int chunks = Math.Max(1, (keys.Length + perChunk - 1) / perChunk);
        int chunk = _chunk++;

        Reply(command, p =>
        {
            p[31] = (byte)(chunk + 1 < chunks ? 1 : 0);
            CopyClamped(table, chunk * 30, p, 1, 30);
        });
    }

    /// <summary>
    /// <c>_GetMappingValue@8</c>: 30 bytes from <c>resp[1..30]</c>. The port reads the
    /// transport byte at <c>resp[2]</c> and the five value bytes at <c>resp[3..8]</c>
    /// (<c>button[i] = value[i + 1]</c> in the reference implementation), so the device puts
    /// <c>[key id, transport, payload…]</c> there.
    /// </summary>
    private void HandleGetMappingValue(byte[] payload)
    {
        byte keyId = payload[1];
        Reply(JpWire.Cmd.GetMappingValue, p =>
        {
            p[1] = keyId;
            if (_mappingValues.TryGetValue(keyId, out byte[]? value))
            {
                p[2] = _mappingTransports[keyId];
                Array.Copy(value, 0, p, 3, MappingPayloadCodec.PayloadLength);
            }
        });
    }

    /// <summary>
    /// <c>_GetMacro@4</c>: the host key ids at <c>resp[1], resp[5], …</c> (the port steps 4),
    /// with <c>resp[29] == 1</c> meaning one more id arrives in a second chunk
    /// (see <see cref="JpProtocol.GetMacro"/>).
    /// </summary>
    private void HandleGetMacro(byte command)
    {
        byte[] keys = MacroKeyIds.OrderBy(k => k).Take(JpWire.MacroSlotCount).ToArray();
        int chunk = _chunk++;

        Reply(command, p =>
        {
            if (chunk == 0)
            {
                p[29] = (byte)(keys.Length > 7 ? 1 : 0);
                for (int i = 0; i < Math.Min(7, keys.Length); i++) p[1 + i * 4] = keys[i];
            }
            else
            {
                p[1] = keys.Length > 7 ? keys[7] : (byte)0;
            }
        });
    }

    /// <summary>
    /// <c>_GetMacroName@8</c>: 28 bytes from <c>resp[4..31]</c>, <c>resp[3] == 1</c> meaning
    /// another chunk follows (see <see cref="JpProtocol.GetMacroName"/>).
    /// </summary>
    private void HandleGetMacroName(byte[] payload)
    {
        byte keyId = payload[1];
        byte[] name = _macroNames.TryGetValue(keyId, out byte[]? stored) ? stored : new byte[DeviceNameCodec.NameBytes];
        int wire = _macroNameLengths.TryGetValue(keyId, out int known) ? known : WireLength(name);
        int chunk = _chunk++;

        Reply(JpWire.Cmd.GetMacroName, p =>
        {
            p[1] = keyId;
            p[3] = (byte)(chunk < 2 && wire > JpWire.MacroNameChunk * (chunk + 1) ? 1 : 0);
            CopyClamped(name, chunk * JpWire.MacroNameChunk, p, 4, JpWire.MacroNameChunk);
        });
    }

    /// <summary>
    /// <c>_GetMacroValue@8</c>: 26 bytes from <c>resp[3..28]</c>, <c>resp[2] == 0</c> meaning
    /// "done" (see <see cref="JpProtocol.GetMacroValue"/>).
    /// </summary>
    private void HandleGetMacroValue(byte[] payload)
    {
        byte keyId = payload[1];
        byte[] record = MacroRecordOf(keyId);
        int chunk = _chunk++;
        int start = chunk * 26;

        Reply(JpWire.Cmd.GetMacroValue, p =>
        {
            p[1] = keyId;
            p[2] = (byte)(start + 26 < record.Length ? 1 : 0);
            CopyClamped(record, start, p, 3, 26);
        });
    }

    /// <summary>
    /// <c>0x76</c> is shared by <c>_SwitchReport@4</c> and <c>writeMacroJP</c>. Every macro
    /// frame carries a non-zero length field at <c>payload[5]</c> (the header chunk writes
    /// <c>0x19</c>/<c>0x1A</c>, a continuation chunk writes the remaining byte count, which is
    /// never zero), while a profile switch is just <c>[0x76, profile, 0…]</c>.
    /// </summary>
    private void HandleSwitchReportOrMacro(byte[] payload)
    {
        if (payload[5] == 0)
        {
            SelectedProfile = payload[1];
            Ack();
            return;
        }

        // writeMacroJP: record offset 0..3 is the header, and every body byte at payload[10 + n]
        // belongs at record offset n + 4; a continuation chunk carries its own record offset in
        // payload[3..4] with its body starting at payload[6].
        byte keyId = payload[1];
        if (!_macroRecords.TryGetValue(keyId, out byte[]? record))
        {
            record = new byte[JpWire.MacroRecordLength];
            _macroRecords[keyId] = record;
        }

        if (payload[2] != 0)                        // header chunk of a multi-chunk write
        {
            record[0] = payload[6];
            record[1] = payload[7];
            record[2] = payload[8];
            record[3] = payload[9];
            Array.Copy(payload, 10, record, JpWire.MacroHeaderLength, JpWire.MacroPayloadLengthLong);
        }
        else if (_writeChunk == 0 && payload[6] == JpWire.MacroRecordMarker)
        {
            record[0] = payload[6];                 // single-chunk write (body < 0x16 bytes)
            record[1] = payload[7];
            record[2] = payload[8];
            record[3] = payload[9];
            Array.Copy(payload, 10, record, JpWire.MacroHeaderLength, JpWire.MacroPayloadLengthLong);
        }
        else
        {
            int offset = payload[3] | (payload[4] << 8);
            int length = Math.Min((int)payload[5], JpWire.MacroChunkLength);
            if (offset + length <= record.Length) Array.Copy(payload, 6, record, offset, length);
            else Unexpected.Add($"macro chunk at record offset {offset} with {length} bytes overflows the record");
        }

        _writeChunk++;
        Ack();
    }

    /// <summary>
    /// <c>writeName</c> (0x70): <c>[0x70, length, more, 29 data]</c>, up to three chunks
    /// (see <see cref="JpProtocol.WriteProfileName"/>).
    /// </summary>
    private void HandleWriteProfileName(byte[] payload)
    {
        int chunk = _writeChunk++;
        CopyClamped(payload, 3, _profileName, chunk * JpWire.ProfileNameChunk, JpWire.ProfileNameChunk);

        // The writer declares the chunk's length in payload[1] and flags the last chunk with
        // payload[2] == 0; the device's own length is where that last chunk ends.
        if (payload[2] == 0) _profileNameLength = chunk * JpWire.ProfileNameChunk + payload[1];
        Ack();
    }

    /// <summary>
    /// <c>writeMacroName</c> (0x74): <c>[0x74, key, length, more, 28 data]</c>, up to three
    /// chunks (see <see cref="JpProtocol.WriteMacroName"/>).
    /// </summary>
    private void HandleWriteMacroName(byte[] payload)
    {
        byte keyId = payload[1];
        if (!_macroNames.TryGetValue(keyId, out byte[]? name))
        {
            name = new byte[DeviceNameCodec.NameBytes];
            _macroNames[keyId] = name;
        }

        CopyClamped(payload, 4, name, _writeChunk * JpWire.MacroNameChunk, JpWire.MacroNameChunk);

        int chunk = _writeChunk++;
        if (payload[3] == 0) _macroNameLengths[keyId] = chunk * JpWire.MacroNameChunk + payload[2];
        Ack();
    }

    /// <summary>
    /// <c>_SetMapping@8</c>/<c>_SetShortCut@8</c>/<c>_SetMouse@8</c>: the fixed prelude, then
    /// <c>[key id, transport, value…]</c>. The device keeps the value bytes it was sent, so a
    /// keyboard/shortcut write only carries four and the fifth stays zero
    /// (see <see cref="JpWire.MappingValueLengthKeyboard"/>).
    /// </summary>
    private void HandleMappingWrite(byte[] payload)
    {
        for (int i = 0; i < JpWire.MappingPrelude.Length; i++)
        {
            if (payload[i] != JpWire.MappingPrelude[i])
            {
                Unexpected.Add($"mapping frame with prelude byte {i} = 0x{payload[i]:x2}");
                return;
            }
        }

        byte keyId = payload[7];
        byte transport = payload[8];
        var value = new byte[MappingPayloadCodec.PayloadLength];
        Array.Copy(payload, 9, value, 0, MappingPayloadCodec.PayloadLength);

        if (transport != MappingPayloadCodec.TransportKeyboard
            && transport != MappingPayloadCodec.TransportShortcut
            && transport != MappingPayloadCodec.TransportMouse)
        {
            Unexpected.Add($"mapping frame with transport 0x{transport:x2}");
        }

        _mappingTransports[keyId] = transport;
        _mappingValues[keyId] = value;
        Ack();
    }

    // ---------------------------------------------------------------- helpers

    private void Reply(byte command, Action<byte[]> fill)
    {
        if (SendStaleReplyFirst)
        {
            SendStaleReplyFirst = false;
            var stale = new byte[JpWire.PayloadLength];
            stale[0] = 0x00;                    // no command owns 0x00, so the port must ignore it
            _inbox.Enqueue(stale);
        }

        var payload = new byte[JpWire.PayloadLength];
        payload[0] = command;
        fill(payload);
        _inbox.Enqueue(payload);
    }

    private void Ack()
    {
        if (NeverAcknowledgeWrites) return;

        if (NakNextWrites > 0)
        {
            NakNextWrites--;
            _inbox.Enqueue(new byte[JpWire.PayloadLength]);   // first byte 0x00, so not an acknowledgement
            return;
        }

        var ack = new byte[JpWire.PayloadLength];
        ack[0] = JpWire.AckFirst;
        ack[1] = JpWire.AckSecond;
        _inbox.Enqueue(ack);
    }

    private static void CopyClamped(byte[] source, int sourceOffset, byte[] target, int targetOffset, int count)
    {
        if (sourceOffset >= source.Length || targetOffset >= target.Length) return;

        int length = Math.Min(count, Math.Min(source.Length - sourceOffset, target.Length - targetOffset));
        if (length > 0) Array.Copy(source, sourceOffset, target, targetOffset, length);
    }

    /// <summary>
    /// Byte index of the first <c>00 00</c> code unit in a 61-byte name field (0 when the
    /// field is completely filled), i.e. the length the device reports for its own name.
    /// </summary>
    private static int WireLength(byte[] field)
    {
        for (int i = 0; i < field.Length / 2; i++)
        {
            if (i != 30 && field[i * 2] == 0 && field[i * 2 + 1] == 0) return i * 2;
        }
        return 0;
    }

    /// <summary>
    /// Byte length the device reports for its profile name: the length the writer declared, or
    /// the field's own zero-pair scan when the name was preloaded rather than written.
    /// </summary>
    private int ProfileNameWireLength
        => _profileNameLength >= 0 ? _profileNameLength : WireLength(_profileName);
}
