// SPDX-License-Identifier: GPL-3.0-or-later

using HidSharp;
using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>
/// Read-only probe that reads the configuration and then prints every Super Button press decoded
/// from the HID input reports, naming its port and target. Used to validate the listener on the
/// hardware without the window.
/// </summary>
internal static class XboxSuperListenProbe
{
    public static int Run(TextWriter output, int seconds)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(750);

        DeviceInfo? keyboard = monitor.Devices.FirstOrDefault(d => KnownDevices.IsXbox(d.Pid));
        if (keyboard is null)
        {
            output.WriteLine("No Retro 87 \"X\" found. Check the cable or the dongle.");
            return 1;
        }

        var collections = monitor.Devices
            .Where(d => DeviceIdentity.SamePhysicalDevice(d, keyboard))
            .ToList();

        var hids = collections
            .Select(d => monitor.FindHidDevice(d.DevicePath))
            .Where(hid => hid is not null)
            .Cast<HidDevice>()
            .ToList();

        KeyboardConfig? config = ReadConfig(hids, output);
        var mappingIds = new byte[SuperButtons.Count];
        var idSpace = MappingIdSpace.Xbox;
        for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal++)
        {
            int position = SuperButtons.PositionOf(ordinal);
            mappingIds[ordinal] = config is not null && position < config.BoardKeys.Length
                ? config.BoardKeys[position]
                : XboxIdTable.UnmappedId;
        }

        output.WriteLine($"Listening for {seconds} s — press the Super Buttons (read-only).");
        using var listener = new SuperButtonInputListener(hids);
        listener.Pressed += (_, press) =>
        {
            byte mappingId = idSpace.IdForUsage(press.Code, press.Kind);
            string target = $"{idSpace.Describe(mappingId)} ({KindLabel(press.Kind)}, id 0x{mappingId:X2})";

            var matches = new List<string>();
            for (int ordinal = 0; ordinal < SuperButtons.Count; ordinal++)
            {
                if (mappingIds[ordinal] != mappingId) continue;
                string? port = SuperButtons.InterfaceOf(ordinal);
                matches.Add($"{SuperButtons.NameOf(ordinal)} · " +
                            (port is null ? "on the keyboard" : $"port {port}"));
            }

            string where = matches.Count switch
            {
                0 => "no Super Button read uses this target",
                1 => matches[0],
                _ => $"ambiguous: {string.Join(" or ", matches)}",
            };

            output.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {target} → {where}");
        };

        if (!listener.Start())
        {
            output.WriteLine($"Could not open the input collections: {listener.LastError}");
            return 1;
        }

        Thread.Sleep(TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 120)));
        output.WriteLine("fim");
        return 0;
    }

    private static KeyboardConfig? ReadConfig(IReadOnlyList<HidDevice> hids, TextWriter output)
    {
        foreach (HidDevice hid in hids)
        {
            // WakeOnOpen stays off: this probe is read-only by construction.
            var protocol = new XboxJpProtocol(new HidSharpTransport(hid));
            try
            {
                if (!protocol.Open()) continue;
                if (protocol.DeviceVersion <= 0f) continue;

                KeyboardConfig config = protocol.ReadConfig();
                if (config.BoardKeys.Length == 0) continue;

                output.WriteLine($"Config read from {hid.DevicePath} (firmware {protocol.DeviceVersion:0.00}).");
                return config;
            }
            catch (Exception ex)
            {
                output.WriteLine($"Failed to read {hid.DevicePath}: {ex.Message}");
            }
            finally
            {
                protocol.Dispose();
            }
        }

        output.WriteLine("Could not read the configuration; only the ids will be shown.");
        return null;
    }

    private static string KindLabel(MappingTargetKind kind) => kind switch
    {
        MappingTargetKind.Media or MappingTargetKind.Shortcut => "media",
        MappingTargetKind.Mouse => "mouse",
        _ => "key",
    };
}
