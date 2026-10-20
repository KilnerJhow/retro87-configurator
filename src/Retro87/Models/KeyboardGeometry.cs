// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>Where one key sits on the drawn keyboard, in key units (1 = a standard key width).</summary>
public readonly record struct KeyBox(int Row, double X, double Width);

/// <summary>
/// Geometry of the 85 board keys of the Retro 87 "X", in key units, for the keyboard the Mapeamento
/// tab draws instead of the old 95-row table.
/// </summary>
/// <remarks>
/// <para>
/// The table is keyed by the key's label (<see cref="KeyboardLayout.PositionNames"/>) and not by
/// position on purpose: the protocol order is not the physical order (the modifiers come last), so
/// a name table is the only thing that cannot silently drift when a position moves. The ten Super
/// Buttons are <b>not</b> in <see cref="Board"/>: they are not keys of the board (see
/// <see cref="SuperButtons"/>). Only the keyboard's own pair is drawn here (<see cref="SuperBoxOf"/>);
/// the other eight are drawn in their own block.
/// </para>
/// <para>
/// The layout is the board's own TKL arrangement. Units are 18.5 columns wide; the function band is
/// row 0 and the five main rows follow at 1.25, 2.25, … so the band reads as a separate strip. The
/// gap at the right of the bottom row is honest: those are the RWin/Menu positions, which this
/// model does not expose, so they are not drawn as clickable keys.
/// </para>
/// </remarks>
public static class KeyboardGeometry
{
    /// <summary>Width of the whole board in key units.</summary>
    public const double Columns = 18.5;

    /// <summary>Height of the whole board in key units (5.25 + one key).</summary>
    public const double Rows = 6.25;

    /// <summary>Vertical offset of a row, in key units (row 0 is the function band).</summary>
    public static double RowOffset(int row) => row switch
    {
        0 => 0.0,
        1 => 1.25,
        2 => 2.25,
        3 => 3.25,
        4 => 4.25,
        5 => 5.25,
        _ => row,
    };

    /// <summary>Box of each of the 85 board positions, indexed by <see cref="KeyboardLayout"/> position.</summary>
    public static readonly KeyBox[] Board = BuildBoard();

    /// <summary>Box of a board position, or a zero box for a position that is not a board key.</summary>
    public static KeyBox BoxOf(int position)
        => position >= 0 && position < Board.Length ? Board[position] : default;

    /// <summary>
    /// Box of the keyboard's own two Super Buttons (ordinals 0 and 1, "KA" and "KB"): they sit on the
    /// space row between RAlt and RCtrl. The other eight live behind the connections and have no box.
    /// </summary>
    public static KeyBox SuperBoxOf(int ordinal) => ordinal switch
    {
        0 => new KeyBox(5, 11.25, 1),
        1 => new KeyBox(5, 12.25, 1),
        _ => default,
    };

    /// <summary>True when every board position has exactly one box (a layout self-check).</summary>
    public static bool IsComplete()
    {
        if (Board.Length != KeyboardLayout.FirstExtraPosition) return false;
        for (int i = 0; i < Board.Length; i++)
        {
            KeyBox box = Board[i];
            if (box.Width <= 0 || box.X < 0 || box.X + box.Width > Columns + 0.001) return false;
            if (box.Row < 0 || box.Row > 5) return false;
        }
        return true;
    }

    private static KeyBox[] BuildBoard()
    {
        // name, row, x, width - the physical arrangement of the board's 85 keys.
        var layout = new (string Name, int Row, double X, double Width)[]
        {
            // function band
            ("Esc", 0, 0, 1),
            ("F1", 0, 2, 1), ("F2", 0, 3, 1), ("F3", 0, 4, 1), ("F4", 0, 5, 1),
            ("F5", 0, 6.5, 1), ("F6", 0, 7.5, 1), ("F7", 0, 8.5, 1), ("F8", 0, 9.5, 1),
            ("F9", 0, 11, 1), ("F10", 0, 12, 1), ("F11", 0, 13, 1), ("F12", 0, 14, 1),
            ("PrtSc", 0, 15.5, 1), ("ScrLk", 0, 16.5, 1), ("Pause", 0, 17.5, 1),

            // digits row + the navigation column
            ("`", 1, 0, 1), ("1", 1, 1, 1), ("2", 1, 2, 1), ("3", 1, 3, 1), ("4", 1, 4, 1),
            ("5", 1, 5, 1), ("6", 1, 6, 1), ("7", 1, 7, 1), ("8", 1, 8, 1), ("9", 1, 9, 1),
            ("0", 1, 10, 1), ("-", 1, 11, 1), ("=", 1, 12, 1), ("Backspace", 1, 13, 2),
            ("Ins", 1, 15.5, 1), ("Home", 1, 16.5, 1), ("PgUp", 1, 17.5, 1),

            // top letter row
            ("Tab", 2, 0, 1.5),
            ("Q", 2, 1.5, 1), ("W", 2, 2.5, 1), ("E", 2, 3.5, 1), ("R", 2, 4.5, 1),
            ("T", 2, 5.5, 1), ("Y", 2, 6.5, 1), ("U", 2, 7.5, 1), ("I", 2, 8.5, 1),
            ("O", 2, 9.5, 1), ("P", 2, 10.5, 1), ("[", 2, 11.5, 1), ("]", 2, 12.5, 1),
            ("\\", 2, 13.5, 1.5),
            ("Del", 2, 15.5, 1), ("End", 2, 16.5, 1), ("PgDn", 2, 17.5, 1),

            // home row
            ("Caps", 3, 0, 1.75),
            ("A", 3, 1.75, 1), ("S", 3, 2.75, 1), ("D", 3, 3.75, 1), ("F", 3, 4.75, 1),
            ("G", 3, 5.75, 1), ("H", 3, 6.75, 1), ("J", 3, 7.75, 1), ("K", 3, 8.75, 1),
            ("L", 3, 9.75, 1), (";", 3, 10.75, 1), ("'", 3, 11.75, 1), ("Enter", 3, 12.75, 2.25),

            // bottom letter row + the up arrow
            ("LShift", 4, 0, 2.25),
            ("Z", 4, 2.25, 1), ("X", 4, 3.25, 1), ("C", 4, 4.25, 1), ("V", 4, 5.25, 1),
            ("B", 4, 6.25, 1), ("N", 4, 7.25, 1), ("M", 4, 8.25, 1), (",", 4, 9.25, 1),
            (".", 4, 10.25, 1), ("/", 4, 11.25, 1), ("RShift", 4, 12.25, 2.75),
            ("Up", 4, 16.5, 1),

            // space row + the other three arrows
            ("LCtrl", 5, 0, 1.5), ("LWin", 5, 1.5, 1), ("LAlt", 5, 2.5, 1.25),
            ("Space", 5, 3.75, 6.25), ("RAlt", 5, 10, 1.25), ("RCtrl", 5, 13.25, 1.75),
            ("Left", 5, 15.5, 1), ("Down", 5, 16.5, 1), ("Right", 5, 17.5, 1),
        };

        var byName = new Dictionary<string, KeyBox>(layout.Length, StringComparer.Ordinal);
        foreach ((string name, int row, double x, double width) in layout)
            byName[name] = new KeyBox(row, x, width);

        var board = new KeyBox[KeyboardLayout.FirstExtraPosition];
        for (int position = 0; position < board.Length; position++)
        {
            // A missing name leaves a zero box; IsComplete() fails and the selftest says so.
            if (byName.TryGetValue(KeyboardLayout.PositionNames[position], out KeyBox box))
                board[position] = box;
        }
        return board;
    }
}
