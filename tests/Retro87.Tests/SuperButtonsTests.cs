// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Tests;

public class SuperButtonsTests
{
    [Fact]
    public void TenButtons_KeepTheDeviceWireOrder()
    {
        Assert.Equal(10, SuperButtons.Count);
        Assert.Equal(new[] { 9, 10, 108, 109, 110, 111, 112, 113, 114, 115 },
                     SuperButtons.WireIndices);
        Assert.Equal(new[] { "KA", "KB", "K1", "K2", "K3", "K4", "K5", "K6", "K7", "K8" },
                     SuperButtons.WireNames);
    }

    [Fact]
    public void PositionsAndInterfaces_FollowTheLayout()
    {
        Assert.Equal(85, SuperButtons.PositionOf(0));
        Assert.Equal(94, SuperButtons.PositionOf(9));
        Assert.Equal(0, SuperButtons.OrdinalOfPosition(85));
        Assert.Equal(9, SuperButtons.OrdinalOfPosition(94));
        Assert.Equal(-1, SuperButtons.OrdinalOfPosition(84));

        Assert.Null(SuperButtons.InterfaceOf(0));
        Assert.Null(SuperButtons.InterfaceOf(1));
        Assert.Equal("A", SuperButtons.InterfaceOf(2));
        Assert.Equal("A", SuperButtons.InterfaceOf(3));
        Assert.Equal("Y", SuperButtons.InterfaceOf(9));

        Assert.Equal(0, SuperButtons.OrdinalOfWireIndex(9));
        Assert.Equal(9, SuperButtons.OrdinalOfWireIndex(115));
        Assert.Equal(-1, SuperButtons.OrdinalOfWireIndex(11));
    }
}
