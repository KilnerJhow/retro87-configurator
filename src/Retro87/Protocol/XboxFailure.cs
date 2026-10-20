// SPDX-License-Identifier: GPL-3.0-or-later
namespace Retro87.Protocol;

/// <summary>Why the last Xbox-family command gave up, as a code the UI can localize.</summary>
public enum XboxFailure
{
    None,
    NoAnswer,
    /// <summary>The device replied <c>0xC0</c>: asleep, or no keyboard behind the dongle.</summary>
    DeviceError,
    Other,
}
