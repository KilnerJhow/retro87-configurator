// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;
using Retro87.Localization;

namespace Retro87.Models;

/// <summary>One 12-byte mapping record to send with <c>cmd 0x01</c>.</summary>
/// <param name="Index">Index inside <c>keyboard_mappings[120]</c> (device order).</param>
/// <param name="KeyCode">Physical key id the record belongs to (the byte at +0).</param>
/// <param name="Record">The 12 bytes to write verbatim at <c>0x28 + Index·12</c>.</param>
public sealed record XboxMappingChange(int Index, byte KeyCode, byte[] Record);

/// <summary>One macro slot to send with <c>cmd 0x05</c> + <c>cmd 0x06</c>.</summary>
/// <param name="Slot">Slot index (device offset is <c>Slot · 0x1000</c>).</param>
/// <param name="SlotBase">Device offset of the slot.</param>
/// <param name="MappingIndex">Index of the mapping record that must reference the slot.</param>
/// <param name="MappingRecord">12 bytes making that key a <c>MACRO</c> mapping.</param>
/// <param name="Info">The 48-byte <c>KEYBOARD_MACROS_INFO</c> to write at the slot base.</param>
/// <param name="Steps">The 4-byte steps to write from <c>SlotBase + 0x40</c>.</param>
/// <param name="StepCount">Number of steps in <paramref name="Steps"/>.</param>
public sealed record XboxMacroChange(
    int Slot, uint SlotBase, int MappingIndex, byte[] MappingRecord,
    byte[] Info, byte[] Steps, int StepCount);

/// <summary>One single-byte configuration field to send with <c>cmd 0x01</c>.</summary>
/// <param name="Offset">Byte offset inside the 1532-byte configuration.</param>
/// <param name="Value">Value to write there.</param>
public sealed record XboxFieldChange(int Offset, byte Value);

/// <summary>Everything <see cref="Protocol.XboxJpProtocol.WriteConfig"/> has to send.</summary>
/// <param name="Mappings">Mapping records to write, in ascending device order.</param>
/// <param name="Macros">Macro slots to write.</param>
/// <param name="ProfileName">New profile name, or null when it did not change.</param>
/// <param name="Fields">
/// Single-byte fields to write (<c>volume_lv</c> and the <c>fast_setting</c> Quick Setup bits),
/// empty when none changed. They use the same <c>cmd 0x01</c> primitive validated on the device
/// in C3.
/// </param>
/// <param name="Warnings">Human-readable notes about edits that could not be turned into a write.</param>
public sealed record XboxWritePlan(
    IReadOnlyList<XboxMappingChange> Mappings,
    IReadOnlyList<XboxMacroChange> Macros,
    string? ProfileName,
    IReadOnlyList<XboxFieldChange> Fields,
    IReadOnlyList<string> Warnings)
{
    /// <summary>True when the plan would not touch the device at all.</summary>
    public bool IsEmpty => Mappings.Count == 0 && Macros.Count == 0 && ProfileName is null && Fields.Count == 0;
}

/// <summary>
/// Inverse of <see cref="XboxToKeyboardConfig"/>: turns the JP-shaped
/// <see cref="KeyboardConfig"/> the UI edits back into Xbox wire operations — the 12-byte
/// records of <c>cmd 0x01</c>, the 32-byte profile name, the single-byte fields
/// (<c>volume_lv</c> and the <c>fast_setting</c> quick-setup bits) and the macro slots of
/// <c>cmd 0x05</c>/<c>cmd 0x06</c>.
/// </summary>
/// <remarks>
/// <para>
/// The UI model only carries the 95 board positions, so the configuration <b>last read from the
/// device</b> is required as the baseline: it supplies the record index of each physical key,
/// the unused <c>+1..+3</c> bytes of every record, the fields the UI does not show, and the
/// "unchanged?" comparison that keeps <c>force:false</c> writing only what the user edited.
/// </para>
/// <para>
/// <b>Evidence.</b> The record shape, the macro region layout and the chunking come from the two
/// captures of the official app (<c>docs/PROTOCOL.md</c> §10/§11). The chosen
/// <see cref="XboxMappingType"/> for a plain keyboard target is <b>inferred</b> from the enum of
/// the reference (<c>0 NONE, 1 KEYCODE, 2 MKEY, 3 MOUSE, 4 MACRO</c>): the captures only prove
/// <c>MKEY</c> for a media target and <c>MACRO</c> for a macro, and the device stores its
/// out-of-the-box mappings as <c>NONE</c> with <c>keyCode == mapping</c>. The native
/// <c>getModeMapping</c> that decides this has not been ported yet.
/// </para>
/// </remarks>
public static class XboxFromKeyboardConfig
{
    /// <summary>First USB-HID usage of the eight modifiers.</summary>
    public const byte ModifierFirst = 0xE0;

    /// <summary>Last USB-HID usage of the eight modifiers.</summary>
    public const byte ModifierLast = 0xE7;

    /// <summary>
    /// <see cref="XboxMappingType"/> for a target id, or false when this layer cannot express
    /// that target yet.
    /// </summary>
    /// <param name="target">Wire id of the target.</param>
    /// <param name="kind">
    /// Category the UI picked the target from, when known. Some ids mean two things
    /// (<c>0xE2</c> is Left Alt or NoVoice, <c>0xE9</c> is the middle mouse button or VoiceUp,
    /// <c>0x6F</c>/<c>0x70</c> are F20/F21 or the brightness keys) and the category is what tells
    /// them apart; without it the id is refused instead of guessed.
    /// </param>
    public static bool TryTypeForTarget(byte target, MappingTargetKind? kind, out XboxMappingType type)
    {
        if (target == 0)
        {
            type = XboxMappingType.None;
            return true;
        }
        if (kind is { } chosen && XboxIdTable.TryGet(target, chosen, out XboxIdEntry byKind))
        {
            type = byKind.Type;
            return true;
        }
        if (kind is null && XboxIdTable.IsAmbiguous(target))
        {
            // The device uses this id for more than one function (0xE2 is Left Alt or NoVoice,
            // 0x6F/0x70 are F20/F21 or the brightness keys) and nothing told us which one.
            type = XboxMappingType.None;
            return false;
        }
        if (IsDeviceKeyId(target) || target is >= ModifierFirst and <= ModifierLast)
        {
            type = XboxMappingType.KeyCode;
            return true;
        }
        if (XboxIdTable.TryUniqueType(target, out type)) return true;

        type = XboxMappingType.None;
        return false;
    }

    /// <summary>Same as <see cref="TryTypeForTarget(byte, MappingTargetKind?, out XboxMappingType)"/> without the hint.</summary>
    public static bool TryTypeForTarget(byte target, out XboxMappingType type)
        => TryTypeForTarget(target, null, out type);

    /// <summary>
    /// True for an id the device knows as a key of this board (<c>KeyboardLayout.PositionKeyId</c>,
    /// the ids the original software addresses with <c>setMappingIndex</c>). Media, mouse and
    /// vendor ids are deliberately <b>not</b> in this set.
    /// </summary>
    public static bool IsDeviceKeyId(byte id) => DeviceKeyIds.Contains(id);

    /// <summary>
    /// The 95 board-key ids translated to the Xbox id space (the modifiers become the HID usages
    /// <c>0xE0..0xE7</c>).
    /// </summary>
    public static IReadOnlyList<byte> DeviceKeyIds { get; } =
        KeyboardLayout.PositionKeyId.Select(XboxToKeyboardConfig.XboxKeyCodeForJpId).Distinct().ToArray();

    /// <summary>
    /// Builds one 12-byte record: <c>keyCode</c> at +0, <paramref name="padding"/> at +1..+3,
    /// <c>mapping</c> (u32 LE) at +4, <c>mappingType</c> (u32 LE) at +8.
    /// </summary>
    public static byte[] BuildRecord(byte keyCode, uint mapping, XboxMappingType type, ReadOnlySpan<byte> padding)
    {
        var record = new byte[XboxWire.MappingSize];
        record[0] = keyCode;
        for (int i = 0; i < 3; i++)
            record[1 + i] = i < padding.Length ? padding[i] : (byte)0;
        record[4] = (byte)(mapping & 0xFF);
        record[5] = (byte)((mapping >> 8) & 0xFF);
        record[6] = (byte)((mapping >> 16) & 0xFF);
        record[7] = (byte)((mapping >> 24) & 0xFF);
        record[8] = (byte)((uint)type & 0xFF);
        record[9] = (byte)(((uint)type >> 8) & 0xFF);
        record[10] = (byte)(((uint)type >> 16) & 0xFF);
        record[11] = (byte)(((uint)type >> 24) & 0xFF);
        return record;
    }

    /// <summary>Encodes a name into the on-wire <b>UTF-16 big-endian</b> field (NUL padded).</summary>
    public static byte[] EncodeNameBigEndian(string name, int bytes)
    {
        var field = new byte[bytes];
        for (int i = 0; i < name.Length && i * 2 + 1 < bytes; i++)
        {
            field[i * 2] = (byte)(name[i] >> 8);
            field[i * 2 + 1] = (byte)(name[i] & 0xFF);
        }
        return field;
    }

    /// <summary>Converts the UI's macro actions into the 4-byte wire steps (clamped to 1008).</summary>
    public static List<XboxMacroStep> BuildSteps(IEnumerable<MacroAction> actions)
    {
        var steps = new List<XboxMacroStep>();
        foreach (MacroAction action in actions)
        {
            if (steps.Count >= XboxWire.MacroMaxSteps) break;
            switch (action.Kind)
            {
                case MacroActionKind.KeyDown:
                    steps.Add(new XboxMacroStep { KeyState = 1, KeyCode = action.Usage });
                    break;
                case MacroActionKind.KeyUp:
                    steps.Add(new XboxMacroStep { KeyState = 2, KeyCode = action.Usage });
                    break;
                case MacroActionKind.Time:
                    steps.Add(new XboxMacroStep { KeyState = 3, Timer = action.DelayMs });
                    break;
            }
        }
        return steps;
    }

    /// <summary>
    /// Builds the 48-byte <c>KEYBOARD_MACROS_INFO</c>. Bytes the UI does not model are taken
    /// from <paramref name="baseline"/>'s raw info (or the profile flag for a brand-new slot).
    /// </summary>
    public static byte[] BuildMacroInfo(MacroSlot slot, byte hostKeyCode, int stepCount, XboxMacroSlot? baseline)
    {
        var info = new byte[XboxWire.MacroInfoSize];
        if (baseline?.RawInfo is { Length: >= XboxWire.MacroInfoSize } raw)
            Array.Copy(raw, info, XboxWire.MacroInfoSize);
        else
            WriteU32(info, XboxWire.MacroInfo.Flag, XboxWire.MacroFlagDefault);

        info[XboxWire.MacroInfo.KeyCode] = hostKeyCode;
        Array.Copy(EncodeNameBigEndian(slot.Name, XboxWire.MacroInfo.NameBytes), 0, info, XboxWire.MacroInfo.Name, XboxWire.MacroInfo.NameBytes);
        WriteU16(info, XboxWire.MacroInfo.MaxStep, (ushort)stepCount);
        WriteU16(info, XboxWire.MacroInfo.Interval, slot.IntervalMs);
        info[XboxWire.MacroInfo.Repeat] = slot.Cycles;
        return info;
    }

    /// <summary>Flattens steps into the wire bytes (<c>keyState, keyCode, timer u16 LE</c>).</summary>
    public static byte[] BuildStepBytes(IReadOnlyList<XboxMacroStep> steps)
    {
        var bytes = new byte[steps.Count * XboxWire.MacroStepSize];
        for (int i = 0; i < steps.Count; i++)
        {
            int o = i * XboxWire.MacroStepSize;
            bytes[o] = steps[i].KeyState;
            bytes[o + 1] = steps[i].KeyCode;
            bytes[o + 2] = (byte)(steps[i].Timer & 0xFF);
            bytes[o + 3] = (byte)(steps[i].Timer >> 8);
        }
        return bytes;
    }

    /// <summary>
    /// Builds the write plan for <paramref name="config"/> against
    /// <paramref name="baseline"/> (the configuration last read from the device).
    /// </summary>
    /// <param name="force">
    /// When false only what differs from the baseline is planned (what the original app does);
    /// when true every known board position and every non-empty macro slot is written.
    /// </param>
    /// <param name="targetKinds">
    /// Optional category per board position, as the UI picker knows it. Needed only to
    /// disambiguate the ids the device shares between keyboard and media/mouse (see
    /// <see cref="TryTypeForTarget(byte, MappingTargetKind?, out XboxMappingType)"/>).
    /// </param>
    public static XboxWritePlan Plan(KeyboardConfig config, XboxConfig baseline, bool force = false,
        IReadOnlyDictionary<int, MappingTargetKind>? targetKinds = null)
    {
        var mappings = new List<XboxMappingChange>();
        var macros = new List<XboxMacroChange>();
        var warnings = new List<string>();

        Dictionary<byte, int> indexByKeyCode = IndexByKeyCode(baseline);

        // Which mapping record each macro slot must be referenced from.
        var macroByIndex = new Dictionary<int, int>();
        for (int slot = 0; slot < config.Macros.Length && slot < baseline.Macros.Length; slot++)
        {
            MacroSlot slotData = config.Macros[slot];
            if (slotData.IsEmpty) continue;

            byte host = slotData.HostKey;
            if (host == 0) host = baseline.Macros[slot]?.HostKeyCode ?? 0;
            if (host == 0 || !indexByKeyCode.TryGetValue(host, out int index))
            {
                warnings.Add(Loc.F("w_macro_no_host", slot + 1, host));
                continue;
            }
            macroByIndex[index] = slot;
        }

        // ------------------------------------------------------------- mapping records
        for (int position = 0; position < KeyboardLayout.PositionCount; position++)
        {
            byte keyCode = XboxToKeyboardConfig.XboxKeyCodeForJpId(KeyboardLayout.PositionKeyId[position]);
            if (!indexByKeyCode.TryGetValue(keyCode, out int index))
            {
                if (CountRecords(baseline, keyCode) > 1)
                    warnings.Add(Loc.F("w_dup_record", keyCode));
                continue;
            }

            uint mapping;
            XboxMappingType type;
            byte[]? baselineRecord = RecordOf(baseline, index);
            if (macroByIndex.TryGetValue(index, out int macroSlot))
            {
                mapping = XboxWire.MacroSlotBase(macroSlot);
                type = XboxMappingType.Macro;
            }
            else
            {
                byte target = config.BoardKeys[position];
                // The reference has no wire value for "nothing": its None entry keeps NONE_MAP
                // (0xF3). Map the UI's "unmapped" (0) there so a key with no target is written
                // the way the official software writes it.
                mapping = target == 0 ? XboxIdTable.UnmappedId : target;

                // The official app only recomputes a record's type when the target actually
                // changes; otherwise what the device already holds is kept verbatim. That is
                // what keeps a media mapping the user never touched intact.
                if (baselineRecord is not null && ReadU32(baselineRecord, 4) == mapping &&
                    !CategoryChanged(baselineRecord, position, targetKinds))
                {
                    // Target unchanged: send the device's own record back (type included) only
                    // when the caller asked to rewrite everything.
                    if (force) mappings.Add(new XboxMappingChange(index, keyCode, baselineRecord));
                    continue;
                }

                if (target == keyCode || target == 0 || target == XboxIdTable.UnmappedId)
                {
                    type = XboxMappingType.None;   // identity / cleared, as the app stores them
                }
                else
                {
                    MappingTargetKind? kind = targetKinds is not null && targetKinds.TryGetValue(position, out var picked)
                        ? picked
                        : null;
                    if (!TryTypeForTarget(target, kind, out type))
                    {
                        string why = XboxIdTable.IsAmbiguous(target)
                            ? Loc.T("w_ambiguous")
                            : Loc.T("w_unsupported");
                        warnings.Add(Loc.F("w_key_unwritten", KeyboardLayout.PositionNames[position], keyCode, target, why));
                        continue;
                    }
                }
            }

            byte[] record = BuildRecord(keyCode, mapping, type,
                baselineRecord is null ? ReadOnlySpan<byte>.Empty : baselineRecord.AsSpan(1, 3));
            if (force || !Matches(baseline.Raw, index, record))
                mappings.Add(new XboxMappingChange(index, keyCode, record));
        }

        // ------------------------------------------------------------- Super Buttons
        // Addressed by record index, not by keyCode: the ten of them share 0xF3 on the wire, so the
        // keyCode lookup above would either guess or refuse. This is the same addressing the
        // official software uses and the reason they can be mapped at all (doc §12.4).
        for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal++)
        {
            int index = SuperButtons.WireIndexOf(ordinal);
            int position = SuperButtons.PositionOf(ordinal);
            byte[]? baselineRecord = RecordOf(baseline, index);

            byte target = config.BoardKeys[position];
            uint mapping = target == 0 ? XboxIdTable.UnmappedId : target;

            // "No target" is 0 in the UI model and 0xF3 on the wire; a device record that is
            // either of them already means "unmapped", so an untouched screen writes nothing.
            uint baselineMapping = baselineRecord is null ? 0 : ReadU32(baselineRecord, 4);
            bool unchanged = baselineRecord is not null
                             && (baselineMapping == mapping || (target == 0 && baselineMapping == 0))
                             && !CategoryChanged(baselineRecord, position, targetKinds);

            if (unchanged)
            {
                // Target unchanged: keep the device's own record (type included) unless forced.
                if (force) mappings.Add(new XboxMappingChange(index, SuperButtons.WireKeyCode, baselineRecord!));
                continue;
            }

            XboxMappingType type;
            if (target == 0 || target == XboxIdTable.UnmappedId)
            {
                type = XboxMappingType.None;
            }
            else
            {
                MappingTargetKind? kind = targetKinds is not null && targetKinds.TryGetValue(position, out var picked)
                    ? picked
                    : null;
                if (!TryTypeForTarget(target, kind, out type))
                {
                    warnings.Add(Loc.F("w_super_unwritten", SuperButtons.NameOf(ordinal), target));
                    continue;
                }
            }

            byte[] record = BuildRecord(SuperButtons.WireKeyCode, mapping, type,
                baselineRecord is null ? ReadOnlySpan<byte>.Empty : baselineRecord.AsSpan(1, 3));
            if (force || !Matches(baseline.Raw, index, record))
                mappings.Add(new XboxMappingChange(index, SuperButtons.WireKeyCode, record));
        }

        // ------------------------------------------------------------- macro slots
        for (int slot = 0; slot < config.Macros.Length && slot < baseline.Macros.Length; slot++)
        {
            MacroSlot slotData = config.Macros[slot];
            if (slotData.IsEmpty) continue;
            if (!macroByIndex.ContainsValue(slot)) continue;   // already warned above

            int index = macroByIndex.First(pair => pair.Value == slot).Key;
            byte host = slotData.HostKey;
            if (host == 0) host = baseline.Macros[slot]?.HostKeyCode ?? 0;

            uint slotBase = XboxWire.MacroSlotBase(slot);
            List<XboxMacroStep> steps = BuildSteps(slotData.Actions);
            byte[] info = BuildMacroInfo(slotData, host, steps.Count, baseline.Macros[slot]);
            byte[] mappingRecord = BuildRecord(host, slotBase, XboxMappingType.Macro, PaddingOf(baseline, index));

            bool unchanged = !force
                             && Matches(baseline.Raw, index, mappingRecord)
                             && MacroMatches(baseline.Macros[slot], slotData, steps);
            if (!unchanged)
                macros.Add(new XboxMacroChange(slot, slotBase, index, mappingRecord,
                    info, BuildStepBytes(steps), steps.Count));
        }

        string? name = force || config.ProfileName != baseline.ProfileName ? config.ProfileName : null;

        // ------------------------------------------------------------- single-byte fields
        // The same cmd 0x01 primitive the C3 hardware test validated for volume_lv. Only what
        // differs from the device is planned, so an untouched screen writes nothing.
        var fields = new List<XboxFieldChange>();
        AddField(fields, baseline, XboxWire.VolumeLevelOffset, config.XboxVolumeLevel, force);
        AddField(fields, baseline, XboxWire.FastSettingOffset, config.XboxFastSetting, force);

        // The device is written in ascending record order; the Super Buttons (9, 10, 108..115)
        // are collected after the board keys, so restore the contract here.
        mappings.Sort((a, b) => a.Index.CompareTo(b.Index));

        return new XboxWritePlan(mappings, macros, name, fields, warnings);
    }

    /// <summary>
    /// Plans one single-byte field when it differs from what the device holds (or always, with
    /// <paramref name="force"/>). A field the configuration did not bring is skipped when the
    /// baseline does not reach its offset.
    /// </summary>
    private static void AddField(List<XboxFieldChange> fields, XboxConfig baseline, int offset,
        byte value, bool force)
    {
        if (baseline.Raw.Length < offset + 1) return;
        if (force || baseline.Raw[offset] != value) fields.Add(new XboxFieldChange(offset, value));
    }

    /// <summary>
    /// Index of each physical key inside <c>keyboard_mappings[]</c>. A key whose <c>keyCode</c>
    /// appears more than once is <b>left out</b>: the real device does have repeated key codes
    /// (the ten <c>0xF3</c> records of the Super Buttons, see <see cref="SuperButtons"/>), and
    /// guessing which record a key belongs to would write the wrong one. Those ten are addressed
    /// by record index instead.
    /// </summary>
    public static Dictionary<byte, int> IndexByKeyCode(XboxConfig baseline)
    {
        var index = new Dictionary<byte, int>();
        var ambiguous = new HashSet<byte>();
        for (int i = 0; i < baseline.Mappings.Length; i++)
        {
            XboxMapping mapping = baseline.Mappings[i];
            if (mapping.KeyCode == 0 && mapping.Mapping == 0 && mapping.Type == XboxMappingType.None) continue;
            if (!index.TryAdd(mapping.KeyCode, i)) ambiguous.Add(mapping.KeyCode);
        }
        foreach (byte keyCode in ambiguous) index.Remove(keyCode);
        return index;
    }

    /// <summary>Number of non-empty records the baseline holds for <paramref name="keyCode"/>.</summary>
    private static int CountRecords(XboxConfig baseline, byte keyCode)
    {
        int count = 0;
        foreach (XboxMapping mapping in baseline.Mappings)
        {
            if (mapping.KeyCode == keyCode && !(mapping.Mapping == 0 && mapping.Type == XboxMappingType.None)) count++;
        }
        return count;
    }

    private static ReadOnlySpan<byte> PaddingOf(XboxConfig baseline, int index)
    {
        int offset = XboxWire.MappingBase + index * XboxWire.MappingSize;
        return baseline.Raw.Length >= offset + XboxWire.MappingSize
            ? baseline.Raw.AsSpan(offset + 1, 3)
            : ReadOnlySpan<byte>.Empty;
    }

    /// <summary>The 12 baseline bytes of a record, or null when the raw configuration is absent.</summary>
    private static byte[]? RecordOf(XboxConfig baseline, int index)
    {
        int offset = XboxWire.MappingBase + index * XboxWire.MappingSize;
        if (baseline.Raw.Length < offset + XboxWire.MappingSize) return null;
        return baseline.Raw.AsSpan(offset, XboxWire.MappingSize).ToArray();
    }

    /// <summary>
    /// True when the id is the same but the UI picked another category for it (Left Alt to NoVoice
    /// share 0xE2): only the record's type changes, and the id comparison alone would skip it.
    /// A record whose type says nothing (NONE) never counts as changed.
    /// </summary>
    private static bool CategoryChanged(byte[] baselineRecord, int position,
        IReadOnlyDictionary<int, MappingTargetKind>? targetKinds)
    {
        if (targetKinds is null || !targetKinds.TryGetValue(position, out MappingTargetKind picked)) return false;
        MappingTargetKind? held = XboxToKeyboardConfig.KindOfType((XboxMappingType)ReadU32(baselineRecord, 8));
        if (held is not { } heldKind) return false;
        return !MappingIdSpace.SameKind(heldKind, picked);
    }

    private static uint ReadU32(byte[] buffer, int offset)
        => (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));

    private static bool Matches(byte[] raw, int index, ReadOnlySpan<byte> record)
    {
        int offset = XboxWire.MappingBase + index * XboxWire.MappingSize;
        if (raw.Length < offset + XboxWire.MappingSize) return false;
        return raw.AsSpan(offset, XboxWire.MappingSize).SequenceEqual(record);
    }

    private static bool MacroMatches(XboxMacroSlot? baseline, MacroSlot slot, List<XboxMacroStep> steps)
    {
        if (baseline is null) return false;
        if (baseline.Name != slot.Name || baseline.Interval != slot.IntervalMs || baseline.Repeat != slot.Cycles)
            return false;
        if (baseline.Steps.Count != steps.Count) return false;
        for (int i = 0; i < steps.Count; i++)
        {
            if (baseline.Steps[i].KeyState != steps[i].KeyState ||
                baseline.Steps[i].KeyCode != steps[i].KeyCode ||
                baseline.Steps[i].Timer != steps[i].Timer)
                return false;
        }
        return true;
    }

    private static void WriteU16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>Kept for symmetry with <see cref="XboxToKeyboardConfig"/>; used by the write plan tests.</summary>
    public static string Describe(XboxMappingType type) => type.ToString();
}
