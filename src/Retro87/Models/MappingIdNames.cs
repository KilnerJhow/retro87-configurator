// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Human-readable names for a keyboard <b>mapping id</b> (the single byte stored per board
/// key in the UI, and the key of the mapping value exchanged with the device).
/// </summary>
/// <remarks>
/// Ids that are the default target of a board position (see
/// <see cref="KeyboardLayout.DefaultMappingId"/>) are labelled with that position's key
/// legend, which is what the official software shows. Every other id is named through
/// <see cref="MappingTables.ToKeyboardEnum"/>; unknown ids fall back to hex.
/// </remarks>
public static class MappingIdNames
{
    private static readonly Dictionary<byte, string> PositionNames = BuildPositionNames();

    /// <summary>Returns a display name for a mapping id ("—" for 0 / unmapped).</summary>
    public static string Describe(byte id)
    {
        if (id == MappingIds.NULL_MAP) return "—";
        if (PositionNames.TryGetValue(id, out var name)) return name;

        KeyboardEnum key = MappingTables.ToKeyboardEnum(id);
        return key is KeyboardEnum.None or KeyboardEnum.empty ? $"0x{id:X2}" : key.ToString();
    }

    private static Dictionary<byte, string> BuildPositionNames()
    {
        var map = new Dictionary<byte, string>();
        for (int i = 0; i < KeyboardLayout.PositionCount; i++)
        {
            byte id = KeyboardLayout.DefaultMappingId[i];
            if (id != MappingIds.NULL_MAP && !map.ContainsKey(id))
                map[id] = KeyboardLayout.PositionNames[i];
        }
        return map;
    }
}
