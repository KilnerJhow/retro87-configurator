// SPDX-License-Identifier: GPL-3.0-or-later

using HidSharp;
using Retro87.Localization;
using Retro87.Models;

namespace Retro87.Services;

/// <summary>
/// Listens to the input collections of the connected Retro 87 and raises a
/// <see cref="HidInputPress"/> for every new key, media or mouse event.
/// </summary>
/// <remarks>
/// <para>
/// Reading the HID reports directly is what makes the Super Buttons work even when the desktop
/// consumes the event first — media keys are the common case: the system handles Play/Pause and
/// the window never sees a key press, but the report still reaches every reader of the hidraw
/// node. It also works while the window is not focused, and on Linux it needs no evdev access:
/// the existing <c>70-8bitdo.rules</c> already grants the session access to the HID nodes.
/// </para>
/// <para>
/// The keyboard, consumer-control, mouse and vendor collections are opened. The vendor one (64
/// bytes) is never used for the protocol's feature reports here — reads only, decoded by
/// <see cref="SuperButtonRawDecoder"/> for the raw "which connection was pressed" signal (see
/// <see cref="RawPortPressed"/>), which is how a connection with no mapped target still gets
/// identified.
/// </para>
/// <para>
/// The listener is read-only and best-effort: a collection that cannot be opened (for example a
/// keyboard/mouse collection on Windows, which the OS may keep for itself) is skipped and the
/// rest keep working. The UI still has the window key-event path as a fallback.
/// </para>
/// </remarks>
public sealed class SuperButtonInputListener : IDisposable
{
    /// <summary>Largest input report accepted: keyboard = 17 bytes, mouse = 5, vendor = 64.</summary>
    private const int MaxInputReportLength = 64;

    /// <summary>Reports at or above this length are the vendor collection, decoded raw.</summary>
    private const int VendorReportLength = 64;

    private readonly List<HidDevice> _devices;
    private readonly List<HidStream> _streams = new();
    private readonly List<Thread> _threads = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();

    private byte _lastCode;
    private MappingTargetKind _lastKind;
    private long _lastPublishTick;
    private string? _lastRawInterface;
    private int _lastRawPairIndex;
    private bool _lastRawIsPressed;
    private long _lastRawPublishTick;
    private bool _disposed;

    public SuperButtonInputListener(IEnumerable<HidDevice> devices)
        => _devices = devices.ToList();

    /// <summary>Raised on the reader thread with every new press (never for a held key).</summary>
    public event EventHandler<HidInputPress>? Pressed;

    /// <summary>
    /// Raised on the reader thread when one specific Super Button's contact closes, read straight
    /// from the vendor collection — fires even when it has no mapped target.
    /// </summary>
    public event EventHandler<SuperButtonRawPress>? RawPortPressed;

    /// <summary>True when at least one collection was opened.</summary>
    public bool IsRunning => _threads.Count > 0;

    /// <summary>Why no collection could be opened, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Opens the readable collections and starts one reader thread per collection.</summary>
    public bool Start()
    {
        foreach (HidDevice device in _devices)
        {
            int maxInput;
            try
            {
                maxInput = device.GetMaxInputReportLength();
            }
            catch (Exception)
            {
                continue;
            }

            if (maxInput < 3 || maxInput > MaxInputReportLength) continue;

            HidStream? stream = null;
            try
            {
                if (!device.TryOpen(out stream) || stream is null) continue;
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                LastError ??= ex.Message;
                continue;
            }

            try
            {
                stream.ReadTimeout = 250;
            }
            catch (Exception)
            {
                // Not every backend supports timeouts; Dispose still unblocks the reader.
            }

            _streams.Add(stream);
            var thread = new Thread(() => ReadLoop(stream, maxInput))
            {
                IsBackground = true,
                Name = "Retro87 HID input",
            };
            _threads.Add(thread);
            thread.Start();
        }

        if (_threads.Count == 0)
            LastError ??= Loc.T("err_no_input");
        return IsRunning;
    }

    private void ReadLoop(HidStream stream, int maxInput)
    {
        bool isVendor = maxInput >= VendorReportLength;
        var decoder = isVendor ? null : new HidInputDecoder();
        var rawDecoder = isVendor ? new SuperButtonRawDecoder() : null;
        var buffer = new byte[Math.Max(64, maxInput)];

        while (!_stop.IsCancellationRequested)
        {
            int read;
            try
            {
                read = stream.Read(buffer);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception)
            {
                break;
            }

            if (read <= 0) continue;

            if (rawDecoder is not null)
            {
                foreach (SuperButtonRawPress press in rawDecoder.Decode(buffer.AsSpan(0, read)))
                    PublishRaw(press);
            }
            else
            {
                foreach (HidInputPress press in decoder!.Decode(buffer.AsSpan(0, read)))
                    Publish(press);
            }
        }
    }

    /// <summary>
    /// Drops the duplicate some devices produce when the same physical press is reported by more
    /// than one collection in the same instant (e.g. a media key also repeated on the keyboard
    /// collection). Fast double presses are still far apart at human speed.
    /// </summary>
    private void Publish(HidInputPress press)
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (press.Code == _lastCode && press.Kind == _lastKind && now - _lastPublishTick < 120)
                return;

            _lastCode = press.Code;
            _lastKind = press.Kind;
            _lastPublishTick = now;
        }

        Pressed?.Invoke(this, press);
    }

    /// <summary>Same debounce as <see cref="Publish"/>, keyed by connection instead of code+kind.</summary>
    private void PublishRaw(SuperButtonRawPress press)
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (press.Interface == _lastRawInterface && press.PairIndex == _lastRawPairIndex
                && press.IsPressed == _lastRawIsPressed && now - _lastRawPublishTick < 120)
                return;

            _lastRawInterface = press.Interface;
            _lastRawPairIndex = press.PairIndex;
            _lastRawIsPressed = press.IsPressed;
            _lastRawPublishTick = now;
        }

        RawPortPressed?.Invoke(this, press);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stop.Cancel();

        // Closing unblocks a reader waiting on the stream. The threads are background threads;
        // waiting a moment keeps dispose from returning while they still touch the buffers.
        foreach (HidStream stream in _streams)
        {
            try { stream.Dispose(); } catch (Exception) { }
        }
        foreach (Thread thread in _threads)
            thread.Join(300);

        _streams.Clear();
        _threads.Clear();
        _stop.Dispose();
    }
}
