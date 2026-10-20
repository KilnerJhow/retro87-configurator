// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>
/// Read-only probe that opens every HID collection of the Retro 87 "X" and prints the input
/// reports it receives. Used to discover how the keyboard reports a Super Button press.
/// </summary>
internal static class XboxInputProbe
{
    public static int Run(TextWriter output, int seconds)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(750);

        var readers = new List<(DeviceInfo Device, HidSharpTransport Transport, Thread Thread)>();
        DateTime stop = DateTime.UtcNow.AddSeconds(seconds);
        object gate = new();

        foreach (DeviceInfo device in monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList())
        {
            var hid = monitor.FindHidDevice(device.DevicePath);
            if (hid is null) continue;

            var transport = new HidSharpTransport(hid);
            if (!transport.Open())
            {
                lock (gate) output.WriteLine($"did not open: {device.DevicePath}");
                continue;
            }

            lock (gate)
            {
                output.WriteLine($"reading: {device.DevicePath}  maxIn={transport.MaxInputReportLength}");
            }

            var thread = new Thread(() =>
            {
                var buffer = new byte[Math.Max(64, transport.MaxInputReportLength)];
                while (DateTime.UtcNow < stop)
                {
                    int read = transport.Read(buffer);
                    if (read <= 0) continue;
                    ReadOnlySpan<byte> report = buffer.AsSpan(0, read);
                    if (report.IndexOfAnyExcept((byte)0) < 0) continue;
                    lock (gate)
                    {
                        output.WriteLine(
                            $"{DateTime.UtcNow:HH:mm:ss.fff} {device.DevicePath} ({read}) {Convert.ToHexString(report)}");
                    }
                }
            })
            { IsBackground = true };
            thread.Start();
            readers.Add((device, transport, thread));
        }

        if (readers.Count == 0)
        {
            output.WriteLine("No Retro 87 \"X\" collection opened.");
            return 1;
        }

        Thread.Sleep(seconds * 1000 + 500);
        foreach ((_, HidSharpTransport transport, Thread thread) in readers)
        {
            transport.Close();
            thread.Join(500);
        }
        output.WriteLine("fim");
        return 0;
    }
}
