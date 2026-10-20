// SPDX-License-Identifier: GPL-3.0-or-later

namespace Retro87.Models;

/// <summary>
/// Key-to-payload mapping ids as used on the wire by the Retro keyboard family.
/// The values are interoperability data confirmed against the device; the constant
/// names are descriptive labels for those values. See docs/PROVENANCE.md.
/// </summary>
public static class MappingIds
{
    /// <summary>Unmapped / null payload (<c>the null payload id</c>, default-initialised to 0).</summary>
    public const byte NULL_MAP = 0;

    public const byte A_MAP = 4;
    public const byte B_MAP = 5;
    public const byte C_MAP = 6;
    public const byte D_MAP = 7;
    public const byte E_MAP = 8;
    public const byte F_MAP = 9;
    public const byte G_MAP = 10;
    public const byte H_MAP = 11;
    public const byte I_MAP = 12;
    public const byte J_MAP = 13;
    public const byte K_MAP = 14;
    public const byte L_MAP = 15;
    public const byte M_MAP = 16;
    public const byte N_MAP = 17;
    public const byte O_MAP = 18;
    public const byte P_MAP = 19;
    public const byte Q_MAP = 20;
    public const byte R_MAP = 21;
    public const byte S_MAP = 22;
    public const byte T_MAP = 23;
    public const byte U_MAP = 24;
    public const byte V_MAP = 25;
    public const byte W_MAP = 26;
    public const byte X_MAP = 27;
    public const byte Y_MAP = 28;
    public const byte Z_MAP = 29;

    public const byte N1_MAP = 30;
    public const byte N2_MAP = 31;
    public const byte N3_MAP = 32;
    public const byte N4_MAP = 33;
    public const byte N5_MAP = 34;
    public const byte N6_MAP = 35;
    public const byte N7_MAP = 36;
    public const byte N8_MAP = 37;
    public const byte N9_MAP = 38;
    public const byte N0_MAP = 39;

    public const byte ENTER_MAP = 40;
    public const byte ESC_MAP = 41;
    public const byte BACKSPACE_MAP = 42;
    public const byte TAB_MAP = 43;
    public const byte SPACE_MAP = 44;
    public const byte MINUS_MAP = 45;
    public const byte EQUAL_CLICK = 46;
    public const byte LEFTBRACKET_MAP = 47;
    public const byte RIGHTBRACKET_MAP = 48;
    public const byte VERTICAL_MAP = 49;
    public const byte MENU_MAP = 50;
    public const byte COLON_MAP = 51;
    public const byte QUOTATION_MAP = 52;
    public const byte TILDE_MAP = 53;
    public const byte LESS_MAP = 54;
    public const byte GREATER_MAP = 55;
    public const byte QUESTION_MAP = 56;
    public const byte CAPSLOCK_MAP = 57;

    public const byte F1_MAP = 58;
    public const byte F2_MAP = 59;
    public const byte F3_MAP = 60;
    public const byte F4_MAP = 61;
    public const byte F5_MAP = 62;
    public const byte F6_MAP = 63;
    public const byte F7_MAP = 64;
    public const byte F8_MAP = 65;
    public const byte F9_MAP = 66;
    public const byte F10_MAP = 67;
    public const byte F11_MAP = 68;
    public const byte F12_MAP = 69;

    public const byte PRTSC_MAP = 70;
    public const byte SCRLK_MAP = 71;
    public const byte PAUSE_MAP = 72;
    public const byte INSERT_MAP = 73;
    public const byte HOME_MAP = 74;
    public const byte PGUP_MAP = 75;
    public const byte DELETE_MAP = 76;
    public const byte END_MAP = 77;
    public const byte PGDN_MAP = 78;
    public const byte RIGHT_MAP = 79;
    public const byte LEFT_MAP = 80;
    public const byte DOWN_MAP = 81;
    public const byte UP_MAP = 82;

    public const byte NUMLOCK_MAP = 83;
    public const byte DIVIDE_MAP = 84;
    public const byte MULTIPLY_MAP = 85;

    // Modifiers: the original declares these all together as 100..107 (it does not follow HID usage ids).
    public const byte LCTRL_MAP = 100;
    public const byte LSHIFT_MAP = 101;
    public const byte LALT_MAP = 102;
    public const byte LWIN_MAP = 103;
    public const byte RCTRL_MAP = 104;
    public const byte RSHIFT_MAP = 105;
    public const byte RALT_MAP = 106;
    public const byte RWIN_MAP = 107;

    public const byte FN_MAP = 3;

    // The original assigns the KA/KB and K1..K8 shortcuts out of order (see field initialisers).
    public const byte KA_MAP = 109;
    public const byte KB_MAP = 108;
    public const byte K1_MAP = 116;
    public const byte K2_MAP = 117;
    public const byte K3_MAP = 114;
    public const byte K4_MAP = 115;
    public const byte K5_MAP = 112;
    public const byte K6_MAP = 113;
    public const byte K7_MAP = 110;
    public const byte K8_MAP = 111;

    public const byte NUMMINUS_MAP = 86;
    public const byte NUMPLUS_MAP = 87;
    public const byte NUMENTER_MAP = 88;
    public const byte NUM1_MAP = 89;
    public const byte NUM2_MAP = 90;
    public const byte NUM3_MAP = 91;
    public const byte NUM4_MAP = 92;
    public const byte NUM5_MAP = 93;
    public const byte NUM6_MAP = 94;
    public const byte NUM7_MAP = 95;
    public const byte NUM8_MAP = 96;
    public const byte NUM9_MAP = 97;
    public const byte NUM0_MAP = 98;
    public const byte NUMDOT_MAP = 99;

    // Mouse codes.
    public const byte LEFT_CLICK = 118;
    public const byte RIGHT_CLICK = 119;
    public const byte MIDDLE_CLICK = 120;
    public const byte FOUR_CLICK = 121;
    public const byte FIVE_CLICK = 122;
    public const byte TWO_TIME = 123;
    public const byte UP_ROLL = 124;
    public const byte DOWN_ROLL = 125;
    public const byte LEFT_ROLL = 126;
    public const byte RIGHT_ROLL = 127;

    // Second-block symbol key codes (the "CLICK" family).
    public const byte WAVE_CLICK = 128;
    public const byte POINT_CLICK = 129;
    public const byte AITE_CLICK = 130;
    public const byte WELL_CLICK = 131;
    public const byte UNIT_CLICK = 132;
    public const byte PERCENT_CLICK = 133;
    public const byte DIVIDE_CLICK = 134;
    public const byte WITH_CLICK = 135;
    public const byte MULTIPLY_CLICK = 136;
    public const byte LEFTBRACKET_CLICK1 = 137;
    public const byte RIGHTBRACKET_CLICK1 = 138;
    public const byte MINUS_CLICK1 = 139;
    public const byte PLUS_MAP = 140;
    public const byte LEFTBRACKET_CLICK2 = 141;
    public const byte RIGHTBRACKET_CLICK2 = 142;
    public const byte VERTICAL_CLICK2 = 143;
    public const byte COLON_CLICK1 = 144;
    public const byte QUOTATION_CLICK1 = 145;
    public const byte LESS_CLICK1 = 146;
    public const byte GREATER_CLICK1 = 147;
    public const byte QUESTION_MAP1 = 148;

    // Media / voice / brightness shortcuts.
    public const byte VoiceLow_MAP = 149;
    public const byte VoiceUp_MAP = 150;
    public const byte NoVoice_MAP = 151;
    public const byte Play_MAP = 152;
    public const byte Last_MAP = 153;
    public const byte Next_MAP = 154;
    public const byte Mail_MAP = 155;
    public const byte Computer_MAP = 156;
    public const byte Counter_MAP = 157;
    public const byte Explorer_MAP = 158;
    public const byte BrightLow_MAP = 159;
    public const byte BrightUp_MAP = 160;

    // Extended function keys. Note the gap: there is no 166 in the original.
    public const byte F13_MAP = 161;
    public const byte F14_MAP = 162;
    public const byte F15_MAP = 163;
    public const byte F16_MAP = 164;
    public const byte F17_MAP = 165;
    public const byte F18_MAP = 167;
    public const byte F19_MAP = 168;
    public const byte F20_MAP = 169;
    public const byte F21_MAP = 170;
    public const byte F22_MAP = 171;
    public const byte F23_MAP = 172;
    public const byte F24_MAP = 173;
}

/// <summary>
/// Keyboard key enumeration with explicit numeric values, so the wire-level
/// ordinals are preserved exactly.
/// </summary>
public enum KeyboardEnum : int
{
    A = 0, B = 1, C = 2, D = 3, E = 4, F = 5, G = 6, H = 7, I = 8,
    J = 9, K = 10, L = 11, M = 12, N = 13, O = 14, P = 15, Q = 16,
    R = 17, S = 18, T = 19, U = 20, V = 21, W = 22, X = 23, Y = 24, Z = 25,
    num0 = 26, num1 = 27, num2 = 28, num3 = 29, num4 = 30, num5 = 31,
    num6 = 32, num7 = 33, num8 = 34, num9 = 35,
    F1 = 36, F2 = 37, F3 = 38, F4 = 39, F5 = 40, F6 = 41, F7 = 42,
    F8 = 43, F9 = 44, F10 = 45, F11 = 46, F12 = 47,
    F13 = 48, F14 = 49, F15 = 50, F16 = 51, F17 = 52, F18 = 53,
    F19 = 54, F20 = 55, F21 = 56, F22 = 57, F23 = 58, F24 = 59,
    Numlock = 60,
    num0_1 = 61, num1_1 = 62, num2_1 = 63, num3_1 = 64, num4_1 = 65,
    num5_1 = 66, num6_1 = 67, num7_1 = 68, num8_1 = 69, num9_1 = 70,
    num_Minus = 71, num_Divide = 72, num_Multiply = 73, num_Plus = 74,
    Dot = 75, num_Enter = 76,
    Up = 77, Down = 78, Left = 79, Right = 80, PGUP = 81, PGDN = 82,
    BACKSPACE = 83, TAB = 84, Space = 85, Enter = 86, Esc = 87,
    INSERT = 88, HOME = 89, DELETE = 90, END = 91, PRTSC = 92, PAUSE = 93,
    LCtrl = 94, RCtrl = 95, LShift = 96, RShift = 97,
    LAlt = 98, RAlt = 99, LWin = 100, RWin = 101,
    CAPSLOCK = 102, SCRLK = 103, Fn = 104, Menu = 105,
    Tilde = 106, Wave = 107, Point = 108, Aite = 109, Well = 110,
    Unit = 111, Percent = 112, Divide = 113, With = 114, Multiply = 115,
    LeftBracket = 116, RightBracket = 117, Minus = 118, Equal = 119,
    Minus1 = 120, Plus = 121,
    LeftBracket1 = 122, RightBracket1 = 123, Vertical = 124,
    LeftBracket2 = 125, RightBracket2 = 126, Vertical1 = 127,
    Colon = 128, Quotation = 129, Colon1 = 130, Quotation1 = 131,
    Less = 132, Greater = 133, Question = 134,
    Less1 = 135, Greater1 = 136, Question1 = 137,
    KB = 138, KA = 139, K1 = 140, K2 = 141, K3 = 142, K4 = 143,
    K5 = 144, K6 = 145, K7 = 146, K8 = 147,
    Left_Click = 148, Right_Click = 149, Middle_Click = 150,
    Four_Time = 151, Five_Time = 152, Two_Time = 153,
    Up_Roll = 154, Down_Roll = 155, Left_Roll = 156, Right_Roll = 157,
    VoiceLow = 158, VoiceUp = 159, NoVoice = 160, Play = 161, Last = 162,
    Next = 163, Mail = 164, Computer = 165, Counter = 166, Explorer = 167,
    BrightLow = 168, BrightUp = 169,
    None = 170, empty = 171,
}

/// <summary>
/// Category of a mapping id: mouse, shortcut, or plain keyboard.
/// </summary>
public enum KeyboardTransport
{
    Keyboard,
    Mouse,
    Shortcut,
}

/// <summary>
/// Kind of a single macro step as encoded by <c>the step encoder</c>.
/// Mirrors the meaningful subset of the original <c>macro action</c>.
/// </summary>
public enum MacroStepKind
{
    /// <summary>No step (the original leaves the 3-byte buffer zeroed).</summary>
    Null,

    /// <summary>Delay step, encoded as [0x0F, delayLo, delayHi].</summary>
    Time,

    /// <summary>Key press, encoded as [0x81, usage, 0x00] (modifiers use 0x83).</summary>
    KeyDown,

    /// <summary>Key release, encoded as [0x01, usage, 0x00] (modifiers use 0x03).</summary>
    KeyUp,
}

/// <summary>
/// Wire-level mapping tables for the Retro keyboard family: the raw id ↔ target
/// conversions the firmware expects.
/// </summary>
public static class MappingTables
{
    /// <summary>
    /// Converts a raw mapping id (one of the <see cref="MappingIds"/> constants) back to a
    /// <see cref="KeyboardEnum"/>. Unrecognised ids return <see cref="KeyboardEnum.None"/>.
    /// </summary>
    public static KeyboardEnum ToKeyboardEnum(byte mapKey) => mapKey switch
    {
        MappingIds.A_MAP => KeyboardEnum.A,
        MappingIds.B_MAP => KeyboardEnum.B,
        MappingIds.C_MAP => KeyboardEnum.C,
        MappingIds.D_MAP => KeyboardEnum.D,
        MappingIds.E_MAP => KeyboardEnum.E,
        MappingIds.F_MAP => KeyboardEnum.F,
        MappingIds.G_MAP => KeyboardEnum.G,
        MappingIds.H_MAP => KeyboardEnum.H,
        MappingIds.I_MAP => KeyboardEnum.I,
        MappingIds.J_MAP => KeyboardEnum.J,
        MappingIds.K_MAP => KeyboardEnum.K,
        MappingIds.L_MAP => KeyboardEnum.L,
        MappingIds.M_MAP => KeyboardEnum.M,
        MappingIds.N_MAP => KeyboardEnum.N,
        MappingIds.O_MAP => KeyboardEnum.O,
        MappingIds.P_MAP => KeyboardEnum.P,
        MappingIds.Q_MAP => KeyboardEnum.Q,
        MappingIds.R_MAP => KeyboardEnum.R,
        MappingIds.S_MAP => KeyboardEnum.S,
        MappingIds.T_MAP => KeyboardEnum.T,
        MappingIds.U_MAP => KeyboardEnum.U,
        MappingIds.V_MAP => KeyboardEnum.V,
        MappingIds.W_MAP => KeyboardEnum.W,
        MappingIds.X_MAP => KeyboardEnum.X,
        MappingIds.Y_MAP => KeyboardEnum.Y,
        MappingIds.Z_MAP => KeyboardEnum.Z,

        MappingIds.N1_MAP => KeyboardEnum.num1,
        MappingIds.N2_MAP => KeyboardEnum.num2,
        MappingIds.N3_MAP => KeyboardEnum.num3,
        MappingIds.N4_MAP => KeyboardEnum.num4,
        MappingIds.N5_MAP => KeyboardEnum.num5,
        MappingIds.N6_MAP => KeyboardEnum.num6,
        MappingIds.N7_MAP => KeyboardEnum.num7,
        MappingIds.N8_MAP => KeyboardEnum.num8,
        MappingIds.N9_MAP => KeyboardEnum.num9,
        MappingIds.N0_MAP => KeyboardEnum.num0,

        MappingIds.ENTER_MAP => KeyboardEnum.Enter,
        MappingIds.ESC_MAP => KeyboardEnum.Esc,
        MappingIds.BACKSPACE_MAP => KeyboardEnum.BACKSPACE,
        MappingIds.TAB_MAP => KeyboardEnum.TAB,
        MappingIds.SPACE_MAP => KeyboardEnum.Space,
        MappingIds.MINUS_MAP => KeyboardEnum.Minus,
        MappingIds.PLUS_MAP => KeyboardEnum.Plus,
        MappingIds.LEFTBRACKET_MAP => KeyboardEnum.LeftBracket,
        MappingIds.RIGHTBRACKET_MAP => KeyboardEnum.RightBracket,
        MappingIds.VERTICAL_MAP => KeyboardEnum.Vertical,
        MappingIds.MENU_MAP => KeyboardEnum.Menu,
        MappingIds.COLON_MAP => KeyboardEnum.Colon,
        MappingIds.QUOTATION_MAP => KeyboardEnum.Quotation,
        MappingIds.TILDE_MAP => KeyboardEnum.Tilde,
        MappingIds.LESS_MAP => KeyboardEnum.Less,
        MappingIds.GREATER_MAP => KeyboardEnum.Greater,
        MappingIds.QUESTION_MAP => KeyboardEnum.Question,
        MappingIds.CAPSLOCK_MAP => KeyboardEnum.CAPSLOCK,

        MappingIds.F1_MAP => KeyboardEnum.F1,
        MappingIds.F2_MAP => KeyboardEnum.F2,
        MappingIds.F3_MAP => KeyboardEnum.F3,
        MappingIds.F4_MAP => KeyboardEnum.F4,
        MappingIds.F5_MAP => KeyboardEnum.F5,
        MappingIds.F6_MAP => KeyboardEnum.F6,
        MappingIds.F7_MAP => KeyboardEnum.F7,
        MappingIds.F8_MAP => KeyboardEnum.F8,
        MappingIds.F9_MAP => KeyboardEnum.F9,
        MappingIds.F10_MAP => KeyboardEnum.F10,
        MappingIds.F11_MAP => KeyboardEnum.F11,
        MappingIds.F12_MAP => KeyboardEnum.F12,

        MappingIds.PRTSC_MAP => KeyboardEnum.PRTSC,
        MappingIds.SCRLK_MAP => KeyboardEnum.SCRLK,
        MappingIds.PAUSE_MAP => KeyboardEnum.PAUSE,
        MappingIds.INSERT_MAP => KeyboardEnum.INSERT,
        MappingIds.HOME_MAP => KeyboardEnum.HOME,
        MappingIds.PGUP_MAP => KeyboardEnum.PGUP,
        MappingIds.DELETE_MAP => KeyboardEnum.DELETE,
        MappingIds.END_MAP => KeyboardEnum.END,
        MappingIds.PGDN_MAP => KeyboardEnum.PGDN,
        MappingIds.RIGHT_MAP => KeyboardEnum.Right,
        MappingIds.LEFT_MAP => KeyboardEnum.Left,
        MappingIds.DOWN_MAP => KeyboardEnum.Down,
        MappingIds.UP_MAP => KeyboardEnum.Up,

        MappingIds.NUMLOCK_MAP => KeyboardEnum.Numlock,
        MappingIds.DIVIDE_MAP => KeyboardEnum.num_Divide,
        MappingIds.MULTIPLY_MAP => KeyboardEnum.num_Multiply,

        MappingIds.LCTRL_MAP => KeyboardEnum.LCtrl,
        MappingIds.LSHIFT_MAP => KeyboardEnum.LShift,
        MappingIds.LALT_MAP => KeyboardEnum.LAlt,
        MappingIds.LWIN_MAP => KeyboardEnum.LWin,
        MappingIds.RCTRL_MAP => KeyboardEnum.RCtrl,
        MappingIds.RSHIFT_MAP => KeyboardEnum.RShift,
        MappingIds.RALT_MAP => KeyboardEnum.RAlt,
        MappingIds.RWIN_MAP => KeyboardEnum.RWin,
        MappingIds.FN_MAP => KeyboardEnum.Fn,

        MappingIds.KA_MAP => KeyboardEnum.KA,
        MappingIds.KB_MAP => KeyboardEnum.KB,
        MappingIds.K1_MAP => KeyboardEnum.K1,
        MappingIds.K2_MAP => KeyboardEnum.K2,
        MappingIds.K3_MAP => KeyboardEnum.K3,
        MappingIds.K4_MAP => KeyboardEnum.K4,
        MappingIds.K5_MAP => KeyboardEnum.K5,
        MappingIds.K6_MAP => KeyboardEnum.K6,
        MappingIds.K7_MAP => KeyboardEnum.K7,
        MappingIds.K8_MAP => KeyboardEnum.K8,

        MappingIds.EQUAL_CLICK => KeyboardEnum.Equal,
        MappingIds.NUMENTER_MAP => KeyboardEnum.num_Enter,
        MappingIds.NUM1_MAP => KeyboardEnum.num1_1,
        MappingIds.NUM2_MAP => KeyboardEnum.num2_1,
        MappingIds.NUM3_MAP => KeyboardEnum.num3_1,
        MappingIds.NUM4_MAP => KeyboardEnum.num4_1,
        MappingIds.NUM5_MAP => KeyboardEnum.num5_1,
        MappingIds.NUM6_MAP => KeyboardEnum.num6_1,
        MappingIds.NUM7_MAP => KeyboardEnum.num7_1,
        MappingIds.NUM8_MAP => KeyboardEnum.num8_1,
        MappingIds.NUM9_MAP => KeyboardEnum.num9_1,
        MappingIds.NUM0_MAP => KeyboardEnum.num0_1,
        MappingIds.NUMDOT_MAP => KeyboardEnum.Dot,
        MappingIds.NUMMINUS_MAP => KeyboardEnum.num_Minus,
        MappingIds.NUMPLUS_MAP => KeyboardEnum.num_Plus,

        MappingIds.LEFT_CLICK => KeyboardEnum.Left_Click,
        MappingIds.RIGHT_CLICK => KeyboardEnum.Right_Click,
        MappingIds.MIDDLE_CLICK => KeyboardEnum.Middle_Click,
        MappingIds.FOUR_CLICK => KeyboardEnum.Four_Time,
        MappingIds.FIVE_CLICK => KeyboardEnum.Five_Time,
        MappingIds.TWO_TIME => KeyboardEnum.Two_Time,
        MappingIds.UP_ROLL => KeyboardEnum.Up_Roll,
        MappingIds.DOWN_ROLL => KeyboardEnum.Down_Roll,
        MappingIds.LEFT_ROLL => KeyboardEnum.Left_Roll,
        MappingIds.RIGHT_ROLL => KeyboardEnum.Right_Roll,

        MappingIds.WAVE_CLICK => KeyboardEnum.Wave,
        MappingIds.POINT_CLICK => KeyboardEnum.Point,
        MappingIds.AITE_CLICK => KeyboardEnum.Aite,
        MappingIds.WELL_CLICK => KeyboardEnum.Well,
        MappingIds.UNIT_CLICK => KeyboardEnum.Unit,
        MappingIds.PERCENT_CLICK => KeyboardEnum.Percent,
        MappingIds.DIVIDE_CLICK => KeyboardEnum.Divide,
        MappingIds.WITH_CLICK => KeyboardEnum.With,
        MappingIds.MULTIPLY_CLICK => KeyboardEnum.Multiply,
        MappingIds.LEFTBRACKET_CLICK1 => KeyboardEnum.LeftBracket1,
        MappingIds.RIGHTBRACKET_CLICK1 => KeyboardEnum.RightBracket1,
        MappingIds.MINUS_CLICK1 => KeyboardEnum.Minus1,
        MappingIds.LEFTBRACKET_CLICK2 => KeyboardEnum.LeftBracket2,
        MappingIds.RIGHTBRACKET_CLICK2 => KeyboardEnum.RightBracket2,
        MappingIds.VERTICAL_CLICK2 => KeyboardEnum.Vertical1,
        MappingIds.COLON_CLICK1 => KeyboardEnum.Colon1,
        MappingIds.QUOTATION_CLICK1 => KeyboardEnum.Quotation1,
        MappingIds.LESS_CLICK1 => KeyboardEnum.Less1,
        MappingIds.GREATER_CLICK1 => KeyboardEnum.Greater1,
        MappingIds.QUESTION_MAP1 => KeyboardEnum.Question1,

        MappingIds.VoiceLow_MAP => KeyboardEnum.VoiceLow,
        MappingIds.VoiceUp_MAP => KeyboardEnum.VoiceUp,
        MappingIds.NoVoice_MAP => KeyboardEnum.NoVoice,
        MappingIds.Play_MAP => KeyboardEnum.Play,
        MappingIds.Last_MAP => KeyboardEnum.Last,
        MappingIds.Next_MAP => KeyboardEnum.Next,
        MappingIds.Mail_MAP => KeyboardEnum.Mail,
        MappingIds.Computer_MAP => KeyboardEnum.Computer,
        MappingIds.Counter_MAP => KeyboardEnum.Counter,
        MappingIds.Explorer_MAP => KeyboardEnum.Explorer,
        MappingIds.BrightLow_MAP => KeyboardEnum.BrightLow,
        MappingIds.BrightUp_MAP => KeyboardEnum.BrightUp,

        MappingIds.F13_MAP => KeyboardEnum.F13,
        MappingIds.F14_MAP => KeyboardEnum.F14,
        MappingIds.F15_MAP => KeyboardEnum.F15,
        MappingIds.F16_MAP => KeyboardEnum.F16,
        MappingIds.F17_MAP => KeyboardEnum.F17,
        MappingIds.F18_MAP => KeyboardEnum.F18,
        MappingIds.F19_MAP => KeyboardEnum.F19,
        MappingIds.F20_MAP => KeyboardEnum.F20,
        MappingIds.F21_MAP => KeyboardEnum.F21,
        MappingIds.F22_MAP => KeyboardEnum.F22,
        MappingIds.F23_MAP => KeyboardEnum.F23,
        MappingIds.F24_MAP => KeyboardEnum.F24,

        _ => KeyboardEnum.None,
    };

    /// <summary>
    /// Classifies a raw mapping id:
    /// the ten mouse codes map to <see cref="KeyboardTransport.Mouse"/>, the
    /// media/voice/brightness set maps to <see cref="KeyboardTransport.Shortcut"/>,
    /// and everything else (including <see cref="KeyboardEnum.None"/>) maps to
    /// <see cref="KeyboardTransport.Keyboard"/>.
    /// </summary>
    /// <remarks>
    /// Note that <see cref="MappingIds.Computer_MAP"/> is deliberately NOT part of the
    /// shortcut set: the original's shortcut comparison chain does not test for it.
    /// </remarks>
    public static KeyboardTransport TransportFor(byte mapKey)
    {
        if (mapKey is MappingIds.LEFT_CLICK
                    or MappingIds.RIGHT_CLICK
                    or MappingIds.MIDDLE_CLICK
                    or MappingIds.FOUR_CLICK
                    or MappingIds.FIVE_CLICK
                    or MappingIds.TWO_TIME
                    or MappingIds.UP_ROLL
                    or MappingIds.DOWN_ROLL
                    or MappingIds.LEFT_ROLL
                    or MappingIds.RIGHT_ROLL)
        {
            return KeyboardTransport.Mouse;
        }

        if (mapKey is MappingIds.VoiceLow_MAP
                    or MappingIds.VoiceUp_MAP
                    or MappingIds.NoVoice_MAP
                    or MappingIds.Play_MAP
                    or MappingIds.Last_MAP
                    or MappingIds.Next_MAP
                    or MappingIds.Mail_MAP
                    or MappingIds.Counter_MAP
                    or MappingIds.Explorer_MAP
                    or MappingIds.BrightLow_MAP
                    or MappingIds.BrightUp_MAP)
        {
            return KeyboardTransport.Shortcut;
        }

        return KeyboardTransport.Keyboard;
    }

    /// <summary>
    /// Returns the 5-byte mapping payload for a target key. Index 4 is only used by the
    /// horizontal scroll-wheel codes.
    /// </summary>
    /// <param name="key">Target key.</param>
    /// <param name="is108Jp">
    /// Models the original's <c>VIDPID.PID_Current == VIDPID.PID_108JP</c> branch, which only
    /// changes the F13..F24 payloads. Defaults to <c>false</c> (the non-108JP path).
    /// </param>
    public static byte[] GetUint(KeyboardEnum key, bool is108Jp = false) => key switch
    {
        // Mouse.
        KeyboardEnum.Left_Click => new byte[] { 1, 0, 0, 0, 0 },
        KeyboardEnum.Right_Click => new byte[] { 2, 0, 0, 0, 0 },
        KeyboardEnum.Middle_Click => new byte[] { 4, 0, 0, 0, 0 },
        KeyboardEnum.Four_Time => new byte[] { 16, 0, 0, 0, 0 },
        KeyboardEnum.Five_Time => new byte[] { 8, 0, 0, 0, 0 },
        KeyboardEnum.Two_Time => new byte[] { 128, 0, 0, 0, 0 },
        KeyboardEnum.Up_Roll => new byte[] { 0, 0, 0, 2, 0 },
        KeyboardEnum.Down_Roll => new byte[] { 0, 0, 0, 254, 0 },
        KeyboardEnum.Left_Roll => new byte[] { 0, 0, 0, 0, 254 },
        KeyboardEnum.Right_Roll => new byte[] { 0, 0, 0, 0, 2 },

        KeyboardEnum.Esc => new byte[] { 0, 41, 0, 0, 0 },

        KeyboardEnum.F1 => new byte[] { 0, 58, 0, 0, 0 },
        KeyboardEnum.F2 => new byte[] { 0, 59, 0, 0, 0 },
        KeyboardEnum.F3 => new byte[] { 0, 60, 0, 0, 0 },
        KeyboardEnum.F4 => new byte[] { 0, 61, 0, 0, 0 },
        KeyboardEnum.F5 => new byte[] { 0, 62, 0, 0, 0 },
        KeyboardEnum.F6 => new byte[] { 0, 63, 0, 0, 0 },
        KeyboardEnum.F7 => new byte[] { 0, 64, 0, 0, 0 },
        KeyboardEnum.F8 => new byte[] { 0, 65, 0, 0, 0 },
        KeyboardEnum.F9 => new byte[] { 0, 66, 0, 0, 0 },
        KeyboardEnum.F10 => new byte[] { 0, 67, 0, 0, 0 },
        KeyboardEnum.F11 => new byte[] { 0, 68, 0, 0, 0 },
        KeyboardEnum.F12 => new byte[] { 0, 69, 0, 0, 0 },

        // F13..F24 differ between the 108JP model and every other model.
        KeyboardEnum.F13 => is108Jp ? new byte[] { 0, 104, 0, 0, 0 } : new byte[] { 0, 118, 0, 0, 0 },
        KeyboardEnum.F14 => is108Jp ? new byte[] { 0, 105, 0, 0, 0 } : new byte[] { 0, 119, 0, 0, 0 },
        KeyboardEnum.F15 => is108Jp ? new byte[] { 0, 106, 0, 0, 0 } : new byte[] { 0, 120, 0, 0, 0 },
        KeyboardEnum.F16 => is108Jp ? new byte[] { 0, 107, 0, 0, 0 } : new byte[] { 0, 121, 0, 0, 0 },
        KeyboardEnum.F17 => is108Jp ? new byte[] { 0, 108, 0, 0, 0 } : new byte[] { 0, 122, 0, 0, 0 },
        KeyboardEnum.F18 => is108Jp ? new byte[] { 0, 109, 0, 0, 0 } : new byte[] { 0, 123, 0, 0, 0 },
        KeyboardEnum.F19 => is108Jp ? new byte[] { 0, 110, 0, 0, 0 } : new byte[] { 0, 124, 0, 0, 0 },
        KeyboardEnum.F20 => is108Jp ? new byte[] { 0, 111, 0, 0, 0 } : new byte[] { 0, 125, 0, 0, 0 },
        KeyboardEnum.F21 => is108Jp ? new byte[] { 0, 112, 0, 0, 0 } : new byte[] { 0, 126, 0, 0, 0 },
        KeyboardEnum.F22 => is108Jp ? new byte[] { 0, 113, 0, 0, 0 } : new byte[] { 0, 127, 0, 0, 0 },
        KeyboardEnum.F23 => is108Jp ? new byte[] { 0, 114, 0, 0, 0 } : new byte[] { 0, 128, 0, 0, 0 },
        KeyboardEnum.F24 => is108Jp ? new byte[] { 0, 115, 0, 0, 0 } : new byte[] { 0, 129, 0, 0, 0 },

        KeyboardEnum.PRTSC => new byte[] { 0, 70, 0, 0, 0 },
        KeyboardEnum.SCRLK => new byte[] { 0, 71, 0, 0, 0 },
        KeyboardEnum.PAUSE => new byte[] { 0, 72, 0, 0, 0 },

        KeyboardEnum.Tilde => new byte[] { 0, 53, 0, 0, 0 },
        KeyboardEnum.Wave => new byte[] { 225, 53, 0, 0, 0 },

        KeyboardEnum.num1 => new byte[] { 0, 30, 0, 0, 0 },
        KeyboardEnum.Point => new byte[] { 225, 30, 0, 0, 0 },
        KeyboardEnum.num2 => new byte[] { 0, 31, 0, 0, 0 },
        KeyboardEnum.Aite => new byte[] { 225, 31, 0, 0, 0 },
        KeyboardEnum.num3 => new byte[] { 0, 32, 0, 0, 0 },
        KeyboardEnum.Well => new byte[] { 225, 32, 0, 0, 0 },
        KeyboardEnum.num4 => new byte[] { 0, 33, 0, 0, 0 },
        KeyboardEnum.Unit => new byte[] { 225, 33, 0, 0, 0 },
        KeyboardEnum.num5 => new byte[] { 0, 34, 0, 0, 0 },
        KeyboardEnum.Percent => new byte[] { 225, 34, 0, 0, 0 },
        KeyboardEnum.num6 => new byte[] { 0, 35, 0, 0, 0 },
        KeyboardEnum.Divide => new byte[] { 225, 35, 0, 0, 0 },
        KeyboardEnum.num7 => new byte[] { 0, 36, 0, 0, 0 },
        KeyboardEnum.With => new byte[] { 225, 36, 0, 0, 0 },
        KeyboardEnum.num8 => new byte[] { 0, 37, 0, 0, 0 },
        KeyboardEnum.Multiply => new byte[] { 225, 37, 0, 0, 0 },
        KeyboardEnum.num9 => new byte[] { 0, 38, 0, 0, 0 },
        KeyboardEnum.LeftBracket => new byte[] { 225, 38, 0, 0, 0 },
        KeyboardEnum.num0 => new byte[] { 0, 39, 0, 0, 0 },
        KeyboardEnum.RightBracket => new byte[] { 225, 39, 0, 0, 0 },

        KeyboardEnum.Minus => new byte[] { 0, 45, 0, 0, 0 },
        KeyboardEnum.Minus1 => new byte[] { 225, 45, 0, 0, 0 },
        KeyboardEnum.Equal => new byte[] { 0, 46, 0, 0, 0 },
        KeyboardEnum.Plus => new byte[] { 225, 46, 0, 0, 0 },

        KeyboardEnum.BACKSPACE => new byte[] { 0, 42, 0, 0, 0 },
        KeyboardEnum.INSERT => new byte[] { 0, 73, 0, 0, 0 },
        KeyboardEnum.HOME => new byte[] { 0, 74, 0, 0, 0 },
        KeyboardEnum.PGUP => new byte[] { 0, 75, 0, 0, 0 },
        KeyboardEnum.TAB => new byte[] { 0, 43, 0, 0, 0 },

        KeyboardEnum.Q => new byte[] { 0, 20, 0, 0, 0 },
        KeyboardEnum.W => new byte[] { 0, 26, 0, 0, 0 },
        KeyboardEnum.E => new byte[] { 0, 8, 0, 0, 0 },
        KeyboardEnum.R => new byte[] { 0, 21, 0, 0, 0 },
        KeyboardEnum.T => new byte[] { 0, 23, 0, 0, 0 },
        KeyboardEnum.Y => new byte[] { 0, 28, 0, 0, 0 },
        KeyboardEnum.U => new byte[] { 0, 24, 0, 0, 0 },
        KeyboardEnum.I => new byte[] { 0, 12, 0, 0, 0 },
        KeyboardEnum.O => new byte[] { 0, 18, 0, 0, 0 },
        KeyboardEnum.P => new byte[] { 0, 19, 0, 0, 0 },

        KeyboardEnum.LeftBracket1 => new byte[] { 0, 47, 0, 0, 0 },
        KeyboardEnum.LeftBracket2 => new byte[] { 225, 47, 0, 0, 0 },
        KeyboardEnum.RightBracket1 => new byte[] { 0, 48, 0, 0, 0 },
        KeyboardEnum.RightBracket2 => new byte[] { 225, 48, 0, 0, 0 },
        KeyboardEnum.Vertical => new byte[] { 0, 49, 0, 0, 0 },
        KeyboardEnum.Vertical1 => new byte[] { 225, 49, 0, 0, 0 },

        KeyboardEnum.DELETE => new byte[] { 0, 76, 0, 0, 0 },
        KeyboardEnum.END => new byte[] { 0, 77, 0, 0, 0 },
        KeyboardEnum.PGDN => new byte[] { 0, 78, 0, 0, 0 },
        KeyboardEnum.CAPSLOCK => new byte[] { 0, 57, 0, 0, 0 },

        KeyboardEnum.A => new byte[] { 0, 4, 0, 0, 0 },
        KeyboardEnum.S => new byte[] { 0, 22, 0, 0, 0 },
        KeyboardEnum.D => new byte[] { 0, 7, 0, 0, 0 },
        KeyboardEnum.F => new byte[] { 0, 9, 0, 0, 0 },
        KeyboardEnum.G => new byte[] { 0, 10, 0, 0, 0 },
        KeyboardEnum.H => new byte[] { 0, 11, 0, 0, 0 },
        KeyboardEnum.J => new byte[] { 0, 13, 0, 0, 0 },
        KeyboardEnum.K => new byte[] { 0, 14, 0, 0, 0 },
        KeyboardEnum.L => new byte[] { 0, 15, 0, 0, 0 },

        KeyboardEnum.Colon => new byte[] { 0, 51, 0, 0, 0 },
        KeyboardEnum.Colon1 => new byte[] { 225, 51, 0, 0, 0 },
        KeyboardEnum.Quotation => new byte[] { 0, 52, 0, 0, 0 },
        KeyboardEnum.Quotation1 => new byte[] { 225, 52, 0, 0, 0 },

        KeyboardEnum.Enter => new byte[] { 0, 40, 0, 0, 0 },
        KeyboardEnum.LShift => new byte[] { 225, 0, 0, 0, 0 },

        KeyboardEnum.Z => new byte[] { 0, 29, 0, 0, 0 },
        KeyboardEnum.X => new byte[] { 0, 27, 0, 0, 0 },
        KeyboardEnum.C => new byte[] { 0, 6, 0, 0, 0 },
        KeyboardEnum.V => new byte[] { 0, 25, 0, 0, 0 },
        KeyboardEnum.B => new byte[] { 0, 5, 0, 0, 0 },
        KeyboardEnum.N => new byte[] { 0, 17, 0, 0, 0 },
        KeyboardEnum.M => new byte[] { 0, 16, 0, 0, 0 },

        KeyboardEnum.Less => new byte[] { 0, 54, 0, 0, 0 },
        KeyboardEnum.Less1 => new byte[] { 225, 54, 0, 0, 0 },
        KeyboardEnum.Greater => new byte[] { 0, 55, 0, 0, 0 },
        KeyboardEnum.Greater1 => new byte[] { 225, 55, 0, 0, 0 },
        KeyboardEnum.Question => new byte[] { 0, 56, 0, 0, 0 },
        KeyboardEnum.Question1 => new byte[] { 225, 56, 0, 0, 0 },

        KeyboardEnum.RShift => new byte[] { 229, 0, 0, 0, 0 },
        KeyboardEnum.Up => new byte[] { 0, 82, 0, 0, 0 },
        KeyboardEnum.LCtrl => new byte[] { 224, 0, 0, 0, 0 },
        KeyboardEnum.LWin => new byte[] { 227, 0, 0, 0, 0 },
        KeyboardEnum.LAlt => new byte[] { 226, 0, 0, 0, 0 },
        KeyboardEnum.Space => new byte[] { 0, 44, 0, 0, 0 },
        KeyboardEnum.RAlt => new byte[] { 230, 0, 0, 0, 0 },
        KeyboardEnum.RCtrl => new byte[] { 228, 0, 0, 0, 0 },
        KeyboardEnum.Left => new byte[] { 0, 80, 0, 0, 0 },
        KeyboardEnum.Down => new byte[] { 0, 81, 0, 0, 0 },
        KeyboardEnum.Right => new byte[] { 0, 79, 0, 0, 0 },

        KeyboardEnum.None => new byte[] { 0, 0, 0, 0, 0 },

        KeyboardEnum.Numlock => new byte[] { 0, 83, 0, 0, 0 },
        KeyboardEnum.num_Divide => new byte[] { 0, 84, 0, 0, 0 },
        KeyboardEnum.num_Multiply => new byte[] { 0, 85, 0, 0, 0 },
        KeyboardEnum.num_Enter => new byte[] { 0, 88, 0, 0, 0 },
        KeyboardEnum.RWin => new byte[] { 231, 0, 0, 0, 0 },
        KeyboardEnum.Fn => new byte[] { 0, 3, 0, 0, 0 },
        KeyboardEnum.num_Minus => new byte[] { 0, 86, 0, 0, 0 },
        KeyboardEnum.num_Plus => new byte[] { 0, 87, 0, 0, 0 },

        KeyboardEnum.num1_1 => new byte[] { 0, 89, 0, 0, 0 },
        KeyboardEnum.num2_1 => new byte[] { 0, 90, 0, 0, 0 },
        KeyboardEnum.num3_1 => new byte[] { 0, 91, 0, 0, 0 },
        KeyboardEnum.num4_1 => new byte[] { 0, 92, 0, 0, 0 },
        KeyboardEnum.num5_1 => new byte[] { 0, 93, 0, 0, 0 },
        KeyboardEnum.num6_1 => new byte[] { 0, 94, 0, 0, 0 },
        KeyboardEnum.num7_1 => new byte[] { 0, 95, 0, 0, 0 },
        KeyboardEnum.num8_1 => new byte[] { 0, 96, 0, 0, 0 },
        KeyboardEnum.num9_1 => new byte[] { 0, 97, 0, 0, 0 },
        KeyboardEnum.num0_1 => new byte[] { 0, 98, 0, 0, 0 },
        KeyboardEnum.Dot => new byte[] { 0, 99, 0, 0, 0 },

        KeyboardEnum.VoiceLow => new byte[] { 234, 0, 0, 0, 0 },
        KeyboardEnum.VoiceUp => new byte[] { 233, 0, 0, 0, 0 },
        KeyboardEnum.NoVoice => new byte[] { 226, 0, 0, 0, 0 },
        KeyboardEnum.Play => new byte[] { 205, 0, 0, 0, 0 },
        KeyboardEnum.Last => new byte[] { 182, 0, 0, 0, 0 },
        KeyboardEnum.Next => new byte[] { 181, 0, 0, 0, 0 },
        KeyboardEnum.Mail => new byte[] { 138, 1, 0, 0, 0 },
        KeyboardEnum.Computer => new byte[] { 227, 8, 0, 0, 0 },
        KeyboardEnum.Counter => new byte[] { 146, 1, 0, 0, 0 },
        KeyboardEnum.Explorer => new byte[] { 35, 2, 0, 0, 0 },
        KeyboardEnum.BrightLow => new byte[] { 112, 0, 0, 0, 0 },
        KeyboardEnum.BrightUp => new byte[] { 111, 0, 0, 0, 0 },

        KeyboardEnum.Menu => new byte[] { 225, 67, 0, 0, 0 },

        // Keys the original getuint never tests (KB, KA, K1..K8, empty) fall through
        // to the zero-initialised buffer.
        _ => new byte[] { 0, 0, 0, 0, 0 },
    };

    /// <summary>
    /// The step encoder, as a generic
    /// 3-byte macro-step encoder.
    /// </summary>
    /// <remarks>
    /// The original carries one branch per <c>macro action</c> value (every
    /// Down/Up pair), all following the same shape:
    /// Down = [0x81, usage, 0x00], Up = [0x01, usage, 0x00], Time = [0x0F, lo, hi],
    /// with the eight modifier keys (LCTRL..RWIN, usage 0xE0..0xE7) and WELL (usage 0xA6)
    /// instead using [0x83, usage, 0x00] / [0x03, usage, 0x00].
    /// This method reproduces that behaviour from (kind, usage); the modifier class is
    /// inferred from the usage byte exactly as the original's fixed action table does.
    /// <para>
    /// Actions that the original <c>getdata</c> has no branch for (K45, APP and DEL, all
    /// Down and Up) leave the buffer zeroed there; this encoder has no representation for
    /// them and would encode them as ordinary keys, so callers must not pass those usages.
    /// </para>
    /// </remarks>
    public static byte[] GetMacroStep(MacroStepKind kind, byte usage, ushort delayMs)
    {
        switch (kind)
        {
            case MacroStepKind.Time:
                return new byte[] { 0x0F, (byte)(delayMs & 0xFF), (byte)((delayMs >> 8) & 0xFF) };
            case MacroStepKind.KeyDown:
                return IsMacroModifier(usage)
                    ? new byte[] { 0x83, usage, 0x00 }
                    : new byte[] { 0x81, usage, 0x00 };
            case MacroStepKind.KeyUp:
                return IsMacroModifier(usage)
                    ? new byte[] { 0x03, usage, 0x00 }
                    : new byte[] { 0x01, usage, 0x00 };
            default:
                return new byte[] { 0x00, 0x00, 0x00 };
        }
    }

    /// <summary>
    /// True for the usages that <c>getdata</c> encodes with the 0x83/0x03 modifier prefix:
    /// the eight modifiers 0xE0..0xE7 and WELL (0xA6).
    /// </summary>
    public static bool IsMacroModifier(byte usage)
        => usage == 0xA6 || (usage >= 0xE0 && usage <= 0xE7);

    /// <summary>
    /// The 95 default board-key payloads, in board order. Positions 85..94 are
    /// intentionally <see cref="MappingIds.NULL_MAP"/>.
    /// </summary>
    public static byte[] GetKeys() => new byte[95]
    {
        MappingIds.A_MAP, MappingIds.B_MAP, MappingIds.C_MAP, MappingIds.D_MAP, MappingIds.E_MAP,
        MappingIds.F_MAP, MappingIds.G_MAP, MappingIds.H_MAP, MappingIds.I_MAP, MappingIds.J_MAP,
        MappingIds.K_MAP, MappingIds.L_MAP, MappingIds.M_MAP, MappingIds.N_MAP, MappingIds.O_MAP,
        MappingIds.P_MAP, MappingIds.Q_MAP, MappingIds.R_MAP, MappingIds.S_MAP, MappingIds.T_MAP,
        MappingIds.U_MAP, MappingIds.V_MAP, MappingIds.W_MAP, MappingIds.X_MAP, MappingIds.Y_MAP,
        MappingIds.Z_MAP, MappingIds.N1_MAP, MappingIds.N2_MAP, MappingIds.N3_MAP, MappingIds.N4_MAP,
        MappingIds.N5_MAP, MappingIds.N6_MAP, MappingIds.N7_MAP, MappingIds.N8_MAP, MappingIds.N9_MAP,
        MappingIds.N0_MAP, MappingIds.ENTER_MAP, MappingIds.ESC_MAP, MappingIds.BACKSPACE_MAP, MappingIds.TAB_MAP,
        MappingIds.SPACE_MAP, MappingIds.MINUS_MAP, MappingIds.EQUAL_CLICK, MappingIds.LEFTBRACKET_CLICK1, MappingIds.RIGHTBRACKET_CLICK1,
        MappingIds.VERTICAL_MAP, MappingIds.COLON_MAP, MappingIds.QUOTATION_MAP, MappingIds.TILDE_MAP, MappingIds.LESS_MAP,
        MappingIds.GREATER_MAP, MappingIds.QUESTION_MAP, MappingIds.CAPSLOCK_MAP, MappingIds.F1_MAP, MappingIds.F2_MAP,
        MappingIds.F3_MAP, MappingIds.F4_MAP, MappingIds.F5_MAP, MappingIds.F6_MAP, MappingIds.F7_MAP,
        MappingIds.F8_MAP, MappingIds.F9_MAP, MappingIds.F10_MAP, MappingIds.F11_MAP, MappingIds.F12_MAP,
        MappingIds.PRTSC_MAP, MappingIds.SCRLK_MAP, MappingIds.PAUSE_MAP, MappingIds.INSERT_MAP, MappingIds.HOME_MAP,
        MappingIds.PGUP_MAP, MappingIds.DELETE_MAP, MappingIds.END_MAP, MappingIds.PGDN_MAP, MappingIds.RIGHT_MAP,
        MappingIds.LEFT_MAP, MappingIds.DOWN_MAP, MappingIds.UP_MAP, MappingIds.LCTRL_MAP, MappingIds.LSHIFT_MAP,
        MappingIds.LALT_MAP, MappingIds.LWIN_MAP, MappingIds.RCTRL_MAP, MappingIds.RSHIFT_MAP, MappingIds.RALT_MAP,
        MappingIds.NULL_MAP, MappingIds.NULL_MAP, MappingIds.NULL_MAP, MappingIds.NULL_MAP, MappingIds.NULL_MAP,
        MappingIds.NULL_MAP, MappingIds.NULL_MAP, MappingIds.NULL_MAP, MappingIds.NULL_MAP, MappingIds.NULL_MAP,
    };

    // ---------------------------------------------------------------------------------
    // Target classification is folded into TransportFor above.
    //
    // A naive classification of a target would test the value against several mouse codes at
    // once, which is unsatisfiable for a single value; the observable behaviour therefore
    // reduces to: "no target" -> the None transport, otherwise the transport implied by the
    // category. That carries no wire data beyond the target itself, so the split that matters
    // on the wire is the one TransportFor implements.
    // ---------------------------------------------------------------------------------
}
