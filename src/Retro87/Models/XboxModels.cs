// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Wire-level constants of the 8BitDo Retro 87 "X" (family <b>Xbox</b>), documented
/// from interoperability analysis and validated by <b>read-only</b> probes on the real
/// keyboard. See <c>docs/PROTOCOL.md</c> for the full note and evidence.
/// </summary>
/// <remarks>
/// Framing (request, 64 bytes):
/// <c>[0]=0x81 [1]=0x04 [2]=cmd [3]=subtype [4]=len [5]=checksum</c>
/// <c>[6..9]=offset u32 LE [10..]=data</c> (zeros up to 64).
/// Reply (64 bytes): <c>[0]=0x02 [1]=0x04 [2]=0x03 [3]=cmd [4]=count</c>,
/// <b><c>[5]=checksum</c> and <c>[6..9]=offset</c> echoed from the request</b>, and data from
/// <b>[10]</b> — the 10-byte header is the correction that unblocked the mapping layout, and the
/// echo is what lets a reply be matched to its request (see the doc §3.2/§5).
/// </remarks>
public static class XboxWire
{
    /// <summary>Keyboard product IDs of the Retro 87 "X" (family Xbox).</summary>
    public const ushort PidUsb = 0x2028;      // "Retro 87 Keyboard X"
    public const ushort PidDongle = 0x202E;   // "Retro 87 Adapter X" (2.4 G)

    /// <summary>Output report id stamped on every request.</summary>
    public const byte ReportIdOut = 0x81;

    /// <summary>Input report id the device answers with (checked by the reply parser).</summary>
    public const byte ReportIdIn = 0x02;

    /// <summary>Constant marker byte at request[1] / reply[1].</summary>
    public const byte Marker = 0x04;

    /// <summary>Constant byte at reply[2] of a normal reply.</summary>
    public const byte ReplyConst = 0x03;

    /// <summary>
    /// Byte at reply[2] when the device answers with an <b>error/terminator</b> instead of a
    /// result. Observed against the dongle with no keyboard powered on (it answers
    /// <c>02 04 C0 …</c> to anything) — the same shape a sleeping device would produce. It is
    /// worth failing fast on this instead of retrying, because retrying cannot fix it.
    /// </summary>
    public const byte ReplyError = 0xC0;

    /// <summary>Length of every request/reply report.</summary>
    public const int FrameLength = 64;

    /// <summary>Byte 5 of a reply: the request's <c>checksum</c>, echoed back.</summary>
    public const int ReplyChecksum = 5;

    /// <summary>Bytes 6..9 of a reply: the request's <c>offset</c> (u32 LE), echoed back.</summary>
    public const int ReplyOffset = 6;

    /// <summary>Request byte where the payload starts.</summary>
    public const int RequestHeaderLength = 10;

    /// <summary>Reply byte where the payload starts (bytes 5..9 echo checksum and offset).</summary>
    public const int ReplyHeaderLength = 10;

    /// <summary>Maximum data bytes carried by one read/write chunk (<c>0x35</c>).</summary>
    public const int MaxChunk = 0x35;

    /// <summary>Retry bound of a read/write operation (native 0x1E).</summary>
    public const int MaxAttempts = 0x1E;

    /// <summary>
    /// How long to keep reading for the matching reply before sending the request again. The
    /// device's input report repeats until it changes, so a mismatch means "not ready yet", not
    /// "lost" — see <c>XboxJpProtocol.Query</c>.
    /// </summary>
    public const int ReplyWaitMs = 250;

    /// <summary>Size of the whole configuration region (<c>0x5FC</c>).</summary>
    public const int ConfigSize = 0x5FC;

    /// <summary>Offset of the first <see cref="XboxMapping"/> in the configuration.</summary>
    public const int MappingBase = 0x28;

    /// <summary>Number of mapping slots (<c>keyboard_mappings[120]</c>).</summary>
    public const int MappingCount = 120;

    /// <summary>Bytes per mapping slot.</summary>
    public const int MappingSize = 12;

    /// <summary>Offset of <c>profileName</c> (32 bytes, <b>UTF-16 big-endian</b> on the wire).</summary>
    public const int ProfileNameOffset = 4;

    /// <summary>Byte length of the <c>profileName</c> field.</summary>
    public const int ProfileNameBytes = 32;

    /// <summary>Offset of the <c>enable</c> flag.</summary>
    public const int EnableOffset = 0x24;

    /// <summary>Offset of the <c>fnLock</c> flag.</summary>
    public const int FnLockOffset = 0x25;

    /// <summary>Offset of <c>fast_setting</c>.</summary>
    public const int FastSettingOffset = 0x5C8;

    /// <summary>Offset of <c>volume_lv</c>.</summary>
    public const int VolumeLevelOffset = 0x5C9;

    /// <summary>Offset of <c>current_color_theme</c>.</summary>
    public const int CurrentColorThemeOffset = 0x5CA;

    /// <summary>Offset of <c>leds_sleep</c> (u32 LE).</summary>
    public const int LedsSleepOffset = 0x5CC;

    /// <summary>Offset of the profile-version <c>flag</c> (u32 LE, e.g. <c>0x20200902</c>).</summary>
    public const int FlagOffset = 0;

    /// <summary>
    /// <c>flag</c> the official app stamps in a macro's info record when it creates the slot —
    /// same value as the profile flag (doc §11.2).
    /// </summary>
    public const uint MacroFlagDefault = 0x20200902;

    /// <summary>Device stride between macro slots (<c>mapping / 4096</c> is the slot index).</summary>
    public const int MacroSlotStride = 0x1000;

    /// <summary>Size of a <c>KEYBOARD_MACROS_INFO</c> record (read with cmd 7 at the slot base).</summary>
    public const int MacroInfoSize = 0x30;

    /// <summary>Offset of the macro step data inside a slot (<c>info_offset + 64</c>).</summary>
    public const int MacroDataOffset = 0x40;

    /// <summary>Maximum macro steps (<c>macroMaxStep</c> upper bound used by the original).</summary>
    public const int MacroMaxSteps = 1008;

    /// <summary>Bytes per macro step (<c>KEYBOARD_MACROS_DETAILS</c>: keyState, keyCode, timer).</summary>
    public const int MacroStepSize = 4;

    /// <summary>Field offsets inside <see cref="XboxMacroSlot"/>'s 48-byte info record.</summary>
    public static class MacroInfo
    {
        public const int Flag = 0;            // u32
        public const int KeyCode = 4;         // u8
        public const int Name = 5;            // 32 bytes (UTF-16BE)
        public const int NameBytes = 32;
        public const int MaxStep = 38;        // u16
        public const int Interval = 40;       // u16
        public const int Repeat = 42;         // u8
        public const int RepeatType = 43;     // u8
        public const int UnifyTime = 44;      // u16
        public const int Reserve = 46;        // u16
    }

    /// <summary>
    /// Bits of the <c>fast_setting</c> byte (<c>0x5C8</c>) — the three <i>Quick Setup</i> toggles
    /// of the official app's Mapping screen (<c>the Quick Setup reader</c>/writer; labels follow the app's own wording).
    /// </summary>
    public static class FastSetting
    {
        /// <summary>"Disable Windows key" (<c>S_KeyBoard.isWin</c>).</summary>
        public const byte DisableWindowsKey = 0x1;

        /// <summary>"Disable Alt + Tab" (<c>S_KeyBoard.isTab</c>).</summary>
        public const byte DisableAltTab = 0x2;

        /// <summary>"Disable Alt + F4" (<c>S_KeyBoard.isF4</c>).</summary>
        public const byte DisableAltF4 = 0x4;
    }

    /// <summary>Command bytes (<c>cmd</c> field of the frame).</summary>
    public static class Cmd
    {
        /// <summary>Write a region of the configuration (write + ack).</summary>
        public const byte Write = 0x01;

        /// <summary>Read a region of the configuration.</summary>
        public const byte Read = 0x02;

        /// <summary>Start a macro write (offset = slot index).</summary>
        public const byte MacroStart = 0x05;

        /// <summary>Macro data.</summary>
        public const byte MacroData = 0x06;

        /// <summary>Read macro / macro info.</summary>
        public const byte MacroRead = 0x07;

        /// <summary>
        /// Session bracket: subtype 0 opens, subtype 1 closes. The official app wraps every
        /// write in this pair (captured); the port does the same on writes only.
        /// </summary>
        public const byte Session = 0x08;

        /// <summary>Unsolicited 16-byte status stream the keyboard sends on its own.</summary>
        public const byte Status = 0x09;

        /// <summary>1-byte status command the app polls the dongle with.</summary>
        public const byte DongleStatus = 0x0B;

        /// <summary>Firmware version + battery of the XboxJP family.</summary>
        public const byte Version = 0x0A;

        /// <summary>Read the LED configuration (283 bytes) — out of scope v1.</summary>
        public const byte LedRead = 0x0F;
    }

    /// <summary>Subtypes of <see cref="Cmd.Session"/>.</summary>
    public static class SessionSubtype
    {
        /// <summary>Open a session (sent before a batch of writes).</summary>
        public const byte Open = 0;

        /// <summary>Close the session (sent after the batch).</summary>
        public const byte Close = 1;
    }

    /// <summary>Device byte offset of macro <paramref name="slot"/> (<c>slot · 0x1000</c>).</summary>
    public static uint MacroSlotBase(int slot) => (uint)slot * MacroSlotStride;
}

/// <summary>
/// Mapping type stored in the <c>mappingType</c> field of an <see cref="XboxMapping"/>
/// (<c>KEYBOARD_MAPPING_TYPE</c> in the reference).
/// </summary>
public enum XboxMappingType : uint
{
    None = 0,
    KeyCode = 1,
    MKey = 2,
    Mouse = 3,
    Macro = 4,
    KeyCode2 = 5,
    OtherKeys = 6,
}

/// <summary>One 12-byte mapping slot of the Xbox configuration.</summary>
/// <remarks>
/// Confirmed layout (read-only probing): <c>keyCode</c> (u8) at +0, <c>mapping</c> (u32 LE)
/// at +4, <c>mappingType</c> (u32 LE) at +8; bytes +1..+3 are unused/garbage. For a normal
/// mapping <c>mapping</c> holds the target id (low byte, e.g. <c>0xE0</c> for LCtrl); for a
/// <see cref="XboxMappingType.Macro"/> mapping it holds the macro slot's device <b>offset</b>
/// (<c>slot * 4096</c>), which is why it is a <see cref="uint"/> here.
/// </remarks>
public sealed class XboxMapping
{
    /// <summary>Physical key id (the byte at +0).</summary>
    public byte KeyCode { get; set; }

    /// <summary>Raw <c>mapping</c> field (u32 LE at +4): a target id, or a macro slot offset.</summary>
    public uint Mapping { get; set; }

    public XboxMappingType Type { get; set; }

    /// <summary>Target id (low byte of <see cref="Mapping"/>).</summary>
    public byte MappingId => (byte)(Mapping & 0xFF);

    /// <summary>Human-readable target name (uses the type, because ids are shared).</summary>
    public string TargetName => XboxMappingIds.Describe(MappingId, Type);
}

/// <summary>One macro step (<c>KEYBOARD_MACROS_DETAILS</c>, 4 bytes on the wire).</summary>
public sealed class XboxMacroStep
{
    /// <summary>1 = press, 2 = release, 3 = interval (delay).</summary>
    public byte KeyState { get; set; }

    /// <summary>Key id acted on (ignored for an interval step).</summary>
    public byte KeyCode { get; set; }

    /// <summary>Delay in ms (for an interval step).</summary>
    public ushort Timer { get; set; }

    public bool IsDelay => KeyState == 3;
}

/// <summary>
/// One macro slot of the Xbox family (<c>KEYBOARD_MACROS_INFO</c> + its step data), read with
/// cmd <c>0x07</c> from the slot's device offset.
/// </summary>
public sealed class XboxMacroSlot
{
    /// <summary>Slot index (<c>mapping / MacroSlotStride</c>).</summary>
    public int Slot { get; set; }

    /// <summary>Physical key id this slot is bound to (from the referencing mapping's keyCode).</summary>
    public byte HostKeyCode { get; set; }

    public uint Flag { get; set; }

    /// <summary>Key code stored in the info record (<c>macroKeyCode</c>).</summary>
    public byte MacroKeyCode { get; set; }

    /// <summary>Macro name (decoded from the on-wire UTF-16 big-endian field).</summary>
    public string Name { get; set; } = string.Empty;

    public ushort MaxStep { get; set; }
    public ushort Interval { get; set; }
    public byte Repeat { get; set; }
    public byte RepeatType { get; set; }
    public ushort UnifyTime { get; set; }

    public List<XboxMacroStep> Steps { get; } = new();

    /// <summary>
    /// The 48 bytes of the info record exactly as read (or null when never read). The write
    /// path reuses it so the fields the UI does not show (<c>flag</c>, <c>repeatType</c>,
    /// <c>unifyTime</c>, <c>reserve</c>) go back unchanged instead of being zeroed.
    /// </summary>
    public byte[]? RawInfo { get; set; }

    /// <summary>True when this slot carries nothing worth writing (no name and no steps).</summary>
    public bool IsEmpty => Steps.Count == 0 && string.IsNullOrEmpty(Name);
}

/// <summary>Configuration read from a Retro 87 "X" (family Xbox).</summary>
public sealed class XboxConfig
{    /// <summary>Raw profile-version flag (u32, e.g. <c>0x20200902</c>).</summary>
    public uint Flag { get; set; }

    /// <summary>Profile name (decoded from the on-wire UTF-16 big-endian field).</summary>
    public string ProfileName { get; set; } = string.Empty;

    public byte Enable { get; set; }
    public byte FnLock { get; set; }

    /// <summary>The 120 mapping slots, in device order.</summary>
    public XboxMapping[] Mappings { get; } = new XboxMapping[XboxWire.MappingCount];

    public byte FastSetting { get; set; }
    public byte VolumeLevel { get; set; }
    public byte CurrentColorTheme { get; set; }
    public uint LedsSleep { get; set; }

    /// <summary>The 1532 raw bytes as read (kept for round-trip/backup; a write is gated).</summary>
    public byte[] Raw { get; set; } = Array.Empty<byte>();

    /// <summary>Macro slots referenced by a <c>MACRO</c> mapping, indexed by slot (null = unused).</summary>
    public XboxMacroSlot?[] Macros { get; } = new XboxMacroSlot?[10];

    public XboxConfig()
    {
        for (int i = 0; i < Mappings.Length; i++) Mappings[i] = new XboxMapping();
    }

    /// <summary>Battery percent of the last version read, or -1 when unknown.</summary>
    public int BatteryPercent { get; set; } = -1;

    /// <summary>Firmware version as <c>major.minor</c> (e.g. 1.07), or 0 when unknown.</summary>
    public float FirmwareVersion { get; set; }
}

/// <summary>
/// Names for an Xbox mapping id, from the ported id table (<see cref="XboxIdTable"/>).
/// </summary>
/// <remarks>
/// The Xbox family numbers most ids like the JP family (USB-HID usages for the keys), but the
/// eight modifiers are the HID usages <c>0xE0..0xE7</c> instead of 100..107, and above the
/// keyboard range it has its own media/mouse ids (<c>Next</c> = <c>0xB5</c>, <c>Play</c> =
/// <c>0xCD</c>, …) that the JP tables do not know. Use <see cref="Describe(byte, XboxMappingType)"/>
/// when the type is known, because some ids mean two things.
/// </remarks>
public static class XboxMappingIds
{
    /// <summary>Display name for an Xbox mapping id ("—" for 0 / unmapped).</summary>
    public static string Describe(byte id) => XboxIdTable.Describe(id);

    /// <summary>Display name for an id whose mapping type is known (disambiguates shared ids).</summary>
    public static string Describe(byte id, XboxMappingType type) => XboxIdTable.Describe(id, type);

    /// <summary>First USB-HID usage of the eight modifiers.</summary>
    public const byte ModifierFirst = 0xE0;

    /// <summary>Last USB-HID usage of the eight modifiers.</summary>
    public const byte ModifierLast = 0xE7;
}
