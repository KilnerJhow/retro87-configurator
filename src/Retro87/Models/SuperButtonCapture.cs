// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Input;

namespace Retro87.Models;

/// <summary>
/// Translates the key the user pressed into the mapping id the keyboard uses for the same target,
/// so the UI can highlight the Super Button that owns it.
/// </summary>
/// <remarks>
/// The ids are USB-HID usages for keyboard keys and the same ids the Xbox family stores in the
/// mapping table. The JP family numbers its modifiers 100..107 instead of 0xE0..0xE7; the caller
/// converts those with <see cref="ForFamily"/>. Media keys are matched by <see cref="MappingTargetKind.Media"/>
/// because some ids (0xE2, 0xE9, 0xEA) are shared between a key and a media/mouse target.
/// </remarks>
public static class SuperButtonCapture
{
    private static readonly Dictionary<Key, (byte Usage, MappingTargetKind Kind)> Special = new()
    {
        // punctuation
        [Key.OemMinus] = (0x2D, MappingTargetKind.Keyboard),
        [Key.OemPlus] = (0x2E, MappingTargetKind.Keyboard),
        [Key.OemOpenBrackets] = (0x2F, MappingTargetKind.Keyboard),
        [Key.OemCloseBrackets] = (0x30, MappingTargetKind.Keyboard),
        [Key.OemPipe] = (0x31, MappingTargetKind.Keyboard),
        [Key.OemSemicolon] = (0x33, MappingTargetKind.Keyboard),
        [Key.OemQuotes] = (0x34, MappingTargetKind.Keyboard),
        [Key.OemTilde] = (0x35, MappingTargetKind.Keyboard),
        [Key.OemComma] = (0x36, MappingTargetKind.Keyboard),
        [Key.OemPeriod] = (0x37, MappingTargetKind.Keyboard),
        [Key.OemQuestion] = (0x38, MappingTargetKind.Keyboard),

        // control and navigation
        [Key.Enter] = (0x28, MappingTargetKind.Keyboard),
        [Key.Back] = (0x2A, MappingTargetKind.Keyboard),
        [Key.Tab] = (0x2B, MappingTargetKind.Keyboard),
        [Key.Space] = (0x2C, MappingTargetKind.Keyboard),
        [Key.CapsLock] = (0x39, MappingTargetKind.Keyboard),
        [Key.NumLock] = (0x53, MappingTargetKind.Keyboard),
        [Key.PrintScreen] = (0x46, MappingTargetKind.Keyboard),
        [Key.Scroll] = (0x47, MappingTargetKind.Keyboard),
        [Key.Pause] = (0x48, MappingTargetKind.Keyboard),
        [Key.Insert] = (0x49, MappingTargetKind.Keyboard),
        [Key.Home] = (0x4A, MappingTargetKind.Keyboard),
        [Key.PageUp] = (0x4B, MappingTargetKind.Keyboard),
        [Key.Delete] = (0x4C, MappingTargetKind.Keyboard),
        [Key.End] = (0x4D, MappingTargetKind.Keyboard),
        [Key.PageDown] = (0x4E, MappingTargetKind.Keyboard),
        [Key.Right] = (0x4F, MappingTargetKind.Keyboard),
        [Key.Left] = (0x50, MappingTargetKind.Keyboard),
        [Key.Down] = (0x51, MappingTargetKind.Keyboard),
        [Key.Up] = (0x52, MappingTargetKind.Keyboard),

        // modifiers
        [Key.LeftCtrl] = (0xE0, MappingTargetKind.Keyboard),
        [Key.LeftShift] = (0xE1, MappingTargetKind.Keyboard),
        [Key.LeftAlt] = (0xE2, MappingTargetKind.Keyboard),
        [Key.LWin] = (0xE3, MappingTargetKind.Keyboard),
        [Key.RightCtrl] = (0xE4, MappingTargetKind.Keyboard),
        [Key.RightShift] = (0xE5, MappingTargetKind.Keyboard),
        [Key.RightAlt] = (0xE6, MappingTargetKind.Keyboard),
        [Key.RWin] = (0xE7, MappingTargetKind.Keyboard),

        // media targets
        [Key.MediaNextTrack] = (0xB5, MappingTargetKind.Media),
        [Key.MediaPreviousTrack] = (0xB6, MappingTargetKind.Media),
        [Key.MediaPlayPause] = (0xCD, MappingTargetKind.Media),
        [Key.VolumeMute] = (0xE2, MappingTargetKind.Media),
        [Key.VolumeUp] = (0xE9, MappingTargetKind.Media),
        [Key.VolumeDown] = (0xEA, MappingTargetKind.Media),
    };

    /// <summary>Prefix ids of modifiers in the JP family (0xE0..0xE7 become 100..107).</summary>
    private const byte JpModifierBase = MappingIds.LCTRL_MAP;

    /// <summary>Maps a key to a HID usage and the kind of target it represents.</summary>
    public static bool TryMap(Key key, out byte usage, out MappingTargetKind kind)
    {
        int code = (int)key;

        int letter = code - (int)Key.A;
        if (letter >= 0 && letter <= (int)Key.Z - (int)Key.A)
        {
            usage = (byte)(0x04 + letter);
            kind = MappingTargetKind.Keyboard;
            return true;
        }

        int function = code - (int)Key.F1;
        if (function >= 0 && function < 12)
        {
            usage = (byte)(0x3A + function);
            kind = MappingTargetKind.Keyboard;
            return true;
        }

        int extendedFunction = code - (int)Key.F13;
        if (extendedFunction >= 0 && extendedFunction < 12)
        {
            usage = (byte)(0x68 + extendedFunction);
            kind = MappingTargetKind.Keyboard;
            return true;
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            int digit = code - (int)Key.D1;
            usage = digit < 0 ? (byte)0x27 : (byte)(0x1E + digit);
            kind = MappingTargetKind.Keyboard;
            return true;
        }

        if (Special.TryGetValue(key, out (byte Usage, MappingTargetKind Kind) target))
        {
            usage = target.Usage;
            kind = target.Kind;
            return true;
        }

        usage = 0;
        kind = MappingTargetKind.None;
        return false;
    }

    /// <summary>Converts the HID modifier ids to the ids the connected family stores.</summary>
    public static byte ForFamily(byte usage, MappingIdSpace space)
        => space == MappingIdSpace.Jp && usage is >= 0xE0 and <= 0xE7
            ? (byte)(JpModifierBase + (usage - 0xE0))
            : usage;

    /// <summary>Short label for the key in status messages.</summary>
    public static string Describe(Key key) => key switch
    {
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        Key.MediaNextTrack => "Next",
        Key.MediaPreviousTrack => "Last",
        Key.MediaPlayPause => "Play",
        Key.VolumeMute => "NoVoice",
        Key.VolumeUp => "VoiceUp",
        Key.VolumeDown => "VoiceLow",
        _ => key.ToString(),
    };
}
