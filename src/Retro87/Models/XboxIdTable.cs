// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// One id of the Xbox (Retro 87 "X") id space: the wire value, the name the official software
/// shows and the <see cref="XboxMappingType"/> the device expects for it.
/// </summary>
/// <remarks>
/// The name and the type are not decorative: the same wire id can mean different things —
/// <c>0xE2</c> is <i>Left Alt</i> as a KEYCODE and <i>NoVoice</i> as a MKEY, <c>0xE9</c> is the
/// middle mouse button as MOUSE and <i>VoiceUp</i> as MKEY, and <c>0x6F</c>/<c>0x70</c> are F20/F21
/// as KEYCODE and <i>BrightUp</i>/<i>BrightLow</i> as MKEY. Consumers that only have the id must
/// therefore treat <see cref="IsAmbiguous"/> ids carefully (see <see cref="TryUniqueType"/>).
/// </remarks>
public sealed record XboxIdEntry(byte Id, string Name, XboxMappingType Type)
{
    /// <summary>Category the id belongs to, in the UI's vocabulary.</summary>
    public MappingTargetKind Kind => Type switch
    {
        XboxMappingType.MKey => MappingTargetKind.Media,
        XboxMappingType.Mouse => MappingTargetKind.Mouse,
        XboxMappingType.None => MappingTargetKind.None,
        _ => MappingTargetKind.Keyboard,
    };
}

/// <summary>
/// Xbox wire ids (<see cref="XboxMapping"/>'s <c>mapping</c> field) with names and mapping types,
/// documented in <c>docs/PROTOCOL.md</c> §12.
/// </summary>
/// <remarks>
/// <para>
/// The names, ids and types are interoperability data. They were spot-checked against the real
/// device: the media ids of the first capture (<c>0xB5</c>, <c>0xB6</c>, <c>0xCD</c>) come out as
/// <i>Next</i>, <i>Last</i> and <i>Play</i> — all MKEY, exactly what the device showed.
/// </para>
/// <para>
/// Ids above <c>0xFF</c> (a matrix-code table used by the symbol keys of some layouts) are
/// deliberately absent: they do not fit the byte the wire field carries for this family, and the
/// app already knows the real ids of the keyboard keys through <see cref="KeyboardLayout"/>.
/// </para>
/// </remarks>
public static partial class XboxIdTable
{
    /// <summary>
    /// The <c>None</c> entry of the reference: <c>setMapping</c> has no case for it, so it keeps
    /// <c>NONE_MAP</c> (<c>0xF3</c>). Also the value the device stores as the key code of the ten
    /// extra keys (KA, KB, K1..K8), which is why they are not written by this port.
    /// </summary>
    public const byte UnmappedId = 0xF3;

    private static readonly XboxIdEntry[] Entries = Generated();

    private static readonly Dictionary<byte, XboxIdEntry[]> ById = BuildById();

    /// <summary>Every id of the family, ordered by type then id.</summary>
    public static IReadOnlyList<XboxIdEntry> All => Entries;

    /// <summary>
    /// Targets a key can be remapped to, keyboard first (the ids the port also knows through
    /// <see cref="KeyboardLayout"/>), then media and mouse — the order the UI picker wants.
    /// </summary>
    public static IReadOnlyList<XboxIdEntry> Catalog { get; } = BuildCatalog();

    /// <summary>Type and subtype position of an entry in the official software's dropdowns.</summary>
    public static (TargetCategory Category, int Order) CategoryOf(XboxIdEntry entry)
        => Categories.TryGetValue((entry.Id, entry.Type), out var found) ? found : (TargetCategory.Symbols, int.MaxValue);

    /// <summary>True when the same id means more than one thing and only the type tells them apart.</summary>
    public static bool IsAmbiguous(byte id) => ById.TryGetValue(id, out XboxIdEntry[]? entries) && entries.Length > 1;

    /// <summary>Resolves an id that means exactly one thing.</summary>
    public static bool TryGet(byte id, out XboxIdEntry entry)
    {
        if (ById.TryGetValue(id, out XboxIdEntry[]? entries) && entries.Length == 1)
        {
            entry = entries[0];
            return true;
        }
        entry = default!;
        return false;
    }

    /// <summary>Resolves an id for a known category (the id+kind the UI picker hands over).</summary>
    public static bool TryGet(byte id, MappingTargetKind kind, out XboxIdEntry entry)
    {
        if (ById.TryGetValue(id, out XboxIdEntry[]? entries))
        {
            foreach (XboxIdEntry candidate in entries)
            {
                if (candidate.Kind == kind)
                {
                    entry = candidate;
                    return true;
                }
            }
        }
        entry = default!;
        return false;
    }

    /// <summary>Mapping type of an id that means exactly one thing.</summary>
    public static bool TryUniqueType(byte id, out XboxMappingType type)
    {
        if (TryGet(id, out XboxIdEntry entry))
        {
            type = entry.Type;
            return true;
        }
        type = XboxMappingType.None;
        return false;
    }

    /// <summary>Display name of an id ("—" for unmapped, hex when the id is not in the table).</summary>
    public static string Describe(byte id)
    {
        if (id == 0) return "—";
        if (TryGet(id, out XboxIdEntry entry)) return entry.Name;
        if (ById.TryGetValue(id, out XboxIdEntry[]? entries)) return entries[0].Name;
        return $"id 0x{id:X2}";
    }

    /// <summary>Display name of an id in a known category (uses the type when the id is shared).</summary>
    public static string Describe(byte id, XboxMappingType type)
    {
        if (id == 0) return "—";
        if (ById.TryGetValue(id, out XboxIdEntry[]? entries))
        {
            foreach (XboxIdEntry entry in entries)
            {
                if (entry.Type == type) return entry.Name;
            }
        }
        return Describe(id);
    }

    /// <summary>Type of the entries that share <paramref name="id"/>, for diagnostics.</summary>
    public static IReadOnlyList<XboxMappingType> TypesOf(byte id)
        => ById.TryGetValue(id, out XboxIdEntry[]? entries)
            ? entries.Select(e => e.Type).ToArray()
            : Array.Empty<XboxMappingType>();

    private static Dictionary<byte, XboxIdEntry[]> BuildById()
    {
        var map = new Dictionary<byte, XboxIdEntry[]>();
        foreach (IGrouping<byte, XboxIdEntry> group in Entries.GroupBy(e => e.Id))
            map[group.Key] = group.OrderBy(e => e.Type).ToArray();
        return map;
    }

    private static IReadOnlyList<XboxIdEntry> BuildCatalog()
    {
        var list = new List<XboxIdEntry>(Entries.Length);
        foreach (MappingTargetKind kind in new[]
                 {
                     MappingTargetKind.Keyboard, MappingTargetKind.Media,
                     MappingTargetKind.Shortcut, MappingTargetKind.Mouse,
                 })
        {
            foreach (XboxIdEntry entry in Entries.OrderBy(e => e.Id))
            {
                if (entry.Kind == kind) list.Add(entry);
            }
        }
        return list;
    }
}
