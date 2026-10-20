// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Protocol;

/// <summary>
/// Read-only hardware probe for the Retro 87 "X" (family Xbox), used by
/// <c>Retro87 --xbox-probe</c>. It enumerates the known PIDs, tries each HID collection and
/// reports the firmware/battery and a short summary of the configuration.
/// </summary>
/// <remarks>
/// <b>Read-only by construction:</b> it only ever calls <see cref="XboxJpProtocol.ReadVersion"/>
/// and <see cref="XboxJpProtocol.ReadConfig"/>. It exists to validate the C# layer
/// (<see cref="HidSharpTransport"/>, device selection, framing) against the real keyboard
/// without the risk of a write — see the project rule "writing to the keyboard only with permission".
/// </remarks>
internal static class XboxHardwareProbe
{
    public static int Run(TextWriter output)
    {
        using var monitor = new DeviceMonitor();
        monitor.Start();
        Thread.Sleep(500);   // let the first poll land

        var candidates = monitor.Devices.Where(d => KnownDevices.IsXbox(d.Pid)).ToList();
        if (candidates.Count == 0)
        {
            output.WriteLine("No Retro 87 \"X\" (PID 0x2028/0x202E) found.");
            return 1;
        }

        output.WriteLine($"{candidates.Count} HID collection(s) of the Retro 87 X:");
        int answered = 0;
        DeviceInfo? answeredDevice = null;
        foreach (var device in candidates)
        {
            var hid = monitor.FindHidDevice(device.DevicePath);
            if (hid is null) continue;

            int outLen, inLen, featureLen;
            try
            {
                outLen = hid.GetMaxOutputReportLength();
                inLen = hid.GetMaxInputReportLength();
                featureLen = hid.GetMaxFeatureReportLength();
            }
            catch (Exception ex)
            {
                output.WriteLine($"  {device.DevicePath}: could not read the descriptors ({ex.GetType().Name})");
                continue;
            }

            output.Write($"  {device.DevicePath}  \"{device.ProductName}\"  out={outLen} in={inLen} feat={featureLen}: ");
            using var transport = new HidSharpTransport(hid);
            var protocol = new XboxJpProtocol(transport);
            if (!protocol.Open())
            {
                output.WriteLine("did not open");
                continue;
            }

            if (!protocol.ReadVersion())
            {
                output.WriteLine("no answer to the version command (is this not the vendor collection?)");
                continue;
            }
            answered++;
            answeredDevice = device;
            output.WriteLine($"firmware {protocol.FirmwareVersion:0.00}, battery {protocol.BatteryPercent}%");

            XboxConfig? config = protocol.ReadXboxConfig();
            if (config is null)
            {
                output.WriteLine("      failed to read the configuration");
                continue;
            }

            output.WriteLine($"      name=\"{config.ProfileName}\" flag=0x{config.Flag:X8} enable={config.Enable} " +
                             $"volume={config.VolumeLevel} color={config.CurrentColorTheme} leds_sleep={config.LedsSleep}");
            int nonIdentity = 0;
            for (int i = 0; i < config.Mappings.Length; i++)
            {
                var m = config.Mappings[i];
                if (m.KeyCode != 0 || m.Mapping != 0 || m.Type != XboxMappingType.None) nonIdentity++;
            }
            output.WriteLine($"      {nonIdentity} non-empty mapping slots (of {config.Mappings.Length})");

            int macroMappings = config.Mappings.Count(m => m.Type == XboxMappingType.Macro);
            output.WriteLine($"      {macroMappings} mapping(s) of type MACRO");
            var firstMacro = protocol.ReadMacro(0, 0);
            output.WriteLine(firstMacro is null
                ? "      cmd 0x07 slot 0: no answer"
                : $"      cmd 0x07 slot 0: maxStep={firstMacro.MaxStep} name=\"{firstMacro.Name}\" " +
                  $"steps={firstMacro.Steps.Count} (0xFFFF/0 = erased)");
        }

        output.WriteLine(answered > 0
            ? $"OK: {answered} collection(s) answered (read only)."
            : "No collection answered.");

        // End-to-end through the same factory + IJpProtocol the UI uses (read-only).
        if (answeredDevice is not null)
        {
            var factory = new ViewModels.JpProtocolFactory(monitor);
            IJpProtocol? session = factory.Create(answeredDevice);
            output.WriteLine(session is null
                ? "  UI factory: did not open a session"
                : "  UI factory: session created");
            if (session is not null)
            {
                try
                {
                    KeyboardConfig cfg = session.ReadConfig();
                    int modified = 0;
                    for (int i = 0; i < cfg.BoardKeys.Length; i++)
                        if (cfg.BoardKeys[i] != MappingIdSpace.Xbox.DefaultTargets[i]) modified++;
                    output.WriteLine($"    CanWrite={session.CanWrite}  name=\"{cfg.ProfileName}\"  " +
                                     $"keys off the default={modified} (via Xbox -> UI conversion)");
                }
                finally
                {
                    session.Dispose();
                }
            }
        }

        return answered > 0 ? 0 : 1;
    }
}
