// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Protocol;

/// <summary>
/// Minimal HID transport used by <see cref="JpProtocol"/>: write, write with a report id,
/// read, and feature-report read/write, without any family-specific framing, so all framing
/// lives in the protocol class.
/// </summary>
public interface IKeyboardTransport : IDisposable
{
    /// <summary>True once a usable handle is open.</summary>
    bool IsOpen { get; }

    int MaxFeatureReportLength { get; }
    int MaxInputReportLength { get; }
    int MaxOutputReportLength { get; }

    /// <summary>Opens the device. Returns false instead of throwing when unavailable.</summary>
    bool Open();

    void Close();

    /// <summary>Sends one feature report (GET_REPORT/SET_FEATURE). Returns bytes written, 0 on failure.</summary>
    int SetFeatureReport(byte[] buffer);

    /// <summary>Reads one feature report into <paramref name="buffer"/>. Returns bytes read, 0 on failure.</summary>
    int GetFeatureReport(byte[] buffer);

    /// <summary>Writes an output report. Returns bytes written, 0 on failure.</summary>
    int Write(byte[] buffer);

    /// <summary>Reads an input report. Returns bytes read, 0 on failure or when nothing arrived in time.</summary>
    int Read(byte[] buffer);

    /// <summary>
    /// Why the last call returned 0 when the cause was an I/O error (device unplugged, permission
    /// lost), or null after a call that worked or only timed out. Lets the protocol tell "the
    /// keyboard did not answer" from "the operating system refused".
    /// </summary>
    string? LastError => null;
}
