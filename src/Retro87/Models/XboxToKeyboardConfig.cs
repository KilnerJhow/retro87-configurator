// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Projects an Xbox-family configuration (Retro 87 "X") into the JP-shaped
/// <see cref="KeyboardConfig"/> the existing UI edits, so the app can show the real keyboard
/// without a second view-model tree.
/// </summary>
/// <remarks>
/// <para>
/// The two families number their keys differently for the eight modifiers: the JP family uses
/// 100..107 (<see cref="MappingIds.LCTRL_MAP"/>..<see cref="MappingIds.RWIN_MAP"/>) while the
/// Xbox family uses the USB-HID usages <c>0xE0..0xE7</c>. Everything else (letters, digits,
/// symbols, function keys, media ids) shares the same byte space, so the conversion only has
/// to translate modifiers and does it in both directions.
/// </para>
/// <para>
/// Fields the Xbox layer does not carry (<c>special feature</c>, <c>voice</c>) are left at their
/// defaults. The scalar fields it <b>does</b> carry — <c>volume_lv</c> (1–5) and the
/// <c>fast_setting</c> Quick Setup bits — are projected into
/// <see cref="KeyboardConfig.XboxVolumeLevel"/>/<see cref="KeyboardConfig.XboxFastSetting"/> so the
/// Config tab can show and edit them. The LED/theme subsystem (<c>*_theme</c> records and
/// <c>current_color_theme</c>) is out of v1 and is not projected.
/// </para>
/// </remarks>
public static class XboxToKeyboardConfig
{
    /// <summary>Builds a <see cref="KeyboardConfig"/> view of <paramref name="xbox"/>.</summary>
    public static KeyboardConfig Convert(XboxConfig xbox)
    {
        var config = new KeyboardConfig
        {
            ProfileName = xbox.ProfileName,
            XboxVolumeLevel = xbox.VolumeLevel,
            XboxFastSetting = xbox.FastSetting,
        };

        var byKeyCode = new Dictionary<byte, XboxMapping>();
        foreach (XboxMapping mapping in xbox.Mappings)
        {
            if (mapping.KeyCode == 0 && mapping.Mapping == 0 && mapping.Type == XboxMappingType.None) continue;
            byKeyCode.TryAdd(mapping.KeyCode, mapping);
        }

        for (int i = 0; i < KeyboardLayout.PositionCount && i < config.BoardKeys.Length; i++)
        {
            byte xboxKeyCode = XboxKeyCodeForJpId(KeyboardLayout.PositionKeyId[i]);
            if (xboxKeyCode != MappingIds.NULL_MAP && byKeyCode.TryGetValue(xboxKeyCode, out XboxMapping? slot))
            {
                config.BoardKeys[i] = slot.MappingId;
                config.BoardKinds[i] = KindOfType(slot.Type);
            }
        }

        // The ten Super Buttons share one keyCode (0xF3), so the lookup above cannot reach them:
        // they are read by record index, in the device order the official app writes them.
        for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal++)
        {
            int index = SuperButtons.WireIndexOf(ordinal);
            int position = SuperButtons.PositionOf(ordinal);
            if (index >= xbox.Mappings.Length || position >= config.BoardKeys.Length) continue;
            config.BoardKeys[position] = xbox.Mappings[index].MappingId;
            config.BoardKinds[position] = KindOfType(xbox.Mappings[index].Type);
        }

        for (int slot = 0; slot < xbox.Macros.Length && slot < config.Macros.Length; slot++)
        {
            if (xbox.Macros[slot] is not { } macro) continue;
            MacroSlot target = config.Macros[slot];
            target.HostKey = macro.HostKeyCode;
            target.Name = macro.Name;
            target.Cycles = macro.Repeat;
            target.IntervalMs = macro.Interval;
            foreach (XboxMacroStep step in macro.Steps)
            {
                MacroActionKind kind = step.KeyState switch
                {
                    1 => MacroActionKind.KeyDown,
                    2 => MacroActionKind.KeyUp,
                    3 => MacroActionKind.Time,
                    _ => MacroActionKind.Null,
                };
                if (kind == MacroActionKind.Null) continue;
                target.Actions.Add(new MacroAction
                {
                    Kind = kind,
                    Usage = step.IsDelay ? (byte)0 : step.KeyCode,
                    DelayMs = step.IsDelay ? step.Timer : (ushort)0,
                });
            }
        }

        return config;
    }

    /// <summary>Category a record type stands for; null when the type says nothing (NONE, MACRO).</summary>
    public static MappingTargetKind? KindOfType(XboxMappingType type) => type switch
    {
        XboxMappingType.KeyCode => MappingTargetKind.Keyboard,
        XboxMappingType.MKey => MappingTargetKind.Media,
        XboxMappingType.Mouse => MappingTargetKind.Mouse,
        _ => null,
    };

    /// <summary>
    /// Factory target of each of the 95 positions in the Xbox id space. A board key maps to
    /// itself out of the box, so its default target is its own Xbox key code.
    /// </summary>
    public static IReadOnlyList<byte> DefaultBoardKeys { get; } =
        KeyboardLayout.DefaultMappingId.Select(XboxKeyCodeForJpId).ToArray();

    /// <summary>
    /// JP board-key id -> Xbox physical key code. Besides the modifiers, the JP family numbers
    /// "[" and "]" 137/138, while the Xbox family keeps them under their HID usages.
    /// </summary>
    public static byte XboxKeyCodeForJpId(byte jpId) => jpId switch
    {
        >= MappingIds.LCTRL_MAP and <= MappingIds.RWIN_MAP => (byte)(0xE0 + (jpId - MappingIds.LCTRL_MAP)),
        MappingIds.LEFTBRACKET_CLICK1 => MappingIds.LEFTBRACKET_MAP,
        MappingIds.RIGHTBRACKET_CLICK1 => MappingIds.RIGHTBRACKET_MAP,
        _ => jpId,
    };
}
