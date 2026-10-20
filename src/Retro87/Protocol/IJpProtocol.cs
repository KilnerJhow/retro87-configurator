// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Models;

namespace Retro87.Protocol;

/// <summary>
/// Device-level protocol for the 8BitDo Retro keyboard family ("JP").
/// Covers version/battery reporting, profile name, mapping read/write and macro
/// read/write, implemented over <see cref="IKeyboardTransport"/> so it needs no vendor
/// library.
/// </summary>
/// <remarks>
/// The concrete implementation <see cref="JpProtocol"/> exposes every command individually;
/// this interface is the shape the view-models use.
/// </remarks>
public interface IJpProtocol : IDisposable
{
    bool IsOpen { get; }

    /// <summary>
    /// True when this session may write to the device. Both known families allow it today; the
    /// UI is what asks the user for permission before any write (project rule).
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Firmware version of the keyboard, or 0 when unknown. This family has no HID command
    /// that reports it (the official software shells out to a bundled firmware tool), so an app on
    /// Linux currently always gets 0 — see <see cref="JpProtocol.DeviceVersion"/>.
    /// </summary>
    float DeviceVersion { get; }

    /// <summary>Battery level in percent from the last version read, or -1 when unknown.</summary>
    int BatteryPercent { get; }

    bool Open();
    void Close();

    /// <summary>Reads the whole configuration (profile name, mappings, macros, special, voice).</summary>
    KeyboardConfig ReadConfig();

    /// <summary>
    /// Rewrites the current profile name unchanged, the way a real write batch does (inside the
    /// session open/close pair on the Xbox family). The dongle only streams its raw vendor report
    /// (how a port with no mapped target is identified) after a write batch like this reaches the
    /// device — normally the official app's doing. Nothing changes on the keyboard.
    /// </summary>
    bool WakeRawSignal(string profileName);

    /// <summary>
    /// Writes the configuration back to the keyboard. With <paramref name="force"/> false only
    /// board keys whose target differs from their default are sent, which is what the
    /// original software does.
    /// </summary>
    bool WriteConfig(KeyboardConfig config, bool force = false);
}

/// <summary>
/// Optional surface a protocol exposes when its family can write: the raw configuration as last
/// read (so the UI can back it up before writing) and notes about edits that could not be turned
/// into a write. The JP family predates this and does not implement it.
/// </summary>
public interface IWriteDiagnostics
{
    /// <summary>Raw configuration as last read from the device, or null when none was read.</summary>
    byte[]? LastRawConfig { get; }

    /// <summary>Notes from the last <see cref="IJpProtocol.WriteConfig"/> (empty when none).</summary>
    IReadOnlyList<string> LastWriteWarnings { get; }
}
