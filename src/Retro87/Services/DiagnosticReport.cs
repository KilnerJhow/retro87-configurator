// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Retro87.Models;
using Retro87.Protocol;
using Retro87.ViewModels;

namespace Retro87.Services;

/// <summary>
/// Read-only support report, modelled on the "attach a report, don't guess"
/// habit of openbitdo. It enumerates the supported keyboards, reads each one
/// through the same <see cref="JpProtocolFactory"/> the UI uses and writes a
/// TOML file with identity, firmware and a summary of the configuration.
/// </summary>
/// <remarks>
/// It never writes configuration. For the Xbox family the session may send the
/// wake handshake (<c>cmd 0x08 sub 0</c>, a control frame) because a sleeping
/// keyboard does not answer reads; the report states that explicitly. Raw
/// configuration bytes are deliberately not included: the report is meant to
/// be shared when asking for support.
/// </remarks>
internal static class DiagnosticReport
{
    public static int Run(TextWriter output, string? path, IReadOnlyList<DeviceInfo>? devicesOverride = null)
    {
        path ??= $"retro87-report-{DateTime.Now:yyyyMMdd-HHmmss}.toml";

        using var monitor = new DeviceMonitor();
        if (devicesOverride is null)
        {
            monitor.Start();
            Thread.Sleep(750); // let the first poll land
        }

        var devices = devicesOverride is null
            ? DeviceIdentity.Distinct(monitor.Devices).ToList()
            : devicesOverride.ToList();
        var toml = new StringBuilder();
        toml.AppendLine("# Retro87 diagnostic report");
        toml.AppendLine("# Read-only: no configuration was written to any device.");
        toml.AppendLine();
        toml.AppendLine("tool = \"Retro87\"");
        toml.AppendLine($"version = \"{Toml(VersionText())}\"");
        toml.AppendLine($"generated_at = \"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}\"");
        toml.AppendLine($"os = \"{Toml(RuntimeInformation.OSDescription)}\"");
        toml.AppendLine("read_only = true");
        toml.AppendLine($"device_count = {devices.Count}");
        toml.AppendLine();

        int readable = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            DeviceInfo device = devices[i];
            bool xbox = KnownDevices.IsXbox(device.Pid);
            bool canWrite = false;
            float firmware = 0f;
            int modified = 0;
            int namedMacros = 0;
            byte special = 0, voice = 0, volume = 0, fastSetting = 0;
            string profile = string.Empty;
            string? error = null;

            var factory = new JpProtocolFactory(monitor);
            IJpProtocol? session = null;
            try
            {
                session = factory.Create(device);
                if (session is null)
                {
                    error = "no HID interface answered the keyboard protocol";
                }
                else
                {
                    KeyboardConfig config = session.ReadConfig();
                    canWrite = session.CanWrite;
                    firmware = session.DeviceVersion;
                    profile = config.ProfileName;
                    special = config.SpecialFeature;
                    voice = config.Voice;
                    volume = config.XboxVolumeLevel;
                    fastSetting = config.XboxFastSetting;
                    IReadOnlyList<byte> defaults = session is XboxJpProtocol
                        ? MappingIdSpace.Xbox.DefaultTargets
                        : MappingIdSpace.Jp.DefaultTargets;
                    for (int k = 0; k < config.BoardKeys.Length && k < defaults.Count; k++)
                    {
                        if (config.BoardKeys[k] != defaults[k])
                            modified++;
                    }
                    namedMacros = config.Macros.Count(m => !m.IsEmpty);
                    readable++;
                }
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                session?.Dispose();
            }

            toml.AppendLine("[[device]]");
            toml.AppendLine($"index = {i}");
            toml.AppendLine($"path = \"{Toml(device.DevicePath)}\"");
            toml.AppendLine($"vid = \"0x{device.Vid:X4}\"");
            toml.AppendLine($"pid = \"0x{device.Pid:X4}\"");
            toml.AppendLine($"product = \"{Toml(device.ProductName)}\"");
            toml.AppendLine($"family = \"{(xbox ? "xbox" : "jp")}\"");
            toml.AppendLine($"profile_name = \"{Toml(profile)}\"");
            toml.AppendLine($"firmware = \"{(firmware > 0f ? firmware.ToString("0.00", CultureInfo.InvariantCulture) : "unknown")}\"");
            toml.AppendLine($"can_write = {(canWrite ? "true" : "false")}");
            toml.AppendLine($"board_keys_total = {KeyboardLayout.DefaultMappingId.Length}");
            toml.AppendLine($"board_keys_modified = {modified}");
            toml.AppendLine($"macros_named = {namedMacros}");
            toml.AppendLine($"special_feature = {special}");
            toml.AppendLine($"voice = {voice}");
            if (xbox)
            {
                toml.AppendLine($"volume = {volume}");
                toml.AppendLine($"fast_setting = {fastSetting}");
                toml.AppendLine($"quick_setup_disable_win = {((fastSetting & XboxWire.FastSetting.DisableWindowsKey) != 0 ? "true" : "false")}");
                toml.AppendLine($"quick_setup_disable_alt_tab = {((fastSetting & XboxWire.FastSetting.DisableAltTab) != 0 ? "true" : "false")}");
                toml.AppendLine($"quick_setup_disable_alt_f4 = {((fastSetting & XboxWire.FastSetting.DisableAltF4) != 0 ? "true" : "false")}");
                toml.AppendLine("session_frames = \"wake handshake cmd 0x08 sub 0 (control only)\"");
            }
            toml.AppendLine($"error = \"{Toml(error ?? string.Empty)}\"");
            toml.AppendLine();

            output.WriteLine(session is null
                ? $"  {device.DisplayName}: {error}"
                : $"  {device.DisplayName}: {(xbox ? "Xbox" : "JP")}, firmware {(firmware > 0f ? firmware.ToString("0.00", CultureInfo.InvariantCulture) : "unknown")}, " +
                  $"{modified} key(s) off the default, {namedMacros} named macro(s){(error is null ? string.Empty : " — " + error)}");
        }

        string fullPath = Path.GetFullPath(path);
        File.WriteAllText(fullPath, toml.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        output.WriteLine($"Report: {fullPath} ({readable}/{devices.Count} device(s) read; no configuration was written).");
        return readable > 0 ? 0 : 1;
    }

    private static string VersionText()
    {
        string? version = typeof(DiagnosticReport).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version.Split('+')[0];
    }

    private static string Toml(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
}
