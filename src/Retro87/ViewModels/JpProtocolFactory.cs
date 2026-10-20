// SPDX-License-Identifier: GPL-3.0-or-later

using HidSharp;
using Retro87.Models;
using Retro87.Protocol;
using Retro87.Services;

namespace Retro87.ViewModels;

/// <summary>
/// Creates and opens a protocol instance for the currently selected device.
/// The window/view-model talks to <see cref="IJpProtocol"/> through here so the
/// transport choice stays in one place.
/// </summary>
public sealed class JpProtocolFactory
{
    private readonly DeviceMonitor _monitor;

    public JpProtocolFactory(DeviceMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Builds an opened protocol for <paramref name="device"/>, or <c>null</c> if no
    /// usable HID interface could be opened. Several collections may exist; the
    /// protocol picks the one that accepts the keyboard's feature reports.
    /// </summary>
    /// <remarks>
    /// Two families are known: the Retro 87 (JP, PID <c>0x5200/0x5201</c>) uses
    /// <see cref="JpProtocol"/>; the Retro 87 "X" (family Xbox, <c>0x2028/0x202E</c>) uses
    /// <see cref="XboxJpProtocol"/>, which writes as well (its sessions report
    /// <see cref="IJpProtocol.CanWrite"/> true; the UI is what asks the user for permission).
    /// A single device exposes several HID collections and only the vendor one answers, so the
    /// selected collection is tried first and then its siblings, until one responds.
    /// </remarks>
    private static bool IsVendorCollection(HidDevice hid)
    {
        try { return hid.GetMaxInputReportLength() >= 64 && hid.GetMaxOutputReportLength() >= 64; }
        catch (Exception) { return false; }
    }

    public IJpProtocol? Create(DeviceInfo device)
    {
        bool xbox = KnownDevices.IsXbox(device.Pid);

        var candidates = new List<HidDevice>();
        var selected = _monitor.FindHidDevice(device.DevicePath);
        if (selected is not null) candidates.Add(selected);
        foreach (var sibling in _monitor.FindHidDevices(device.Vid, device.Pid))
            if (!candidates.Any(c => c.DevicePath == sibling.DevicePath))
                candidates.Add(sibling);

        // Only the vendor collection (64-byte reports) answers the protocol; each of the others costs
        // 30 retries of 250 ms (7.5 s) before it is given up on. Trying it first is what keeps a
        // connection from taking many seconds, whatever order the system lists the collections in.
        if (xbox)
            candidates = candidates.OrderByDescending(IsVendorCollection).ToList();

        foreach (var hid in candidates)
        {
            // The Retro 87 "X" goes idle and stops answering; the app wakes it with the same
            // control frame the official software sends first (_ReportXbox(0), cmd 0x08 sub 0).
            // The read-only CLI tools leave WakeOnOpen off, so they stay read-only by construction.
            IJpProtocol protocol = xbox
                ? new XboxJpProtocol(new HidSharpTransport(hid)) { WakeOnOpen = true }
                : new JpProtocol(new HidSharpTransport(hid));

            if (protocol.Open())
            {
                // The Xbox family answers a version command; a collection that does not is the
                // wrong one (e.g. the boot keyboard interface), so move on to the next.
                if (xbox && protocol.DeviceVersion <= 0f)
                {
                    protocol.Dispose();
                    continue;
                }
                return protocol;
            }
            protocol.Dispose();
        }
        return null;
    }
}
