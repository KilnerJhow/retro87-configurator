// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Protocol;

/// <summary>
/// What the keyboard did when a single <c>cmd 0x0A</c> probe asked it to answer <b>right now</b>.
/// </summary>
/// <remarks>
/// The three states matter because they mean different things: <see cref="Awake"/> is a normal
/// answer; <see cref="ErrorReply"/> is the device's own <c>02 04 C0 …</c> (the dongle says it has
/// no keyboard answering — asleep, or powered off, or the dongle has no keyboard linked);
/// <see cref="NoAnswer"/> is nobody answering at all (the collection was not there, or the reply
/// never came). Telling them apart is what turns "it did not answer" into a diagnosis.
/// </remarks>
internal enum XboxProbeState
{
    Awake,
    ErrorReply,
    NoAnswer,
}

/// <summary>Classifies and describes the result of a one-shot probe (shared by the sleep tools).</summary>
internal static class XboxProbe
{
    /// <summary>Classifies a probe result: answered, the device's <c>0xC0</c>, or silence.</summary>
    internal static XboxProbeState Classify(bool answered, string? reason)
    {
        if (answered) return XboxProbeState.Awake;
        // The 0xC0 diagnosis is the one Query/TryReadReply leaves in LastError / the ping detail.
        return reason?.Contains("0xC0") == true ? XboxProbeState.ErrorReply : XboxProbeState.NoAnswer;
    }

    /// <summary>Short human-readable name of a state.</summary>
    internal static string Describe(XboxProbeState state) => state switch
    {
        XboxProbeState.Awake => "awake (answered)",
        XboxProbeState.ErrorReply => "asleep (the device answered error 0xC0)",
        _ => "no answer (not even error 0xC0)",
    };
}
