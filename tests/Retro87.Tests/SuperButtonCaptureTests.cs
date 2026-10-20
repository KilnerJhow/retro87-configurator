// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Input;
using Retro87.Models;

namespace Retro87.Tests;

public class SuperButtonCaptureTests
{
    [Theory]
    [InlineData(Key.A, 0x04, MappingTargetKind.Keyboard)]
    [InlineData(Key.Z, 0x1D, MappingTargetKind.Keyboard)]
    [InlineData(Key.D1, 0x1E, MappingTargetKind.Keyboard)]
    [InlineData(Key.D0, 0x27, MappingTargetKind.Keyboard)]
    [InlineData(Key.F1, 0x3A, MappingTargetKind.Keyboard)]
    [InlineData(Key.F12, 0x45, MappingTargetKind.Keyboard)]
    [InlineData(Key.F13, 0x68, MappingTargetKind.Keyboard)]
    [InlineData(Key.Pause, 0x48, MappingTargetKind.Keyboard)]
    [InlineData(Key.MediaNextTrack, 0xB5, MappingTargetKind.Media)]
    [InlineData(Key.MediaPlayPause, 0xCD, MappingTargetKind.Media)]
    [InlineData(Key.VolumeUp, 0xE9, MappingTargetKind.Media)]
    public void TryMap_ReturnsTheWireUsageAndKind(
        Key key, byte expectedUsage, MappingTargetKind expectedKind)
    {
        Assert.True(SuperButtonCapture.TryMap(key, out byte usage, out MappingTargetKind kind));
        Assert.Equal(expectedUsage, usage);
        Assert.Equal(expectedKind, kind);
    }

    [Fact]
    public void ForFamily_TranslatesModifiersOnlyForJp()
    {
        Assert.Equal(0xE0, SuperButtonCapture.ForFamily(0xE0, MappingIdSpace.Xbox));
        Assert.Equal(MappingIds.LCTRL_MAP, SuperButtonCapture.ForFamily(0xE0, MappingIdSpace.Jp));
        Assert.Equal(MappingIds.RWIN_MAP, SuperButtonCapture.ForFamily(0xE7, MappingIdSpace.Jp));
        Assert.Equal(0x48, SuperButtonCapture.ForFamily(0x48, MappingIdSpace.Jp));
    }
}
