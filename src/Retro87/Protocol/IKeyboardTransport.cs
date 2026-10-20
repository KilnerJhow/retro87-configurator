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

    /// <summary>Reads an input report. Returns bytes read, 0 on failure.</summary>
    int Read(byte[] buffer);
}
