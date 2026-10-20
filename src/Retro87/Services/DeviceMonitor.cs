// SPDX-License-Identifier: GPL-3.0-or-later

using HidSharp;
using Retro87.Models;

namespace Retro87.Services;

/// <summary>
/// Watches the USB bus for 8BitDo Retro keyboards (VID 0x2DC8) and publishes the
/// current set of matching HID devices. Mirrors the role of the native
/// <c>FindHidPath</c> / <c>Open_Hid</c> enumeration, but keeps every candidate
/// interface so the protocol layer can pick the collection that accepts 64-byte
/// feature reports (see the open question about the vendor collection).
/// </summary>
public sealed class DeviceMonitor : IDisposable
{
    private readonly TimeSpan _pollInterval;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private IReadOnlyList<DeviceInfo> _devices = Array.Empty<DeviceInfo>();
    private IReadOnlyList<HidDevice> _hidDevices = Array.Empty<HidDevice>();
    private bool _disposed;

    public DeviceMonitor(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised whenever the set of matching devices changes.</summary>
    public event EventHandler<IReadOnlyList<DeviceInfo>>? DevicesChanged;

    /// <summary>Currently attached Retro keyboards (may be empty).</summary>
    public IReadOnlyList<DeviceInfo> Devices
    {
        get { lock (_gate) return _devices; }
    }

    /// <summary>The device the UI is currently talking to, or <c>null</c>.</summary>
    public DeviceInfo? Selected { get; private set; }

    /// <summary>Starts polling for device changes (also performs an immediate scan).</summary>
    public void Start()
    {
        Poll();
        _timer.Change(_pollInterval, _pollInterval);
    }

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void Select(DeviceInfo? device) => Selected = device;

    /// <summary>Resolves the underlying HidSharp device for a path (used by the transport).</summary>
    public HidDevice? FindHidDevice(string devicePath)
    {
        lock (_gate)
        {
            foreach (var device in _hidDevices)
                if (string.Equals(device.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase))
                    return device;
        }
        return null;
    }

    /// <summary>All enumerated HID devices with the given VID/PID (a device may expose several collections).</summary>
    public IReadOnlyList<HidDevice> FindHidDevices(int vid, int pid)
    {
        lock (_gate)
        {
            return _hidDevices.Where(d => d.VendorID == vid && d.ProductID == pid).ToList();
        }
    }

    private void Poll()
    {
        if (_disposed) return;

        List<HidDevice> found = new();
        foreach (int pid in KnownDevices.SupportedPids)
        {
            try
            {
                foreach (var device in DeviceList.Local.GetHidDevices(KnownDevices.EightBitDoVid, pid))
                    found.Add(device);
            }
            catch (Exception)
            {
                // A transient enumeration failure must not tear the monitor down.
            }
        }

        var infos = found.Select(ToInfo).ToList();

        bool changed;
        lock (_gate)
        {
            changed = !SameDevices(_devices, infos);
            _devices = infos;
            _hidDevices = found;
            if (Selected is not null && !infos.Any(d => d.DevicePath == Selected.DevicePath))
                Selected = null;
        }

        if (changed)
            DevicesChanged?.Invoke(this, infos);
    }

    private static bool SameDevices(IReadOnlyList<DeviceInfo> a, IReadOnlyList<DeviceInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].DevicePath, b[i].DevicePath, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static DeviceInfo ToInfo(HidDevice device)
    {
        string product;
        try { product = device.GetProductName() ?? "8BitDo Retro Keyboard"; }
        catch (Exception) { product = "8BitDo Retro Keyboard"; }

        string? serial = null;
        try { serial = device.GetSerialNumber(); }
        catch (Exception) { /* serial number is optional */ }

        return new DeviceInfo(
            (ushort)device.VendorID,
            (ushort)device.ProductID,
            product,
            serial,
            device.DevicePath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
    }
}
