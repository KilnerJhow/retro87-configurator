// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// A press or release on one of the eight wireless Super Buttons, decoded from the keyboard's
/// 64-byte vendor report — the one <see cref="HidInputDecoder"/> never reads.
/// </summary>
/// <param name="Interface">The connection: "A", "B", "X" or "Y".</param>
/// <param name="PairIndex">0 for the first Super Button of the connection's pair, 1 for the second.</param>
/// <param name="IsPressed">True while the contact is closed; false the instant it opens again.</param>
public readonly record struct SuperButtonRawPress(string Interface, int PairIndex, bool IsPressed);

/// <summary>
/// Decodes the vendor-defined 64-byte collection the dongle streams continuously (report id
/// <c>0x02</c>, read every 1 ms). Two status bytes carry one bit per Super Button — eight
/// independent contacts, each reported exactly as it is: pressed or not, regardless of its pair's
/// other button.
/// </summary>
/// <remarks>
/// Observed on the wire on 2026-10-19 (USB capture of the dongle): byte 26
/// holds A (bits 2-3), B (bits 4-5) and X (bits 6-7); byte 27 holds Y (bits 0-1). Within a pair the
/// lower bit is the first Super Button, the higher bit the second. The two bits of a pair are
/// independent contacts — both read 1 when both buttons are held at once.
/// </remarks>
public sealed class SuperButtonRawDecoder
{
    /// <summary>Report id of the vendor collection.</summary>
    public const byte ReportId = 0x02;

    private const int ByteLow = 26;
    private const int ByteHigh = 27;

    private bool _a0, _a1, _b0, _b1, _x0, _x1, _y0, _y1;

    /// <summary>Decodes one report and returns the presses and releases it introduced.</summary>
    public IReadOnlyList<SuperButtonRawPress> Decode(ReadOnlySpan<byte> report)
    {
        var presses = new List<SuperButtonRawPress>(2);
        if (report.Length <= ByteHigh || report[0] != ReportId) return presses;

        byte low = report[ByteLow];
        byte high = report[ByteHigh];

        Emit((low & 0b0000_0100) != 0, ref _a0, "A", 0, presses);
        Emit((low & 0b0000_1000) != 0, ref _a1, "A", 1, presses);
        Emit((low & 0b0001_0000) != 0, ref _b0, "B", 0, presses);
        Emit((low & 0b0010_0000) != 0, ref _b1, "B", 1, presses);
        Emit((low & 0b0100_0000) != 0, ref _x0, "X", 0, presses);
        Emit((low & 0b1000_0000) != 0, ref _x1, "X", 1, presses);
        Emit((high & 0b0000_0001) != 0, ref _y0, "Y", 0, presses);
        Emit((high & 0b0000_0010) != 0, ref _y1, "Y", 1, presses);

        return presses;
    }

    private static void Emit(bool isOn, ref bool wasOn, string interfaceName, int pairIndex,
        List<SuperButtonRawPress> presses)
    {
        if (isOn != wasOn) presses.Add(new SuperButtonRawPress(interfaceName, pairIndex, isOn));
        wasOn = isOn;
    }
}
