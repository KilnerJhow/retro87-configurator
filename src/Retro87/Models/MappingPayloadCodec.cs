// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Converts between the raw mapping value exchanged with the keyboard and the single-byte
/// <c>*_MAP</c> id used by the UI and by <see cref="MappingTables"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Write direction</b> — the id becomes a <see cref="KeyboardEnum"/>, which becomes a
/// 5-byte value whose bytes 0..3 (5 for mouse) are copied into the command frame; the mouse
/// variant copies one byte more.
/// </para>
/// <para>
/// <b>Read direction</b> — the value the device returns is prefixed by a <b>transport byte</b>:
/// the five payload bytes are <c>button[1..5]</c> and <c>button[0]</c> is <c>7</c> (keyboard),
/// <c>12</c> (shortcut) or <c>1</c> (mouse). The transport disambiguates ids that share a
/// payload — e.g. the LAlt branch is <c>button[0] == 7 &amp;&amp; button[1] == 226</c> while the
/// NoVoice branch is <c>button[0] == 12 &amp;&amp; button[1] == 226</c> for the very same
/// payload: <see cref="MappingIds.LALT_MAP"/> (102) and <see cref="MappingIds.NoVoice_MAP"/>
/// (151) both encode <c>E2 00 00 00 00</c>.
/// </para>
/// <para>
/// An all-zero payload is "no mapping" and always decodes to <see cref="MappingIds.NULL_MAP"/>.
/// </para>
/// <para>
/// With the transport taken into account no two ids share one lookup key (verified by
/// <c>Retro87 --selftest</c>), so the decode is exact.
/// </para>
/// </remarks>
public static class MappingPayloadCodec
{
    /// <summary>Number of value bytes compared/transported (native <c>byte[5]</c>).</summary>
    public const int PayloadLength = 5;

    /// <summary>Transport byte the device reports/expects for a plain keyboard remap.</summary>
    public const byte TransportKeyboard = 7;

    /// <summary>Transport byte for a media/shortcut action.</summary>
    public const byte TransportShortcut = 12;

    /// <summary>Transport byte for a mouse action.</summary>
    public const byte TransportMouse = 1;

    private static readonly Dictionary<ulong, byte> PayloadToId = BuildPayloadToId();
    private static readonly Dictionary<ulong, byte> TransportPayloadToId = BuildTransportPayloadToId();

    /// <summary>
    /// Every mapping id with a non-empty wire payload, ascending by id. This is the set the
    /// UI can offer as remap targets (see <see cref="MappingTargetCatalog"/>); ids whose
    /// payload is all zeros (the extra board keys, unmapped) are excluded.
    /// </summary>
    public static IReadOnlyList<byte> TargetIds { get; } = BuildTargetIds();

    /// <summary>
    /// Decodes the transport byte and the five value bytes into a mapping id
    /// (0 when unknown/unmapped).
    /// </summary>
    /// <param name="transport">Transport byte the device reported (<c>button[0]</c>).</param>
    /// <param name="payload">The five value bytes (<c>button[1..5]</c>).</param>
    public static byte DecodeId(byte transport, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < PayloadLength) return MappingIds.NULL_MAP;

        ulong packed = Pack(payload);
        if (packed == 0) return MappingIds.NULL_MAP;

        if (TransportPayloadToId.TryGetValue(((ulong)transport << 40) | packed, out byte exact)) return exact;

        // Fallback for a transport byte the device did not report as expected.
        return PayloadToId.TryGetValue(packed, out byte id) ? id : MappingIds.NULL_MAP;
    }

    /// <summary>Decodes the five value bytes without a transport byte.</summary>
    public static byte DecodeId(ReadOnlySpan<byte> payload) => DecodeId(0, payload);

    /// <summary>Returns the five value bytes the device stores for <paramref name="mappingId"/>.</summary>
    public static byte[] EncodePayload(byte mappingId)
        => MappingTables.GetUint(MappingTables.ToKeyboardEnum(mappingId));

    /// <summary>Category of a mapping id, straight from <c>the mapping classifier</c>'s branches.</summary>
    public static MappingTargetKind KindOf(byte mappingId) => MappingTables.TransportFor(mappingId) switch
    {
        KeyboardTransport.Mouse => MappingTargetKind.Mouse,
        KeyboardTransport.Shortcut => MappingTargetKind.Shortcut,
        _ => MappingTargetKind.Keyboard,
    };

    /// <summary>Transport byte the device stores for <paramref name="mappingId"/>.</summary>
    public static byte TransportByteOf(byte mappingId) => MappingTables.TransportFor(mappingId) switch
    {
        KeyboardTransport.Mouse => TransportMouse,
        KeyboardTransport.Shortcut => TransportShortcut,
        _ => TransportKeyboard,
    };

    private static ulong Pack(ReadOnlySpan<byte> p)
        => p[0]
         | ((ulong)p[1] << 8)
         | ((ulong)p[2] << 16)
         | ((ulong)p[3] << 24)
         | ((ulong)p[4] << 32);

    private static Dictionary<ulong, byte> BuildPayloadToId()
    {
        var map = new Dictionary<ulong, byte>();
        foreach ((byte id, ulong packed) in EnumerateIds())
            map.TryAdd(packed, id);   // lowest id wins
        return map;
    }

    private static Dictionary<ulong, byte> BuildTransportPayloadToId()
    {
        var map = new Dictionary<ulong, byte>();
        foreach ((byte id, ulong packed) in EnumerateIds())
            map.TryAdd(((ulong)TransportByteOf(id) << 40) | packed, id);
        return map;
    }

    /// <summary>
    /// Every id with a non-empty payload, paired with its packed value bytes.
    /// </summary>
    private static IEnumerable<(byte Id, ulong Packed)> EnumerateIds()
    {
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            byte id = (byte)raw;
            KeyboardEnum key = MappingTables.ToKeyboardEnum(id);
            if (key is KeyboardEnum.None or KeyboardEnum.empty) continue;

            byte[] value = MappingTables.GetUint(key);
            if (value.Length < PayloadLength) continue;

            ulong packed = Pack(value);
            if (packed == 0) continue;   // all-zero payload means "unmapped"
            yield return (id, packed);
        }
    }

    private static IReadOnlyList<byte> BuildTargetIds()
    {
        var ids = new List<byte>();
        foreach ((byte id, _) in EnumerateIds())
            ids.Add(id);
        return ids;
    }
}
