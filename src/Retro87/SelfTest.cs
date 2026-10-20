// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Protocol;
using Retro87.Services;
using Retro87.ViewModels;

namespace Retro87;

/// <summary>
/// Hardware-free verification of the ported model/protocol layer, run with
/// <c>Retro87 --selftest</c>. It exercises the tables, the mapping payload codec, the name
/// codec, the macro body builder and the <c>writeMacroJP</c> chunk arithmetic, printing one
/// line per check so the result can be inspected on any platform.
/// </summary>
internal static class SelfTest
{
    internal static bool Run(TextWriter output)
    {
        bool ok = true;

        void Check(bool condition, string name)
        {
            output.WriteLine($"{(condition ? "ok  " : "FAIL")}  {name}");
            if (!condition) ok = false;
        }

        void Info(string message) => output.WriteLine($"      {message}");

        // ------------------------------------------------------------- layout tables
        output.WriteLine("layout");
        Check(KeyboardLayout.DefaultMappingId.Length == KeyboardLayout.PositionCount, "DefaultMappingId has 95 positions");
        Check(KeyboardLayout.PositionKeyId.Length == KeyboardLayout.PositionCount, "PositionKeyId has 95 positions");
        Check(KeyboardLayout.PositionNames.Length == KeyboardLayout.PositionCount, "PositionNames has 95 positions");

        byte[] keys = MappingTables.GetKeys();
        bool sameBackingTable = true;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != KeyboardLayout.DefaultMappingId[i]) sameBackingTable = false;
        }
        Check(sameBackingTable, "DefaultMappingId is MappingTables.GetKeys()");

        Check(KeyboardLayout.PositionKeyId[0] == MappingIds.A_MAP, "position 0 is A");
        Check(KeyboardLayout.PositionKeyId[52] == MappingIds.CAPSLOCK_MAP, "position 52 is CapsLock");
        Check(KeyboardLayout.PositionKeyId[78] == MappingIds.LCTRL_MAP, "position 78 is LeftCtrl");
        Check(KeyboardLayout.PositionKeyId[84] == MappingIds.RALT_MAP, "position 84 is RightAlt");
        Check(KeyboardLayout.DefaultMappingId[85] == MappingIds.NULL_MAP, "extra keys default to unmapped");

        byte[] expectedExtra =
        {
            MappingIds.KA_MAP, MappingIds.KB_MAP,
            MappingIds.K1_MAP, MappingIds.K2_MAP, MappingIds.K3_MAP, MappingIds.K4_MAP,
            MappingIds.K5_MAP, MappingIds.K6_MAP, MappingIds.K7_MAP, MappingIds.K8_MAP,
        };
        bool extraOk = true;
        for (int i = 0; i < expectedExtra.Length; i++)
        {
            if (KeyboardLayout.PositionKeyId[KeyboardLayout.FirstExtraPosition + i] != expectedExtra[i]) extraOk = false;
        }
        Check(extraOk, "positions 85..94 are KA, KB, K1..K8");

        bool positionsResolve = true;
        for (int i = 0; i < KeyboardLayout.PositionKeyId.Length; i++)
        {
            if (KeyboardLayout.PositionOfKeyId(KeyboardLayout.PositionKeyId[i]) != i) positionsResolve = false;
        }
        Check(positionsResolve, "PositionOfKeyId inverts PositionKeyId");

        // ------------------------------------------------------------- mapping codec
        output.WriteLine("mapping payload codec");

        int aliasesWithoutTransport = 0;
        int aliasesWithTransport = 0;
        int roundTripFailures = 0;
        int fixpointFailures = 0;
        int unmapped = 0;
        bool noPayloadForUnmapped = true;
        var aliasReport = new List<string>();

        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            byte id = (byte)raw;
            byte[] payload = MappingPayloadCodec.EncodePayload(id);
            bool empty = true;
            foreach (byte b in payload)
            {
                if (b != 0) empty = false;
            }

            if (empty)
            {
                unmapped++;
                if (MappingPayloadCodec.DecodeId(payload) != MappingIds.NULL_MAP) noPayloadForUnmapped = false;
                if (MappingPayloadCodec.DecodeId(0, payload) != MappingIds.NULL_MAP) noPayloadForUnmapped = false;
                continue;
            }

            byte transport = MappingPayloadCodec.TransportByteOf(id);
            byte decoded = MappingPayloadCodec.DecodeId(transport, payload);

            if (decoded != id)
            {
                aliasesWithTransport++;
                aliasReport.Add($"0x{id:X2} (transport {transport}) -> 0x{decoded:X2}");
            }
            if (MappingPayloadCodec.DecodeId(payload) != id) aliasesWithoutTransport++;

            // The property that matters on hardware: a read followed by a write must send the
            // very same bytes back, even when the id itself is not recovered exactly.
            byte[] again = MappingPayloadCodec.EncodePayload(decoded);
            for (int i = 0; i < payload.Length; i++)
            {
                if (payload[i] != again[i]) fixpointFailures++;
            }

            if (decoded == MappingIds.NULL_MAP) roundTripFailures++;
        }

        Check(noPayloadForUnmapped, "ids without a payload decode to unmapped");
        Check(roundTripFailures == 0, "no id with a payload decodes to unmapped");
        Check(aliasesWithTransport == 0, "transport + payload identifies every id exactly");
        Check(fixpointFailures == 0, "decode -> encode returns the identical payload (read/write idempotent)");
        foreach (string line in aliasReport) Info($"alias {line}");
        Info($"{aliasesWithoutTransport} of 256 ids would be ambiguous if the transport byte were ignored");
        Info($"{unmapped} ids have an empty payload (unmapped / extra keys without a body)");

        Check(MappingPayloadCodec.DecodeId(new byte[] { 1, 0, 0, 0, 0 }) == MappingIds.LEFT_CLICK, "mouse left click decodes");
        Check(MappingPayloadCodec.DecodeId(MappingPayloadCodec.TransportMouse, new byte[] { 1, 0, 0, 0, 0 }) == MappingIds.LEFT_CLICK, "mouse left click decodes with its transport");
        Check(MappingPayloadCodec.DecodeId(new byte[] { 0, 41, 0, 0, 0 }) == MappingIds.ESC_MAP, "escape decodes");
        Check(MappingPayloadCodec.DecodeId(new byte[] { 0, 0, 0, 0, 0 }) == MappingIds.NULL_MAP, "empty payload is unmapped");
        Check(MappingPayloadCodec.DecodeId(MappingPayloadCodec.TransportKeyboard, new byte[] { 0xE2, 0, 0, 0, 0 }) == MappingIds.LALT_MAP,
            "payload 0xE2 on the keyboard transport is LeftAlt");
        Check(MappingPayloadCodec.DecodeId(MappingPayloadCodec.TransportShortcut, new byte[] { 0xE2, 0, 0, 0, 0 }) == MappingIds.NoVoice_MAP,
            "payload 0xE2 on the shortcut transport is NoVoice");
        Check(MappingPayloadCodec.TransportByteOf(MappingIds.NoVoice_MAP) == MappingPayloadCodec.TransportShortcut,
            "NoVoice is dispatched as a shortcut");
        Check(MappingPayloadCodec.EncodePayload(MappingIds.LCTRL_MAP)[0] == 0xE0, "LeftCtrl payload uses the 0xE0 modifier bit");
        Check(MappingPayloadCodec.EncodePayload(MappingIds.LSHIFT_MAP)[0] == 0xE1, "LeftShift payload uses the 0xE1 modifier bit");

        // ------------------------------------------------------------- name codec
        output.WriteLine("name codec");
        Check(DeviceNameCodec.Decode(DeviceNameCodec.Encode("Retro 87")) == "Retro 87", "name round trip");
        Check(DeviceNameCodec.Decode(DeviceNameCodec.Encode(string.Empty)) == string.Empty, "empty name round trip");
        Check(DeviceNameCodec.Decode(new byte[DeviceNameCodec.NameBytes]) == string.Empty, "zeroed field is an empty name");
        Check(DeviceNameCodec.WireLength("ABC") == 6, "wire length is UTF-16 based");

        // ------------------------------------------------------------- macro body + chunks
        output.WriteLine("macro body and writeMacroJP chunking");

        bool bodyCountOk = true;
        for (int steps = 1; steps <= 24; steps++)
        {
            var actions = new List<MacroAction>();
            for (int i = 0; i < steps; i++)
                actions.Add(new MacroAction { Kind = MacroActionKind.KeyDown, Usage = (byte)(4 + i) });
            byte[] body = JpProtocol.BuildMacroBody(actions, 500);

            if (body.Length != steps * 3 + 4) bodyCountOk = false;
            if (body[body.Length - 4] != 0x0F) bodyCountOk = false;
            if (body[body.Length - 3] != (byte)(500 & 0xFF)) bodyCountOk = false;
            if (body[body.Length - 2] != (byte)(500 >> 8)) bodyCountOk = false;
            if (body.Length / 3 != steps + 1) bodyCountOk = false;
        }
        Check(bodyCountOk, "body is steps*3 + the appended delay entry, and count includes it");

        byte[] single = JpProtocol.BuildMacroBody(
            new List<MacroAction> { new() { Kind = MacroActionKind.KeyDown, Usage = 4 } }, 0);
        Check(single.Length == 3, "a single step without a delay appends no entry");

        bool chunkOk = true;
        var chunkReport = new List<string>();
        foreach (int length in new[] { 0, 1, 3, 6, 21, 22, 24, 25, 40, 46, 64, 90, 100 })
        {
            var body = new byte[length];
            for (int i = 0; i < length; i++) body[i] = (byte)(i + 1);   // never zero, so a dropped byte shows up

            List<byte[]> frames = JpProtocol.BuildMacroFrames(4, body, 3);
            bool thisOk = true;

            if (length < 0x16)
            {
                thisOk &= frames.Count == 1;
                thisOk &= frames[0][0] == 0x76 && frames[0][1] == 4;
                thisOk &= frames[0][2] == 0 && frames[0][3] == 0 && frames[0][4] == 0;
                thisOk &= frames[0][5] == 0x1A && frames[0][6] == 0x01;
                thisOk &= frames[0][7] == 3 && frames[0][8] == 0;
                thisOk &= frames[0][9] == length / 3;
                for (int i = 0; i < length; i++) thisOk &= frames[0][10 + i] == body[i];
            }
            else
            {
                thisOk &= frames[0][2] == 1 && frames[0][5] == 0x19;
                thisOk &= frames[0][6] == 0x01 && frames[0][7] == 3 && frames[0][8] == 0;
                thisOk &= frames[0][9] == length / 3;
                for (int i = 0; i < 0x15; i++) thisOk &= frames[0][10 + i] == body[i];

                int cursor = 0x15;
                for (int f = 1; f < frames.Count; f++)
                {
                    byte[] chunk = frames[f];
                    int offset = chunk[3] | (chunk[4] << 8);
                    int chunkLength = chunk[5];

                    thisOk &= offset == cursor + 4;                       // contiguous record
                    thisOk &= chunkLength == Math.Min(0x18, length - cursor);
                    thisOk &= chunk[2] == (cursor + chunkLength < length ? 1 : 0);
                    for (int i = 0; i < chunkLength; i++) thisOk &= chunk[6 + i] == body[cursor + i];

                    cursor += chunkLength;
                }
                thisOk &= cursor == length;
                thisOk &= frames[frames.Count - 1][2] == 0;
            }

            chunkOk &= thisOk;
            chunkReport.Add($"{length}B -> {frames.Count} chunk(s){(thisOk ? "" : "  <== PROBLEM")}");
        }
        Check(chunkOk, "every body length is chunked contiguously with correct offsets and lengths");
        foreach (string line in chunkReport) Info(line);

        List<byte[]> infinite = JpProtocol.BuildMacroFrames(9, new byte[6], 0xFF);
        Check(infinite[0][7] == 0 && infinite[0][8] == 0x20, "cycles 0xFF is written as the infinite flag");

        // ------------------------------------------------------------- macro record decode
        output.WriteLine("macro record decode");

        var cases = new List<MacroSlot>
        {
            new() { HostKey = 4, Name = "M1", Cycles = 3, IntervalMs = 50 },
            new() { HostKey = 5, Name = "M2", Cycles = 0xFF, IntervalMs = 0 },
            new() { HostKey = 6, Name = "M3", Cycles = 1, IntervalMs = 300 },
            new() { HostKey = 7, Name = "M4", Cycles = 2, IntervalMs = 1000 },
        };
        cases[0].Actions.AddRange(new[]
        {
            new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.A_MAP },
            new MacroAction { Kind = MacroActionKind.KeyUp, Usage = MappingIds.A_MAP },
        });
        cases[1].Actions.Add(new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.B_MAP });
        for (int i = 0; i < 10; i++)
        {
            cases[2].Actions.Add(new MacroAction
            {
                Kind = MacroActionKind.KeyDown,
                Usage = (byte)(MappingIds.A_MAP + i),
                Op = i % 2 == 0 ? (byte)0x81 : (byte)0x83,   // plain and modifier variants
            });
            cases[2].Actions.Add(new MacroAction { Kind = MacroActionKind.KeyUp, Usage = (byte)(MappingIds.A_MAP + i) });
        }
        for (int i = 0; i < 6; i++)
            cases[3].Actions.Add(new MacroAction { Kind = MacroActionKind.KeyDown, Usage = (byte)(MappingIds.A_MAP + i) });

        bool macroOk = true;
        foreach (MacroSlot original in cases)
        {
            byte[] body = JpProtocol.BuildMacroBody(original.Actions, original.IntervalMs);

            var record = new byte[JpWire.MacroRecordLength];
            record[0] = 0x01;
            if (original.Cycles == 0xFF) record[2] = 0x20;
            else record[1] = original.Cycles;
            record[3] = (byte)(body.Length / 3);
            Array.Copy(body, 0, record, 4, body.Length);

            var decoded = new MacroSlot();
            JpProtocol.DecodeMacroSlot(decoded, original.HostKey, DeviceNameCodec.Encode(original.Name), record);

            bool thisOk = decoded.HostKey == original.HostKey
                       && decoded.Name == original.Name
                       && decoded.Cycles == original.Cycles
                       && decoded.IntervalMs == original.IntervalMs
                       && decoded.Actions.Count == original.Actions.Count;

            for (int i = 0; thisOk && i < original.Actions.Count; i++)
            {
                // Compare the wire bytes, not the semantic fields: an action built without an
                // explicit Op encodes to the common 0x81/0x01 form and must decode to the same
                // bytes, which is what a real read -> write cycle needs.
                byte[] expected = original.Actions[i].Encode();
                byte[] actual = decoded.Actions[i].Encode();
                thisOk &= decoded.Actions[i].Kind == original.Actions[i].Kind;
                thisOk &= expected.Length == actual.Length;
                for (int b = 0; thisOk && b < expected.Length; b++) thisOk &= expected[b] == actual[b];
            }

            macroOk &= thisOk;
            Info($"{original.Name}: {original.Actions.Count} step(s), interval {original.IntervalMs} ms, " +
                 $"record count {record[3]} -> {(thisOk ? "ok" : "MISMATCH")}");
        }
        Check(macroOk, "slots survive encode -> record -> decode (steps, ops, cycles, delay)");

        // ------------------------------------------------------------- one row per keyboard
        output.WriteLine("device identity");

        static DeviceInfo Make(ushort pid, string path, string? serial, string name = "8BitDo Retro 87 Keyboard X")
            => new(KnownDevices.EightBitDoVid, pid, name, serial, path);

        var vendorCollection = Make(KnownDevices.Retro87XDongle, @"\\?\hid#vid_2dc8&pid_202e&mi_02#9&2161af57&0&0000", "5CE89EA497");
        var mouseCollection = vendorCollection with { DevicePath = @"\\?\hid#vid_2dc8&pid_202e&mi_00#9&154dfdda&0&0000" };
        var keyboardCollection = vendorCollection with { DevicePath = @"\\?\hid#vid_2dc8&pid_202e&mi_01&col03#9&39253b9c&0&0002\kbd" };

        Check(DeviceIdentity.SamePhysicalDevice(vendorCollection, keyboardCollection),
            "collections of one keyboard share the physical identity");
        Check(DeviceIdentity.Distinct(new[] { vendorCollection, mouseCollection, keyboardCollection }).Count == 1,
            "the HID collections of one keyboard collapse into one row");
        Check(DeviceIdentity.Distinct(new[] { vendorCollection, mouseCollection })[0].DevicePath == vendorCollection.DevicePath,
            "the first collection seen is the representative");
        Check(DeviceIdentity.Distinct(new[] { vendorCollection, mouseCollection })[0].DisplayName == vendorCollection.ProductName,
            "a single keyboard keeps its clean product name");

        var secondUnit = vendorCollection with
        {
            SerialNumber = "AAAAAAAAAA",
            DevicePath = @"\\?\hid#vid_2dc8&pid_202e&mi_02#9&11223344&0&0000",
        };
        IReadOnlyList<DeviceInfo> twoUnits = DeviceIdentity.Distinct(new[] { vendorCollection, secondUnit });
        Check(twoUnits.Count == 2, "two units with different serial numbers are two rows");
        Check(twoUnits[0].DisplayName == "8BitDo Retro 87 Keyboard X · A497"
              && twoUnits[1].DisplayName == "8BitDo Retro 87 Keyboard X · AAAA",
            "two units of the same model are told apart by the serial tail");

        var anonymousA = vendorCollection with { SerialNumber = null, DevicePath = "a" };
        var anonymousB = mouseCollection with { SerialNumber = null, DevicePath = "b" };
        Check(DeviceIdentity.Distinct(new[] { anonymousA, anonymousB }).Count == 1,
            "without a serial the product name is the fallback identity (identical units are indistinguishable)");
        Check(DeviceIdentity.Distinct(new[] { anonymousA, anonymousA with { ProductName = "8BitDo Retro 68" } }).Count == 2,
            "different models stay separate even without serials");

        // The automation peer of a picker row falls back to the item's text, so a record that does
        // not override ToString makes the screen reader read "{ Id = 4, Kind = Keyboard }".
        Check(new DeviceInfo(KnownDevices.EightBitDoVid, KnownDevices.Retro87Usb, "Retro 87", "SN", "p").ToString() == "Retro 87",
            "a device announces its product name, not the record");
        Check(new MappingTarget(MappingIds.A_MAP, "A", MappingTargetKind.Keyboard).ToString() == "A (teclado)",
            "a remap target announces name and category");
        Check(new MacroKindOption(MacroActionKind.KeyDown, "Pressionar").ToString() == "Pressionar",
            "a macro step kind announces its label");
        Check(new ProfileInfo("f.json", "Skipult", KnownDevices.EightBitDoVid, KnownDevices.Retro87XDongle,
                new DateTime(2026, 10, 18, 16, 50, 0, DateTimeKind.Local)).ToString() == "Skipult · 2026-10-18 16:50",
            "a profile announces its name and time");

        // ------------------------------------------------------------- super buttons
        output.WriteLine("super buttons");

        Check(SuperButtons.Count == 10 && SuperButtons.WireIndices.Length == SuperButtons.Count,
            "ten Super Buttons with ten wire indices");
        Check(KeyboardLayout.PositionNames[SuperButtons.PositionOf(0)] == "Super Button 1"
              && KeyboardLayout.PositionNames[SuperButtons.PositionOf(9)] == "Super Button 10",
            "positions 85..94 are named Super Button 1..10");
        Check(SuperButtons.WireIndexOf(0) == 9 && SuperButtons.WireIndexOf(1) == 10 && SuperButtons.WireIndexOf(9) == 115,
            "device order: record 9, 10 and 108..115");
        Check(SuperButtons.WireIndices.Distinct().Count() == SuperButtons.Count, "no wire index repeats");
        Check(SuperButtons.InterfaceOf(0) is null && SuperButtons.InterfaceOf(1) is null
              && SuperButtons.InterfaceOf(2) == "A" && SuperButtons.InterfaceOf(3) == "A"
              && SuperButtons.InterfaceOf(6) == "X" && SuperButtons.InterfaceOf(7) == "X"
              && SuperButtons.InterfaceOf(8) == "Y" && SuperButtons.InterfaceOf(9) == "Y",
            "the pairs 3..10 belong to interfaces A B X Y; 1 and 2 are built into the keyboard");

        // Read: a record the device already holds at index 113 (Volume Up, MKEY) must reach
        // Super Button 8 - the pair of the third interface, exactly as the official screen shows.
        var superRead = new XboxConfig();
        superRead.Mappings[113].KeyCode = SuperButtons.WireKeyCode;
        superRead.Mappings[113].Mapping = 0xCD;
        superRead.Mappings[113].Type = XboxMappingType.MKey;
        Check(XboxToKeyboardConfig.Convert(superRead).BoardKeys[SuperButtons.PositionOf(7)] == 0xCD,
            "a Super Button is read by record index (113 -> Super Button 8)");

        // Write: editing Super Button 7 must plan the record at its own index (112), keyCode F3,
        // even though ten records share that keyCode.
        var superBaseline = new XboxConfig { Raw = new byte[XboxWire.ConfigSize] };
        superBaseline.Mappings[112].KeyCode = SuperButtons.WireKeyCode;
        superBaseline.Mappings[112].Mapping = 0xCD;
        superBaseline.Mappings[112].Type = XboxMappingType.MKey;

        KeyboardConfig superEdited = XboxToKeyboardConfig.Convert(superBaseline);
        superEdited.BoardKeys[SuperButtons.PositionOf(6)] = 0xE2;   // Volume Down
        var superKinds = new Dictionary<int, MappingTargetKind>
        {
            [SuperButtons.PositionOf(6)] = MappingTargetKind.Media,
        };
        XboxWritePlan superPlan = XboxFromKeyboardConfig.Plan(superEdited, superBaseline, targetKinds: superKinds);
        XboxMappingChange? superChange = superPlan.Mappings.FirstOrDefault(m => m.Index == 112);
        Check(superChange is not null && superChange.KeyCode == SuperButtons.WireKeyCode && superChange.Record[4] == 0xE2,
            "editing a Super Button plans its own record index, not a keyCode lookup");
        Check(superPlan.Warnings.Count == 0, "the Super Button write needs no ambiguity warning");

        bool ascending = true;
        for (int i = 1; i < superPlan.Mappings.Count; i++)
        {
            if (superPlan.Mappings[i].Index <= superPlan.Mappings[i - 1].Index) ascending = false;
        }
        Check(ascending, "the write plan stays in ascending record order");

        // The block on the Mapeamento tab reads the ten buttons as five connections, and the amber
        // marker on a connection asks the pair whether either button was edited. That wiring is
        // view-model code, but it decides a state the screen shows, so it is checked here too.
        var superRowLeft = new MappingRowViewModel(SuperButtons.PositionOf(2), "Super Button 3", 0xCD, 0xCD)
        {
            SuperOrdinal = 2,
            WireName = "K1",
            InterfaceName = "A",
        };
        var superRowRight = new MappingRowViewModel(SuperButtons.PositionOf(3), "Super Button 4", 0xF3, 0xF3)
        {
            SuperOrdinal = 3,
            WireName = "K2",
            InterfaceName = "A",
        };
        var superConnection = new SuperConnectionViewModel("A", "of connection A", "K1 · K2",
            new[] { superRowLeft, superRowRight });

        Check(!superConnection.HasModified && superConnection.Buttons.Count == 2,
            "a connection holds the pair and starts unedited");
        superRowRight.MappingId = 0xE2;
        Check(superConnection.HasModified, "editing either button marks the connection as edited");

        // ------------------------------------------------------------- profile store
        output.WriteLine("profile store");

        string root = Path.Combine(Path.GetTempPath(), "retro87-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new ProfileStore(root);
            var device = new DeviceInfo(KnownDevices.EightBitDoVid, KnownDevices.Retro87Usb, "Retro 87", "SN123", "hid://test");

            var config = new KeyboardConfig { ProfileName = "Meu Perfil", SpecialFeature = 0x03, Voice = 1 };
            config.BoardKeys[0] = MappingIds.LEFT_CLICK;
            config.BoardKeys[43] = MappingIds.ESC_MAP;
            config.BoardKeys[94] = MappingIds.LCTRL_MAP;
            config.Macros[0].HostKey = MappingIds.A_MAP;
            config.Macros[0].Name = "M1";
            config.Macros[0].Cycles = 0xFF;
            config.Macros[0].IntervalMs = 250;
            config.Macros[0].Actions.Add(new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.A_MAP, Op = 0x83 });
            config.Macros[0].Actions.Add(new MacroAction { Kind = MacroActionKind.Time, DelayMs = 120 });
            config.Macros[0].Actions.Add(new MacroAction { Kind = MacroActionKind.KeyUp, Usage = MappingIds.A_MAP });

            Check(store.Save(device, config), "save writes the profile");
            Check(store.LastError is null, "save reports no error");

            IReadOnlyList<ProfileInfo> listed = store.List();
            Check(listed.Count == 1, "the saved profile is listed");
            Check(listed.Count == 1 && listed[0].ProfileName == "Meu Perfil", "the listing keeps the profile name");
            Check(listed.Count == 1 && listed[0].Pid == device.Pid, "the listing keeps the device pid");
            Check(store.List(KnownDevices.Retro87Wireless).Count == 0, "the listing filters by pid");

            KeyboardConfig? loaded = store.Load(listed[0].FileName);
            bool same = loaded is not null
                     && loaded.ProfileName == config.ProfileName
                     && loaded.SpecialFeature == config.SpecialFeature
                     && loaded.Voice == config.Voice;
            for (int i = 0; same && i < config.BoardKeys.Length; i++) same &= loaded!.BoardKeys[i] == config.BoardKeys[i];
            same &= loaded!.Macros[0].HostKey == config.Macros[0].HostKey
                 && loaded.Macros[0].Name == config.Macros[0].Name
                 && loaded.Macros[0].Cycles == config.Macros[0].Cycles
                 && loaded.Macros[0].IntervalMs == config.Macros[0].IntervalMs
                 && loaded.Macros[0].Actions.Count == 3
                 && loaded.Macros[1].Name.Length == 0;
            for (int i = 0; same && i < config.Macros[0].Actions.Count; i++)
            {
                byte[] expected = config.Macros[0].Actions[i].Encode();
                byte[] actual = loaded!.Macros[0].Actions[i].Encode();
                for (int b = 0; same && b < expected.Length; b++) same &= expected[b] == actual[b];
            }
            Check(same, "a saved profile reloads byte for byte (keys, special, voice, macros)");

            // The categories survive the file (0xE2 is Left Alt or NoVoice on the Xbox family).
            var typed = new KeyboardConfig { ProfileName = "Types" };
            typed.BoardKeys[10] = 0xE2;
            typed.BoardKinds[10] = MappingTargetKind.Media;
            typed.BoardKinds[11] = MappingTargetKind.Keyboard;
            Check(store.Save(device, typed), "a profile with categories saves");
            KeyboardConfig? typedBack = store.Load(ProfileStore.FileNameFor("Types"));
            Check(typedBack is { } && typedBack.BoardKinds[10] == MappingTargetKind.Media
                  && typedBack.BoardKinds[11] == MappingTargetKind.Keyboard && typedBack.BoardKinds[12] is null,
                "a profile keeps the category of each key");

            // Copy keeps the original, rename moves it, and neither overwrites another profile.
            string? copied = store.Duplicate(ProfileStore.FileNameFor("Types"), "Types 2", keepOriginal: true);
            Check(copied is not null && store.Exists("Types") && store.Exists("Types 2"), "copy keeps the original");
            Check(store.Load(copied ?? "x")?.ProfileName == "Types 2", "the copy carries its new name");
            Check(store.Duplicate(ProfileStore.FileNameFor("Types"), "Types 2", keepOriginal: true) is null
                  && store.LastError is not null, "copy refuses a name that already exists");
            string? renamed = store.Duplicate(copied ?? "x", "Other", keepOriginal: false);
            Check(renamed is not null && !store.Exists("Types 2") && store.Exists("Other"), "rename moves the file");
            store.Delete(ProfileStore.FileNameFor("Types"));
            store.Delete(ProfileStore.FileNameFor("Other"));

            Check(store.Delete(listed[0].FileName) && store.List().Count == 0, "delete removes the profile");
            Check(ProfileStore.FileNameFor("a/b:c") == "a_b_c.json", "unsafe characters are replaced");
            Check(ProfileStore.FileNameFor("   ") == "profile.json", "an empty name gets a default");
            Check(ProfileStore.FileNameFor("a<b>c\"d\\e|f?g*h") == "a_b_c_d_e_f_g_h.json",
                  "the rejected set is the same on every platform (Windows set, not the host set)");
            Check(ProfileStore.FileNameFor("perfil.. ") == "perfil.json", "trailing dots and spaces are dropped");
            Check(ProfileStore.FileNameFor("nul") == "nul_.json", "a Windows reserved name is made safe");
            Check(ProfileStore.FileNameFor("COM1") == "COM1_.json", "reserved names are matched case-insensitively");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }

        // ------------------------------------------------------------- display names
        output.WriteLine("display names");
        bool namesOk = true;
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            string description = MappingIdNames.Describe((byte)raw);
            if (string.IsNullOrEmpty(description)) namesOk = false;
        }
        Check(namesOk, "every id has a display name");
        Check(MappingIdNames.Describe(MappingIds.NULL_MAP) == "—", "unmapped is shown as an em dash");
        Check(MappingIdNames.Describe(MappingIds.LCTRL_MAP) == "LCtrl", "LeftCtrl uses the keyboard legend");

        // ------------------------------------------------------------- target catalog
        output.WriteLine("mapping target catalog");
        IReadOnlyList<MappingTarget> catalog = MappingTargetCatalog.All;

        Check(catalog.Count > 1 && catalog[0].Id == MappingIds.NULL_MAP, "the catalog starts with unmapped");
        Check(catalog.Select(t => t.Id).Distinct().Count() == catalog.Count, "catalog ids are unique");
        Check(catalog.All(t => !string.IsNullOrEmpty(t.Name)), "every target has a display name");

        bool everyPayloadIdOffered = true;
        foreach (byte id in MappingPayloadCodec.TargetIds)
        {
            if (MappingTargetCatalog.Find(id).Id != id) everyPayloadIdOffered = false;
        }
        Check(everyPayloadIdOffered, "every id with a payload is offered as a target");

        bool kindsMatch = true;
        foreach (MappingTarget t in catalog)
        {
            if (t.Id == MappingIds.NULL_MAP) continue;
            if (t.Kind != MappingPayloadCodec.KindOf(t.Id)) kindsMatch = false;
        }
        Check(kindsMatch, "each target carries the category its payload is dispatched with");

        bool defaultsSelectable = true;
        for (int i = 0; i < KeyboardLayout.PositionCount; i++)
        {
            byte id = KeyboardLayout.DefaultMappingId[i];
            if (MappingTargetCatalog.Find(id).Id != id) defaultsSelectable = false;
        }
        Check(defaultsSelectable, "every default target of the board is selectable");

        static int Rank(MappingTargetKind kind) => kind switch
        {
            MappingTargetKind.None => 0,
            MappingTargetKind.Keyboard => 1,
            MappingTargetKind.Shortcut => 2,
            MappingTargetKind.Mouse => 3,
            _ => 4,
        };
        bool grouped = true;
        for (int i = 1; i < catalog.Count; i++)
        {
            if (Rank(catalog[i - 1].Kind) > Rank(catalog[i].Kind)) grouped = false;
        }
        Check(grouped, "the catalog is ordered by category (unmapped, keyboard, shortcut, mouse)");

        // ------------------------------------------------------------- editor view-models
        output.WriteLine("editor view-models");

        var row = new MappingRowViewModel(0, "A", MappingIds.A_MAP, MappingIds.A_MAP);
        Check(!row.IsModified && row.SelectedTarget?.Id == MappingIds.A_MAP, "a default row starts unmodified");
        row.SelectedTarget = MappingTargetCatalog.Find(MappingIds.LEFT_CLICK);
        Check(row.MappingId == MappingIds.LEFT_CLICK && row.IsModified,
            "choosing a target updates the id and marks the row modified");
        row.RestoreDefault();
        Check(row.MappingId == MappingIds.A_MAP && !row.IsModified, "restore-default clears the modification");

        var slot = new MacroSlotViewModel(0);
        var source = new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.A_MAP, Op = 0x83 };
        var step = new MacroStepViewModel(slot, source);
        Check(step.Kind == MacroActionKind.KeyDown && step.IsKeyStep && !step.IsDelayStep,
            "a key-down step starts in the key mode");
        byte[] stepBefore = source.Encode();
        byte[] stepAfter = step.ToAction().Encode();
        Check(stepBefore.Length == stepAfter.Length && stepBefore.SequenceEqual(stepAfter),
            "an untouched step keeps the op byte read from the device");

        step.SelectedTarget = MappingTargetCatalog.Find(MappingIds.LCTRL_MAP);
        Check(step.Usage == MappingIds.LCTRL_MAP, "choosing a key updates the step usage");
        Check(step.ToAction().Encode()[0] == 0x83, "a modifier key uses the modifier op (0x83)");

        step.SelectedKind = MacroStepViewModel.KindOptions.First(o => o.Kind == MacroActionKind.Time);
        Check(step.IsDelayStep && !step.IsKeyStep, "changing the kind flips the editor to the delay field");
        Check(step.ToAction().Kind == MacroActionKind.Time, "the delay kind survives the snapshot");

        slot.AddKeyDownCommand.Execute(null);
        slot.AddKeyUpCommand.Execute(null);
        slot.AddDelayCommand.Execute(null);
        Check(slot.Steps.Count == 3 && slot.StepCount == 3, "the slot adds the three step kinds");
        Check(slot.Steps[0].ToAction().Encode()[0] == 0x81, "a fresh key-down encodes to the plain op (0x81)");
        Check(slot.Steps[2].ToAction().Kind == MacroActionKind.Time && slot.Steps[2].DelayMs == 50,
            "a fresh delay defaults to 50 ms");

        var moved = slot.Steps[2];
        slot.MoveStep(moved, -1);
        Check(slot.Steps[1] == moved, "move-up reorders the steps");
        slot.RemoveStep(moved);
        Check(slot.Steps.Count == 2 && !slot.Steps.Contains(moved), "remove drops the step");

        // ------------------------------------------------------------- protocol dry run
        output.WriteLine("protocol dry run (fake keyboard)");
        ProtocolDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox protocol dry run
        output.WriteLine("xbox protocol dry run (fake keyboard)");
        XboxProtocolDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox write dry run
        output.WriteLine("xbox write dry run (fake keyboard)");
        XboxWriteDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox macro write test
        output.WriteLine("xbox macro test dry run (fake keyboard)");
        XboxMacroTestDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox config write test
        output.WriteLine("xbox config test dry run (fake keyboard)");
        XboxConfigTestDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox media/mouse write test
        output.WriteLine("xbox media/mouse test dry run (fake keyboard)");
        XboxMediaTestDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox wake / error replies
        output.WriteLine("xbox wake and error-reply dry run (fake keyboard)");
        XboxWakeDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox ui config (volume/quick setup)
        output.WriteLine("xbox ui config test dry run (fake keyboard)");
        XboxUiConfigTestDryRun(output, Check, Info);

        // ------------------------------------------------------------- xbox link-loss sleep/wake
        output.WriteLine("xbox link-loss sleep/wake dry run (fake keyboard)");
        XboxLinkLossDryRun(output, Check, Info);

        output.WriteLine(ok ? "SELFTEST PASSED" : "SELFTEST FAILED");
        return ok;
    }

    /// <summary>
    /// Drives <see cref="JpProtocol"/> against <see cref="FakeKeyboardDevice"/> end to end:
    /// reads a device that was configured "elsewhere", writes that configuration to a fresh
    /// device and reads it back, and walks the failure paths the hardware session would
    /// otherwise be the first to hit.
    /// </summary>
    /// <remarks>
    /// This is self-consistency against the frame model in <see cref="FakeKeyboardDevice"/>,
    /// not hardware evidence: it catches a wrong reply offset or a broken chunk boundary in the
    /// port, but the real keyboard still has to confirm the model itself (reply offsets, the
    /// acknowledgement timing and the <c>writeMacroJP</c> deviation).
    /// </remarks>
    private static void ProtocolDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        static bool SameSlot(MacroSlot? left, MacroSlot? right)
        {
            if (left is null || right is null) return false;
            if (left.HostKey != right.HostKey || left.Name != right.Name) return false;
            if (left.Cycles != right.Cycles || left.IntervalMs != right.IntervalMs) return false;
            if (left.Actions.Count != right.Actions.Count) return false;

            for (int i = 0; i < left.Actions.Count; i++)
            {
                byte[] a = left.Actions[i].Encode();
                byte[] b = right.Actions[i].Encode();
                if (!a.SequenceEqual(b)) return false;
            }
            return true;
        }

        static MacroSlot? SlotFor(KeyboardConfig config, byte hostKey)
            => config.Macros.FirstOrDefault(m => m.HostKey == hostKey);

        static bool SameBoardKeys(KeyboardConfig left, KeyboardConfig right)
        {
            for (int i = 0; i < left.BoardKeys.Length; i++)
            {
                if (left.BoardKeys[i] != right.BoardKeys[i]) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- the reference configuration
        // Positions 0..2 are A, B and C: one keyboard remap, one mouse action (five value bytes)
        // and one shortcut (payload shared with a keyboard id, told apart by the transport byte).
        const byte mousePosition = 1;
        const byte shortcutPosition = 2;

        var expected = new KeyboardConfig { ProfileName = "Dry Run", SpecialFeature = 0x03, Voice = 1 };
        Array.Copy(KeyboardLayout.DefaultMappingId, expected.BoardKeys, KeyboardLayout.PositionCount);
        expected.BoardKeys[0] = MappingIds.LALT_MAP;
        expected.BoardKeys[mousePosition] = MappingIds.LEFT_CLICK;
        expected.BoardKeys[shortcutPosition] = MappingIds.NoVoice_MAP;

        var slotOne = new MacroSlot
        {
            HostKey = MappingIds.ESC_MAP,
            Name = "M1",
            Cycles = JpWire.MacroCyclesInfinite,
            IntervalMs = 250,
        };
        slotOne.Actions.Add(new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.A_MAP, Op = 0x83 });
        slotOne.Actions.Add(new MacroAction { Kind = MacroActionKind.Time, DelayMs = 120 });
        slotOne.Actions.Add(new MacroAction { Kind = MacroActionKind.KeyUp, Usage = MappingIds.A_MAP });

        var slotTwo = new MacroSlot
        {
            HostKey = MappingIds.F1_MAP,
            Name = "M2",
            Cycles = 2,
            IntervalMs = 0x1234,
        };
        for (int i = 0; i < 8; i++)
        {
            slotTwo.Actions.Add(new MacroAction
            {
                Kind = i % 2 == 0 ? MacroActionKind.KeyDown : MacroActionKind.KeyUp,
                Usage = (byte)(MappingIds.A_MAP + i),
            });
        }

        // A keyboard configured elsewhere: the device state is set directly, so the read path is
        // exercised without the write path having produced it.
        var device = new FakeKeyboardDevice();
        device.SetProfileName(expected.ProfileName);
        device.Special = expected.SpecialFeature;
        device.Voice = expected.Voice;
        for (int position = 0; position < expected.BoardKeys.Length; position++)
        {
            if (expected.BoardKeys[position] != KeyboardLayout.DefaultMappingId[position])
                device.StoreMapping(KeyboardLayout.PositionKeyId[position], expected.BoardKeys[position]);
        }
        device.StoreMacro(slotOne.HostKey, slotOne.Name, slotOne.Cycles, slotOne.IntervalMs, slotOne.Actions);
        device.StoreMacro(slotTwo.HostKey, slotTwo.Name, slotTwo.Cycles, slotTwo.IntervalMs, slotTwo.Actions);

        // ---------------------------------------------------------------- open and read
        var protocol = new JpProtocol(device);
        check(protocol.Open(), "open completes the report switch handshake");
        check(device.SelectedProfile == 0xFF, "open selects the default profile (SwitchReport 0xFF)");

        KeyboardConfig read = protocol.ReadConfig();
        check(read.ProfileName == expected.ProfileName, "read recovers the profile name");
        check(read.SpecialFeature == expected.SpecialFeature && read.Voice == expected.Voice,
            "read recovers the special feature and the voice level");
        check(SameBoardKeys(read, expected), "read recovers all 95 board keys");
        check(SameSlot(SlotFor(read, slotOne.HostKey), slotOne),
            "read recovers a three-step macro (modifier op 0x83, delay step, interval)");
        check(SameSlot(SlotFor(read, slotTwo.HostKey), slotTwo),
            "read recovers an eight-step macro (multi-chunk body, interval 0x1234)");
        check(read.Macros.Count(m => !m.IsEmpty) == 2, "read stops after the macros the device reports");

        // ---------------------------------------------------------------- write it back
        var target = new FakeKeyboardDevice();
        var writer = new JpProtocol(target);
        check(writer.Open(), "the second device opens");
        check(writer.WriteConfig(read), "write config to a fresh device succeeds");

        check(target.MappedKeyIds.Count == 3, "only the keys that differ from the default are written");
        check(target.Special == expected.SpecialFeature && target.Voice == expected.Voice,
            "the special feature and the voice level reach the device");
        check(target.MacroKeyIds.Count == 2, "both named macro slots reach the device");

        bool recordsMatch = true;
        foreach (MacroSlot source in new[] { slotOne, slotTwo })
        {
            byte[] sent = target.MacroRecordOf(source.HostKey);
            byte[] wanted = device.MacroRecordOf(source.HostKey);
            if (!sent.SequenceEqual(wanted)) recordsMatch = false;
        }
        check(recordsMatch, "the macro records the device holds are byte for byte the ones it reported");

        KeyboardConfig again = writer.ReadConfig();
        bool stable = SameBoardKeys(again, read)
                   && again.SpecialFeature == read.SpecialFeature
                   && again.Voice == read.Voice;
        for (int slot = 0; slot < read.Macros.Length; slot++)
        {
            if (!SameSlot(again.Macros[slot], read.Macros[slot])) stable = false;
        }
        check(stable, "read -> write -> read returns the same configuration");
        check(target.ProfileName.Length == 0,
            "write leaves the profile name alone (the original never binds writeName)");

        // ---------------------------------------------------------------- every target survives
        var catalogDevice = new FakeKeyboardDevice();
        var catalogProtocol = new JpProtocol(catalogDevice);
        catalogProtocol.Open();

        byte probeKey = KeyboardLayout.PositionKeyId[0];
        var lostTargets = new List<string>();
        foreach (byte targetId in MappingPayloadCodec.TargetIds)
        {
            catalogProtocol.SetMappingEntry(probeKey, targetId);
            if (!catalogProtocol.GetMappingValue(probeKey, out byte[] value))
            {
                lostTargets.Add($"0x{targetId:X2} (no reply)");
                continue;
            }

            byte back = MappingPayloadCodec.DecodeId(
                value[1], new ReadOnlySpan<byte>(value, 2, MappingPayloadCodec.PayloadLength));
            if (back != targetId)
            {
                lostTargets.Add($"0x{targetId:X2} -> 0x{back:X2} " +
                                $"(transport {value[1]}, value {Convert.ToHexString(value[2..7])})");
            }
        }
        foreach (string loss in lostTargets) info(loss);
        check(lostTargets.Count == 0, "every remap target survives a write and a read back");

        // ---------------------------------------------------------------- the writeMacroJP deviation
        // Six steps is the boundary the shipped DLL mishandles: its first chunk copies 20 body
        // bytes while the continuation offset is still 21, so the byte at record offset 24 — the
        // delay's high byte — is the frame's zero padding. This port writes 21 instead.
        byte[] sixStepBody = JpProtocol.BuildMacroBody(slotTwo.Actions.Take(6).ToList(), (ushort)0x1234);
        check(sixStepBody.Length == 22, "a six-step body is 22 bytes (18 step bytes + the 4-byte delay entry)");

        var macroDevice = new FakeKeyboardDevice();
        var macroProtocol = new JpProtocol(macroDevice);
        macroProtocol.Open();
        check(macroProtocol.WriteMacro(slotTwo.HostKey, sixStepBody, 0x02), "the 22-byte body is written");

        byte[] portRecord = macroDevice.MacroRecordOf(slotTwo.HostKey);
        bool portIntact = true;
        for (int i = 0; i < sixStepBody.Length; i++)
        {
            if (portRecord[JpWire.MacroHeaderLength + i] != sixStepBody[i]) portIntact = false;
        }
        check(portIntact, "the 21-byte first chunk keeps the whole 22-byte body contiguous");

        byte[] dllRecord = (byte[])portRecord.Clone();
        dllRecord[JpWire.MacroHeaderLength + 20] = 0;     // the byte the DLL's 20-byte copy drops
        var portSlot = new MacroSlot();
        var dllSlot = new MacroSlot();
        JpProtocol.DecodeMacroSlot(portSlot, slotTwo.HostKey, new byte[DeviceNameCodec.NameBytes], portRecord);
        JpProtocol.DecodeMacroSlot(dllSlot, slotTwo.HostKey, new byte[DeviceNameCodec.NameBytes], dllRecord);
        check(portSlot.IntervalMs == 0x1234, "the port's record decodes back to interval 0x1234");
        info($"the shipped DLL's 20-byte copy would decode it as 0x{dllSlot.IntervalMs:X4}");
        check(dllSlot.IntervalMs == 0x0034, "the DLL's 20-byte copy would drop the delay's high byte");

        // ---------------------------------------------------------------- force writes every position
        var forcedDevice = new FakeKeyboardDevice();
        var forcedWriter = new JpProtocol(forcedDevice);
        forcedWriter.Open();
        check(forcedWriter.WriteConfig(read, force: true), "force writes the whole configuration");
        check(forcedDevice.MappedKeyIds.Count == 95, "force writes all 95 board positions");
        check(SameBoardKeys(forcedWriter.ReadConfig(), read), "a forced write reads back unchanged");

        // A key the user resets is not sent unless force is used: parity with the original, and
        // the reason the UI has "restore all" plus a write.
        var parityDevice = new FakeKeyboardDevice();
        parityDevice.StoreMapping(KeyboardLayout.PositionKeyId[0], MappingIds.LALT_MAP);
        var parityProtocol = new JpProtocol(parityDevice);
        parityProtocol.Open();

        KeyboardConfig reset = parityProtocol.ReadConfig();
        check(reset.BoardKeys[0] == MappingIds.LALT_MAP, "the device reports the key that was remapped elsewhere");
        reset.BoardKeys[0] = KeyboardLayout.DefaultMappingId[0];

        check(parityProtocol.WriteConfig(reset), "writing a reset configuration succeeds");
        check(parityProtocol.ReadConfig().BoardKeys[0] == MappingIds.LALT_MAP,
            "without force a reset key is not sent (documented parity with the original)");
        check(parityProtocol.WriteConfig(reset, force: true), "the forced write succeeds");
        check(parityProtocol.ReadConfig().BoardKeys[0] == KeyboardLayout.DefaultMappingId[0],
            "with force the reset key is sent");

        // ---------------------------------------------------------------- failure paths
        var lostDevice = new FakeKeyboardDevice();
        lostDevice.SetProfileName("Lost Frames");
        var lostProtocol = new JpProtocol(lostDevice);
        lostProtocol.Open();
        lostDevice.LoseNextRequests = 2;
        check(lostProtocol.ReadConfig().ProfileName == "Lost Frames",
            "a lost frame is recovered by re-sending the request");

        var staleDevice = new FakeKeyboardDevice();
        staleDevice.SetProfileName("Stale Reply");
        var staleProtocol = new JpProtocol(staleDevice);
        staleProtocol.Open();
        staleDevice.SendStaleReplyFirst = true;
        check(staleProtocol.ReadConfig().ProfileName == "Stale Reply",
            "a reply for another command is skipped");

        var noisyDevice = new FakeKeyboardDevice { InputReportId = 0x00, ReportLength = 64 };
        noisyDevice.SetProfileName("Wide Reports");
        var noisyProtocol = new JpProtocol(noisyDevice);
        noisyProtocol.Open();
        check(noisyProtocol.ReadConfig().ProfileName == "Wide Reports",
            "reads do not depend on the input report id or on the report length (64-byte reports)");

        var silentDevice = new FakeKeyboardDevice { NeverAcknowledgeWrites = true };
        var silentProtocol = new JpProtocol(silentDevice);
        check(silentProtocol.Open(), "open still succeeds when the switch is not acknowledged");
        check(!silentProtocol.SwitchReport(0xFF), "an unacknowledged write is reported as a failure");

        var refused = new KeyboardConfig();
        refused.BoardKeys[0] = MappingIds.LEFT_CLICK;
        check(!silentProtocol.WriteConfig(refused), "a refused write makes WriteConfig fail");

        var nakDevice = new FakeKeyboardDevice();
        var nakProtocol = new JpProtocol(nakDevice);
        nakProtocol.Open();
        nakDevice.NakNextWrites = 1;
        check(nakProtocol.WriteMacroName(MappingIds.ESC_MAP, DeviceNameCodec.Encode("M1")),
            "a bad reply makes the macro name writer re-send and succeed");
        check(nakDevice.ReceivedPayloads.Count(p => p[0] == JpWire.Cmd.WriteMacroName) == 2,
            "the macro name frame was indeed sent twice");

        // ---------------------------------------------------------------- profile name writing
        var nameDevice = new FakeKeyboardDevice();
        var nameProtocol = new JpProtocol(nameDevice);
        nameProtocol.Open();

        string longName = new string('N', 30);
        check(nameProtocol.WriteProfileName(longName), "writeName sends a name longer than one chunk");
        check(nameProtocol.GetProfileName(out byte[] nameField) && DeviceNameCodec.Decode(nameField) == longName,
            "the written profile name reads back unchanged (three chunks)");

        // ---------------------------------------------------------------- invariants worth locking in
        // The keyboard and shortcut writers copy four value bytes and the mouse writer five, so a
        // keyboard/shortcut target whose fifth byte mattered would be truncated on the wire. Only
        // the two horizontal roll codes use that byte, and both are mouse actions.
        bool fourBytesEnough = true;
        foreach (byte targetId in MappingPayloadCodec.TargetIds)
        {
            KeyboardTransport transport = MappingTables.TransportFor(targetId);
            if (transport == KeyboardTransport.Mouse) continue;
            if (MappingPayloadCodec.EncodePayload(targetId)[4] != 0) fourBytesEnough = false;
        }
        check(fourBytesEnough, "no keyboard or shortcut target needs the fifth value byte (four bytes are lossless)");

        // A slot the device reports without a name is read but never written back, because
        // The macro writer skips unnamed slots. Worth knowing before a "restore" runs on hardware.
        var unnamedDevice = new FakeKeyboardDevice();
        unnamedDevice.StoreMacro(MappingIds.ESC_MAP, string.Empty, 1, 100, new[]
        {
            new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.A_MAP },
        });
        var unnamedProtocol = new JpProtocol(unnamedDevice);
        unnamedProtocol.Open();

        KeyboardConfig withUnnamed = unnamedProtocol.ReadConfig();
        check(withUnnamed.Macros.Any(m => m.Name.Length == 0 && m.Actions.Count > 0),
            "read reports a macro slot that has steps but no name");

        var unnamedTarget = new FakeKeyboardDevice();
        var unnamedWriter = new JpProtocol(unnamedTarget);
        unnamedWriter.Open();
        check(unnamedWriter.WriteConfig(withUnnamed), "writing a configuration with an unnamed macro succeeds");
        check(unnamedTarget.MacroKeyIds.Count == 0,
            "an unnamed macro slot is not written back (documented parity with the macro writer)");

        // ---------------------------------------------------------------- frame hygiene
        check(device.Unexpected.Count == 0, "the port sent no frame the device model did not understand");
        check(!device.FeatureReportsUsed, "the keyboard protocol never used a feature report");
        check(macroDevice.ReceivedPayloads.All(p => p.Length == JpWire.PayloadLength),
            "every frame the protocol sent was a full 32-byte payload");
    }

    /// <summary>
    /// Drives <see cref="XboxJpProtocol"/> against <see cref="FakeXboxDevice"/>: the frame
    /// layout, the reply header (10 bytes — the bug that unblocked the mapping layout), the
    /// chunked 1532-byte read and the version/battery command. Read-only; writing is not part
    /// of this layer by project rule.
    /// </summary>
    private static void XboxProtocolDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        // ---------------------------------------------------------------- request framing
        byte[] request = XboxJpProtocol.BuildRequest(XboxWire.Cmd.Read, 0, 0x12345678, ReadOnlySpan<byte>.Empty, 0x35);
        check(request.Length == XboxWire.FrameLength, "xbox request frame is 64 bytes");
        check(request[0] == 0x81 && request[1] == 0x04, "xbox request starts 81 04");
        check(request[2] == XboxWire.Cmd.Read && request[3] == 0, "xbox request carries cmd/subtype");
        check(request[4] == 0x35, "xbox request carries the requested length");
        check(request[5] == 0, "empty read request has checksum 0");
        check(request[6] == 0x78 && request[7] == 0x56 && request[8] == 0x34 && request[9] == 0x12,
            "xbox request carries a little-endian offset");

        byte[] withData = XboxJpProtocol.BuildRequest(XboxWire.Cmd.Write, 0, 0, new byte[] { 0x01, 0x02, 0x03 });
        check(withData[5] == 0x06, "xbox checksum is the sum of the data bytes");

        // the reply header is 10 bytes; a reply to another command is not accepted
        var reply = new byte[XboxWire.FrameLength];
        reply[0] = 0x02; reply[1] = 0x04; reply[2] = 0x03; reply[3] = XboxWire.Cmd.Read;
        check(XboxJpProtocol.IsReplyFor(XboxWire.Cmd.Read, reply), "xbox reply is recognised for its cmd");
        check(!XboxJpProtocol.IsReplyFor(XboxWire.Cmd.Version, reply), "xbox reply to another cmd is rejected");
        check(!XboxJpProtocol.IsReplyFor(XboxWire.Cmd.Read, reply.AsSpan(0, 5)), "a 5-byte header is not a valid reply");

        // ---------------------------------------------------------------- read a configured device
        var device = new FakeXboxDevice();
        var protocol = new XboxJpProtocol(device);
        check(protocol.Open(), "xbox protocol opens");

        check(protocol.ReadVersion(), "xbox version command answers");
        check(Math.Abs(protocol.FirmwareVersion - 1.07f) < 0.001f, "xbox firmware parses as 1.07");
        check(protocol.BatteryPercent == 100, "xbox battery parses as 100%");

        XboxConfig? config = protocol.ReadXboxConfig();
        check(config is not null, "xbox config reads");
        check(config!.Raw.Length == XboxWire.ConfigSize, "xbox config is 1532 bytes");
        check(config.Flag == 0x20200902u, "xbox flag is decoded past the 10-byte reply header");
        check(config.ProfileName == "Skipult", "xbox profile name is UTF-16 big-endian");
        check(config.Enable == 1, "xbox enable flag");
        check(config.LedsSleep == 300, "xbox leds_sleep is 300");
        check(config.VolumeLevel == 1 && config.CurrentColorTheme == 1, "xbox volume/color fields");

        // mapping layout: keyCode@0, mapping@4, mappingType@8
        check(config.Mappings[0].KeyCode == 0xE0 && config.Mappings[0].Mapping == 0xE0,
            "xbox mapping slot 0 is LCtrl -> LCtrl");
        check(config.Mappings[12].KeyCode == 0x04 && config.Mappings[12].Mapping == 0x04,
            "xbox mapping slot 12 is A -> A");
        check(XboxMappingIds.Describe(0xE0) == "Left Ctrl" && XboxMappingIds.Describe(0) == "—",
            "xbox mapping id names come from the ported Xbox table");
        check(XboxMappingIds.Describe(0x04) == "A" && XboxMappingIds.Describe(0xCD) == "Play",
            "xbox names cover the keyboard and the media ids");

        // ---------------------------------------------------------------- projection to the UI
        check(XboxToKeyboardConfig.XboxKeyCodeForJpId(MappingIds.LCTRL_MAP) == 0xE0,
            "xbox projection translates the JP LeftCtrl id to the Xbox usage");
        check(MappingIdSpace.Xbox.Find(0xE2).Name == "Left Alt" && MappingIdSpace.Xbox.Find(0x66).Name != "Left Alt",
            "xbox projection keeps the modifier ids as the device stores them (0xE2, not the JP 102)");
        check(XboxToKeyboardConfig.DefaultBoardKeys[KeyboardLayout.PositionOfKeyId(MappingIds.LALT_MAP)] == 0xE2,
            "xbox projection: the factory target of Left Alt is its own usage, 0xE2");

        KeyboardConfig projected = XboxToKeyboardConfig.Convert(config);
        check(projected.ProfileName == "Skipult", "xbox -> UI keeps the profile name");
        check(projected.BoardKeys[0] == MappingIds.A_MAP, "xbox -> UI maps A to board position 0");
        check(projected.BoardKeys[78] == 0xE0, "xbox -> UI maps LeftCtrl to position 78, keeping the Xbox id 0xE0");
        check(projected.BoardKeys[85] == MappingIds.NULL_MAP, "xbox -> UI leaves the extra keys unmapped");

        // the Xbox session can write (the write path mirrors the captured app)
        check(((IJpProtocol)protocol).CanWrite, "the xbox session reports CanWrite = true");
        check(protocol.DeviceVersion > 1.0f, "xbox DeviceVersion surfaces the firmware version");

        // ---------------------------------------------------------------- macros (cmd 0x07)
        var macroDevice = new FakeXboxDevice();
        macroDevice.SetMacro(slot: 2, mappingIndex: 40, hostKeyCode: 0x04, name: "Combo",
            interval: 250, repeat: 3,
            new XboxMacroStep { KeyState = 1, KeyCode = 0x04 },
            new XboxMacroStep { KeyState = 3, Timer = 100 },
            new XboxMacroStep { KeyState = 2, KeyCode = 0x1D });
        var macroProtocol = new XboxJpProtocol(macroDevice);
        macroProtocol.Open();
        XboxConfig? macroConfig = macroProtocol.ReadXboxConfig();
        check(macroConfig?.Macros[2] is not null, "xbox reads the macro slot a MACRO mapping points at");
        check(macroConfig!.Macros[2]!.Name == "Combo", "xbox macro name is UTF-16 big-endian");
        check(macroConfig.Macros[2]!.MaxStep == 3 && macroConfig.Macros[2]!.Steps.Count == 3,
            "xbox macro step count comes from macroMaxStep");
        check(macroConfig.Macros[2]!.Interval == 250 && macroConfig.Macros[2]!.Repeat == 3,
            "xbox macro interval/repeat are decoded");
        check(macroConfig.Macros[2]!.Steps[1].IsDelay && macroConfig.Macros[2]!.Steps[1].Timer == 100,
            "xbox macro delay step is decoded");
        check(macroConfig.Macros[0] is null, "an unreferenced macro slot is not read");

        KeyboardConfig projectedMacro = XboxToKeyboardConfig.Convert(macroConfig);
        check(projectedMacro.Macros[2].Name == "Combo" && projectedMacro.Macros[2].Actions.Count == 3,
            "xbox macro projects into the UI model");
        check(projectedMacro.Macros[2].Actions[0].Kind == MacroActionKind.KeyDown &&
              projectedMacro.Macros[2].Actions[1].Kind == MacroActionKind.Time &&
              projectedMacro.Macros[2].Actions[2].Kind == MacroActionKind.KeyUp,
            "xbox macro key states project as down/delay/up");
        check(projectedMacro.Macros[2].Actions[1].DelayMs == 100 &&
              projectedMacro.Macros[2].Actions[2].Usage == 0x1D,
            "xbox macro delay/usage project into the UI model");
        check(macroDevice.Unexpected.Count == 0, "xbox macro read sent no unknown request");

        // ---------------------------------------------------------------- replies are matched to their request
        static byte[] Reply(byte cmd, uint offset, byte checksum)
        {
            var frame = new byte[XboxWire.FrameLength];
            frame[0] = XboxWire.ReportIdIn;
            frame[1] = XboxWire.Marker;
            frame[2] = XboxWire.ReplyConst;
            frame[3] = cmd;
            frame[XboxWire.ReplyChecksum] = checksum;
            frame[XboxWire.ReplyOffset] = (byte)(offset & 0xFF);
            frame[XboxWire.ReplyOffset + 1] = (byte)((offset >> 8) & 0xFF);
            frame[XboxWire.ReplyOffset + 2] = (byte)((offset >> 16) & 0xFF);
            frame[XboxWire.ReplyOffset + 3] = (byte)((offset >> 24) & 0xFF);
            return frame;
        }
        check(XboxJpProtocol.IsReplyTo(XboxWire.Cmd.Read, 53, ReadOnlySpan<byte>.Empty,
                Reply(XboxWire.Cmd.Read, 53, 0)),
            "xbox reply is matched by command and echoed offset");
        check(!XboxJpProtocol.IsReplyTo(XboxWire.Cmd.Read, 106, ReadOnlySpan<byte>.Empty,
                Reply(XboxWire.Cmd.Read, 53, 0)),
            "a late reply for another offset is rejected (this caused the C1 desync)");
        check(!XboxJpProtocol.IsReplyTo(XboxWire.Cmd.Write, 0, new byte[] { 1, 2, 3 },
                Reply(XboxWire.Cmd.Write, 0, 0x05)),
            "a write reply with a different checksum is rejected");
        check(XboxJpProtocol.IsReplyTo(XboxWire.Cmd.Write, 0, new byte[] { 1, 2, 3 },
                Reply(XboxWire.Cmd.Write, 0, 0x06)),
            "a write reply with the echoed checksum is accepted");
        check(XboxJpProtocol.Checksum(new byte[] { 0x47, 0x48, 0x01 }) == 0x90,
            "the request checksum is the sum of the data (0x47+0x48+0x01)");

        var staleDevice = new FakeXboxDevice();
        var staleProtocol = new XboxJpProtocol(staleDevice);
        staleProtocol.Open();
        staleDevice.QueueMisleadingReply(XboxWire.Cmd.Read, wrongOffset: 0, requestChecksum: 0, payload: new byte[53]);
        check(staleProtocol.ReadRegion(53, 53, out byte[] chunk53) && chunk53.Length == 53,
            "xbox read survives a stale reply sitting in the input buffer");
        check(chunk53.SequenceEqual(staleDevice.Config.Skip(53).Take(53)),
            "and still lands on the right chunk (re-requested, not shifted)");

        // ---------------------------------------------------------------- reassembly + retries
        check(device.Requests.Count(r => r.Cmd == XboxWire.Cmd.Read) >= 29,
            "xbox config read used one request per 53-byte chunk");
        check(device.Unexpected.Count == 0, "xbox device saw no unknown request");

        var flaky = new FakeXboxDevice { LoseNextRequests = 3 };
        var flakyProtocol = new XboxJpProtocol(flaky);
        flakyProtocol.Open();
        check(flakyProtocol.ReadRegion(0, 64, out byte[] first) && first.Length == 64,
            "xbox read recovers from lost request frames");
        check(first[0] == 0x02 && first[5] == 0x53, "xbox reassembled bytes start at the config head");

        // ---------------------------------------------------------------- writes are gated in the UI
        info("the old read-only guarantee now lives in the view-model switch (the confirmation in the save dialog)");
        check(device.Requests.All(r => r.Cmd is XboxWire.Cmd.Read or XboxWire.Cmd.Version),
            "xbox read session only ever read (never wrote)");
    }

    /// <summary>
    /// Drives the Xbox <b>write</b> path against <see cref="FakeXboxDevice"/>: the plan
    /// (diff, records, macro slots), the frames it produces, and the read-back of what the device
    /// now holds. Self-consistency against the frame model — the keyboard is the authority for
    /// whether the device accepts these writes at all.
    /// </summary>
    private static void XboxWriteDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        // ---------------------------------------------------------------- type rule
        check(XboxFromKeyboardConfig.TryTypeForTarget(0x05, out var letterType) &&
              letterType == XboxMappingType.KeyCode,
            "xbox write: a letter target is KEYCODE (getMode default)");
        check(XboxFromKeyboardConfig.TryTypeForTarget(0xE1, out var modifierType) &&
              modifierType == XboxMappingType.KeyCode,
            "xbox write: a modifier target is KEYCODE");
        check(XboxFromKeyboardConfig.TryTypeForTarget(0, out var noneType) &&
              noneType == XboxMappingType.None,
            "xbox write: an empty target is NONE");
        check(!XboxFromKeyboardConfig.TryTypeForTarget(0xE2, out _),
            "xbox write: a shared id (0xE2 = Left Alt or NoVoice) is refused without the category");
        check(XboxFromKeyboardConfig.TryTypeForTarget(0xE2, MappingTargetKind.Media, out var muteType) &&
              muteType == XboxMappingType.MKey,
            "xbox write: the category from the picker disambiguates it (NoVoice -> MKEY)");
        check(XboxFromKeyboardConfig.TryTypeForTarget(0xE2, MappingTargetKind.Keyboard, out var altType) &&
              altType == XboxMappingType.KeyCode,
            "xbox write: the same id as a keyboard target is KEYCODE (Left Alt)");
        check(XboxFromKeyboardConfig.TryTypeForTarget(0xB6, out var lastType) &&
              lastType == XboxMappingType.MKey,
            "xbox write: a unique media id (Last) is MKEY, as the device showed");
        check(XboxFromKeyboardConfig.TryTypeForTarget(0xE9, MappingTargetKind.Mouse, out var midType) &&
              midType == XboxMappingType.Mouse,
            "xbox write: the middle mouse button resolves to MOUSE");

        // ---------------------------------------------------------------- the ported id table
        check(XboxIdTable.All.Count >= 130, $"xbox ids: the table is ported ({XboxIdTable.All.Count} entries)");
        check(XboxIdTable.TryGet(0xB5, out XboxIdEntry next) &&
              next.Name == "Next" && next.Type == XboxMappingType.MKey,
            "xbox ids: Next is 0xB5 as MKEY (exactly what the device showed)");
        check(XboxIdTable.TryGet(0xCD, out XboxIdEntry play) && play.Name == "Play" &&
              XboxIdTable.TryGet(0xE8, out XboxIdEntry mouse) && mouse.Type == XboxMappingType.Mouse,
            "xbox ids: play and the mouse buttons are ported");
        check(XboxIdTable.IsAmbiguous(0xE2) && XboxIdTable.IsAmbiguous(0x6F) && !XboxIdTable.IsAmbiguous(0xCD),
            "xbox ids: shared ids are flagged, unique ones are not");
        check(XboxIdTable.Describe(0xCD) == "Play" && XboxIdTable.Describe(0x01) == "id 0x01",
            "xbox ids: unnamed ids fall back to a self-describing hex");
        check(MappingIdSpace.Xbox.Describe(0xCD) == "Play" && MappingIdSpace.Jp.Describe(0xCD) != "Play",
            "xbox id space names the media ids the JP space does not know");
        check(MappingIdSpace.Xbox.Find(MappingIds.NULL_MAP).Kind == MappingTargetKind.None &&
              MappingIdSpace.Xbox.Find(XboxIdTable.UnmappedId).Name == "—",
            "xbox id space treats 0 and NONE_MAP (0xF3) as unmapped");
        check(MappingIdSpace.Xbox.Targets.Any(t => t.Id == 0xCD && t.Kind == MappingTargetKind.Media),
            "xbox id space offers the media targets to the picker");

        // ---------------------------------------------------------------- encoding helpers
        byte[] nameField = XboxFromKeyboardConfig.EncodeNameBigEndian("Ab", 32);
        check(nameField[0] == 0x00 && nameField[1] == 0x41 && nameField[2] == 0x00 && nameField[3] == 0x62,
            "xbox write: names are UTF-16 big-endian on the wire");

        byte[] record = XboxFromKeyboardConfig.BuildRecord(0xF3, 0x2000, XboxMappingType.Macro,
            new byte[] { 0xE9, 0x37, 0x03 });
        check(record.Length == 12 && record[0] == 0xF3 && record[1] == 0xE9 && record[3] == 0x03,
            "xbox write: record keeps the physical key and the unused bytes");
        check(record[4] == 0x00 && record[5] == 0x20 && record[6] == 0x00 && record[7] == 0x00,
            "xbox write: mapping is a little-endian u32 at +4");
        check(record[8] == 4 && record[9] == 0 && record[10] == 0 && record[11] == 0,
            "xbox write: mappingType is a little-endian u32 at +8");

        // ---------------------------------------------------------------- nothing changed -> nothing sent
        var device = new FakeXboxDevice();
        var protocol = new XboxJpProtocol(device);
        protocol.Open();
        XboxConfig read = protocol.ReadXboxConfig()!;
        KeyboardConfig ui = XboxToKeyboardConfig.Convert(read);

        check(protocol.WriteConfig(ui), "xbox write: writing an untouched configuration succeeds");
        check(device.Requests.Count(r => r.Cmd is XboxWire.Cmd.Write or XboxWire.Cmd.MacroStart or XboxWire.Cmd.MacroData) == 0,
            "xbox write: an untouched configuration sends no write at all");
        check(device.SessionSubtypes.Count == 0, "xbox write: an empty plan does not open a session");

        // ---------------------------------------------------------------- remap one key (A -> B)
        int positionA = KeyboardLayout.PositionOfKeyId(MappingIds.A_MAP);
        ui.BoardKeys[positionA] = MappingIds.B_MAP;
        XboxWritePlan plan = XboxFromKeyboardConfig.Plan(ui, read);
        check(plan.Mappings.Count == 1, "xbox write: one edited key produces exactly one record");
        XboxMappingChange change = plan.Mappings[0];
        check(change.Index == 12 && change.KeyCode == MappingIds.A_MAP,
            "xbox write: the edited key keeps its device index (A is record 12)");
        check(change.Record[4] == MappingIds.B_MAP && change.Record[8] == (byte)XboxMappingType.KeyCode,
            "xbox write: A -> B becomes mapping B with type KEYCODE");
        check(plan.ProfileName is null && plan.Macros.Count == 0 && plan.Warnings.Count == 0,
            "xbox write: nothing else is planned for a single remap");

        check(protocol.WriteConfig(ui), "xbox write: the remap is written");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write &&
                                       r.Offset == XboxWire.MappingBase + 12 * XboxWire.MappingSize && r.Len == 12),
            "xbox write: cmd 0x01 carries 12 bytes at 0x28 + index*12");
        check(device.SessionSubtypes.SequenceEqual(new byte[] { 0, 1 }),
            "xbox write: the batch is bracketed by cmd 0x08 open/close");

        // the device model applied it: read back through the protocol
        XboxConfig after = protocol.ReadXboxConfig()!;
        check(after.Mappings[12].Mapping == MappingIds.B_MAP &&
              after.Mappings[12].Type == XboxMappingType.KeyCode,
            "xbox write: the device holds A -> B after the write");
        KeyboardConfig reread = XboxToKeyboardConfig.Convert(after);
        check(reread.BoardKeys[positionA] == MappingIds.B_MAP, "xbox write: A -> B survives the round trip");

        // an unchanged key is not resent: the baseline moved with the read-back
        check(protocol.WriteConfig(ui), "xbox write: rewriting the same configuration succeeds");
        int writes = device.Requests.Count(r => r.Cmd == XboxWire.Cmd.Write);
        check(writes == 1, "xbox write: the second pass sends nothing new");

        // force rewrites the known positions instead of diffing
        check(protocol.WriteConfig(ui, force: true), "xbox write: force succeeds");
        check(device.Requests.Count(r => r.Cmd == XboxWire.Cmd.Write) > 1,
            "xbox write: force sends the board positions again");

        // ---------------------------------------------------------------- identity stays NONE
        var idle = new XboxJpProtocol(device);
        idle.Open();
        XboxConfig idleBase = idle.ReadXboxConfig()!;
        KeyboardConfig idleUi = XboxToKeyboardConfig.Convert(idleBase);
        KindleKey(idleUi, MappingIds.C_MAP, MappingIds.C_MAP);
        XboxWritePlan identity = XboxFromKeyboardConfig.Plan(idleUi, idleBase);
        check(identity.Mappings.Count == 0,
            "xbox write: a key mapped to itself is not written (device stores identity as NONE)");

        // ---------------------------------------------------------------- media and mouse targets
        var mediaDevice = new FakeXboxDevice();
        var mediaProtocol = new XboxJpProtocol(mediaDevice);
        mediaProtocol.Open();
        KeyboardConfig mediaUi = XboxToKeyboardConfig.Convert(mediaProtocol.ReadXboxConfig()!);
        int positionC = KeyboardLayout.PositionOfKeyId(MappingIds.C_MAP);
        mediaUi.BoardKeys[positionC] = 0xCD;                      // Play
        mediaProtocol.TargetKinds = new Dictionary<int, MappingTargetKind>
        {
            [positionC] = MappingTargetKind.Media,
        };
        check(mediaProtocol.WriteConfig(mediaUi), "xbox write: a media target (Play) is written");
        XboxConfig mediaRead = mediaProtocol.ReadXboxConfig()!;
        check(mediaRead.Mappings[14].Mapping == 0xCD && mediaRead.Mappings[14].Type == XboxMappingType.MKey,
            "xbox write: the device holds Play as MKEY on that key");
        check(mediaRead.Mappings[14].TargetName == "Play",
            "xbox write: and the read-back names it Play (the table, not the JP one)");

        // a shared id keeps the category the device reported (0xE2 is Left Alt or NoVoice)
        mediaUi.BoardKeys[positionC] = 0xE2;
        mediaProtocol.TargetKinds = new Dictionary<int, MappingTargetKind> { [positionC] = MappingTargetKind.Media };
        check(mediaProtocol.WriteConfig(mediaUi), "xbox shared id: NoVoice (MKEY 0xE2) is written");
        XboxConfig noVoiceBase = mediaProtocol.ReadXboxConfig()!;
        KeyboardConfig noVoiceUi = XboxToKeyboardConfig.Convert(noVoiceBase);
        check(noVoiceUi.BoardKinds[positionC] == MappingTargetKind.Media,
            "xbox shared id: the read keeps the category of the record (Media)");
        check(MappingIdSpace.Xbox.Find(0xE2, MappingTargetKind.Media).Name == "NoVoice" &&
              MappingIdSpace.Xbox.Find(0xE2, MappingTargetKind.Keyboard).Name == "Left Alt",
            "xbox shared id: id plus category finds NoVoice or Left Alt");
        var noVoiceRow = new MappingRowViewModel(positionC, "C", 0xE2, MappingIds.C_MAP,
            MappingIdSpace.Xbox, readKind: noVoiceUi.BoardKinds[positionC]);
        check(noVoiceRow.MappingName == TargetLabels.Of("NoVoice") && !noVoiceRow.IsEditedSinceRead,
            "xbox shared id: the row shows Mute and is not edited");
        check(noVoiceRow.SelectedCategory is not null && noVoiceRow.SelectedCategory.Category == noVoiceRow.SelectedTarget!.Category &&
              noVoiceRow.SubTargets.Contains(noVoiceRow.SelectedTarget),
            "side panel: a row just built already has its type and subtype filled (Target is not empty)");
        var plainRow = new MappingRowViewModel(0, "A", MappingIds.A_MAP, MappingIds.A_MAP, MappingIdSpace.Xbox);
        check(plainRow.SelectedCategory is not null && plainRow.HasSubtype && plainRow.SubTargets.Contains(plainRow.SelectedTarget!),
            "side panel: a plain key (A) starts with its type and subtype filled");
        var guardRow = new MappingRowViewModel(3, "D", MappingIds.D_MAP, MappingIds.D_MAP, MappingIdSpace.Xbox);
        guardRow.SelectedTarget = null;
        guardRow.SelectedCategory = null;
        check(guardRow.MappingId == MappingIds.D_MAP && guardRow.SelectedTarget is not null && !guardRow.IsEditedSinceRead,
            "side panel: the drop-downs writing 'nothing' back (list rebuilt) do not change the key");
        check(ReferenceEquals(guardRow.Categories, new MappingRowViewModel(4, "E", MappingIds.E_MAP, MappingIds.E_MAP, MappingIdSpace.Xbox).Categories),
            "side panel: every key of a family shares the same type list");
        noVoiceRow.SelectedTarget = MappingIdSpace.Xbox.Find(0xE2, MappingTargetKind.Keyboard);
        check(noVoiceRow.MappingName == "Left Alt" && noVoiceRow.IsEditedSinceRead,
            "xbox shared id: picking Left Alt on the same id is an edit");
        XboxWritePlan sameId = XboxFromKeyboardConfig.Plan(noVoiceUi, noVoiceBase, false,
            new Dictionary<int, MappingTargetKind> { [positionC] = MappingTargetKind.Keyboard });
        check(sameId.Mappings.Count == 1 &&
              (XboxMappingType)sameId.Mappings[0].Record[8] == XboxMappingType.KeyCode,
            "xbox shared id: a category-only change is planned as a KEYCODE record");
        XboxWritePlan untouched = XboxFromKeyboardConfig.Plan(noVoiceUi, noVoiceBase, false,
            new Dictionary<int, MappingTargetKind> { [positionC] = MappingTargetKind.Media });
        check(untouched.Mappings.Count == 0, "xbox shared id: the same category plans nothing");

        // a shared id without the category is reported, never guessed
        var warnDevice = new FakeXboxDevice();
        var warnProtocol = new XboxJpProtocol(warnDevice);
        warnProtocol.Open();
        KeyboardConfig warnUi = XboxToKeyboardConfig.Convert(warnProtocol.ReadXboxConfig()!);
        warnUi.BoardKeys[positionC] = 0xE2;
        check(warnProtocol.WriteConfig(warnUi), "xbox write: a shared id without a category does not fail the write");
        check(warnProtocol.LastWriteWarnings.Count == 1 && warnProtocol.LastWriteWarnings[0].Contains("0xE2"),
            "xbox write: it is reported instead of guessed");
        check(!warnDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write &&
                                            r.Offset == XboxWire.MappingBase + 14 * XboxWire.MappingSize),
            "xbox write: and nothing at all was sent for that key");

        // a cleared key uses the app's None value (0xF3), not zero
        var clearDevice = new FakeXboxDevice();
        var clearProtocol = new XboxJpProtocol(clearDevice);
        clearProtocol.Open();
        KeyboardConfig clearUi = XboxToKeyboardConfig.Convert(clearProtocol.ReadXboxConfig()!);
        int positionD = KeyboardLayout.PositionOfKeyId(MappingIds.D_MAP);
        clearUi.BoardKeys[positionD] = MappingIds.NULL_MAP;
        check(clearProtocol.WriteConfig(clearUi), "xbox write: clearing a key succeeds");
        XboxConfig clearRead = clearProtocol.ReadXboxConfig()!;
        check(clearRead.Mappings[15].Mapping == XboxIdTable.UnmappedId &&
              clearRead.Mappings[15].Type == XboxMappingType.None,
            "xbox write: a cleared key uses NONE_MAP (0xF3), as the official software does");

        // ---------------------------------------------------------------- profile name
        var nameDevice = new FakeXboxDevice();
        var nameProtocol = new XboxJpProtocol(nameDevice);
        nameProtocol.Open();
        KeyboardConfig nameUi = XboxToKeyboardConfig.Convert(nameProtocol.ReadXboxConfig()!);
        nameUi.ProfileName = "Port";
        check(nameProtocol.WriteConfig(nameUi), "xbox write: a renamed profile is written");
        check(nameDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write && r.Offset == XboxWire.ProfileNameOffset && r.Len == 32),
            "xbox write: the profile name is 32 bytes at offset 4");
        check(nameProtocol.ReadXboxConfig()!.ProfileName == "Port", "xbox write: the name reads back");

        // ---------------------------------------------------------------- single-byte fields
        var fieldsDevice = new FakeXboxDevice();
        var fieldsProtocol = new XboxJpProtocol(fieldsDevice);
        fieldsProtocol.Open();
        KeyboardConfig fieldsUi = XboxToKeyboardConfig.Convert(fieldsProtocol.ReadXboxConfig()!);
        check(fieldsUi.XboxVolumeLevel == 1 && fieldsUi.XboxFastSetting == 0,
            "xbox write: volume and the quick-setup bits come from the device into the UI model");
        check(XboxFromKeyboardConfig.Plan(fieldsUi, fieldsProtocol.ReadXboxConfig()!).IsEmpty,
            "xbox write: an untouched configuration plans no field writes");

        fieldsUi.XboxVolumeLevel = 5;
        fieldsUi.XboxFastSetting = XboxWire.FastSetting.DisableWindowsKey | XboxWire.FastSetting.DisableAltTab;
        XboxWritePlan fieldsPlan = XboxFromKeyboardConfig.Plan(fieldsUi, fieldsProtocol.ReadXboxConfig()!);
        check(fieldsPlan.Fields.Count == 2, "xbox write: two changed fields produce two 1-byte writes");
        check(fieldsPlan.Fields.Any(f => f.Offset == XboxWire.VolumeLevelOffset && f.Value == 5) &&
              fieldsPlan.Fields.Any(f => f.Offset == XboxWire.FastSettingOffset && f.Value == 3),
            "xbox write: the field writes carry the documented offsets and values");

        check(fieldsProtocol.WriteConfig(fieldsUi), "xbox write: the config tab fields are written");
        check(fieldsDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write && r.Offset == XboxWire.VolumeLevelOffset && r.Len == 1) &&
              fieldsDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write && r.Offset == XboxWire.FastSettingOffset && r.Len == 1),
            "xbox write: each field goes as a single cmd 0x01 byte");

        XboxConfig fieldsReadBack = fieldsProtocol.ReadXboxConfig()!;
        check(fieldsReadBack.VolumeLevel == 5 && fieldsReadBack.FastSetting == 3,
            "xbox write: volume and quick setup read back from the device");
        check(XboxFromKeyboardConfig.Plan(XboxToKeyboardConfig.Convert(fieldsReadBack), fieldsReadBack).Fields.Count == 0,
            "xbox write: a second pass plans nothing (the device already holds the values)");

        // The three Quick Setup bits use the app's own labels and the Quick Setup reader.
        var quickDevice = new FakeXboxDevice();
        var quickProtocol = new XboxJpProtocol(quickDevice);
        quickProtocol.Open();
        KeyboardConfig quickUi = XboxToKeyboardConfig.Convert(quickProtocol.ReadXboxConfig()!);
        quickUi.XboxFastSetting = XboxWire.FastSetting.DisableWindowsKey;
        check(XboxFromKeyboardConfig.Plan(quickUi, quickProtocol.ReadXboxConfig()!).Fields
                  .Any(f => f.Offset == XboxWire.FastSettingOffset && f.Value == 0x01),
            "xbox write: 'Disable Windows key' is bit 0x1 of fast_setting");
        quickUi.XboxFastSetting = XboxWire.FastSetting.DisableAltTab;
        check(XboxFromKeyboardConfig.Plan(quickUi, quickProtocol.ReadXboxConfig()!).Fields
                  .Any(f => f.Offset == XboxWire.FastSettingOffset && f.Value == 0x02),
            "xbox write: 'Disable Alt + Tab' is bit 0x2 of fast_setting");
        quickUi.XboxFastSetting = XboxWire.FastSetting.DisableAltF4;
        check(XboxFromKeyboardConfig.Plan(quickUi, quickProtocol.ReadXboxConfig()!).Fields
                  .Any(f => f.Offset == XboxWire.FastSettingOffset && f.Value == 0x04),
            "xbox write: 'Disable Alt + F4' is bit 0x4 of fast_setting");

        // ---------------------------------------------------------------- a macro slot
        var macroDevice = new FakeXboxDevice();
        var macroProtocol = new XboxJpProtocol(macroDevice);
        macroProtocol.Open();
        KeyboardConfig macroUi = XboxToKeyboardConfig.Convert(macroProtocol.ReadXboxConfig()!);
        int macroSlot = 2;
        MacroSlot slot = macroUi.Macros[macroSlot];
        slot.HostKey = MappingIds.Z_MAP;      // a key the default configuration numbers once
        slot.Name = "Combo";
        slot.Cycles = 3;
        slot.IntervalMs = 250;
        slot.Actions.Add(new MacroAction { Kind = MacroActionKind.KeyDown, Usage = MappingIds.A_MAP });
        slot.Actions.Add(new MacroAction { Kind = MacroActionKind.Time, DelayMs = 100 });
        slot.Actions.Add(new MacroAction { Kind = MacroActionKind.KeyUp, Usage = MappingIds.Z_MAP });

        XboxWritePlan macroPlan = XboxFromKeyboardConfig.Plan(macroUi, macroProtocol.ReadXboxConfig()!);
        check(macroPlan.Macros.Count == 1, "xbox write: one filled macro slot produces one macro write");
        XboxMacroChange macroChange = macroPlan.Macros[0];
        check(macroChange.SlotBase == XboxWire.MacroSlotBase(macroSlot),
            "xbox write: the slot is addressed by slot*0x1000");
        check(macroChange.Info.Length == 48 && macroChange.StepCount == 3 &&
              macroChange.Steps.Length == 12,
            "xbox write: the info is 48 bytes and each step 4");
        check(XboxJpProtocol.ReadU16(macroChange.Info, XboxWire.MacroInfo.MaxStep) == 3 &&
              XboxJpProtocol.ReadU16(macroChange.Info, XboxWire.MacroInfo.Interval) == 250 &&
              macroChange.Info[XboxWire.MacroInfo.Repeat] == 3,
            "xbox write: info carries the step count, interval and repeat");
        check(macroChange.Info[XboxWire.MacroInfo.KeyCode] == 0x1D,
            "xbox write: info carries the host key code");
        check(macroChange.Steps[0] == 1 && macroChange.Steps[1] == 0x04 && macroChange.Steps[4] == 3 &&
              macroChange.Steps[6] == 100 && macroChange.Steps[8] == 2 && macroChange.Steps[9] == 0x1D,
            "xbox write: steps encode press/delay/release with the timer in little-endian");
        check(macroPlan.Mappings.Any(m => m.Index == 37 && m.Record[8] == (byte)XboxMappingType.Macro &&
                                         m.Record[4] == 0x00 && m.Record[5] == 0x20),
            "xbox write: the host key becomes a MACRO mapping pointing at the slot");

        check(macroProtocol.WriteConfig(macroUi), "xbox write: the macro is written");
        check(macroDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.MacroStart && r.Offset == XboxWire.MacroSlotBase(macroSlot)),
            "xbox write: cmd 0x05 starts the slot at its offset");
        check(macroDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.MacroData && r.Offset == XboxWire.MacroSlotBase(macroSlot) && r.Len == 48),
            "xbox write: cmd 0x06 writes the 48-byte info at the slot base");
        check(macroDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.MacroData &&
                                            r.Offset == XboxWire.MacroSlotBase(macroSlot) + XboxWire.MacroDataOffset),
            "xbox write: cmd 0x06 writes the steps from slot base + 0x40");

        XboxConfig macroReadBack = macroProtocol.ReadXboxConfig()!;
        check(macroReadBack.Macros[macroSlot] is { Name: "Combo", MaxStep: 3 },
            "xbox write: the macro reads back with its name and step count");
        check(macroReadBack.Macros[macroSlot]!.Steps.Count == 3 &&
              macroReadBack.Macros[macroSlot]!.Steps[1].IsDelay &&
              macroReadBack.Macros[macroSlot]!.Steps[1].Timer == 100,
            "xbox write: the steps read back");
        check(XboxToKeyboardConfig.Convert(macroReadBack).Macros[macroSlot].Actions.Count == 3,
            "xbox write: the macro projects back into the UI model");

        // ---------------------------------------------------------------- chunking a long macro
        var longDevice = new FakeXboxDevice();
        var longProtocol = new XboxJpProtocol(longDevice);
        longProtocol.Open();
        KeyboardConfig longUi = XboxToKeyboardConfig.Convert(longProtocol.ReadXboxConfig()!);
        MacroSlot longSlot = longUi.Macros[1];
        longSlot.HostKey = MappingIds.Z_MAP;
        longSlot.Name = "Long";
        for (int i = 0; i < 60; i++)
            longSlot.Actions.Add(new MacroAction { Kind = MacroActionKind.Time, DelayMs = (ushort)(i + 1) });

        XboxWritePlan longPlan = XboxFromKeyboardConfig.Plan(longUi, longProtocol.ReadXboxConfig()!);
        check(longPlan.Macros.Count == 1 && longPlan.Macros[0].StepCount == 60,
            "xbox write: one changed slot on a device without macros");
        check(longProtocol.WriteConfig(longUi), "xbox write: the long macro is written");
        int dataRequests = longDevice.Requests.Count(r => r.Cmd == XboxWire.Cmd.MacroData);
        // 48 bytes of info + 240 bytes of steps = 1 + 5 chunks
        check(dataRequests == 6, "xbox write: macro data is chunked by 53 bytes");
        info($"long macro: {dataRequests} cmd 0x06 frames for 48 + 240 bytes");

        // ---------------------------------------------------------------- field write + backup
        var fieldDevice = new FakeXboxDevice();
        var fieldProtocol = new XboxJpProtocol(fieldDevice);
        fieldProtocol.Open();
        fieldProtocol.ReadXboxConfig();
        check(fieldProtocol.WriteField(XboxWire.VolumeLevelOffset, 2),
            "xbox write: a single configuration byte (volume) can be written");
        check(fieldDevice.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write && r.Offset == XboxWire.VolumeLevelOffset && r.Len == 1),
            "xbox write: volume is one byte at 0x5C9");

        string backupRoot = Path.Combine(Path.GetTempPath(), "retro87-selftest-backup");
        try
        {
            var backups = new ConfigBackup(backupRoot);
            string? path = backups.Save("Retro 87 X", fieldProtocol.LastRawConfig!);
            check(path is not null && File.Exists(path), "xbox write: the backup file is created");
            check(File.ReadAllBytes(path!).SequenceEqual(fieldProtocol.LastRawConfig!),
                "xbox write: the backup is byte-for-byte the read configuration");
        }
        finally
        {
            try { Directory.Delete(backupRoot, recursive: true); } catch { /* temp dir */ }
        }

        check(fieldProtocol.LastRawConfig?.Length == XboxWire.ConfigSize,
            "xbox write: the session exposes the raw configuration for backup");
        check(fieldProtocol is IWriteDiagnostics { LastWriteWarnings: not null },
            "xbox write: the session exposes its write diagnostics");
    }

    /// <summary>
    /// Drives the macro hardware test (<see cref="XboxMacroTest"/>) against
    /// <see cref="FakeXboxDevice"/>: the choice of host key and empty slot, the bytes it plans, and
    /// the write → read-back → revert sequence the real keyboard will see.
    /// </summary>
    private static void XboxMacroTestDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        // A device like the real one: Scroll Lock at record 79 (still at its default) and the
        // official app's macro sitting in slot 0 with no key firing it.
        byte[] likeTheDevice = FakeXboxDevice.BuildDefault();
        const int scrollLockRecord = 79;
        int recordOffset = XboxWire.MappingBase + scrollLockRecord * XboxWire.MappingSize;
        likeTheDevice[recordOffset] = 0x47;                       // keyCode = Scroll Lock
        likeTheDevice[recordOffset + 4] = 0x47;                   // mapping = itself
        likeTheDevice[recordOffset + 8] = (byte)XboxMappingType.None;
        var device = new FakeXboxDevice(likeTheDevice);
        device.SetMacro(0, 40, 0x09, "Test", 0, 1,
            new XboxMacroStep { KeyState = 1, KeyCode = MappingIds.A_MAP });

        var protocol = new XboxJpProtocol(device);
        protocol.Open();
        XboxConfig config = protocol.ReadXboxConfig()!;
        List<XboxMacroSlotProbe> probes = XboxMacroTest.ProbeSlots(protocol, TextWriter.Null);

        check(XboxMacroTest.TryBuildPlan(config, probes, out XboxMacroTestPlan plan, out string error) &&
              error.Length == 0,
            "macro test: a plan is built when a slot is free");
        check(plan.Slot == 1 && plan.SlotBase == 0x1000,
            "macro test: an empty slot >= 1 is used, addressed by slot*0x1000 (slot 0 is left alone)");
        check(plan.MappingIndex == scrollLockRecord && plan.KeyCode == 0x47 && plan.KeyName == "Scroll Lock",
            "macro test: the host key is the harmless Scroll Lock at its device index");
        check(plan.MacroRecord[0] == 0x47 && plan.MacroRecord[5] == 0x10 &&
              plan.MacroRecord[8] == (byte)XboxMappingType.Macro,
            "macro test: the record makes the key a MACRO mapping pointing at the slot base");
        check(plan.StepCount == 3 && plan.Steps[0] == 1 && plan.Steps[1] == MappingIds.A_MAP &&
              plan.Steps[4] == 3 && plan.Steps[6] == 100 &&
              plan.Steps[8] == 2 && plan.Steps[9] == MappingIds.A_MAP,
            "macro test: the steps are press A, 100 ms wait, release A");
        check(XboxJpProtocol.ReadU16(plan.Info, XboxWire.MacroInfo.MaxStep) == 3 &&
              plan.Info[XboxWire.MacroInfo.KeyCode] == 0x47 &&
              plan.Info[XboxWire.MacroInfo.Repeat] == 1 &&
              XboxJpProtocol.DecodeProfileName(plan.Info, XboxWire.MacroInfo.Name, XboxWire.MacroInfo.NameBytes) == "RT87T",
            "macro test: the info carries the name, host key, step count and repeat");

        // the write -> read-back -> revert sequence, exactly as the hardware test runs it
        check(protocol.SendSession(XboxWire.SessionSubtype.Open), "macro test: the session handshake is sent");
        check(protocol.WriteMapping(plan.MappingIndex, plan.MacroRecord), "macro test: the host key is remapped");
        check(protocol.WriteMacroRegion(plan.SlotBase, plan.Info, plan.Steps, plan.StepCount),
            "macro test: cmd 0x05 + 0x06 write the info and the steps");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.MacroStart && r.Offset == plan.SlotBase && r.Len == 0),
            "macro test: cmd 0x05 carries the slot base as offset and no data");

        XboxConfig applied = protocol.ReadXboxConfig()!;
        bool appliedOk = applied.Mappings[plan.MappingIndex].Type == XboxMappingType.Macro &&
                         applied.Macros[plan.Slot] is { MaxStep: 3, Name: "RT87T" } appliedSlot &&
                         appliedSlot.Steps.Count == 3 && appliedSlot.Steps[1].IsDelay;
        check(appliedOk, "macro test: the ordinary read shows the macro bound to that key");
        check(XboxMacroTest.ProbeSlots(protocol, TextWriter.Null)[plan.Slot].IsEmpty == false,
            "macro test: the slot reads back as filled");

        check(protocol.WriteMapping(plan.MappingIndex, plan.OriginalRecord), "macro test: the record is reverted");
        check(protocol.WriteMacroRegion(plan.SlotBase, plan.OriginalInfo, plan.OriginalSteps,
                  plan.OriginalSteps.Length / XboxWire.MacroStepSize),
            "macro test: the slot is put back to its erased bytes");
        protocol.SendSession(XboxWire.SessionSubtype.Close);
        XboxConfig reverted = protocol.ReadXboxConfig()!;
        check(reverted.Macros[plan.Slot] is null &&
              reverted.Mappings[plan.MappingIndex].Type == XboxMappingType.None &&
              reverted.Raw.SequenceEqual(config.Raw),
            "macro test: after the revert the device is byte-for-byte as before");
        check(device.MacroRegion.AsSpan(0x1000, 48).ToArray().All(b => b == 0xFF),
            "macro test: the used slot finished erased (0xFF), as it started");

        // no harmless key in its default state -> refuse, never guess
        var bareDevice = new FakeXboxDevice();
        var bareProtocol = new XboxJpProtocol(bareDevice);
        bareProtocol.Open();
        check(!XboxMacroTest.TryBuildPlan(bareProtocol.ReadXboxConfig()!,
                  XboxMacroTest.ProbeSlots(bareProtocol, TextWriter.Null), out _, out string bareError) &&
              bareError.Contains("harmless"),
            "macro test: without a default harmless key the plan is refused");

        // every slot taken -> refuse instead of overwriting a macro
        var fullDevice = new FakeXboxDevice(likeTheDevice);
        for (int slot = 0; slot < 10; slot++)
            fullDevice.SetMacro(slot, 40 + slot, 0x09, $"M{slot}", 0, 1,
                new XboxMacroStep { KeyState = 1, KeyCode = MappingIds.A_MAP });
        var fullProtocol = new XboxJpProtocol(fullDevice);
        fullProtocol.Open();
        check(!XboxMacroTest.TryBuildPlan(fullProtocol.ReadXboxConfig()!,
                  XboxMacroTest.ProbeSlots(fullProtocol, TextWriter.Null), out _, out string fullError) &&
              fullError.Contains("no empty"),
            "macro test: with every slot taken the plan is refused");
        info($"probes on a device with all slots filled: {fullError}");
    }

    /// <summary>
    /// Drives the configuration write test (<see cref="XboxConfigTest"/>) against
    /// <see cref="FakeXboxDevice"/>: the temporary name and byte it plans and the
    /// write → read-back → revert sequence the real keyboard will see.
    /// </summary>
    private static void XboxConfigTestDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        var device = new FakeXboxDevice();
        var protocol = new XboxJpProtocol(device);
        protocol.Open();
        XboxConfig config = protocol.ReadXboxConfig()!;

        check(XboxConfigTest.TryBuildPlan(config, out XboxConfigTestPlan plan, out string error) && error.Length == 0,
            "config test: a plan is built from the configuration read");
        check(plan.OriginalName == "Skipult" && plan.TestName == "RT87 TEST",
            "config test: it keeps the current name and picks an obviously temporary one");
        check(plan.OriginalNameField.Length == 32 && plan.OriginalNameField[0] == 0x00 && plan.OriginalNameField[1] == 0x53,
            "config test: the name field is the 32 raw bytes, UTF-16 big-endian");
        check(plan.TestNameField[0] == 0x00 && plan.TestNameField[1] == 0x52 && plan.TestNameField[2] == 0x00,
            "config test: the temporary name is encoded the same way");
        check(plan.FieldOffset == XboxWire.VolumeLevelOffset && plan.OriginalField == 1 && plan.TestField == 2,
            "config test: the loose byte is volume_lv (0x5C9), 1 -> 2 as the official app did");
        check(XboxConfigTestPlan.AlternateField(2) == 1 && XboxConfigTestPlan.AlternateField(1) == 2,
            "config test: the alternate value stays inside 1..2 whatever the device holds");
        check(XboxConfigTestPlan.AlternateField(0) == 2 && XboxConfigTestPlan.AlternateField(3) == 2,
            "config test: any other value still steps to 2");

        // the write -> read-back -> revert sequence, exactly as the hardware test runs it
        check(protocol.SendSession(XboxWire.SessionSubtype.Open), "config test: the session handshake is sent");
        check(protocol.WriteField(plan.FieldOffset, plan.OriginalField), "config test: the no-op byte is written");
        check(protocol.WriteField(plan.FieldOffset, plan.TestField), "config test: the temporary byte is written");
        check(protocol.WriteProfileName(plan.TestName), "config test: the temporary name is written");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write &&
                                       r.Offset == XboxWire.ProfileNameOffset && r.Len == 32),
            "config test: cmd 0x01 writes 32 bytes at offset 4");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write &&
                                       r.Offset == XboxWire.VolumeLevelOffset && r.Len == 1),
            "config test: cmd 0x01 writes one byte at 0x5C9");

        XboxConfig applied = protocol.ReadXboxConfig()!;
        check(applied.ProfileName == "RT87 TEST" && applied.Raw[XboxWire.VolumeLevelOffset] == 2,
            "config test: the ordinary read shows the temporary name and byte");

        check(protocol.WriteProfileName(plan.OriginalName), "config test: the name is reverted");
        check(protocol.WriteField(plan.FieldOffset, plan.OriginalField), "config test: the byte is reverted");
        protocol.SendSession(XboxWire.SessionSubtype.Close);
        XboxConfig reverted = protocol.ReadXboxConfig()!;
        check(reverted.ProfileName == "Skipult" && reverted.Raw[XboxWire.VolumeLevelOffset] == 1,
            "config test: the name and byte are back");

        // a device already holding 2 flips to 1 instead of staying put
        byte[] heldAtTwo = FakeXboxDevice.BuildDefault();
        heldAtTwo[XboxWire.VolumeLevelOffset] = 2;
        var twoDevice = new FakeXboxDevice(heldAtTwo);
        var twoProtocol = new XboxJpProtocol(twoDevice);
        twoProtocol.Open();
        check(XboxConfigTest.TryBuildPlan(twoProtocol.ReadXboxConfig()!, out XboxConfigTestPlan twoPlan, out _) &&
              twoPlan.OriginalField == 2 && twoPlan.TestField == 1,
            "config test: a device at 2 is tested with 1");
        info($"plan: \"{twoPlan.OriginalName}\" -> \"{twoPlan.TestName}\", byte {twoPlan.FieldOffset} " +
             $"0x{twoPlan.OriginalField:X2} -> 0x{twoPlan.TestField:X2}");

        // a name that already is the temporary one does not stay put
        byte[] named = FakeXboxDevice.BuildDefault();
        byte[] already = XboxFromKeyboardConfig.EncodeNameBigEndian("RT87 TEST", XboxWire.ProfileNameBytes);
        Array.Copy(already, 0, named, XboxWire.ProfileNameOffset, already.Length);
        var namedDevice = new FakeXboxDevice(named);
        var namedProtocol = new XboxJpProtocol(namedDevice);
        namedProtocol.Open();
        check(XboxConfigTest.TryBuildPlan(namedProtocol.ReadXboxConfig()!, out XboxConfigTestPlan namedPlan, out _) &&
              namedPlan.OriginalName == "RT87 TEST" && namedPlan.TestName == "RT87 TST",
            "config test: a device already named like the test still gets a different temporary name");
    }

    /// <summary>
    /// Drives the media/mouse write test (<see cref="XboxMediaTest"/>) against
    /// <see cref="FakeXboxDevice"/>: the targets it picks and the remap → read-back → revert
    /// sequence the real keyboard will see through the UI path.
    /// </summary>
    private static void XboxMediaTestDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        // A device like the real one: Scroll Lock at record 79, still at its default.
        byte[] shaped = FakeXboxDevice.BuildDefault();
        const int scrollLockRecord = 79;
        int recordOffset = XboxWire.MappingBase + scrollLockRecord * XboxWire.MappingSize;
        shaped[recordOffset] = MappingIds.SCRLK_MAP;
        shaped[recordOffset + 4] = MappingIds.SCRLK_MAP;

        var device = new FakeXboxDevice(shaped);
        var protocol = new XboxJpProtocol(device);
        protocol.Open();
        XboxConfig config = protocol.ReadXboxConfig()!;

        check(XboxMediaTest.TryBuildPlan(config, out XboxMediaTestPlan plan, out string error) && error.Length == 0,
            "media test: a plan is built from the configuration read");
        check(plan.KeyCode == MappingIds.SCRLK_MAP && plan.MappingIndex == scrollLockRecord &&
              plan.Position == KeyboardLayout.PositionOfKeyId(MappingIds.SCRLK_MAP),
            "media test: the host key is the harmless Scroll Lock, with a UI position");
        check(plan.Media is { Id: 0xCD, Name: "Play", Type: XboxMappingType.MKey, Kind: MappingTargetKind.Media },
            "media test: the media target is Play (0xCD, MKEY), the id the capture showed");
        check(plan.Mouse is { Id: 0xE8, Name: "Left_Click", Type: XboxMappingType.Mouse, Kind: MappingTargetKind.Mouse },
            "media test: the mouse target is Left_Click (0xE8, MOUSE), unambiguous");
        check(plan.RecordFor(plan.Media)[4] == 0xCD && plan.RecordFor(plan.Media)[8] == (byte)XboxMappingType.MKey,
            "media test: the media record carries the id and the MKEY type");
        check(plan.RecordFor(plan.Mouse)[4] == 0xE8 && plan.RecordFor(plan.Mouse)[8] == (byte)XboxMappingType.Mouse,
            "media test: the mouse record carries the id and the MOUSE type");

        // media through the UI path
        KeyboardConfig mediaUi = protocol.ReadConfig();
        mediaUi.BoardKeys[plan.Position] = plan.Media.Id;
        protocol.TargetKinds = new Dictionary<int, MappingTargetKind> { [plan.Position] = plan.Media.Kind };
        try
        {
            check(protocol.WriteConfig(mediaUi), "media test: the media target is written through the UI path");
        }
        finally
        {
            protocol.TargetKinds = null;
        }
        XboxConfig mediaRead = protocol.ReadXboxConfig()!;
        check(mediaRead.Mappings[scrollLockRecord].Type == XboxMappingType.MKey &&
              mediaRead.Mappings[scrollLockRecord].MappingId == 0xCD &&
              mediaRead.Mappings[scrollLockRecord].TargetName == "Play",
            "media test: the device holds Play as MKEY and the read-back names it Play");
        check(XboxToKeyboardConfig.Convert(mediaRead).BoardKeys[plan.Position] == 0xCD,
            "media test: the UI projection shows the media target on that key");

        // mouse through the UI path
        KeyboardConfig mouseUi = protocol.ReadConfig();
        mouseUi.BoardKeys[plan.Position] = plan.Mouse.Id;
        protocol.TargetKinds = new Dictionary<int, MappingTargetKind> { [plan.Position] = plan.Mouse.Kind };
        try
        {
            check(protocol.WriteConfig(mouseUi), "media test: the mouse target is written through the UI path");
        }
        finally
        {
            protocol.TargetKinds = null;
        }
        XboxConfig mouseRead = protocol.ReadXboxConfig()!;
        check(mouseRead.Mappings[scrollLockRecord].Type == XboxMappingType.Mouse &&
              mouseRead.Mappings[scrollLockRecord].MappingId == 0xE8 &&
              mouseRead.Mappings[scrollLockRecord].TargetName == "Left_Click",
            "media test: the device holds Left_Click as MOUSE");
        check(XboxToKeyboardConfig.Convert(mouseRead).BoardKeys[plan.Position] == 0xE8,
            "media test: the UI projection shows the mouse target on that key");

        // revert
        check(protocol.WriteMapping(plan.MappingIndex, plan.OriginalRecord), "media test: the record is reverted");
        XboxConfig reverted = protocol.ReadXboxConfig()!;
        check(reverted.Mappings[scrollLockRecord].Type == XboxMappingType.None &&
              reverted.Mappings[scrollLockRecord].MappingId == MappingIds.SCRLK_MAP &&
              reverted.Raw.SequenceEqual(config.Raw),
            "media test: after the revert the device is byte-for-byte as before");

        // ambiguous ids stay refused without the category (the reason this test uses unambiguous ones)
        KeyboardConfig ambiguous = protocol.ReadConfig();
        ambiguous.BoardKeys[plan.Position] = 0xE9;                    // middle mouse button OR VoiceUp
        check(protocol.WriteConfig(ambiguous) && protocol.LastWriteWarnings.Count == 1,
            "media test: a shared id (0xE9) is still reported instead of guessed");

        // no key with a UI position in its default state -> refuse
        var bareDevice = new FakeXboxDevice();
        var bareProtocol = new XboxJpProtocol(bareDevice);
        bareProtocol.Open();
        check(!XboxMediaTest.TryBuildPlan(bareProtocol.ReadXboxConfig()!, out _, out string bareError) &&
              bareError.Contains("harmless"),
            "media test: without a default key with a UI position the plan is refused");
        info($"targets: {plan.Media.Name} (0x{plan.Media.Id:X2}/{plan.Media.Type}) and " +
             $"{plan.Mouse.Name} (0x{plan.Mouse.Id:X2}/{plan.Mouse.Type})");
    }

    /// <summary>
    /// Drives the wake handshake and the error/terminator reply (cmd <c>0x08 sub 0</c> and the
    /// <c>02 04 C0 …</c> frame) against <see cref="FakeXboxDevice"/>, including the
    /// wake-before-read retry the app uses when the keyboard went idle.
    /// </summary>
    private static void XboxWakeDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        // ---------------------------------------------------------------- the wake frame itself
        var device = new FakeXboxDevice();
        var protocol = new XboxJpProtocol(device);
        protocol.Open();

        check(protocol.Wake(), "xbox wake: cmd 0x08 sub 0 is acknowledged");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.Session && r.Len == 0),
            "xbox wake: the request is the cmd 0x08 frame with no payload");
        check(device.SessionSubtypes.Contains(XboxWire.SessionSubtype.Open),
            "xbox wake: it carries subtype 0 (the native _ReportXbox(0))");
        check(protocol.Ping() && protocol.FirmwareVersion > 1f,
            "xbox wake: a one-shot version ping answers and fills the version");

        check(XboxJpProtocol.IsErrorReply(new byte[] { 0x02, 0x04, 0xC0, 0x0A, 0, 0, 0, 0, 0, 0 }),
            "xbox wake: 02 04 C0 is recognised as the device's error reply");
        check(!XboxJpProtocol.IsErrorReply(new byte[] { 0x02, 0x04, 0x03, 0x0A, 0, 0, 0, 0, 0, 0 }),
            "xbox wake: a normal reply is not mistaken for an error");

        // ---------------------------------------------------------------- WakeOnOpen sends it first
        var openDevice = new FakeXboxDevice();
        var openProtocol = new XboxJpProtocol(openDevice) { WakeOnOpen = true };
        openProtocol.Open();
        check(openDevice.SessionSubtypes.FirstOrDefault() == XboxWire.SessionSubtype.Open &&
              openDevice.Requests[0].Cmd == XboxWire.Cmd.Session,
            "xbox wake: with WakeOnOpen the handshake is the first request, before the version");
        check(openProtocol.FirmwareVersion > 1f, "xbox wake: the version read still happens after it");

        // ---------------------------------------------------------------- a sleeping keyboard fails fast
        var asleepDevice = new FakeXboxDevice { AsleepUntilWake = true };
        var asleepProtocol = new XboxJpProtocol(asleepDevice);
        asleepProtocol.Open(probeVersion: false);
        check(!asleepProtocol.Ping(), "xbox wake: a sleeping keyboard does not answer the ping");
        check(!asleepProtocol.ReadVersion(), "xbox wake: reading the version fails while it sleeps");
        check(asleepDevice.Requests.Count <= 2 && asleepDevice.ErrorReplyCount > 0,
            "xbox wake: the 0xC0 error is not retried 30 times (fails fast)");
        check(asleepProtocol.LastFailure == XboxFailure.DeviceError,
            "xbox wake: LastFailure classifies the 0xC0 condition");
        check(asleepProtocol.LastError?.Contains("0xC0") == true,
            "xbox wake: LastError names the 0xC0 condition");
        check(asleepProtocol.ReadXboxConfig() is null && asleepProtocol.LastError?.Contains("0xC0") == true,
            "xbox wake: a read against the sleeping device returns null with the 0xC0 diagnosis");
        check(asleepDevice.WakeCount == 0, "xbox wake: nothing woke it when the caller did not opt in");

        // ---------------------------------------------------------------- opting in wakes and retries
        asleepProtocol.WakeOnOpen = true;
        XboxConfig? afterWake = asleepProtocol.ReadXboxConfig();
        check(afterWake is not null, "xbox wake: with WakeOnOpen the read wakes the device and succeeds");
        check(asleepDevice.WakeCount == 1, "xbox wake: exactly one wake frame was needed");
        check(afterWake?.Raw.Length == XboxWire.ConfigSize,
            "xbox wake: the retried read brings the whole configuration");
        info($"sleeping device: {asleepDevice.ErrorReplyCount} error reply(ies), {asleepDevice.WakeCount} wake(s)");

        // ---------------------------------------------------------------- the factory wakes (contract)
        var factoryDevice = new FakeXboxDevice();
        var factoryProtocol = new XboxJpProtocol(factoryDevice) { WakeOnOpen = true };
        check(factoryProtocol.WakeOnOpen, "xbox wake: WakeOnOpen is a public switch the app turns on");
    }

    /// <summary>
    /// Drives the Config-tab write test (<see cref="XboxUiConfigTest"/>) against
    /// <see cref="FakeXboxDevice"/>: the values it picks and the write → read-back → revert
    /// sequence for the volume and the Quick Setup bits, through the UI path.
    /// </summary>
    private static void XboxUiConfigTestDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        var device = new FakeXboxDevice();
        var protocol = new XboxJpProtocol(device);
        protocol.Open();
        XboxConfig config = protocol.ReadXboxConfig()!;

        check(XboxUiConfigTest.TryBuildPlan(config, out XboxUiConfigTestPlan plan, out string error) && error.Length == 0,
            "ui config test: a plan is built from the configuration read");
        check(plan.OriginalVolume == 1 && plan.TestVolume == 3,
            "ui config test: the temporary volume stays inside the app's 1..5 (1 -> 3)");
        check(plan.OriginalFastSetting == 0 && plan.TestFastSetting == XboxWire.FastSetting.DisableAltF4,
            "ui config test: only the 'Disable Alt + F4' bit is toggled");
        check(XboxUiConfigTestPlan.Describe(0).StartsWith("nothing disabled"),
            "ui config test: the Quick Setup byte is described in words");
        check(XboxUiConfigTestPlan.Describe(plan.TestFastSetting).Contains("Alt + F4"),
            "ui config test: the description names the bit that was turned on");

        // volume through the UI path
        KeyboardConfig volumeUi = protocol.ReadConfig();
        volumeUi.XboxVolumeLevel = plan.TestVolume;
        check(protocol.WriteConfig(volumeUi), "ui config test: the volume is written through the UI path");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write &&
                                       r.Offset == XboxWire.VolumeLevelOffset && r.Len == 1),
            "ui config test: the volume goes as one cmd 0x01 byte");
        check(protocol.ReadXboxConfig()!.VolumeLevel == plan.TestVolume,
            "ui config test: the volume reads back");
        check(XboxToKeyboardConfig.Convert(protocol.ReadXboxConfig()!).XboxVolumeLevel == plan.TestVolume,
            "ui config test: the Config tab would show the new volume");

        // quick setup through the UI path
        KeyboardConfig quickUi = protocol.ReadConfig();
        quickUi.XboxFastSetting = plan.TestFastSetting;
        check(protocol.WriteConfig(quickUi), "ui config test: the quick setup byte is written through the UI path");
        check(device.Requests.Any(r => r.Cmd == XboxWire.Cmd.Write &&
                                       r.Offset == XboxWire.FastSettingOffset && r.Len == 1),
            "ui config test: the quick setup byte goes as one cmd 0x01 byte");
        XboxConfig quickRead = protocol.ReadXboxConfig()!;
        check(quickRead.FastSetting == plan.TestFastSetting,
            "ui config test: the quick setup byte reads back");
        check(XboxToKeyboardConfig.Convert(quickRead).XboxFastSetting == plan.TestFastSetting,
            "ui config test: the Config tab would show the new quick setup");

        // revert through the UI path
        KeyboardConfig revertUi = protocol.ReadConfig();
        revertUi.XboxVolumeLevel = plan.OriginalVolume;
        revertUi.XboxFastSetting = plan.OriginalFastSetting;
        check(protocol.WriteConfig(revertUi), "ui config test: both fields are reverted through the UI path");
        XboxConfig reverted = protocol.ReadXboxConfig()!;
        check(reverted.VolumeLevel == plan.OriginalVolume && reverted.FastSetting == plan.OriginalFastSetting,
            "ui config test: volume and quick setup are back to the original bytes");
        check(reverted.Raw.SequenceEqual(config.Raw),
            "ui config test: the whole configuration ends identical to the read one");

        // an untouched screen writes nothing (the diff that protects the device)
        XboxWritePlan empty = XboxFromKeyboardConfig.Plan(protocol.ReadConfig(), reverted);
        check(empty.IsEmpty && empty.Fields.Count == 0,
            "ui config test: a screen with the read values plans no field write");

        // a device already at the temporary volume gets the other one
        byte[] atThree = FakeXboxDevice.BuildDefault();
        atThree[XboxWire.VolumeLevelOffset] = 3;
        var threeDevice = new FakeXboxDevice(atThree);
        var threeProtocol = new XboxJpProtocol(threeDevice);
        threeProtocol.Open();
        check(XboxUiConfigTest.TryBuildPlan(threeProtocol.ReadXboxConfig()!, out XboxUiConfigTestPlan threePlan, out _) &&
              threePlan.TestVolume == 2,
            "ui config test: a device already at volume 3 is tested with 2");

        info($"plan: volume {plan.OriginalVolume} -> {plan.TestVolume}, quick setup " +
             $"0x{plan.OriginalFastSetting:X2} -> 0x{plan.TestFastSetting:X2}");
    }

    /// <summary>
    /// Drives the link-loss measurement (<see cref="XboxLinkLossTest.MeasureOpen"/>) against
    /// <see cref="FakeXboxDevice"/>: the two outcomes that matter (the keyboard slept with the link
    /// gone and the wake brings it back; and it never slept), plus the probe classifier.
    /// </summary>
    private static void XboxLinkLossDryRun(TextWriter output, Action<bool, string> check, Action<string> info)
    {
        // ---------------------------------------------------------------- the probe classifier
        check(XboxProbe.Classify(true, "qualquer coisa") == XboxProbeState.Awake,
            "link: an answer is Awake, whatever the reason text says");
        check(XboxProbe.Classify(false, "device error (0xC0: asleep or no keyboard on the dongle)") ==
              XboxProbeState.ErrorReply,
            "link: the device's 0xC0 is told apart from silence");
        check(XboxProbe.Classify(false, "no answer (timeout)") == XboxProbeState.NoAnswer,
            "link: silence is NoAnswer");
        info($"states: \"{XboxProbe.Describe(XboxProbeState.Awake)}\" / " +
             $"\"{XboxProbe.Describe(XboxProbeState.ErrorReply)}\"");

        // ---------------------------------------------------------------- it slept, the wake recovers
        byte[] config = FakeXboxDevice.BuildDefault();
        var asleepDevice = new FakeXboxDevice(config) { AsleepUntilWake = true };
        var asleepProtocol = new XboxJpProtocol(asleepDevice);
        asleepProtocol.Open(probeVersion: false);
        XboxLinkOutcome recovered = XboxLinkLossTest.MeasureOpen(asleepProtocol, config, _ => { });
        check(recovered.Before == XboxProbeState.ErrorReply && recovered.BeforeDetail.Contains("0xC0"),
            "link: with the link gone the keyboard answers the 0xC0 error, not silence");
        check(recovered.WakeAcknowledged && recovered.After == XboxProbeState.Awake && recovered.Recovered,
            "link: the wake frame is acknowledged and the keyboard answers again");
        check(recovered.WakeAttempts == 1, "link: one wake frame is enough when the device is listening");
        check(recovered.ConfigUnchanged == true, "link: the configuration is identical to the pre-unplug state");
        check(XboxLinkLossTest.Summarize(recovered).Contains("recovers it"),
            "link: the verdict names the wake as the recovery");

        // ---------------------------------------------------------------- it never slept
        var awakeDevice = new FakeXboxDevice(FakeXboxDevice.BuildDefault());
        var awakeProtocol = new XboxJpProtocol(awakeDevice);
        awakeProtocol.Open(probeVersion: false);
        XboxLinkOutcome neverSlept = XboxLinkLossTest.MeasureOpen(awakeProtocol, null, _ => { });
        check(neverSlept.NeverSlept && neverSlept.After == XboxProbeState.Awake,
            "link: an already-answering keyboard counts as 'never slept'");
        check(awakeDevice.WakeCount == 0 && !neverSlept.WakeAcknowledged,
            "link: nothing (not even the wake) is sent when the probe already answered");
        check(XboxLinkLossTest.Summarize(neverSlept).Contains("did NOT sleep"),
            "link: the verdict says it did not sleep");

        // ---------------------------------------------------------------- it slept and never came back
        var deadDevice = new FakeXboxDevice(FakeXboxDevice.BuildDefault()) { ErrorReplies = true };
        var deadProtocol = new XboxJpProtocol(deadDevice);
        deadProtocol.Open(probeVersion: false);
        XboxLinkOutcome dead = XboxLinkLossTest.MeasureOpen(deadProtocol, null, _ => { }, insistSeconds: 1);
        check(dead.Before == XboxProbeState.ErrorReply && !dead.WakeAcknowledged && !dead.Recovered,
            "link: a device that keeps saying 0xC0 is reported as not recovered");
        check(dead.WakeAttempts > 1, "link: insisting keeps trying the wake frame");
        check(dead.ConfigUnchanged is null,
            "link: when nothing answered, 'the configuration is unchanged' is not claimed");
        check(XboxLinkLossTest.Summarize(dead).Contains("did NOT recover it"),
            "link: the verdict tells the wake did not suffice");
    }

    /// <summary>Points <paramref name="from"/> at <paramref name="to"/> in the UI model.</summary>
    private static void KindleKey(KeyboardConfig config, byte from, byte to)
        => config.BoardKeys[KeyboardLayout.PositionOfKeyId(from)] = to;
}
