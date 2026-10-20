// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Identity of a <b>physical</b> keyboard behind the per-collection view HidSharp gives of a
/// composite USB device.
/// </summary>
/// <remarks>
/// One Retro 87 exposes several HID collections (mouse, consumer control, keyboard, vendor), each
/// with its own <see cref="DeviceInfo.DevicePath"/>. Enumerating the bus therefore yields the same
/// keyboard many times, which is noise in the picker. All collections of one unit share the USB
/// serial number, so <c>VID:PID:serial</c> is the identity. A device without a serial number has
/// no shared attribute left, so the product name is used as the fallback: two identical units
/// without serials collapse into one entry (they are indistinguishable to the app anyway — the
/// protocol still reaches every collection of both through <c>DeviceMonitor.FindHidDevices</c>).
/// </remarks>
public static class DeviceIdentity
{
    /// <summary>Stable key shared by every HID collection of the same physical keyboard.</summary>
    public static string PhysicalKey(DeviceInfo device)
    {
        string serial = device.SerialNumber?.Trim() ?? string.Empty;
        string shared = serial.Length > 0
            ? "serial=" + serial
            : "name=" + device.ProductName.Trim();

        return $"{device.Vid:X4}:{device.Pid:X4}:{shared.ToUpperInvariant()}";
    }

    /// <summary>True when both entries describe collections of the same physical keyboard.</summary>
    public static bool SamePhysicalDevice(DeviceInfo a, DeviceInfo b)
        => string.Equals(PhysicalKey(a), PhysicalKey(b), StringComparison.Ordinal);

    /// <summary>Keeps the first collection of each physical keyboard, preserving enumeration order.</summary>
    public static IReadOnlyList<DeviceInfo> Distinct(IEnumerable<DeviceInfo> collections)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keyboards = new List<DeviceInfo>();
        foreach (DeviceInfo collection in collections)
            if (seen.Add(PhysicalKey(collection)))
                keyboards.Add(collection);

        // Two units of the same model are indistinguishable by product name alone; append the
        // serial tail only in that case, so a single keyboard keeps its clean name.
        var perName = keyboards
            .GroupBy(d => d.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < keyboards.Count; i++)
        {
            DeviceInfo device = keyboards[i];
            if (perName[device.ProductName] < 2) continue;

            string serial = device.SerialNumber?.Trim() ?? string.Empty;
            if (serial.Length == 0) continue;

            string tail = serial.Length <= 4 ? serial : serial[^4..];
            keyboards[i] = device with { DisplayName = $"{device.ProductName} · {tail}" };
        }

        return keyboards;
    }
}
