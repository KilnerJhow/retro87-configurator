// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Tests;

/// <summary>
/// The JP family numbers "[" and "]" 137/138, but the Retro 87 X keeps them under their HID
/// usages 0x2F/0x30 (records 55 and 56 of a real read). These tests pin both directions.
/// </summary>
public class XboxBracketKeysTests
{
    private static readonly int LeftBracketPosition = Array.IndexOf(KeyboardLayout.PositionNames, "[");
    private static readonly int RightBracketPosition = Array.IndexOf(KeyboardLayout.PositionNames, "]");

    [Fact]
    public void Convert_ReadsTheBracketsFromTheirHidKeyCodes()
    {
        KeyboardConfig config = XboxToKeyboardConfig.Convert(FactoryXboxConfig());

        Assert.Equal(0x2F, config.BoardKeys[LeftBracketPosition]);
        Assert.Equal(0x30, config.BoardKeys[RightBracketPosition]);
    }

    [Fact]
    public void Convert_OfAFactoryConfig_LeavesEveryBoardKeyAtTheXboxDefault()
    {
        KeyboardConfig config = XboxToKeyboardConfig.Convert(FactoryXboxConfig());

        for (int position = 0; position < KeyboardLayout.FirstExtraPosition; position++)
            Assert.True(config.BoardKeys[position] == MappingIdSpace.Xbox.DefaultTargets[position],
                $"position {position} ({KeyboardLayout.PositionNames[position]}): read " +
                $"0x{config.BoardKeys[position]:X2}, default 0x{MappingIdSpace.Xbox.DefaultTargets[position]:X2}");
    }

    [Fact]
    public void Plan_WritesAnEditedLeftBracketToItsOwnRecord()
    {
        XboxConfig baseline = FactoryXboxConfig();
        KeyboardConfig ui = XboxToKeyboardConfig.Convert(baseline);
        ui.BoardKeys[LeftBracketPosition] = 0x48;   // Pause

        XboxWritePlan plan = XboxFromKeyboardConfig.Plan(ui, baseline);

        XboxMappingChange change = Assert.Single(plan.Mappings);
        Assert.Equal(0x2F, change.KeyCode);
        Assert.Equal(0x48, change.Record[4]);
    }

    [Fact]
    public void JpDefaults_KeepTheJpBracketIds()
    {
        Assert.Equal(MappingIds.LEFTBRACKET_CLICK1, MappingIdSpace.Jp.DefaultTargets[LeftBracketPosition]);
        Assert.Equal(MappingIds.RIGHTBRACKET_CLICK1, MappingIdSpace.Jp.DefaultTargets[RightBracketPosition]);
    }

    /// <summary>
    /// An Xbox configuration where every key maps to itself, as the device ships: one identity
    /// record per HID usage of the board (letters through F12/navigation, and the eight modifiers).
    /// </summary>
    private static XboxConfig FactoryXboxConfig()
    {
        var usages = Enumerable.Range(0x04, 0x65 - 0x04 + 1)
            .Concat(Enumerable.Range(0xE0, 8))
            .Select(usage => (byte)usage)
            .ToArray();

        var config = new XboxConfig { Raw = new byte[XboxWire.ConfigSize] };
        for (int index = 0; index < usages.Length; index++)
        {
            byte usage = usages[index];
            config.Mappings[index].KeyCode = usage;
            config.Mappings[index].Mapping = usage;
            config.Mappings[index].Type = XboxMappingType.None;

            byte[] record = XboxFromKeyboardConfig.BuildRecord(usage, usage, XboxMappingType.None, new byte[3]);
            record.CopyTo(config.Raw, XboxWire.MappingBase + index * XboxWire.MappingSize);
        }
        return config;
    }
}
