// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Tests;

public class XboxFromKeyboardConfigTests
{
    [Fact]
    public void Plan_AddressesSuperButtonByRecordIndex()
    {
        var baseline = new XboxConfig { Raw = new byte[XboxWire.ConfigSize] };
        var ui = new KeyboardConfig();
        ui.BoardKeys[SuperButtons.PositionOf(0)] = 0x48;   // Pause

        XboxWritePlan plan = XboxFromKeyboardConfig.Plan(ui, baseline);

        XboxMappingChange change = Assert.Single(plan.Mappings);
        Assert.Equal(SuperButtons.WireIndexOf(0), change.Index);
        Assert.Equal(SuperButtons.WireKeyCode, change.KeyCode);
        Assert.Equal(SuperButtons.WireKeyCode, change.Record[0]);
        Assert.Equal(0x48, change.Record[4]);
        Assert.Equal((byte)XboxMappingType.KeyCode, change.Record[8]);
        Assert.Empty(plan.Macros);
    }

    [Fact]
    public void Plan_LeavesUnchangedSuperButtonsAlone()
    {
        var baseline = new XboxConfig { Raw = new byte[XboxWire.ConfigSize] };

        XboxWritePlan plan = XboxFromKeyboardConfig.Plan(new KeyboardConfig(), baseline);

        Assert.Empty(plan.Mappings);
        Assert.Empty(plan.Macros);
    }

    [Fact]
    public void BuildRecord_WritesTheWireFields()
    {
        byte[] record = XboxFromKeyboardConfig.BuildRecord(
            SuperButtons.WireKeyCode, 0x12345678, XboxMappingType.Macro,
            new byte[] { 0xAA, 0xBB, 0xCC });

        Assert.Equal(XboxWire.MappingSize, record.Length);
        Assert.Equal(SuperButtons.WireKeyCode, record[0]);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, record[1..4]);
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, record[4..8]);
        Assert.Equal((byte)XboxMappingType.Macro, record[8]);
    }
}
