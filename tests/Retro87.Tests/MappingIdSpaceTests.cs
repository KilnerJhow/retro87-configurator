// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Tests;

public class MappingIdSpaceTests
{
    [Theory]
    [InlineData(0x04, MappingTargetKind.Keyboard, 4)]              // A
    [InlineData(0x29, MappingTargetKind.Keyboard, MappingIds.ESC_MAP)]
    [InlineData(0x48, MappingTargetKind.Keyboard, MappingIds.PAUSE_MAP)]
    [InlineData(0xE0, MappingTargetKind.Keyboard, MappingIds.LCTRL_MAP)]
    [InlineData(0xE7, MappingTargetKind.Keyboard, MappingIds.RWIN_MAP)]
    [InlineData(0xB5, MappingTargetKind.Media, MappingIds.Next_MAP)]
    [InlineData(0xCD, MappingTargetKind.Media, MappingIds.Play_MAP)]
    [InlineData(0xE9, MappingTargetKind.Media, MappingIds.VoiceUp_MAP)]
    [InlineData(0xE8, MappingTargetKind.Mouse, MappingIds.LEFT_CLICK)]
    [InlineData(0xEE, MappingTargetKind.Mouse, MappingIds.UP_ROLL)]
    public void Jp_TranslatesTheUsageToItsOwnId(
        byte usage, MappingTargetKind kind, byte expected)
        => Assert.Equal(expected, MappingIdSpace.Jp.IdForUsage(usage, kind));

    [Theory]
    [InlineData(0x04, MappingTargetKind.Keyboard)]
    [InlineData(0xE0, MappingTargetKind.Keyboard)]
    [InlineData(0xB5, MappingTargetKind.Media)]
    [InlineData(0xCD, MappingTargetKind.Media)]
    [InlineData(0xE8, MappingTargetKind.Mouse)]
    public void Xbox_KeepsTheUsageAsTheId(byte usage, MappingTargetKind kind)
        => Assert.Equal(usage, MappingIdSpace.Xbox.IdForUsage(usage, kind));

    [Fact]
    public void MediaAndShortcut_AreTheSameCategoryForTheJpFamily()
    {
        Assert.Equal(
            MappingIdSpace.Jp.IdForUsage(0xCD, MappingTargetKind.Media),
            MappingIdSpace.Jp.IdForUsage(0xCD, MappingTargetKind.Shortcut));
    }
}
