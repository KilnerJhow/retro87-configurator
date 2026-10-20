// SPDX-License-Identifier: GPL-3.0-or-later

using HidSharp;

namespace Retro87.Protocol;

/// <summary>
/// <see cref="IKeyboardTransport"/> over HidSharp, so the port works on Linux (hidraw)
/// as well as Windows. The native Windows DLL is not used at all.
/// </summary>
public sealed class HidSharpTransport : IKeyboardTransport
{
    private readonly HidDevice _device;
    private HidStream? _stream;

    public HidSharpTransport(HidDevice device)
    {
        _device = device;
        MaxFeatureReportLength = device.GetMaxFeatureReportLength();
        MaxInputReportLength = device.GetMaxInputReportLength();
        MaxOutputReportLength = device.GetMaxOutputReportLength();
    }

    public bool IsOpen => _stream is not null;

    public int MaxFeatureReportLength { get; }
    public int MaxInputReportLength { get; }
    public int MaxOutputReportLength { get; }

    public bool Open()
    {
        if (_stream is not null) return true;
        try
        {
            if (_device.TryOpen(out var stream))
            {
                // Without a timeout a read on a HID collection that does not answer blocks
                // forever (observed on a non-vendor collection of the Retro 87 "X" dongle).
                // The protocol layers rely on Read returning 0 to drive their retry loops.
                try
                {
                    stream.ReadTimeout = 300;
                    stream.WriteTimeout = 300;
                }
                catch (Exception)
                {
                    // Not every backend supports timeouts; the retry loops still work.
                }
                _stream = stream;
                return true;
            }
        }
        catch (Exception)
        {
            // Reported as failure; the UI decides what to tell the user.
        }
        return false;
    }

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public int SetFeatureReport(byte[] buffer)
    {
        if (_stream is null) return 0;
        try { _stream.SetFeature(buffer); return buffer.Length; }
        catch (Exception) { return 0; }
    }

    public int GetFeatureReport(byte[] buffer)
    {
        if (_stream is null) return 0;
        try { _stream.GetFeature(buffer); return buffer.Length; }
        catch (Exception) { return 0; }
    }

    public int Write(byte[] buffer)
    {
        if (_stream is null) return 0;
        try { _stream.Write(buffer); return buffer.Length; }
        catch (Exception) { return 0; }
    }

    public int Read(byte[] buffer)
    {
        if (_stream is null) return 0;
        try { return _stream.Read(buffer); }
        catch (Exception) { return 0; }
    }

    public void Dispose() => Close();
}
