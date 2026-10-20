// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Protocol;

using Retro87.Models;

/// <summary>
/// Wire-level constants of the 8BitDo Retro keyboard family ("JP").
/// </summary>
/// <remarks>
/// Framing:
/// <list type="bullet">
/// <item>a request is a 33-byte <b>output report</b>: report id <c>0x52</c> followed by 32
/// payload bytes.</item>
/// <item>the jump variant is the same with report id <c>0xB2</c>.</item>
/// <item>a read returns one input report, with a 100 ms wait; the report id is dropped, so
/// in this app <c>resp[i] == rawReport[i + 1]</c>.</item>
/// <item>every write is followed by a read loop waiting for the 2-byte acknowledgement
/// <c>0xE4 0x08</c>.</item>
/// <item>the feature-report path (id <c>7</c>, 64 bytes) is not used by the keyboard profile
/// protocol.</item>
/// </list>
/// The input report id is never asserted, because the reference code drops it.
/// </remarks>
internal static class JpWire
{
    /// <summary>Output report id used by all keyboard-family commands (native <c>0x52</c>).</summary>
    internal const byte ReportIdOut = 0x52;

    /// <summary>Output report id used by the "jump" writer (native <c>0xB2</c>); unused by v1.</summary>
    internal const byte ReportIdJump = 0xB2;

    /// <summary>Total length of a keyboard-family report: report id + 32 payload bytes (native <c>0x21</c>).</summary>
    internal const int FrameLength = 33;

    /// <summary>Payload length of a keyboard-family report (frame minus the report id).</summary>
    internal const int PayloadLength = FrameLength - 1;

    /// <summary>First byte of the write acknowledgement report.</summary>
    internal const byte AckFirst = 0xE4;

    /// <summary>Second byte of the write acknowledgement report.</summary>
    internal const byte AckSecond = 0x08;

    /// <summary>Delay the native code uses between a read attempt and the next one.</summary>
    internal const int ReadGapMs = 5;

    /// <summary>Native input-report read timeout (WaitForSingleObject, ms).</summary>
    internal const int ReadTimeoutMs = 100;

    /// <summary>Native retry bound of the polling Get* exports (0x1E reads).</summary>
    internal const int MaxReadAttempts = 0x1E;

    /// <summary>Native retry bound of <c>SwitchReport</c>/<c>SetFeature</c> (0x1E acks).</summary>
    internal const int AckAttempts = 0x1E;

    /// <summary>Retry bound of <c>_SetMapping@8</c> (0x14 acks).</summary>
    internal const int MappingAckAttempts = 0x14;

    /// <summary>Retry bound of <c>_setVoice@4</c> (0x14 acks, 100 ms apart).</summary>
    internal const int VoiceAckAttempts = 0x14;

    /// <summary>Native gap of the mapping writers' acknowledgement loop (<c>Sleep(10)</c>).</summary>
    internal const int MappingGapMs = 10;

    /// <summary>Native gap of the voice writer's acknowledgement loop (<c>Sleep(100)</c>).</summary>
    internal const int VoiceGapMs = 100;

    /// <summary>Native gap of the macro writers' acknowledgement loop (<c>Sleep(10)</c>).</summary>
    internal const int MacroWriteGapMs = 10;

    /// <summary>Retry bound of the macro writers' acknowledgement loop (400).</summary>
    internal const int MacroWriteAckAttempts = 400;

    /// <summary>
    /// Body bytes carried by the header chunk of <c>writeMacroJP</c> (native copies 0x14,
    /// this port writes 0x15 — see <see cref="JpProtocol.WriteMacro"/>).
    /// </summary>
    internal const int MacroPayloadLengthLong = 0x15;

    /// <summary>Body bytes carried by a continuation chunk of <c>writeMacroJP</c> (0x18).</summary>
    internal const int MacroChunkLength = 0x18;

    /// <summary>Command bytes (native <c>_Get*</c>/<c>_Set*</c>/<c>_write*</c> exports).</summary>
    internal static class Cmd
    {
        internal const byte GetBattery = 0x06;
        internal const byte WriteProfileName = 0x70;
        internal const byte WriteMacroName = 0x74;
        internal const byte SwitchReport = 0x76;
        internal const byte WriteMacro = 0x76;
        internal const byte SetFeature = 0x78;
        internal const byte SetVoice = 0x79;
        internal const byte SwitchReportExtra = 0x7B;
        internal const byte GetProfileName = 0x80;
        internal const byte GetMapping = 0x81;
        internal const byte GetMacro = 0x82;
        internal const byte GetMappingValue = 0x83;
        internal const byte GetMacroName = 0x84;
        internal const byte GetMacroValue = 0x86;
        internal const byte GetSpecial = 0x88;
        internal const byte GetVoice = 0x89;
    }

    /// <summary>
    /// Fixed 7-byte prelude shared by <c>_SetMapping@8</c>/<c>_SetShortCut@8</c>/<c>_SetMouse@8</c>
    /// Parameters: key id, then the transport byte, then the payload — Evidence
    /// </summary>
    internal static readonly byte[] MappingPrelude = { 0xFA, 0x03, 0x0C, 0x00, 0xAA, 0x09, 0x71 };

    /// <summary>Transport selector for a plain keyboard remap (native 0x07).</summary>
    internal const byte MappingKindKeyboard = MappingPayloadCodec.TransportKeyboard;

    /// <summary>Transport selector for a media/shortcut action (native 0x0C).</summary>
    internal const byte MappingKindShortcut = MappingPayloadCodec.TransportShortcut;

    /// <summary>Transport selector for a mouse action (native 0x01).</summary>
    internal const byte MappingKindMouse = MappingPayloadCodec.TransportMouse;

    /// <summary>
    /// Payload bytes accepted by each transport: 4 for keyboard/shortcut, 5 for mouse
    /// (native <c>_SetMapping</c>/<c>_SetShortCut</c> copy 4 bytes, <c>_SetMouse</c> copies 5).
    /// </summary>
    internal const int MappingValueLengthKeyboard = 4;
    internal const int MappingValueLengthMouse = 5;

    /// <summary>Chunk size of <c>writeName</c> (native 0x1D).</summary>
    internal const int ProfileNameChunk = 29;

    /// <summary>Profile/macro name length on the wire (native copies 0x1D + 0x1D + 3 = 61).</summary>
    internal const int NameLength = 61;

    /// <summary>Chunk size of <c>writeMacroName</c> (native 0x1C).</summary>
    internal const int MacroNameChunk = 28;

    /// <summary>Macro action/data area returned by <c>GetMacroValue</c> (native 0x261 = 609).</summary>
    internal const int MacroRecordLength = 609;

    /// <summary>Macro header length inside <see cref="MacroRecordLength"/>: index/flags/count.</summary>
    internal const int MacroHeaderLength = 4;

    /// <summary>Marker byte at the start of a macro record (native writes <c>0x01</c>).</summary>
    internal const byte MacroRecordMarker = 0x01;

    /// <summary>Flag byte meaning "loop forever" in a macro record (native <c>0x20</c>).</summary>
    internal const byte MacroInfiniteFlag = 0x20;

    /// <summary>First byte of a macro delay step (native <c>0x0F</c>).</summary>
    internal const byte MacroStepTime = 0x0F;

    /// <summary>Cycle value that the native code stores for an infinite-loop macro.</summary>
    internal const byte MacroCyclesInfinite = 0xFF;

    /// <summary>Number of board positions (native <c>board_key_t_JP.key[95]</c>).</summary>
    internal const int BoardPositionCount = 95;

    /// <summary>Number of macro slots (native <c>jP_Macro_Fun_Record_T[8]</c>).</summary>
    internal const int MacroSlotCount = 8;

    /// <summary>Bytes per macro step (native reads/writes 3 bytes per step).</summary>
    internal const int MacroStepBytes = 3;
}
