// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Physical layout of the Retro 87 board as the keyboard protocol numbers its keys.
///
/// The mapping array sent to/from the device has 95 entries whose <b>position</b> order
/// is fixed by the original software (<c>the mapping writer</c> /
/// <c>the board key table</c> in the reference implementation). Position order is NOT the
/// same as the HID usage order.
/// </summary>
/// <remarks>
/// Two id arrays are needed because the original uses two different ids per position:
/// <list type="bullet">
/// <item><see cref="PositionKeyId"/> — the id the device knows the <i>physical</i> key by.
/// It is the <c>key</c> parameter of <c>SetMapping</c>/<c>SetShortCut</c>/<c>SetMouse</c>,
/// the id returned by <c>GetMapping</c>, and the <c>key</c> field of a macro slot.</item>
/// <item><see cref="DefaultMappingId"/> — the <i>target</i> the key maps to out of the box,
/// i.e. <c>the board key table</c>. Positions 85..94 (the ten
/// <see cref="SuperButtons">Super Buttons</see>) default to
/// <see cref="MappingIds.NULL_MAP"/> = unmapped.</item>
/// </list>
/// For positions 0..84 both arrays are equal; they differ only for the ten Super Buttons.
/// </remarks>
public static class KeyboardLayout
{
    public const int PositionCount = 95;

    /// <summary>First position whose key id is not also its default target (KA).</summary>
    public const int FirstExtraPosition = 85;

    /// <summary>Default target id per position.</summary>
    public static readonly byte[] DefaultMappingId = MappingTables.GetKeys();

    /// <summary>Physical key id per position — the ids <c>the mapping writer</c> addresses.</summary>
    public static readonly byte[] PositionKeyId = BuildPositionKeyIds();

    /// <summary>Display label per position, in protocol order.</summary>
    public static readonly string[] PositionNames = BuildNames();

    /// <summary>Position of a physical key id, or -1 when the id is not a board key.</summary>
    public static int PositionOfKeyId(byte keyId)
    {
        for (int i = 0; i < PositionKeyId.Length; i++)
        {
            if (PositionKeyId[i] == keyId) return i;
        }
        return -1;
    }

    /// <summary>Default target id of the position addressed by <paramref name="keyId"/>.</summary>
    public static byte DefaultOfKeyId(byte keyId)
    {
        int position = PositionOfKeyId(keyId);
        return position < 0 ? MappingIds.NULL_MAP : DefaultMappingId[position];
    }

    private static byte[] BuildPositionKeyIds()
    {
        // Positions 0..84: the physical key id equals the default target id.
        var ids = (byte[])MappingTables.GetKeys().Clone();

        // Positions 85..94: the ten extra keys keep a key id even though their default
        // target is NULL_MAP. Taken from the last ten setMappingIndex() calls.
        byte[] extra =
        {
            MappingIds.KA_MAP,
            MappingIds.KB_MAP,
            MappingIds.K1_MAP,
            MappingIds.K2_MAP,
            MappingIds.K3_MAP,
            MappingIds.K4_MAP,
            MappingIds.K5_MAP,
            MappingIds.K6_MAP,
            MappingIds.K7_MAP,
            MappingIds.K8_MAP,
        };
        Array.Copy(extra, 0, ids, FirstExtraPosition, extra.Length);
        return ids;
    }

    private static string[] BuildNames()
    {
        var n = new string[PositionCount];
        for (int i = 0; i < 26; i++) n[i] = ((char)('A' + i)).ToString();
        for (int i = 0; i < 10; i++) n[26 + i] = ((i + 1) % 10).ToString();
        string[] mid =
        {
            "Enter", "Esc", "Backspace", "Tab", "Space",
            "-", "=", "[", "]", "\\", ";", "'", "`", ",", ".", "/", "Caps",
        };
        Array.Copy(mid, 0, n, 36, mid.Length);
        for (int i = 0; i < 12; i++) n[53 + i] = "F" + (i + 1);
        string[] nav = { "PrtSc", "ScrLk", "Pause", "Ins", "Home", "PgUp", "Del", "End", "PgDn", "Right", "Left", "Down", "Up" };
        Array.Copy(nav, 0, n, 65, nav.Length);
        string[] mods = { "LCtrl", "LShift", "LAlt", "LWin", "RCtrl", "RShift", "RAlt" };
        Array.Copy(mods, 0, n, 78, mods.Length);

        // Positions 85..94 are not keys of the board: they are the ten Super Buttons (see
        // SuperButtons). The wire names (KA, KB, K1..K8) stay on WireNames as the secondary line.
        for (int i = 0; i < SuperButtons.Count; i++)
            n[SuperButtons.PositionOf(i)] = SuperButtons.NameOf(i);
        return n;
    }
}
