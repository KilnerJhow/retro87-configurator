// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;

namespace Retro87.Models;

/// <summary>
/// Encodes/decodes the profile and macro name fields, which the keyboard carries as
/// UTF-16LE in a fixed 61-byte field (30 code units + terminator pair).
/// </summary>
/// <remarks>
/// Name field codec. The string ends at the first byte of a <c>00 00</c> pair, and the cut
/// index is rounded down to an even offset before decoding as Unicode.
/// the string ends at the first byte of a <c>00 00</c> pair, and the cut index is rounded
/// down to an even offset before decoding as Unicode. The macro-name write length in
/// <c>the macro writer</c> uses the same 16-bit zero-pair scan.
/// </remarks>
public static class DeviceNameCodec
{
    /// <summary>Wire field size in bytes (native <c>name[61]</c>).</summary>
    public const int NameBytes = 61;

    /// <summary>Decodes a fixed-size name field into a string.</summary>
    public static string Decode(byte[] field)
    {
        if (field is null || field.Length == 0) return string.Empty;

        int cut = field.Length;
        bool previousWasZero = false;
        for (int i = 0; i < field.Length; i++)
        {
            if (previousWasZero && field[i] == 0)
            {
                cut = i;
                break;
            }
            previousWasZero = field[i] == 0;
        }
        if (cut % 2 != 0) cut--;

        return cut <= 0 ? string.Empty : Encoding.Unicode.GetString(field, 0, cut);
    }

    /// <summary>Byte length of <paramref name="name"/> as UTF-16 (native <c>getFile_name_length</c>).</summary>
    public static int WireLength(string name) => Encoding.Unicode.GetByteCount(name ?? string.Empty);

    /// <summary>
    /// Encodes <paramref name="name"/> into a fresh <see cref="NameBytes"/>-long field,
    /// truncated so the field always keeps a terminator.
    /// </summary>
    public static byte[] Encode(string name)
    {
        var field = new byte[NameBytes];
        if (string.IsNullOrEmpty(name)) return field;

        byte[] bytes = Encoding.Unicode.GetBytes(name);
        int copy = Math.Min(bytes.Length, NameBytes - 2);
        Array.Copy(bytes, field, copy);
        return field;
    }
}
