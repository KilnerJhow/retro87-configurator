// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Localization;

namespace Retro87.Models;

/// <summary>
/// One selectable remap target: a mapping id (see <see cref="MappingIds"/>) with the name the
/// UI shows and the category it belongs to.
/// </summary>
public sealed record MappingTarget(byte Id, string Name, MappingTargetKind Kind)
{
    /// <summary>Type the official software files the target under (its first dropdown).</summary>
    public TargetCategory Category { get; init; } =
        Kind == MappingTargetKind.None ? TargetCategory.Disable : TargetCategory.Alphanumeric;

    /// <summary>Name shown to the user: plain words for media and mouse targets, the key legend otherwise.</summary>
    public string Label => TargetLabels.Of(Name);

    /// <summary>Position inside the subtype list, in the official software's order.</summary>
    public int Order { get; init; }

    /// <summary>Portuguese label of the category, shown next to the name in the picker.</summary>
    public string KindLabel => Kind switch
    {
        MappingTargetKind.Mouse => Loc.T("kind_mouse"),
        MappingTargetKind.Shortcut => Loc.T("kind_shortcut"),
        MappingTargetKind.Media => Loc.T("kind_media"),
        MappingTargetKind.None => "—",
        _ => Loc.T("kind_keyboard"),
    };

    /// <summary>
    /// What a screen reader announces for a picker row: the peer falls back to the item's text, so
    /// it must read like the row does and never like the record (<c>MappingTarget { Id = … }</c>).
    /// </summary>
    public override string ToString() => $"{Name} ({KindLabel})";
}

/// <summary>
/// The "type" dropdown of the official software, with its English labels. The order of the members
/// is the order of the dropdown.
/// </summary>
public enum TargetCategory
{
    Alphanumeric,
    Function,
    Numpad,
    Navigation,
    Modifiers,
    Symbols,
    MouseFunction,
    ShortCut,
    Disable,
}

public static class TargetCategoryLabels
{
    /// <summary>Plain-words label of the category, in the current language.</summary>
    public static string Of(TargetCategory category) => Loc.T("category_" + category);

    /// <summary>Category of a JP-family id, from the same enum ranges the official software tests.</summary>
    public static TargetCategory OfJpKey(KeyboardEnum key) => key switch
    {
        <= KeyboardEnum.num9 => TargetCategory.Alphanumeric,
        <= KeyboardEnum.F24 => TargetCategory.Function,
        <= KeyboardEnum.num_Enter => TargetCategory.Numpad,
        <= KeyboardEnum.PAUSE => TargetCategory.Navigation,
        <= KeyboardEnum.Menu => TargetCategory.Modifiers,
        <= KeyboardEnum.Question1 => TargetCategory.Symbols,
        <= KeyboardEnum.Right_Roll => TargetCategory.MouseFunction,
        <= KeyboardEnum.BrightUp => TargetCategory.ShortCut,
        _ => TargetCategory.Disable,
    };
}

/// <summary>
/// The list of targets a board key can be remapped to, built from the ids the
/// <see cref="MappingPayloadCodec"/> can encode — i.e. every id with a non-empty wire payload.
/// </summary>
/// <remarks>
/// <para>
/// The set is exactly the ids whose payload is non-empty: the extra board keys (KA/KB/K1..K8)
/// and every other id <c>getuint</c> leaves zeroed are not selectable, because the device has
/// no wire value for them. <see cref="MappingIds.NULL_MAP"/> ("unmapped") is offered first so a
/// key can be cleared.
/// </para>
/// <para>
/// <see cref="All"/> is ordered by category (<see cref="MappingTargetKind"/>) and then by id
/// inside the category, so a picker bound to it reads grouped even though the ComboBox itself
/// has no group headers.
/// </para>
/// </remarks>
public static class MappingTargetCatalog
{
    /// <summary>The "unmapped" target (id 0), always the first entry.</summary>
    public static MappingTarget Unmapped { get; } =
        new(MappingIds.NULL_MAP, MappingIdNames.Describe(MappingIds.NULL_MAP), MappingTargetKind.None);

    /// <summary>Every selectable target: unmapped, then keyboard, shortcut and mouse.</summary>
    public static IReadOnlyList<MappingTarget> All { get; } = BuildAll();

    private static readonly Dictionary<byte, MappingTarget> ById = All.ToDictionary(t => t.Id);

    /// <summary>
    /// Target for <paramref name="id"/>. Ids outside the catalog (which the device cannot
    /// produce) get a hex placeholder rather than being rewritten.
    /// </summary>
    public static MappingTarget Find(byte id)
        => ById.TryGetValue(id, out MappingTarget? target)
            ? target
            : new MappingTarget(id, $"0x{id:X2}", MappingTargetKind.None);

    private static IReadOnlyList<MappingTarget> BuildAll()
    {
        var keyboard = new List<MappingTarget>();
        var shortcut = new List<MappingTarget>();
        var mouse = new List<MappingTarget>();

        // TargetIds is ascending, so each bucket keeps the original table order (A..Z, 1..0, ...).
        foreach (byte id in MappingPayloadCodec.TargetIds)
        {
            KeyboardEnum key = MappingTables.ToKeyboardEnum(id);
            var target = new MappingTarget(id, MappingIdNames.Describe(id), MappingPayloadCodec.KindOf(id))
            {
                Category = TargetCategoryLabels.OfJpKey(key),
                Order = (int)key,
            };
            switch (target.Kind)
            {
                case MappingTargetKind.Shortcut: shortcut.Add(target); break;
                case MappingTargetKind.Mouse: mouse.Add(target); break;
                default: keyboard.Add(target); break;
            }
        }

        var all = new List<MappingTarget>(keyboard.Count + shortcut.Count + mouse.Count + 1) { Unmapped };
        all.AddRange(keyboard);
        all.AddRange(shortcut);
        all.AddRange(mouse);
        return all;
    }
}

/// <summary>One entry of the "type" dropdown, with the label the official software uses.</summary>
public sealed record CategoryOption(TargetCategory Category, string Label)
{
    /// <summary>What a screen reader announces for the picker row.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// Plain words for the targets the official software names with codes ("NoVoice", "Scroll_Up").
/// Keys keep their legend ("Esc", "F5"), which is already what the user sees on the keyboard.
/// </summary>
public static class TargetLabels
{
    public static string Of(string name)
    {
        string key = "target_" + name;
        string text = Loc.T(key);
        return text == key ? name : text;
    }
}
