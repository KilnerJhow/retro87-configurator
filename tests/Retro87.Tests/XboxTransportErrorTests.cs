// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Protocol;

namespace Retro87.Tests;

/// <summary>
/// A write the operating system refuses (device unplugged, permission lost) must not be reported
/// as a keyboard that simply did not answer.
/// </summary>
public class XboxTransportErrorTests
{
    private sealed class DeadWriteTransport(string? error) : IKeyboardTransport
    {
        public bool IsOpen => true;
        public int MaxFeatureReportLength => 64;
        public int MaxInputReportLength => 64;
        public int MaxOutputReportLength => 64;
        public string? LastError { get; } = error;
        public bool Open() => true;
        public void Close() { }
        public int SetFeatureReport(byte[] buffer) => 0;
        public int GetFeatureReport(byte[] buffer) => 0;
        public int Write(byte[] buffer) => 0;
        public int Read(byte[] buffer) => 0;
        public void Dispose() { }
    }

    [Fact]
    public void ReadVersion_WhenTheWriteFailsWithAnIoError_NamesTheOperatingSystemError()
    {
        var protocol = new XboxJpProtocol(new DeadWriteTransport("device removed"));

        Assert.False(protocol.ReadVersion());

        Assert.Contains("I/O error", protocol.LastError);
        Assert.Contains("device removed", protocol.LastError);
    }

    [Fact]
    public void ReadVersion_WhenNothingFailedButNothingAnswered_ReportsNoAnswer()
    {
        var protocol = new XboxJpProtocol(new DeadWriteTransport(null));

        Assert.False(protocol.ReadVersion());

        Assert.Contains("no answer", protocol.LastError);
        Assert.Equal(XboxFailure.NoAnswer, protocol.LastFailure);
    }
}
