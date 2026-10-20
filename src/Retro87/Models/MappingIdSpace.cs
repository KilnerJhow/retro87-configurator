// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// How one keyboard family names its mapping ids and which targets the UI may offer.
/// </summary>
/// <remarks>
/// <para>
/// The two families share most ids (USB-HID usages for letters, digits, function keys, navigation
/// and modifiers) but they are <b>not</b> the same id space: the JP family numbers the eight
/// modifiers 100..107 and the Xbox family uses their HID usages <c>0xE0..0xE7</c>; above the
/// keyboard range the Xbox family has its own media/mouse ids (<c>Next</c> = <c>0xB5</c>,
/// <c>Play</c> = <c>0xCD</c>, <c>Scroll_Up</c> = <c>0xEE</c>, …) that the JP tables do not know.
/// </para>
/// <para>
/// So the picker and the names must follow the connected family, otherwise the user is offered
/// ids the device would store as something else (the very reason the Xbox write path refuses ids
/// it cannot classify — see <see cref="XboxFromKeyboardConfig.TryTypeForTarget(byte, MappingTargetKind?, out XboxMappingType)"/>).
/// </para>
/// </remarks>
public sealed class MappingIdSpace
{
    private readonly Func<byte, MappingTarget> _find;
    private readonly bool _idsAreUsages;

    private MappingIdSpace(IReadOnlyList<MappingTarget> targets, Func<byte, MappingTarget> find,
        IReadOnlyList<byte> defaultTargets, bool idsAreUsages = false)
    {
        Targets = targets;
        _find = find;
        DefaultTargets = defaultTargets;
        _idsAreUsages = idsAreUsages;
    }

    /// <summary>Targets the picker offers, ordered by category.</summary>
    public IReadOnlyList<MappingTarget> Targets { get; }

    private CategoryOption[]? _categories;
    private readonly Dictionary<TargetCategory, MappingTarget[]> _subTargets = new();

    /// <summary>
    /// The "type" dropdown's items, one shared list per family. Every key's side panel uses the very
    /// same instances: with a list per key, moving the panel to another key replaced the list under
    /// the ComboBox, which rewrote the previous selection into the new key.
    /// </summary>
    public IReadOnlyList<CategoryOption> Categories => _categories ??= Targets
        .Select(t => t.Category).Distinct().OrderBy(c => c)
        .Select(c => new CategoryOption(c, TargetCategoryLabels.Of(c))).ToArray();

    /// <summary>The language is per process; the screen tests switch language and ask for the names again.</summary>
    internal void ForgetTranslatedTexts() => _categories = null;

    /// <summary>The "subtype" dropdown's items for a type, shared like <see cref="Categories"/>.</summary>
    public IReadOnlyList<MappingTarget> SubTargetsOf(TargetCategory category)
    {
        if (category == TargetCategory.Disable) return Array.Empty<MappingTarget>();
        if (!_subTargets.TryGetValue(category, out MappingTarget[]? list))
            _subTargets[category] = list = Targets.Where(t => t.Category == category).OrderBy(t => t.Order).ToArray();
        return list;
    }

    /// <summary>
    /// Target each of the 95 positions maps to out of the box, in this family's ids: what
    /// "modified" and "restore the default" compare against.
    /// </summary>
    public IReadOnlyList<byte> DefaultTargets { get; }

    /// <summary>Target for an id ("—" when it is unmapped, a hex placeholder when unnamed).</summary>
    public MappingTarget Find(byte id) => _find(id);

    /// <summary>
    /// Target for an id when the device also told its category: the Xbox family shares ids between
    /// categories, so the id alone cannot say whether <c>0xE2</c> is Left Alt or NoVoice.
    /// </summary>
    public MappingTarget Find(byte id, MappingTargetKind? kind)
    {
        if (kind is { } wanted)
        {
            MappingTarget? exact = Targets.FirstOrDefault(t => t.Id == id && SameKind(t.Kind, wanted));
            if (exact is not null) return exact;
        }
        return Find(id);
    }

    public static bool SameKind(MappingTargetKind a, MappingTargetKind b)
        => (a == MappingTargetKind.Shortcut ? MappingTargetKind.Media : a)
           == (b == MappingTargetKind.Shortcut ? MappingTargetKind.Media : b);

    /// <summary>True when two targets are the same function (same id and same category).</summary>
    public static bool SameTarget(MappingTarget a, MappingTarget b) => a.Id == b.Id && SameKind(a.Kind, b.Kind);

    /// <summary>Name shown for an id.</summary>
    public string Describe(byte id) => Find(id).Name;

    /// <summary>
    /// Mapping id that produces <paramref name="usage"/> for a press of <paramref name="kind"/>.
    /// </summary>
    /// <remarks>
    /// The Xbox family stores HID usages directly, so the usage is already the id. The JP family
    /// numbers media and mouse targets in its own table (<c>Play</c> = 152, <c>Next</c> = 154,
    /// <c>Left_Click</c> = 118) and numbers the modifiers 100..107, so the id is found by looking
    /// at the payload the family stores for each candidate. <see cref="MappingTargetKind.Media"/>
    /// and <see cref="MappingTargetKind.Shortcut"/> are the same category here.
    /// </remarks>
    public byte IdForUsage(byte usage, MappingTargetKind kind)
    {
        if (_idsAreUsages) return usage;

        MappingTargetKind wanted = Normalize(kind);
        if (wanted == MappingTargetKind.Mouse && JpMouseIds.TryGetValue(usage, out byte mouseId))
            return mouseId;

        foreach (byte id in MappingPayloadCodec.TargetIds)
        {
            if (Normalize(MappingPayloadCodec.KindOf(id)) != wanted) continue;

            byte[] payload = MappingPayloadCodec.EncodePayload(id);
            bool matches = wanted switch
            {
                // Keys store the usage in the second byte; modifiers in the first.
                MappingTargetKind.Keyboard => payload[0] == usage || payload[1] == usage,
                _ => payload[0] == usage,
            };
            if (matches) return id;
        }

        return usage;
    }

    private static MappingTargetKind Normalize(MappingTargetKind kind)
        => kind == MappingTargetKind.Shortcut ? MappingTargetKind.Media : kind;

    /// <summary>Mouse ids of the JP family, keyed by the Xbox id the HID decoder reports.</summary>
    private static readonly Dictionary<byte, byte> JpMouseIds = new()
    {
        [0xE8] = MappingIds.LEFT_CLICK,      // left button
        [0xEA] = MappingIds.RIGHT_CLICK,     // right button
        [0xE9] = MappingIds.MIDDLE_CLICK,    // middle button
        [0xEB] = MappingIds.FIVE_CLICK,      // fifth button
        [0xEC] = MappingIds.FOUR_CLICK,      // fourth button
        [0xED] = MappingIds.TWO_TIME,        // double click
        [0xEE] = MappingIds.UP_ROLL,
        [0xEF] = MappingIds.DOWN_ROLL,
        [0xF0] = MappingIds.LEFT_ROLL,
        [0xF1] = MappingIds.RIGHT_ROLL,
    };

    /// <summary>The JP family (Retro 87, PID <c>0x5200/0x5201</c>): the original JP ids.</summary>
    public static MappingIdSpace Jp { get; } = new(MappingTargetCatalog.All, MappingTargetCatalog.Find, KeyboardLayout.DefaultMappingId);

    /// <summary>The Xbox family (Retro 87 "X", PID <c>0x2028/0x202E</c>): its own id table.</summary>
    public static MappingIdSpace Xbox { get; } = BuildXbox();

    private static MappingIdSpace BuildXbox()
    {
        var targets = new List<MappingTarget>(XboxIdTable.All.Count + 1)
        {
            new(XboxIdTable.UnmappedId, "—", MappingTargetKind.None),
        };
        foreach (XboxIdEntry entry in XboxIdTable.Catalog)
        {
            (TargetCategory category, int order) = XboxIdTable.CategoryOf(entry);
            targets.Add(new MappingTarget(entry.Id, entry.Name, entry.Kind) { Category = category, Order = order });
        }

        var byId = new Dictionary<byte, MappingTarget>();
        foreach (MappingTarget target in targets)
            byId.TryAdd(target.Id, target);

        MappingTarget Find(byte id)
        {
            if (id == 0 || id == XboxIdTable.UnmappedId) return targets[0];
            if (byId.TryGetValue(id, out MappingTarget? target)) return target;
            return new MappingTarget(id, $"id 0x{id:X2}", MappingTargetKind.Keyboard);
        }

        return new MappingIdSpace(targets, Find, XboxToKeyboardConfig.DefaultBoardKeys, idsAreUsages: true);
    }
}
