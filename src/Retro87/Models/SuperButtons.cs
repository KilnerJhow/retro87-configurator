// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// The ten Super Buttons of the Retro 87 "X": the two on the keyboard itself and the eight behind
/// the four wireless interfaces (<b>A</b>, <b>B</b>, <b>X</b>, <b>Y</b>), two buttons each.
/// </summary>
/// <remarks>
/// <para>
/// They are the last ten positions of <see cref="KeyboardLayout"/> (85..94) and the original
/// software names them "Super Button 1".."Super Button 10".
/// On the wire they are the ten records whose <c>keyCode</c> is <c>0xF3</c>
/// (<see cref="XboxIdTable.UnmappedId"/>, the "no key" marker) laid out in device order as
/// <c>KA, KB, K1..K8</c> at indices <b>9, 10, 108..115</b> — the order declared by
/// <c>the Xbox key mapping table</c> for the 120 records.
/// </para>
/// <para>
/// Because all ten share a single <c>keyCode</c>, the keyCode lookup the rest of the port uses
/// cannot address them (it refuses to guess, see <c>docs/PROTOCOL.md</c> §12.4). They are
/// read and written by <b>record index</b>, which is what the official app does.
/// </para>
/// <para>
/// <b>Evidence for the pairs.</b> The 2026-10-17 capture of the real keyboard holds Volume Down and
/// Volume Up (<c>0xE2</c>/<c>0xCD</c>, type MKEY) at indices 112/113 — Super Buttons 7 and 8, the
/// pair of the third interface. The official screen shows "Super Button 8" while that interface is
/// selected, so interface A holds Super Buttons 3 and 4, B holds 5 and 6, X holds 7 and 8 and Y
/// holds 9 and 10.
/// </para>
/// </remarks>
public static class SuperButtons
{
    /// <summary>The <c>keyCode</c> every Super Button record carries on the wire.</summary>
    public const byte WireKeyCode = XboxIdTable.UnmappedId;

    /// <summary>Number of Super Buttons (2 on the keyboard + 4 interfaces × 2).</summary>
    public const int Count = 10;

    /// <summary>First <see cref="KeyboardLayout"/> position that is a Super Button ("Super Button 1").</summary>
    public const int FirstPosition = KeyboardLayout.FirstExtraPosition;

    /// <summary>Record index inside <c>keyboard_mappings[120]</c>, per Super Button, in order.</summary>
    public static readonly int[] WireIndices = { 9, 10, 108, 109, 110, 111, 112, 113, 114, 115 };

    /// <summary>Name the wire order gives each Super Button (the device's own naming).</summary>
    public static readonly string[] WireNames = { "KA", "KB", "K1", "K2", "K3", "K4", "K5", "K6", "K7", "K8" };

    /// <summary>Name of the four wireless interfaces, in order.</summary>
    public static readonly string[] InterfaceNames = { "A", "B", "X", "Y" };

    /// <summary>Name the interface shows, or null for the two buttons on the keyboard itself.</summary>
    public static string? InterfaceOf(int ordinal)
        => IsBuiltIn(ordinal) ? null : InterfaceNames[(ordinal - 2) / 2];

    /// <summary>True for the two Super Buttons built into the keyboard.</summary>
    public static bool IsBuiltIn(int ordinal) => ordinal is 0 or 1;

    /// <summary>True for the first button of an interface's pair (the built-in ones count as pairs).</summary>
    public static bool IsFirstOfPair(int ordinal) => ordinal % 2 == 0;

    /// <summary>Display name of the n-th Super Button (0-based).</summary>
    public static string NameOf(int ordinal) => $"Super Button {ordinal + 1}";

    /// <summary><see cref="KeyboardLayout"/> position of the n-th Super Button.</summary>
    public static int PositionOf(int ordinal) => FirstPosition + ordinal;

    /// <summary>Record index of the n-th Super Button.</summary>
    public static int WireIndexOf(int ordinal) => WireIndices[ordinal];

    /// <summary>Ordinal of the Super Button behind a record index, or -1 for any other record.</summary>
    public static int OrdinalOfWireIndex(int index) => Array.IndexOf(WireIndices, index);

    /// <summary>Ordinal of the Super Button at a layout position, or -1 for a board key.</summary>
    public static int OrdinalOfPosition(int position)
        => position >= FirstPosition && position < FirstPosition + Count ? position - FirstPosition : -1;

    /// <summary>
    /// Ordinal of the Super Button behind a wireless connection and pair index (0 or 1), the inverse
    /// of <see cref="InterfaceOf"/> plus <see cref="IsFirstOfPair"/>. Used by
    /// <see cref="SuperButtonRawPress"/>, whose <c>Interface</c> is never null.
    /// </summary>
    /// <remarks>
    /// <see cref="SuperButtonRawPress.PairIndex"/> numbers each pair's two bits low-to-high (0 = the
    /// connection's lower bit). Confirmed against the official app on 2026-10-19: for B, X and Y the
    /// lower bit is the pair's first Super Button, as expected — but for A it is the second. Nothing
    /// else distinguishes A from the others on the wire; this is a quirk of the firmware's own
    /// numbering, not a pattern this code can derive.
    /// </remarks>
    public static int OrdinalOf(string interfaceLetter, int pairIndex)
    {
        int effectivePairIndex = interfaceLetter == "A" ? 1 - pairIndex : pairIndex;
        return 2 + Array.IndexOf(InterfaceNames, interfaceLetter) * 2 + effectivePairIndex;
    }
}
