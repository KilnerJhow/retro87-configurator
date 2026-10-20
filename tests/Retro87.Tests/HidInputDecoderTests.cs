// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Tests;

public class HidInputDecoderTests
{
    private static readonly byte[] NkroRelease = new byte[17] { 0x03, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    [Fact]
    public void ConsumerReport_DecodesTheMediaUsage()
    {
        var decoder = new HidInputDecoder();

        HidInputPress press = Assert.Single(decoder.Decode(new byte[] { 0x02, 0xCD, 0x00 }));

        Assert.Equal(0xCD, press.Code);
        Assert.Equal(MappingTargetKind.Media, press.Kind);
        Assert.Empty(decoder.Decode(new byte[] { 0x02, 0x00, 0x00 }));
    }

    [Fact]
    public void ConsumerReport_DoesNotRepeatWhileHeld()
    {
        var decoder = new HidInputDecoder();
        byte[] play = { 0x02, 0xCD, 0x00 };

        Assert.Single(decoder.Decode(play));
        Assert.Empty(decoder.Decode(play));
    }

    [Fact]
    public void NkroReport_DecodesTheKeyBit()
    {
        var decoder = new HidInputDecoder();
        byte[] report = (byte[])NkroRelease.Clone();
        report[2] = 0x10;   // byte 0 of the bitmap, bit 4 = usage 4 = A

        HidInputPress press = Assert.Single(decoder.Decode(report));

        Assert.Equal(0x04, press.Code);
        Assert.Equal(MappingTargetKind.Keyboard, press.Kind);
    }

    [Fact]
    public void NkroReport_EmitsOnlyNewPressesAndHandlesRelease()
    {
        var decoder = new HidInputDecoder();
        byte[] report = (byte[])NkroRelease.Clone();
        report[2] = 0x10;

        Assert.Single(decoder.Decode(report));
        Assert.Empty(decoder.Decode(report));       // held: same bitmap
        Assert.Empty(decoder.Decode(NkroRelease));  // released
        Assert.Single(decoder.Decode(report));      // pressed again
    }

    [Fact]
    public void NkroReport_DecodesModifiers()
    {
        var decoder = new HidInputDecoder();
        byte[] report = (byte[])NkroRelease.Clone();
        report[1] = 0x02;   // left shift

        HidInputPress press = Assert.Single(decoder.Decode(report));

        Assert.Equal(0xE1, press.Code);
        Assert.Equal(MappingTargetKind.Keyboard, press.Kind);
    }

    [Fact]
    public void NkroReport_SkipsErrorAndReservedUsages()
    {
        var decoder = new HidInputDecoder();
        byte[] report = (byte[])NkroRelease.Clone();
        report[2] = 0x0F;   // usages 0..3: reserved/error codes

        Assert.Empty(decoder.Decode(report));
    }

    [Fact]
    public void BootReport_DecodesTheSixKeyArray()
    {
        var decoder = new HidInputDecoder();

        HidInputPress press = Assert.Single(decoder.Decode(new byte[] { 0x01, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00 }));

        Assert.Equal(0x04, press.Code);
        Assert.Equal(MappingTargetKind.Keyboard, press.Kind);
        Assert.Empty(decoder.Decode(new byte[] { 0x01, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00 }));
    }

    [Theory]
    [InlineData(0x01, 0xE8)]   // left button
    [InlineData(0x02, 0xEA)]   // right button
    [InlineData(0x04, 0xE9)]   // middle button
    [InlineData(0x08, 0xEB)]   // five
    [InlineData(0x10, 0xEC)]   // four
    public void MouseReport_DecodesTheButtonId(byte buttons, byte expected)
    {
        var decoder = new HidInputDecoder();

        HidInputPress press = Assert.Single(decoder.Decode(new byte[] { 0x04, buttons, 0x00, 0x00, 0x00 }));

        Assert.Equal(expected, press.Code);
        Assert.Equal(MappingTargetKind.Mouse, press.Kind);
    }

    [Fact]
    public void MouseReport_DecodesTheWheelStep()
    {
        var decoder = new HidInputDecoder();

        HidInputPress up = Assert.Single(decoder.Decode(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x02 }));
        HidInputPress down = Assert.Single(decoder.Decode(new byte[] { 0x04, 0x00, 0x00, 0x00, 0xFE }));

        Assert.Equal(0xEE, up.Code);
        Assert.Equal(0xEF, down.Code);
    }

    [Fact]
    public void UnknownReport_IsIgnored()
    {
        var decoder = new HidInputDecoder();

        Assert.Empty(decoder.Decode(new byte[] { 0x7F, 0x01, 0x02 }));
    }
}
