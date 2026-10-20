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
using Retro87.Models;
using Retro87.Protocol;
using Retro87.Services;
using Retro87.Localization;

namespace Retro87.ViewModels;

public partial class MainWindowViewModel
{
    private void UpdateDevices(IReadOnlyList<DeviceInfo> collections)
    {
        // The dongle restarts its USB connection some seconds after a write that changed something
        // (seen on the hardware, 2026-10-20). Every collection path of the open session then
        // disappears, the protocol handle is dead and nothing says so: notice it here and reconnect.
        bool connectionLost = IsConnected && SelectedDevice is not null && _connectedPaths.Count > 0 &&
                              !collections.Any(c => _connectedPaths.Contains(c.DevicePath));
        DeviceInfo? lostDevice = connectionLost ? SelectedDevice : null;
        if (lostDevice is not null)
        {
            bool hadChanges = HasChanges;
            Disconnect();
            _reconnectTarget = lostDevice;
            SetStatus(StatusSeverity.Warning, hadChanges
                ? Loc.T("st_conn_reset_discard")
                : Loc.T("st_conn_reset"));
        }

        // One keyboard exposes several HID collections (mouse, consumer, keyboard, vendor); the
        // picker shows one row per physical unit instead of one per collection.
        IReadOnlyList<DeviceInfo> devices = DeviceIdentity.Distinct(collections);

        Devices.Clear();
        foreach (var d in devices)
            Devices.Add(d);

        // The instances are rebuilt on every poll: keep the selection by physical identity.
        if (SelectedDevice is null || !devices.Any(d => DeviceIdentity.SamePhysicalDevice(d, SelectedDevice)))
            SelectedDevice = devices.FirstOrDefault();

        if (devices.Count == 0)
        {
            SetStatus(StatusSeverity.Info,
                Loc.T("st_none_found"));
        }
        else if (!IsConnected && _reconnectTarget is null)
        {
            SetStatus(StatusSeverity.Info, devices.Count == 1
                ? Loc.T("st_found_one")
                : Loc.F("st_found_many", devices.Count));
        }

        if (_reconnectTarget is { } target && !_reconnecting)
            _ = ReconnectAsync(target);
    }

    /// <summary>
    /// Connects again to a keyboard that restarted its link, waiting for it to come back: the
    /// collections show up before the device answers, so a failed try is repeated a few times.
    /// </summary>
    private async Task ReconnectAsync(DeviceInfo target)
    {
        _reconnecting = true;
        try
        {
            for (int attempt = 0; attempt < 8 && !IsConnected && _reconnectTarget is not null; attempt++)
            {
                DeviceInfo? present = Devices.FirstOrDefault(d => DeviceIdentity.SamePhysicalDevice(d, target));
                if (present is not null && !IsBusy)
                {
                    SelectedDevice = present;
                    await ConnectAsync();
                    if (IsConnected) break;
                }
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            if (!IsConnected && _reconnectTarget is not null)
            {
                SetStatus(StatusSeverity.Warning,
                    Loc.T("st_reconnect_failed"));
            }
        }
        finally
        {
            _reconnectTarget = null;
            _reconnecting = false;
        }
    }

    [RelayCommand]
    private void RefreshDevices() => UpdateDevices(_monitor.Devices);

    /// <summary>
    /// Alt+1..5 — goes to the tab whose zero-based index the key binding passes. Living in the
    /// view-model (and not in a code-behind handler) keeps the shortcut list in one place with the
    /// commands it triggers.
    /// </summary>
    [RelayCommand]
    private void GoToTab(string? index)
    {
        if (int.TryParse(index, out int tab) && tab >= 0 && tab < DeviceTabCount)
            SelectedTabIndex = tab;
    }

    /// <summary>Number of tabs in the window (Mapping, Macros, Settings, Profiles, About the keyboard).</summary>
    public const int DeviceTabCount = 5;

    /// <summary>Closes the session and goes back to the keyboard list.</summary>
    [RelayCommand]
    private void DisconnectDevice()
    {
        if (IsBusy) return;
        Disconnect();
        RefreshDevices();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice is null)
        {
            SetStatus(StatusSeverity.Warning, Loc.T("st_no_selection"));
            return;
        }
        if (IsBusy) return;

        DeviceInfo device = SelectedDevice;
        IsBusy = true;
        SetStatus(StatusSeverity.Info, Loc.F("st_connecting", device.ProductName));
        try
        {
            Disconnect();

            // Opening the HID interface and reading the version can take a moment on Linux
            // (hidraw enumeration and the first command); keep it off the UI thread so the
            // window keeps repainting and the progress bar stays alive.
            _protocol = await Task.Run(() => _factory.Create(device));
            IsConnected = _protocol is not null;
            _connectedPaths = IsConnected
                ? _monitor.Devices.Where(d => DeviceIdentity.SamePhysicalDevice(d, device))
                    .Select(d => d.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _idSpace = _protocol is XboxJpProtocol ? MappingIdSpace.Xbox : MappingIdSpace.Jp;
            NotifyFamilyChanged();
            if (IsConnected)
                StartInputListening(device);
            if (!IsConnected)
            {
                SetStatus(StatusSeverity.Error,
                    Loc.T("st_hid_failed"));
            }
            else if (_protocol!.CanWrite)
            {
                SetStatus(StatusSeverity.Success,
                    Loc.F("st_connected", device.ProductName));
            }
            else
            {
                SetStatus(StatusSeverity.Info,
                    Loc.F("st_connected_ro", device.ProductName));
            }
        }
        catch (Exception ex)
        {
            _protocol = null;
            IsConnected = false;
            NotifyFamilyChanged();
            SetStatus(StatusSeverity.Error, Loc.F("st_connect_failed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }

        // The picker is the whole "connect" gesture: the screen opens already filled.
        if (IsConnected)
            await ReadConfigAsync();
    }

    /// <summary>
    /// What to tell the user when an Xbox session reads nothing. The advice is the one measured on
    /// the hardware (2026-10-18): when the keyboard loses the dongle (or the PC goes off) it goes to
    /// sleep, and then <b>only a key press brings it back</b> — the wake frame (<c>cmd 0x08 sub 0</c>)
    /// was tried once and 19 times in a row without an answer, and the app has no way to press a key.
    /// </summary>
    private static string DescribeUnresponsive(XboxJpProtocol xbox)
    {
        var failure = xbox.LastFailure;
        string cause = failure switch
        {
            XboxFailure.DeviceError => Loc.T("unresp_device_error"),
            XboxFailure.NoAnswer or XboxFailure.None => Loc.T("unresp_no_answer"),
            _ => xbox.LastError ?? Loc.T("unresp_no_answer"),
        };
        return Loc.F(failure == XboxFailure.DeviceError ? "unresp_asleep" : "unresp_other", cause);
    }

    private void Disconnect()
    {
        StopInputListening();
        _protocol?.Dispose();
        _protocol = null;
        IsConnected = false;
        _loadedConfig = null;
        RefreshChanges();
        NotifyFamilyChanged();
    }

    /// <summary>Raises the family flags after the protocol (and therefore the family) changed.</summary>
    private void NotifyFamilyChanged()
    {
        OnPropertyChanged(nameof(ProfileNameMaxLength));
        OnPropertyChanged(nameof(IsXboxFamily));
        OnPropertyChanged(nameof(IsJpFamily));
        OnPropertyChanged(nameof(IsFamilyUnknown));
        OnPropertyChanged(nameof(CanSave));
    }
}
