// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Tests;

public class SuperButtonRawDecoderTests
{
    private static byte[] Idle()
    {
        var report = new byte[64];
        report[0] = SuperButtonRawDecoder.ReportId;
        return report;
    }

    [Theory]
    [InlineData((byte)0x04, "A", 0)]  // bit 2: first of A's pair
    [InlineData((byte)0x08, "A", 1)]  // bit 3: second of A's pair
    [InlineData((byte)0x10, "B", 0)]  // bit 4
    [InlineData((byte)0x20, "B", 1)]  // bit 5
    [InlineData((byte)0x40, "X", 0)]  // bit 6
    [InlineData((byte)0x80, "X", 1)]  // bit 7
    public void Byte26_DecodesTheExactButtonOfEachPair(byte byte26, string expectedInterface, int expectedPairIndex)
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] report = Idle();
        report[26] = byte26;

        SuperButtonRawPress press = Assert.Single(decoder.Decode(report));
        Assert.Equal(expectedInterface, press.Interface);
        Assert.Equal(expectedPairIndex, press.PairIndex);
        Assert.True(press.IsPressed);
    }

    [Theory]
    [InlineData((byte)0x01, 0)]
    [InlineData((byte)0x02, 1)]
    public void Byte27_DecodesConnectionY(byte byte27, int expectedPairIndex)
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] report = Idle();
        report[27] = byte27;

        SuperButtonRawPress press = Assert.Single(decoder.Decode(report));
        Assert.Equal("Y", press.Interface);
        Assert.Equal(expectedPairIndex, press.PairIndex);
        Assert.True(press.IsPressed);
    }

    [Fact]
    public void BothButtonsOfAPairPressedTogetherEachProduceOnePress()
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] report = Idle();
        report[26] = 0x0c;   // both bits of A's pair

        IReadOnlyList<SuperButtonRawPress> presses = decoder.Decode(report);

        Assert.Equal(2, presses.Count);
        Assert.Contains(presses, p => p is { Interface: "A", PairIndex: 0, IsPressed: true });
        Assert.Contains(presses, p => p is { Interface: "A", PairIndex: 1, IsPressed: true });
    }

    [Fact]
    public void DoesNotRepeatWhileHeld()
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] held = Idle();
        held[26] = 0x04;

        Assert.Single(decoder.Decode(held));
        Assert.Empty(decoder.Decode(held));
    }

    [Fact]
    public void ReleaseFiresWhenTheBitClears()
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] pressed = Idle();
        pressed[26] = 0x04;
        Assert.Single(decoder.Decode(pressed));

        SuperButtonRawPress release = Assert.Single(decoder.Decode(Idle()));
        Assert.Equal("A", release.Interface);
        Assert.Equal(0, release.PairIndex);
        Assert.False(release.IsPressed);
    }

    [Fact]
    public void FiresAgainAfterReleaseAndPress()
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] pressed = Idle();
        pressed[26] = 0x04;
        Assert.Single(decoder.Decode(pressed));

        Assert.Single(decoder.Decode(Idle()));          // release
        Assert.Single(decoder.Decode(pressed));         // pressed again
    }

    [Fact]
    public void TwoConnectionsPressedTogetherEachProduceOnePress()
    {
        var decoder = new SuperButtonRawDecoder();

        byte[] report = Idle();
        report[26] = 0x04;  // A, first of pair
        report[27] = 0x01;  // Y, first of pair

        IReadOnlyList<SuperButtonRawPress> presses = decoder.Decode(report);

        Assert.Equal(2, presses.Count);
        Assert.Contains(presses, p => p.Interface == "A" && p.PairIndex == 0);
        Assert.Contains(presses, p => p.Interface == "Y" && p.PairIndex == 0);
    }

    [Fact]
    public void IgnoresReportsFromAnotherCollectionOrTooShort()
    {
        var decoder = new SuperButtonRawDecoder();

        Assert.Empty(decoder.Decode(new byte[] { 0x01, 0xff }));           // wrong report id, short
        Assert.Empty(decoder.Decode(new byte[28]));                        // report id 0x00, long enough
    }
}
