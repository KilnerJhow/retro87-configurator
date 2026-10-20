// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Protocol;

/// <summary>
/// The keyboard side of the Xbox (Retro 87 "X") wire protocol, so <see cref="XboxJpProtocol"/>
/// can be driven end to end without hardware (the "xbox protocol dry run" section of
/// <c>Retro87 --selftest</c>).
/// </summary>
/// <remarks>
/// Like <see cref="FakeKeyboardDevice"/>, this is a model of the frame documentation, not a
/// capture: it proves self-consistency (frames have the documented shape, chunked reads
/// reassemble, the 10-byte reply header is honoured, writes land where the command says) but the
/// hardware remains the authority. The reserved reply bytes 5..9 are filled with non-zero
/// garbage on purpose, to catch a parser that assumes a 5-byte header.
/// </remarks>
internal sealed class FakeXboxDevice : IKeyboardTransport
{
    private readonly Queue<byte[]> _inbox = new();
    private byte[] _config;
    private readonly byte[] _macroRegion = BuildErasedMacroRegion();

    public FakeXboxDevice(byte[]? config = null)
    {
        _config = config ?? BuildDefault();
        if (_config.Length != XboxWire.ConfigSize)
            Array.Resize(ref _config, XboxWire.ConfigSize);
    }

    private static byte[] BuildErasedMacroRegion()
    {
        var region = new byte[10 * XboxWire.MacroSlotStride];
        Array.Fill(region, (byte)0xFF);   // erased flash, as the real device reports
        return region;
    }

    /// <summary>Programs a macro into a slot and adds the <c>MACRO</c> mapping that references it.</summary>
    public void SetMacro(int slot, int mappingIndex, byte hostKeyCode, string name,
        ushort interval, byte repeat, params XboxMacroStep[] steps)
    {
        int baseOffset = slot * XboxWire.MacroSlotStride;
        int info = baseOffset;
        _macroRegion[info + XboxWire.MacroInfo.Flag] = 1;
        _macroRegion[info + XboxWire.MacroInfo.KeyCode] = hostKeyCode;
        byte[] nameBytes = EncodeNameBigEndian(name, XboxWire.MacroInfo.NameBytes);
        Array.Copy(nameBytes, 0, _macroRegion, info + XboxWire.MacroInfo.Name, nameBytes.Length);
        WriteU16(_macroRegion, info + XboxWire.MacroInfo.MaxStep, (ushort)steps.Length);
        WriteU16(_macroRegion, info + XboxWire.MacroInfo.Interval, interval);
        _macroRegion[info + XboxWire.MacroInfo.Repeat] = repeat;

        for (int i = 0; i < steps.Length; i++)
        {
            int o = baseOffset + XboxWire.MacroDataOffset + i * XboxWire.MacroStepSize;
            _macroRegion[o] = steps[i].KeyState;
            _macroRegion[o + 1] = steps[i].KeyCode;
            WriteU16(_macroRegion, o + 2, steps[i].Timer);
        }

        // reference it from the config
        int m = XboxWire.MappingBase + mappingIndex * XboxWire.MappingSize;
        _config[m] = hostKeyCode;
        _config[m + 4] = (byte)(baseOffset & 0xFF);
        _config[m + 5] = (byte)((baseOffset >> 8) & 0xFF);
        _config[m + 6] = (byte)((baseOffset >> 16) & 0xFF);
        _config[m + 7] = (byte)((baseOffset >> 24) & 0xFF);
        _config[m + 8] = (byte)XboxMappingType.Macro;
    }

    private static void WriteU16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static byte[] EncodeNameBigEndian(string name, int bytes)
    {
        var result = new byte[bytes];
        for (int i = 0; i < name.Length && (i * 2 + 1) < bytes; i++)
        {
            result[i * 2] = (byte)(name[i] >> 8);
            result[i * 2 + 1] = (byte)(name[i] & 0xFF);
        }
        return result;
    }

    /// <summary>Replaces the configuration this device reports.</summary>
    public void SetConfig(byte[] config) => _config = config;

    /// <summary>Configuration currently held by the model.</summary>
    public byte[] Config => _config;

    /// <summary>Battery percent reported by the version command.</summary>
    public byte BatteryPercent { get; set; } = 100;

    /// <summary>Firmware version reported by the version command (raw, hundredths: 107 = 1.07).</summary>
    public ushort Firmware { get; set; } = 107;

    /// <summary>Drops the next N requests (no reply, no state change).</summary>
    public int LoseNextRequests { get; set; }

    /// <summary>
    /// When true every command is answered with the device's error/terminator frame
    /// (<c>02 04 C0 …</c>) — the "no keyboard on the dongle / asleep" condition, which the
    /// protocol must fail fast on.
    /// </summary>
    public bool ErrorReplies { get; set; }

    /// <summary>How many error frames were handed out (for the selftest).</summary>
    public int ErrorReplyCount { get; private set; }

    /// <summary>
    /// Models a keyboard that went idle: until it receives the wake frame (<c>cmd 0x08 sub 0</c>)
    /// every command is answered with the error/terminator frame, exactly like the real dongle
    /// when the keyboard is not answering.
    /// </summary>
    public bool AsleepUntilWake { get; set; }

    /// <summary>How many wake frames this model received.</summary>
    public int WakeCount { get; private set; }

    /// <summary>Requests seen, as (cmd, offset, len).</summary>
    public List<(byte Cmd, uint Offset, int Len)> Requests { get; } = new();

    /// <summary>Requests the model did not understand / commands it must never receive.</summary>
    public List<string> Unexpected { get; } = new();

    public bool IsOpen { get; private set; }
    public int MaxFeatureReportLength => XboxWire.FrameLength;
    public int MaxInputReportLength => XboxWire.FrameLength;
    public int MaxOutputReportLength => XboxWire.FrameLength;

    public bool Open() { IsOpen = true; return true; }
    public void Close() => IsOpen = false;
    public void Dispose() => Close();

    public int SetFeatureReport(byte[] buffer)
    {
        Unexpected.Add("feature report (never used by this family)");
        return 0;
    }

    public int GetFeatureReport(byte[] buffer)
    {
        Unexpected.Add("feature report read (never used by this family)");
        return 0;
    }

    public int Write(byte[] buffer)
    {
        if (LoseNextRequests > 0)
        {
            LoseNextRequests--;
            return buffer.Length;
        }

        if (buffer.Length < XboxWire.RequestHeaderLength || buffer[0] != XboxWire.ReportIdOut || buffer[1] != XboxWire.Marker)
        {
            Unexpected.Add($"bad request header {Convert.ToHexString(buffer.AsSpan(0, Math.Min(4, buffer.Length)))}");
            return buffer.Length;
        }

        byte cmd = buffer[2];
        int len = buffer[4];
        byte requestChecksum = buffer[5];
        uint offset = (uint)(buffer[6] | (buffer[7] << 8) | (buffer[8] << 16) | (buffer[9] << 24));
        Requests.Add((cmd, offset, len));
        ReadOnlySpan<byte> data = buffer.AsSpan(XboxWire.RequestHeaderLength);
        if (len > data.Length) len = data.Length;

        // A sleeping keyboard only answers the wake frame; anything else gets the error reply.
        if (AsleepUntilWake && cmd == XboxWire.Cmd.Session && buffer[3] == XboxWire.SessionSubtype.Open)
        {
            AsleepUntilWake = false;
            WakeCount++;
            SessionSubtypes.Add(buffer[3]);
            QueueAck(cmd, len, requestChecksum, offset);
            return buffer.Length;
        }
        if (AsleepUntilWake || ErrorReplies)
        {
            QueueErrorReply(cmd);
            return buffer.Length;
        }

        switch (cmd)
        {
            case XboxWire.Cmd.Read:
                QueueRead(cmd, offset, len, requestChecksum);
                break;
            case XboxWire.Cmd.MacroRead:
                QueueMacroRead(cmd, offset, len, requestChecksum);
                break;
            case XboxWire.Cmd.Version:
                QueueReply(cmd, BuildVersionPayload(), requestChecksum, offset);
                break;
            case XboxWire.Cmd.Write:
                ApplyTo(_config, offset, data, len);
                QueueAck(cmd, len, requestChecksum, offset);
                break;
            case XboxWire.Cmd.MacroStart:
                QueueAck(cmd, len, requestChecksum, offset);
                break;
            case XboxWire.Cmd.MacroData:
                ApplyTo(_macroRegion, offset, data, len);
                QueueAck(cmd, len, requestChecksum, offset);
                break;
            case XboxWire.Cmd.Session:
                SessionSubtypes.Add(buffer[3]);
                QueueAck(cmd, len, requestChecksum, offset);
                break;
            default:
                Unexpected.Add($"command 0x{cmd:X2} (this model serves 0x01/0x02/0x05/0x06/0x07/0x08/0x0A)");
                break;
        }
        return buffer.Length;
    }

    /// <summary>Writes the request payload where the command says (the device-side effect).</summary>
    private static void ApplyTo(byte[] target, uint offset, ReadOnlySpan<byte> data, int len)
    {
        if (offset >= target.Length || len <= 0) return;
        int count = Math.Min(len, target.Length - (int)offset);
        data.Slice(0, count).CopyTo(target.AsSpan((int)offset));
    }

    /// <summary>Acknowledgement, as captured: the reply echoes <c>len</c> as <c>count</c>, the
    /// request's checksum and offset, with zeroed data.</summary>
    private void QueueAck(byte cmd, int len, byte requestChecksum, uint requestOffset)
        => QueueReply(cmd, new byte[Math.Max(0, len)], requestChecksum, requestOffset);

    /// <summary>Subtypes of the <c>cmd 0x08</c> session frames received, in order.</summary>
    public List<byte> SessionSubtypes { get; } = new();

    /// <summary>The macro region as it stands now (what a write would have changed).</summary>
    public byte[] MacroRegion => _macroRegion;

    private void QueueMacroRead(byte cmd, uint offset, int requested, byte requestChecksum)
    {
        int remaining = (int)offset >= _macroRegion.Length ? 0 : _macroRegion.Length - (int)offset;
        int count = Math.Min(Math.Min(requested, XboxWire.MaxChunk), Math.Max(0, remaining));
        var payload = count > 0 ? _macroRegion.AsSpan((int)offset, count).ToArray() : Array.Empty<byte>();
        QueueReply(cmd, payload, requestChecksum, offset);
    }

    private void QueueRead(byte cmd, uint offset, int requested, byte requestChecksum)
    {
        int remaining = (int)offset >= _config.Length ? 0 : _config.Length - (int)offset;
        int count = Math.Min(Math.Min(requested, XboxWire.MaxChunk), Math.Max(0, remaining));
        var payload = count > 0 ? _config.AsSpan((int)offset, count).ToArray() : Array.Empty<byte>();
        QueueReply(cmd, payload, requestChecksum, offset);
    }

    private byte[] BuildVersionPayload() => new[] { (byte)BatteryPercent, (byte)(Firmware & 0xFF), (byte)(Firmware >> 8), (byte)0x00, (byte)0x00, (byte)0x03 };

    private void QueueReply(byte cmd, byte[] payload, byte requestChecksum = 0, uint requestOffset = 0)
    {
        var frame = new byte[XboxWire.FrameLength];
        frame[0] = XboxWire.ReportIdIn;
        frame[1] = XboxWire.Marker;
        frame[2] = XboxWire.ReplyConst;
        frame[3] = cmd;
        frame[4] = (byte)payload.Length;
        // The real device echoes the request's checksum and offset here (confirmed against the
        // captures). A model that zeroed them, or filled them with garbage, would let a
        // stale-reply bug through — the selftest feeds one of those on purpose.
        frame[XboxWire.ReplyChecksum] = requestChecksum;
        frame[XboxWire.ReplyOffset] = (byte)(requestOffset & 0xFF);
        frame[XboxWire.ReplyOffset + 1] = (byte)((requestOffset >> 8) & 0xFF);
        frame[XboxWire.ReplyOffset + 2] = (byte)((requestOffset >> 16) & 0xFF);
        frame[XboxWire.ReplyOffset + 3] = (byte)((requestOffset >> 24) & 0xFF);
        Array.Copy(payload, 0, frame, XboxWire.ReplyHeaderLength, Math.Min(payload.Length, XboxWire.FrameLength - XboxWire.ReplyHeaderLength));
        _inbox.Enqueue(frame);
    }

    /// <summary>Queues a reply that claims a different offset, to exercise the stale-reply guard.</summary>
    public void QueueMisleadingReply(byte cmd, uint wrongOffset, byte requestChecksum, byte[] payload)
        => QueueReply(cmd, payload, requestChecksum, wrongOffset);

    /// <summary>Queues the device's error/terminator frame (<c>02 04 C0 …</c>).</summary>
    private void QueueErrorReply(byte cmd)
    {
        var frame = new byte[XboxWire.FrameLength];
        frame[0] = XboxWire.ReportIdIn;
        frame[1] = XboxWire.Marker;
        frame[2] = XboxWire.ReplyError;
        frame[3] = cmd;
        _inbox.Enqueue(frame);
        ErrorReplyCount++;
    }

    public int Read(byte[] buffer)
    {
        if (_inbox.Count == 0) return 0;
        byte[] frame = _inbox.Dequeue();
        Array.Copy(frame, buffer, Math.Min(frame.Length, buffer.Length));
        return Math.Min(frame.Length, buffer.Length);
    }

    /// <summary>Builds a representative default configuration (identity mappings, name "Skipult").</summary>
    public static byte[] BuildDefault()
    {
        var cfg = new byte[XboxWire.ConfigSize];
        cfg[0] = 0x02; cfg[1] = 0x09; cfg[2] = 0x20; cfg[3] = 0x20;   // flag 0x20200902
        // profileName "Skipult" in UTF-16 big-endian at offset 4
        byte[] name = { 0x00, 0x53, 0x00, 0x6B, 0x00, 0x69, 0x00, 0x70, 0x00, 0x75, 0x00, 0x6C, 0x00, 0x74 };
        Array.Copy(name, 0, cfg, XboxWire.ProfileNameOffset, name.Length);
        cfg[XboxWire.EnableOffset] = 1;
        cfg[XboxWire.FastSettingOffset] = 0;
        cfg[XboxWire.VolumeLevelOffset] = 1;
        cfg[XboxWire.CurrentColorThemeOffset] = 1;
        cfg[XboxWire.LedsSleepOffset] = 0x2C; cfg[XboxWire.LedsSleepOffset + 1] = 0x01;   // 300

        // Identity mappings: LCtrl..RWin (0xE0..0xE7) in slots 0..7, A (0x04) in slot 12 …
        for (int i = 0; i < 8; i++)
            WriteMapping(cfg, i, (byte)(0xE0 + i), (byte)(0xE0 + i), XboxMappingType.None);
        for (int i = 0; i < 26; i++)
            WriteMapping(cfg, 12 + i, (byte)(0x04 + i), (byte)(0x04 + i), XboxMappingType.None);
        return cfg;
    }

    private static void WriteMapping(byte[] cfg, int slot, byte keyCode, byte mapping, XboxMappingType type)
    {
        int o = XboxWire.MappingBase + slot * XboxWire.MappingSize;
        cfg[o] = keyCode;
        cfg[o + 4] = mapping;
        cfg[o + 8] = (byte)type;
    }
}
