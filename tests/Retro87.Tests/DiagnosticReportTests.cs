// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;
using Retro87.Services;

namespace Retro87.Tests;

public class DiagnosticReportTests
{
    [Fact]
    public void Report_WithNoDevices_IsReadOnlyAndHasNoDeviceBlocks()
    {
        string path = TempReportPath();
        try
        {
            int exit = DiagnosticReport.Run(TextWriter.Null, path, Array.Empty<DeviceInfo>());

            Assert.Equal(1, exit);
            string toml = File.ReadAllText(path);
            Assert.Contains("tool = \"Retro87\"", toml);
            Assert.Contains("read_only = true", toml);
            Assert.Contains("device_count = 0", toml);
            Assert.Contains("no configuration was written", toml);
            Assert.DoesNotContain("[[device]]", toml);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Report_WithAnXboxDevice_DescribesItWithoutRawBytes()
    {
        string path = TempReportPath();
        try
        {
            var device = new DeviceInfo(
                KnownDevices.EightBitDoVid, KnownDevices.Retro87XUsb,
                "Fake Retro 87 X", null, "/dev/null");

            int exit = DiagnosticReport.Run(TextWriter.Null, path, new[] { device });

            Assert.Equal(1, exit);
            string toml = File.ReadAllText(path);
            Assert.Contains("[[device]]", toml);
            Assert.Contains("family = \"xbox\"", toml);
            Assert.Contains("can_write = false", toml);
            Assert.Contains("board_keys_total = 95", toml);
            Assert.Contains("error = \"", toml);
            Assert.DoesNotContain("profile_hex", toml);
        }
        finally
        {
            Delete(path);
        }
    }

    private static string TempReportPath()
        => Path.Combine(Path.GetTempPath(), $"retro87-report-{Guid.NewGuid():N}.toml");

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
