// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HidSharp;
using Retro87.Localization;
using Retro87.Models;
using Retro87.Protocol;
using Retro87.Services;

namespace Retro87.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>
    /// Turns on capture mode: the next key/media event is matched against the Super Button
    /// mappings and the block jumps to the button that owns it. Escape cancels.
    /// </summary>
    [RelayCommand]
    private async Task ListenSuperButtonAsync()
    {
        if (IsListeningForSuperButton)
        {
            IsListeningForSuperButton = false;
            IsAskingSuperButton = false;
            SetStatus(StatusSeverity.Info, Loc.T("st_capture_cancelled"));
            return;
        }
        if (IsMappingEmpty)
        {
            SetStatus(StatusSeverity.Warning, Loc.T("st_read_first_listen"));
            return;
        }

        if (!await WakeRawSuperButtonSignalAsync()) return;

        IsAskingSuperButton = false;
        IsListeningForSuperButton = true;
        SetStatus(StatusSeverity.Info,
            Loc.T("st_press_super"));
        StartSuperQuestionTimer();
    }

    /// <summary>
    /// Rewrites the current profile name unchanged — a harmless write the dongle needs to start
    /// streaming its raw vendor report (how a port with no mapped target still gets identified).
    /// Without this, that report only flows while the official 8BitDo app is open: picking a
    /// profile there does the same write as a side effect. Runs once per "Ouvir Super Button"
    /// click, not per press.
    /// </summary>
    private async Task<bool> WakeRawSuperButtonSignalAsync()
    {
        if (_protocol is null) { SetStatus(StatusSeverity.Warning, Loc.T("st_connect_first")); return false; }
        if (!CanWriteDevice)
        {
            SetStatus(StatusSeverity.Warning,
                Loc.T("st_capture_unavailable"));
            return false;
        }
        IJpProtocol protocol = _protocol;
        string name = _loadedConfig?.ProfileName ?? string.Empty;
        IsBusy = true;
        SetStatus(StatusSeverity.Info,
            Loc.T("st_waking"));
        try
        {
            bool ok = await Task.Run(() => protocol.WakeRawSignal(name));
            if (!ok)
                SetStatus(StatusSeverity.Error, Loc.T("st_wake_failed"));
            return ok;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// After a few seconds without a key event, asks which button was pressed. This covers
    /// media/mouse-mapped buttons, whose events may be consumed by the desktop before the window
    /// sees them.
    /// </summary>
    private void StartSuperQuestionTimer()
    {
        _superQuestionTimer?.Stop();
        _superQuestionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _superQuestionTimer.Tick += (_, _) =>
        {
            _superQuestionTimer?.Stop();
            if (!IsListeningForSuperButton) return;

            IsListeningForSuperButton = false;
            SuperButtonChoices = AllSuperButtons;
            SuperButtonQuestion = Loc.T("super_question_missed");
            IsAskingSuperButton = true;
            SetStatus(StatusSeverity.Info, Loc.T("st_pick_super"));
        };
        _superQuestionTimer.Start();
    }

    /// <summary>Closes the “which Super Button?” question without selecting anything.</summary>
    [RelayCommand]
    private void CancelSuperButtonQuestion()
    {
        IsAskingSuperButton = false;
        IsListeningForSuperButton = false;
        SetStatus(StatusSeverity.Info, Loc.T("st_question_cancelled"));
    }

    /// <summary>
    /// Handles a key while capture mode is on. Returns true when the event belongs to the capture
    /// (either it identified a button or it is a key the capture should consume).
    /// </summary>
    /// <remarks>
    /// This is the fallback path used when the HID listener could not open the keyboard
    /// collection. When it is running, the same press also arrives from
    /// <see cref="OnInputPressed"/>; the handler is idempotent, so the duplicate is harmless.
    /// </remarks>
    public bool TryCaptureSuperButton(Key key)
    {
        if (!IsListeningForSuperButton) return false;

        if (key == Key.Escape)
        {
            IsListeningForSuperButton = false;
            SetStatus(StatusSeverity.Info, Loc.T("st_capture_cancelled"));
            return true;
        }

        if (!SuperButtonCapture.TryMap(key, out byte usage, out MappingTargetKind kind))
            return false;

        HandleSuperButtonPress(usage, kind, SuperButtonCapture.Describe(key));
        return true;
    }

    /// <summary>Receives a press from the HID listener (reader thread) and moves it to the UI thread.</summary>
    private void OnInputPressed(object? sender, HidInputPress press)
        => Dispatcher.UIThread.Post(() => HandleSuperButtonPress(press.Code, press.Kind, keyLabel: null));

    /// <summary>Receives a raw Super Button press/release (reader thread) and moves it to the UI thread.</summary>
    private void OnRawPortPressed(object? sender, SuperButtonRawPress press)
        => Dispatcher.UIThread.Post(() => HandleRawPortPress(press));

    /// <summary>
    /// Keeps the pressed button lit for exactly as long as it is physically held — the vendor
    /// signal says so directly, unlike the mapped-id path's fixed-duration flash — and, for a
    /// press, arms a short wait for a mapped-id event that might explain the same physical event.
    /// If none arrives, <see cref="ResolveRawPortPress"/> handles it alone: this button has no
    /// mapped target, the case <see cref="HandleSuperButtonPress"/> alone cannot identify.
    /// </summary>
    private void HandleRawPortPress(SuperButtonRawPress press)
    {
        if (IsMappingEmpty) return;

        int ordinal = SuperButtons.OrdinalOf(press.Interface, press.PairIndex);
        MappingRowViewModel? row = _allMappings.FirstOrDefault(r => r.SuperOrdinal == ordinal);

        if (!press.IsPressed)
        {
            if (row is not null)
            {
                row.IsPressed = false;
                _heldRawRows.Remove(row);
            }
            if (_pendingRawPress is { } pending && pending.Interface == press.Interface
                && pending.PairIndex == press.PairIndex)
                _pendingRawPress = null;   // released before the wait below ran its course
            return;
        }

        if (row is not null)
        {
            row.IsPressed = true;
            _heldRawRows.Add(row);
        }

        _pendingRawPress = press;
        _pendingRawPressTick = Environment.TickCount64;

        _rawResolveTimer?.Stop();
        _rawResolveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RawPortResolveDelayMs) };
        _rawResolveTimer.Tick += (_, _) =>
        {
            _rawResolveTimer?.Stop();
            if (_pendingRawPress == press) ResolveRawPortPress(press);
        };
        _rawResolveTimer.Start();
    }

    /// <summary>
    /// A raw Super Button press that no mapped-id event explained within the wait: it has no
    /// target, which is exactly why <see cref="HandleSuperButtonPress"/> never ran for it. Unlike
    /// the mapped path, the vendor signal already says exactly which button — nothing to ask.
    /// </summary>
    private void ResolveRawPortPress(SuperButtonRawPress press)
    {
        _pendingRawPress = null;

        int ordinal = SuperButtons.OrdinalOf(press.Interface, press.PairIndex);
        MappingRowViewModel? row = _allMappings.FirstOrDefault(r => r.SuperOrdinal == ordinal);
        if (row is null) return;

        bool identifying = IsListeningForSuperButton || IsAskingSuperButton;
        LastSuperButtonText = Loc.F("last_none", DescribeButton(row));
        SelectKey(row);
        if (!identifying) return;

        IsAskingSuperButton = false;
        SetStatus(StatusSeverity.Success,
            Loc.F("identified_none", row.KeyName, row.InterfaceName ?? Loc.T("on_keyboard")));
    }

    /// <summary>
    /// Resolves a decoded press against the ten Super Buttons and either selects it or asks which
    /// of the matching ports was pressed. Runs on the UI thread.
    /// </summary>
    private void HandleSuperButtonPress(byte code, MappingTargetKind kind, string? keyLabel)
    {
        if (IsMappingEmpty) return;

        // The vendor signal for the same physical press, if it arrived first: use it to tell apart
        // two Super Buttons sharing a target, then drop it — a mapped press explains the event, so
        // ResolveRawPortPress must not ask about it again as "no target".
        SuperButtonRawPress? rawPress = Environment.TickCount64 - _pendingRawPressTick < 300 ? _pendingRawPress : null;
        _pendingRawPress = null;
        _rawResolveTimer?.Stop();

        byte mappingId = _idSpace.IdForUsage(code, kind);
        string description = DescribePress(code, mappingId, kind, keyLabel);
        bool identifying = IsListeningForSuperButton || IsAskingSuperButton;

        List<MappingRowViewModel> pressed = _allMappings
            .Where(row => row.MappingId == mappingId && KindMatches(row.SelectedTarget?.Kind, kind))
            .ToList();
        FlashPressed(pressed);
        // The "Last" line belongs to the Super Buttons block: an ordinary key still lights up on the
        // board, but it never writes here (and never wipes the last Super Button shown).
        List<MappingRowViewModel> pressedSuper = pressed.Where(row => row.IsSuperButton).ToList();
        if (!identifying && pressedSuper.Count > 0)
            LastSuperButtonText = Loc.F("last_one", string.Join(" " + Loc.T("or_word") + " ", pressedSuper.Select(DescribeButton)));

        List<MappingRowViewModel> matches = pressed.Where(row => row.IsSuperButton).ToList();

        if (matches.Count == 1)
        {
            ApplySuperButtonPress(matches[0], identifying);
            return;
        }

        if (matches.Count == 0)
        {
            // A normal key that no Super Button uses: only worth mentioning while identifying.
            if (!identifying) return;

            SuperButtonChoices = AllSuperButtons;
            SuperButtonQuestion =
                Loc.F("super_no_match", description);
            IsListeningForSuperButton = false;
            IsAskingSuperButton = true;
            SetStatus(StatusSeverity.Info, Loc.T("st_pick_super"));
            return;
        }

        // More than one Super Button shares the target (two ports on Play/Pause, for example): the
        // HID report alone says what was pressed, not which physical button produced it — but the
        // vendor signal (if it arrived for the same press) already names the exact button.
        if (rawPress is { } raw)
        {
            int rawOrdinal = SuperButtons.OrdinalOf(raw.Interface, raw.PairIndex);
            MappingRowViewModel? resolved = matches.FirstOrDefault(row => row.SuperOrdinal == rawOrdinal);
            if (resolved is not null)
            {
                ApplySuperButtonPress(resolved, identifying);
                return;
            }
        }

        string ports = string.Join(" " + Loc.T("or_word") + " ", matches.Select(DescribeButton));
        LastSuperButtonText = Loc.F("last_one", ports);
        if (!identifying) return;

        SuperButtonChoices = matches;
        SuperButtonQuestion =
            Loc.F("super_multi", description, matches.Count);
        IsListeningForSuperButton = false;
        IsAskingSuperButton = true;
        SetStatus(StatusSeverity.Info, Loc.T("st_pick_port"));
    }

    /// <summary>
    /// Lights every key and Super Button that owns the reported target for a moment, so a press
    /// shows where it came from even when nothing is being identified.
    /// </summary>
    private void FlashPressed(IReadOnlyList<MappingRowViewModel> rows)
    {
        _pressedTimer?.Stop();
        foreach (MappingRowViewModel row in _allMappings.Where(r => r.IsPressed && !_heldRawRows.Contains(r)))
            row.IsPressed = false;
        foreach (MappingRowViewModel row in rows) row.IsPressed = true;
        if (rows.Count == 0) return;

        _pressedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _pressedTimer.Tick += (_, _) =>
        {
            _pressedTimer?.Stop();
            foreach (MappingRowViewModel row in rows.Where(r => !_heldRawRows.Contains(r))) row.IsPressed = false;
        };
        _pressedTimer.Start();
    }

    /// <summary>Selects the button, opens its port and reports the result.</summary>
    private void ApplySuperButtonPress(MappingRowViewModel row, bool identifying)
    {
        LastSuperButtonText = Loc.F("last_one", DescribeButton(row));
        SelectKey(row);
        if (!identifying) return;

        IsAskingSuperButton = false;
        SetStatus(StatusSeverity.Success,
            Loc.F("identified", row.KeyName, row.InterfaceName ?? Loc.T("on_keyboard"), row.MappingName));
    }

    /// <summary>Port and name of a button, e.g. "porta ▾ (Y) · Super Button 9 (Play)".</summary>
    private static string DescribeButton(MappingRowViewModel row)
    {
        string port = row.InterfaceName ?? Loc.T("on_keyboard");
        return $"{port} · {row.KeyName} ({row.MappingName})";
    }

    /// <summary>Human-readable press: the key label, or the id name decoded for the family.</summary>
    private string DescribePress(byte code, byte mappingId, MappingTargetKind kind, string? keyLabel)
    {
        string label = keyLabel ?? _idSpace.Describe(mappingId);
        string origin = kind switch
        {
            MappingTargetKind.Media or MappingTargetKind.Shortcut => Loc.T("origin_media"),
            MappingTargetKind.Mouse => Loc.T("origin_mouse"),
            _ => string.Empty,
        };
        return origin.Length == 0 ? label : $"{label} ({origin})";
    }

    /// <summary>
    /// The JP family classifies media targets as <see cref="MappingTargetKind.Shortcut"/> while the
    /// HID/shared model calls them media; both names mean the same row here.
    /// </summary>
    private static bool KindMatches(MappingTargetKind? rowKind, MappingTargetKind pressKind)
    {
        if (rowKind is null) return false;

        static MappingTargetKind Normalize(MappingTargetKind kind)
            => kind == MappingTargetKind.Shortcut ? MappingTargetKind.Media : kind;

        return Normalize(rowKind.Value) == Normalize(pressKind);
    }

    /// <summary>Starts reading the keyboard's input collections for the selected device.</summary>
    private void StartInputListening(DeviceInfo device)
    {
        StopInputListening();

        var collections = _monitor.Devices
            .Where(d => DeviceIdentity.SamePhysicalDevice(d, device))
            .Select(d => _monitor.FindHidDevice(d.DevicePath))
            .Where(hid => hid is not null)
            .Cast<HidDevice>()
            .ToList();

        if (collections.Count == 0) return;

        var listener = new SuperButtonInputListener(collections);
        listener.Pressed += OnInputPressed;
        listener.RawPortPressed += OnRawPortPressed;
        if (!listener.Start())
        {
            listener.Pressed -= OnInputPressed;
            listener.RawPortPressed -= OnRawPortPressed;
            listener.Dispose();
            return;
        }

        _inputListener = listener;
    }

    /// <summary>Stops the HID listener (disconnect, window close, or a new connection).</summary>
    private void StopInputListening()
    {
        if (_inputListener is null) return;
        _inputListener.Pressed -= OnInputPressed;
        _inputListener.RawPortPressed -= OnRawPortPressed;
        _inputListener.Dispose();
        _inputListener = null;

        // The device is gone: any button the raw signal left lit would otherwise never clear.
        foreach (MappingRowViewModel row in _heldRawRows) row.IsPressed = false;
        _heldRawRows.Clear();
        _pendingRawPress = null;
        _rawResolveTimer?.Stop();
    }
}
