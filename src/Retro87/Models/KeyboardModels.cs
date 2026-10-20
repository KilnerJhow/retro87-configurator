// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Localization;

namespace Retro87.Models;

/// <summary>
/// Physical keys of the Retro 87 board that can be remapped, numbered by USB HID usage.
/// </summary>
/// <remarks>
/// This enum is a UI convenience only: the <b>protocol</b> id space is the byte set of
/// <see cref="MappingIds"/> (<c>A_MAP</c>…<c>RALT_MAP</c> = 4…106, then <c>KA_MAP</c> 109,
/// <c>KB_MAP</c> 108 and <c>K1_MAP</c>…<c>K8_MAP</c> 116…111), which differs from HID usage
/// for the seven modifiers (<c>LCTRL_MAP</c> is 100, not 83). Use
/// <see cref="KeyboardLayout.PositionKeyId"/> for wire traffic.
/// </remarks>
public enum KeyId : byte
{
    A = 4, B = 5, C = 6, D = 7, E = 8, F = 9, G = 10, H = 11, I = 12,
    J = 13, K = 14, L = 15, M = 16, N = 17, O = 18, P = 19, Q = 20,
    R = 21, S = 22, T = 23, U = 24, V = 25, W = 26, X = 27, Y = 28,
    Z = 29,
    D1 = 30, D2 = 31, D3 = 32, D4 = 33, D5 = 34, D6 = 35, D7 = 36,
    D8 = 37, D9 = 38, D0 = 39,
    Enter = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44,
    Minus = 45, Equal = 46, LeftBracket = 47, RightBracket = 48,
    Vertical = 49, Menu = 50, Colon = 51, Quotation = 52, Tilde = 53,
    Less = 54, Greater = 55, Question = 56, CapsLock = 57,
    F1 = 58, F2 = 59, F3 = 60, F4 = 61, F5 = 62, F6 = 63, F7 = 64,
    F8 = 65, F9 = 66, F10 = 67, F11 = 68, F12 = 69,
    PrintScreen = 70, ScrollLock = 71, Pause = 72,
    Insert = 73, Home = 74, PageUp = 75, Delete = 76, End = 77,
    PageDown = 78, Right = 79, Left = 80, Down = 81, Up = 82,
    LeftCtrl = 83, LeftShift = 84, LeftAlt = 85, LeftGui = 86,
    RightCtrl = 87, RightShift = 88, RightAlt = 89, RightGui = 90,
    App = 91,
}

/// <summary>Mapped target categories, as presented by the original UI.</summary>
public enum MappingTargetKind
{
    None,
    Keyboard,
    Media,
    Mouse,
    Shortcut,
}

/// <summary>One remapped key: source key id + 6-byte mapping payload.</summary>
public sealed class KeyMapping
{
    public KeyId Key { get; set; }
    public MappingTargetKind Kind { get; set; } = MappingTargetKind.Keyboard;

    /// <summary>Raw 6-byte mapping value (bytes 1..6 of GetMappingValue's 33-byte reply).</summary>
    public byte[] Value { get; set; } = new byte[6];

    public string DisplayName => Key.ToString();
}

/// <summary>Kind of one macro step.</summary>
public enum MacroActionKind
{
    Null,
    Time,
    KeyDown,
    KeyUp,
}

/// <summary>
/// One macro step. Wire encoding:
/// KeyDown = [0x81, usage, 0x00] (modifiers use <c>0x83</c>); KeyUp = [0x01, usage, 0x00]
/// (modifiers use <c>0x03</c>); Time = [0x0F, msLo, msHi].
/// </summary>
/// <remarks>
/// <see cref="Op"/> keeps the exact leading byte read from the device so that the modifier
/// variants (<c>0x83</c>/<c>0x03</c>, used for the eight modifier keys and for WELL) survive a
/// read → write round trip. Leave it at 0 to let <see cref="Encode"/> derive the common value
/// from <see cref="Kind"/>.
/// </remarks>
public sealed class MacroAction
{
    public MacroActionKind Kind { get; set; }

    /// <summary>Third byte of the step with the key's mapping id (see <see cref="MappingIds"/>).</summary>
    public byte Usage { get; set; }      // key id inside the macro record's id space
    public ushort DelayMs { get; set; }  // for Time steps

    /// <summary>Leading byte as read from the device; 0 means "derive from <see cref="Kind"/>".</summary>
    public byte Op { get; set; }

    public byte[] Encode()
    {
        if (Kind == MacroActionKind.Time)
            return new byte[] { 0x0F, (byte)(DelayMs & 0xFF), (byte)((DelayMs >> 8) & 0xFF) };
        if (Kind == MacroActionKind.KeyDown)
            return new byte[] { Op != 0 ? Op : (byte)0x81, Usage, 0x00 };
        if (Kind == MacroActionKind.KeyUp)
            return new byte[] { Op != 0 ? Op : (byte)0x01, Usage, 0x00 };
        return new byte[] { 0, 0, 0 };
    }
}

/// <summary>
/// One of the 8 macro slots (<c>the macro record</c>:
/// count, cycles_num, interval_ms, key, name[61], records[200]).
/// </summary>
public sealed class MacroSlot
{
    public byte HostKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Cycles { get; set; }
    public ushort IntervalMs { get; set; }
    public List<MacroAction> Actions { get; } = new();

    public bool IsEmpty => string.IsNullOrEmpty(Name);
}

/// <summary>
/// Full keyboard configuration (<c>the keyboard config record</c>
/// plus the read/write orchestration).
/// </summary>
public sealed class KeyboardConfig
{
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>95 board-key mapping values, indexed by position A..Z,1..0,etc.</summary>
    public byte[] BoardKeys { get; } = new byte[95];

    /// <summary>
    /// What the device says each target in <see cref="BoardKeys"/> is, when it says: the Xbox family
    /// reuses one id for two functions (<c>0xE2</c> is Left Alt or NoVoice), and the record's type
    /// is the only thing that tells them apart. Null where the device keeps no type (factory keys,
    /// cleared keys) or the family has none.
    /// </summary>
    public MappingTargetKind?[] BoardKinds { get; } = new MappingTargetKind?[95];

    public byte SpecialFeature { get; set; }

    /// <summary>0 = off, 1/2 = feedback volume levels (2 = default, untouched).</summary>
    public byte Voice { get; set; } = 2;

    /// <summary>
    /// <c>volume_lv</c> of the Xbox family (<c>0x5C9</c>). The JP family has no such field and
    /// leaves it at 0; the Xbox write path only sends it when it differs from the device.
    /// </summary>
    /// <remarks>
    /// The official app's <i>Volume</i> tab is a slider from <b>1 to 5</b> and its code stores it
    /// straight into <c>volume_lv</c> (<c>volume_lv = S_KeyBoard.voice</c>); the field was also
    /// written on the device in C3 — see <c>docs/PROTOCOL.md</c> §10.1/§16/§19.
    /// </remarks>
    public byte XboxVolumeLevel { get; set; }

    /// <summary>
    /// <c>fast_setting</c> of the Xbox family (<c>0x5C8</c>), a bit field for the three
    /// <i>Quick Setup</i> toggles of the official app's Mapping screen.
    /// </summary>
    /// <remarks>
    /// Bit <c>0x1</c> = Disable Windows key,
    /// <c>0x2</c> = Disable Alt + Tab, <c>0x4</c> = Disable Alt + F4 (the Quick Setup reader and writer round-trip them).
    /// Other bits are kept as read.
    /// </remarks>
    public byte XboxFastSetting { get; set; }

    public MacroSlot[] Macros { get; } = new MacroSlot[8];

    public KeyboardConfig()
    {
        for (int i = 0; i < 8; i++) Macros[i] = new MacroSlot();
    }
}

/// <summary>Device identity summary.</summary>
public sealed record DeviceInfo(
    ushort Vid,
    ushort Pid,
    string ProductName,
    string? SerialNumber,
    string DevicePath)
{
    /// <summary>
    /// Name shown in the picker. It is the product name unless two units of the same model are
    /// attached, in which case <see cref="DeviceIdentity"/> appends the serial tail so the rows
    /// can be told apart.
    /// </summary>
    public string DisplayName { get; init; } = ProductName;

    /// <summary>How the keyboard reaches the PC, told by the PID.</summary>
    public string ConnectionKind => Pid switch
    {
        KnownDevices.Retro87Usb or KnownDevices.Retro87XUsb => Loc.T("conn_usb_cable"),
        KnownDevices.Retro87Wireless or KnownDevices.Retro87XDongle => Loc.T("conn_dongle"),
        _ => "USB",
    };

    /// <summary>Protocol family of the model (see PROTOCOL.md): the Retro 87 "X" speaks Xbox.</summary>
    public string FamilyName => Pid is KnownDevices.Retro87XUsb or KnownDevices.Retro87XDongle
        ? "Xbox “X”" : "JP";

    /// <summary>Serial number for the picker, with a plain fallback when the device reports none.</summary>
    public string SerialText => string.IsNullOrEmpty(SerialNumber) ? Loc.T("serial_unknown") : SerialNumber;

    /// <summary>Second line of a picker row.</summary>
    public string Summary => $"{ConnectionKind} · {SerialText}";

    /// <summary>
    /// What a screen reader announces for the picker row: the peer falls back to the item's text,
    /// so it must be <see cref="DisplayName"/> and never the record dump with VID/PID/path.
    /// </summary>
    public override string ToString() => DisplayName;
}

public static class KnownDevices
{
    public const ushort EightBitDoVid = 11720; // 0x2DC8

    /// <summary>Retro Mechanical Keyboard (R87 / R68) — 2.4 G dongle.</summary>
    public const ushort Retro87Wireless = 0x5200;      // 20992 PID_JP

    /// <summary>Retro Mechanical Keyboard — wired USB.</summary>
    public const ushort Retro87Usb = 0x5201;           // 20993 PID_JPUSB

    /// <summary>Retro 87 "X" (family Xbox) — wired USB.</summary>
    public const ushort Retro87XUsb = XboxWire.PidUsb;         // 0x2028

    /// <summary>Retro 87 "X" (family Xbox) — 2.4 G dongle.</summary>
    public const ushort Retro87XDongle = XboxWire.PidDongle;   // 0x202E

    /// <summary>PIDs of the JP family (the protocol <c>JpProtocol</c> implements).</summary>
    public static readonly ushort[] Retro87Pids = { Retro87Wireless, Retro87Usb, 0xFF08 };

    /// <summary>PIDs of the Xbox family (the protocol <c>XboxJpProtocol</c> implements).</summary>
    public static readonly ushort[] Retro87XPids = { Retro87XUsb, Retro87XDongle };

    /// <summary>Every Retro 87 PID this app knows how to enumerate.</summary>
    public static readonly ushort[] SupportedPids =
        Retro87Pids.Concat(Retro87XPids).ToArray();

    public static bool IsRetro87(ushort vid, ushort pid)
        => vid == EightBitDoVid && SupportedPids.Contains(pid);

    /// <summary>True for the Xbox family (Retro 87 "X"), which speaks a different protocol.</summary>
    public static bool IsXbox(ushort pid) => Retro87XPids.Contains(pid);
}
