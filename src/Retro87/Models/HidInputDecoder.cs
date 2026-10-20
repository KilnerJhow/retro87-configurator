// SPDX-License-Identifier: GPL-3.0-or-later

using System.Numerics;

namespace Retro87.Models;

/// <summary>
/// One press decoded from a HID input report. <see cref="Code"/> is the HID usage for keyboard and
/// media reports and the Xbox mouse id (<c>0xE8</c>..<c>0xF1</c>) for mouse reports, so the caller
/// can resolve it against <see cref="MappingIdSpace"/>.
/// </summary>
public readonly record struct HidInputPress(byte Code, MappingTargetKind Kind);

/// <summary>
/// Decodes the input reports of the Retro 87's keyboard, consumer-control and mouse collections.
/// </summary>
/// <remarks>
/// <para>
/// The collections expose three input report formats (see the HID report descriptor):
/// </para>
/// <list type="bullet">
/// <item><c>0x01</c> — boot keyboard: modifier byte + six key codes;</item>
/// <item><c>0x02</c> — consumer control: one 16-bit usage, little-endian;</item>
/// <item><c>0x03</c> — NKRO keyboard: modifier byte + 120 one-bit usages (<c>0x00</c>..<c>0x77</c>);</item>
/// <item><c>0x04</c> — mouse: five buttons, X, Y and wheel.</item>
/// </list>
/// <para>
/// The decoder holds the previous state of each report so it can emit only <b>new</b> presses;
/// holding a key produces repeated identical reports and must not fire again. Reports from the
/// device include the report id as the first byte (verified on the hardware).
/// </para>
/// <para>
/// Media keys are decoded here even when the desktop consumes them before the window sees them —
/// which is exactly the case the Super Button listener exists for.
/// </para>
/// </remarks>
public sealed class HidInputDecoder
{
    /// <summary>Report id of the boot keyboard collection.</summary>
    public const byte ReportBootKeyboard = 0x01;

    /// <summary>Report id of the consumer (media) collection.</summary>
    public const byte ReportConsumer = 0x02;

    /// <summary>Report id of the NKRO keyboard collection.</summary>
    public const byte ReportNkroKeyboard = 0x03;

    /// <summary>Report id of the mouse collection.</summary>
    public const byte ReportMouse = 0x04;

    private const int NkroBytes = 15;   // 120 bits, usages 0x00..0x77
    private const int BootKeys = 6;

    private byte _modifiers;
    private readonly byte[] _nkro = new byte[NkroBytes];
    private byte _consumerLow;
    private byte _consumerHigh;
    private readonly byte[] _bootKeys = new byte[BootKeys];
    private byte _mouseButtons;

    // Xbox ids of the mouse actions: the family stores them in the mapping table with this
    // numbering, and the JP family translates them in MappingIdSpace.IdForUsage.
    private static readonly byte[] MouseButtonIds = { 0xE8, 0xEA, 0xE9, 0xEB, 0xEC, 0x00, 0x00, 0xED };
    private const byte MouseScrollUp = 0xEE;
    private const byte MouseScrollDown = 0xEF;

    /// <summary>Decodes one report and returns the presses it introduced, in report order.</summary>
    public IReadOnlyList<HidInputPress> Decode(ReadOnlySpan<byte> report)
    {
        var presses = new List<HidInputPress>(4);
        if (report.Length < 2) return presses;

        switch (report[0])
        {
            case ReportConsumer:
                DecodeConsumer(report, presses);
                break;
            case ReportNkroKeyboard:
                DecodeNkro(report, presses);
                break;
            case ReportBootKeyboard:
                DecodeBoot(report, presses);
                break;
            case ReportMouse:
                DecodeMouse(report, presses);
                break;
        }
        return presses;
    }

    private void DecodeConsumer(ReadOnlySpan<byte> report, List<HidInputPress> presses)
    {
        if (report.Length < 3) return;

        byte low = report[1];
        byte high = report[2];
        if (low == _consumerLow && high == _consumerHigh) return;

        _consumerLow = low;
        _consumerHigh = high;

        // 0x0000 is the "released" state; anything else is the media usage currently held.
        ushort usage = (ushort)(low | (high << 8));
        if (usage != 0)
            presses.Add(new HidInputPress((byte)usage, MappingTargetKind.Media));
    }

    private void DecodeNkro(ReadOnlySpan<byte> report, List<HidInputPress> presses)
    {
        if (report.Length < 2 + NkroBytes) return;

        DecodeModifiers(report[1], presses);

        for (int index = 0; index < NkroBytes; index++)
        {
            byte current = report[2 + index];
            byte previous = _nkro[index];
            _nkro[index] = current;

            int changed = current & ~previous;
            while (changed != 0)
            {
                int bit = BitOperations.TrailingZeroCount(changed);
                changed &= changed - 1;

                int usage = index * 8 + bit;
                // 0x00 is reserved and 0x01..0x03 are the error/rollover codes.
                if (usage <= 3 || usage > 0x77) continue;
                presses.Add(new HidInputPress((byte)usage, MappingTargetKind.Keyboard));
            }
        }
    }

    private void DecodeBoot(ReadOnlySpan<byte> report, List<HidInputPress> presses)
    {
        if (report.Length < 2 + BootKeys) return;

        DecodeModifiers(report[1], presses);

        Span<byte> current = stackalloc byte[BootKeys];
        report.Slice(2, BootKeys).CopyTo(current);

        for (int i = 0; i < BootKeys; i++)
        {
            byte code = current[i];
            if (code is 0 or 1 or 2 or 3) continue;
            if (Array.IndexOf(_bootKeys, code) >= 0) continue;
            presses.Add(new HidInputPress(code, MappingTargetKind.Keyboard));
        }

        current.CopyTo(_bootKeys);
    }

    private void DecodeModifiers(byte modifiers, List<HidInputPress> presses)
    {
        int changed = modifiers & ~_modifiers;
        _modifiers = modifiers;

        while (changed != 0)
        {
            int bit = BitOperations.TrailingZeroCount(changed);
            changed &= changed - 1;
            presses.Add(new HidInputPress((byte)(0xE0 + bit), MappingTargetKind.Keyboard));
        }
    }

    private void DecodeMouse(ReadOnlySpan<byte> report, List<HidInputPress> presses)
    {
        if (report.Length < 3) return;

        byte buttons = report[1];
        int changed = buttons & ~_mouseButtons;
        _mouseButtons = buttons;

        while (changed != 0)
        {
            int bit = BitOperations.TrailingZeroCount(changed);
            changed &= changed - 1;
            if (bit >= MouseButtonIds.Length) continue;

            byte id = MouseButtonIds[bit];
            if (id != 0)
                presses.Add(new HidInputPress(id, MappingTargetKind.Mouse));
        }

        // The wheel is relative: every non-zero report is one step, not a held state.
        if (report.Length >= 5)
        {
            sbyte wheel = (sbyte)report[4];
            if (wheel > 0)
                presses.Add(new HidInputPress(MouseScrollUp, MappingTargetKind.Mouse));
            else if (wheel < 0)
                presses.Add(new HidInputPress(MouseScrollDown, MappingTargetKind.Mouse));
        }
    }
}
