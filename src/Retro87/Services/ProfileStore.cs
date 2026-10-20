// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Retro87.Localization;
using Retro87.Models;

namespace Retro87.Services;

/// <summary>
/// Local profile storage: one JSON document per saved profile, under
/// <c>&lt;ApplicationData&gt;/Retro87/profiles</c> (<c>~/.config</c> on Linux,
/// <c>%APPDATA%</c> on Windows).
/// </summary>
/// <remarks>
/// <para>
/// The official Windows software keeps profiles as <c>.ini</c> files next to the executable
/// and reads them with the Win32 profile API, which is not portable as it stands. This app
/// keeps the same <i>semantics</i> (a named snapshot of a <see cref="KeyboardConfig"/> bound
/// to a device PID) with a portable JSON format instead.
/// </para>
/// <para>
/// Note for anyone wanting to read files written by the Windows app: its on-disk name fields
/// pack 16-bit code units
/// <b>high byte first</b> — the opposite of the UTF-16LE used on the HID wire
/// (see <see cref="DeviceNameCodec"/>). A converter is therefore needed before such a file can
/// be imported here.
/// </para>
/// <para>
/// Nothing here touches the device: it only snapshots a configuration the user already read
/// (or is about to write), so a snapshot can always be taken before a write.
/// </para>
/// </remarks>
public sealed class ProfileStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public ProfileStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Retro87",
            "profiles");
    }

    /// <summary>Directory holding the profile documents.</summary>
    public string RootDirectory { get; }

    /// <summary>Reason the last <see cref="Save"/>/<see cref="Load"/> failed, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>Profiles on disk, newest first; optionally filtered to one product id.</summary>
    public IReadOnlyList<ProfileInfo> List(ushort? pid = null)
    {
        var list = new List<ProfileInfo>();
        try
        {
            if (!Directory.Exists(RootDirectory)) return list;

            foreach (string path in Directory.EnumerateFiles(RootDirectory, "*.json"))
            {
                ProfileDto? dto = Read(path);
                if (dto is null) continue;
                if (pid is not null && dto.Pid != pid) continue;

                list.Add(new ProfileInfo(Path.GetFileName(path), dto.ProfileName, dto.Vid, dto.Pid, dto.SavedAtUtc));
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        return list.OrderByDescending(p => p.SavedAtUtc).ToList();
    }

    /// <summary>
    /// Writes <paramref name="config"/> as <paramref name="fileName"/> (names are sanitised).
    /// Returns false and sets <see cref="LastError"/> on failure.
    /// </summary>
    public bool Save(DeviceInfo device, KeyboardConfig config, string? fileName = null)
    {
        LastError = null;
        try
        {
            Directory.CreateDirectory(RootDirectory);

            var dto = new ProfileDto
            {
                Version = FormatVersion,
                Vid = device.Vid,
                Pid = device.Pid,
                ProductName = device.ProductName,
                SavedAtUtc = DateTime.UtcNow,
                ProfileName = config.ProfileName ?? string.Empty,
                BoardKeys = Convert.ToHexString(config.BoardKeys),
                BoardKinds = EncodeKinds(config.BoardKinds),
                SpecialFeature = config.SpecialFeature,
                Voice = config.Voice,
                XboxVolumeLevel = config.XboxVolumeLevel,
                XboxFastSetting = config.XboxFastSetting,
            };

            foreach (MacroSlot slot in config.Macros)
            {
                var macro = new MacroDto
                {
                    HostKey = slot.HostKey,
                    Name = slot.Name ?? string.Empty,
                    Cycles = slot.Cycles,
                    IntervalMs = slot.IntervalMs,
                };

                foreach (MacroAction action in slot.Actions)
                {
                    if (action.Kind == MacroActionKind.Null) break;
                    macro.Actions.Add(new MacroActionDto
                    {
                        Kind = action.Kind.ToString(),
                        Usage = action.Usage,
                        Op = action.Op,
                        DelayMs = action.DelayMs,
                    });
                }

                dto.Macros.Add(macro);
            }

            string path = Path.Combine(RootDirectory, FileNameFor(fileName ?? dto.ProfileName));
            File.WriteAllText(path, JsonSerializer.Serialize(dto, Json), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>Reads a profile back; returns null (and sets <see cref="LastError"/>) if unreadable.</summary>
    public KeyboardConfig? Load(string fileName)
    {
        LastError = null;
        try
        {
            ProfileDto? dto = Read(Path.Combine(RootDirectory, fileName));
            if (dto is null)
            {
                LastError = Loc.F("err_profile_not_found", fileName);
                return null;
            }

            var config = new KeyboardConfig { ProfileName = dto.ProfileName };

            byte[] keys = ParseHex(dto.BoardKeys);
            Array.Copy(keys, config.BoardKeys, Math.Min(keys.Length, config.BoardKeys.Length));
            DecodeKinds(dto.BoardKinds, config.BoardKinds);

            config.SpecialFeature = dto.SpecialFeature;
            config.Voice = dto.Voice;
            config.XboxVolumeLevel = dto.XboxVolumeLevel;
            config.XboxFastSetting = dto.XboxFastSetting;

            for (int i = 0; i < config.Macros.Length && i < dto.Macros.Count; i++)
            {
                MacroDto macro = dto.Macros[i];
                MacroSlot slot = config.Macros[i];
                slot.HostKey = macro.HostKey;
                slot.Name = macro.Name ?? string.Empty;
                slot.Cycles = macro.Cycles;
                slot.IntervalMs = macro.IntervalMs;

                foreach (MacroActionDto action in macro.Actions)
                {
                    slot.Actions.Add(new MacroAction
                    {
                        Kind = Enum.TryParse(action.Kind, out MacroActionKind kind) ? kind : MacroActionKind.KeyDown,
                        Usage = action.Usage,
                        Op = action.Op,
                        DelayMs = action.DelayMs,
                    });
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>Deletes a stored profile. Returns false if it could not be removed.</summary>
    public bool Delete(string fileName)
    {
        LastError = null;
        try
        {
            File.Delete(Path.Combine(RootDirectory, fileName));
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Copies a stored profile under a new name (<paramref name="keepOriginal"/> false makes it a
    /// rename). Returns the new file name, or null with <see cref="LastError"/> set — including when
    /// a profile with that name already exists.
    /// </summary>
    public string? Duplicate(string fileName, string newName, bool keepOriginal)
    {
        LastError = null;
        try
        {
            string target = FileNameFor(newName);
            if (File.Exists(PathFor(target)) && !target.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                LastError = Loc.T("err_profile_exists");
                return null;
            }

            ProfileDto? dto = Read(PathFor(fileName));
            if (dto is null)
            {
                LastError = Loc.F("err_profile_not_found", fileName);
                return null;
            }

            dto.ProfileName = newName.Trim();
            dto.SavedAtUtc = DateTime.UtcNow;
            File.WriteAllText(PathFor(target), JsonSerializer.Serialize(dto, Json), Encoding.UTF8);
            if (!keepOriginal && !target.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                File.Delete(PathFor(fileName));
            return target;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>True when a profile stored under this name exists.</summary>
    public bool Exists(string profileName) => File.Exists(PathFor(FileNameFor(profileName)));

    /// <summary>Path a profile with this name is stored as (profile names are user text).</summary>
    public string PathFor(string fileName) => Path.Combine(RootDirectory, fileName);

    /// <summary>
    /// Characters refused by at least one of the platforms this port runs on (Windows rejects
    /// all of these; Linux only <c>NUL</c> and <c>/</c>).
    /// </summary>
    private const string UnsafeFileNameChars = "<>:\"/\\|?*";

    /// <summary>
    /// Base names Windows reserves for devices, whatever the extension. Saving a profile called
    /// "nul" works on Linux but fails on Windows, so the name is adjusted here instead.
    /// </summary>
    private static readonly string[] ReservedFileNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Turns a user-visible profile name into a safe file name.</summary>
    /// <remarks>
    /// The rejected set is fixed rather than taken from
    /// <see cref="Path.GetInvalidFileNameChars"/>, which is host-dependent: on Linux that call
    /// returns only <c>NUL</c> and <c>/</c>, so a name that is legal there could be written and
    /// then be unusable in the Windows build of the same app (or vice versa). Sanitising for the
    /// strictest platform keeps a profile directory copyable between them.
    /// </remarks>
    public static string FileNameFor(string profileName)
    {
        var builder = new StringBuilder(profileName.Length);
        foreach (char c in profileName.Trim())
        {
            bool bad = char.IsControl(c) || UnsafeFileNameChars.Contains(c);
            builder.Append(bad ? '_' : c);
        }

        string name = builder.ToString().Trim().TrimEnd('.');
        if (name.Length == 0) name = "profile";
        if (name.Length > 64) name = name[..64];
        if (ReservedFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)) name += "_";
        return name + ".json";
    }

    private static ProfileDto? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ProfileDto>(File.ReadAllText(path, Encoding.UTF8), Json);
        }
        catch (JsonException)
        {
            return null;   // a corrupt document must not stop the listing
        }
    }

    /// <summary>One digit per position (the <see cref="MappingTargetKind"/> value), '.' where the device keeps no type.</summary>
    private static string EncodeKinds(MappingTargetKind?[] kinds)
        => new(kinds.Select(k => k is null ? '.' : (char)('0' + (int)k.Value)).ToArray());

    private static void DecodeKinds(string? text, MappingTargetKind?[] kinds)
    {
        if (string.IsNullOrEmpty(text)) return;
        for (int i = 0; i < kinds.Length && i < text.Length; i++)
        {
            int v = text[i] - '0';
            kinds[i] = v >= 0 && Enum.IsDefined(typeof(MappingTargetKind), v) ? (MappingTargetKind)v : null;
        }
    }

    private static byte[] ParseHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    private sealed class ProfileDto
    {
        public int Version { get; set; }
        public ushort Vid { get; set; }
        public ushort Pid { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; }
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>95 protocol mapping ids, upper-case hex (readable and diff-friendly).</summary>
        public string BoardKeys { get; set; } = string.Empty;

        /// <summary>Category of each position (see <see cref="KeyboardConfig.BoardKinds"/>); empty in files from before it existed.</summary>
        public string BoardKinds { get; set; } = string.Empty;

        public byte SpecialFeature { get; set; }
        public byte Voice { get; set; }

        /// <summary>Xbox-family scalar fields (<c>volume_lv</c> and the <c>fast_setting</c> bits).</summary>
        public byte XboxVolumeLevel { get; set; }
        public byte XboxFastSetting { get; set; }

        public List<MacroDto> Macros { get; set; } = new();
    }

    private sealed class MacroDto
    {
        public byte HostKey { get; set; }
        public string Name { get; set; } = string.Empty;
        public byte Cycles { get; set; }
        public ushort IntervalMs { get; set; }
        public List<MacroActionDto> Actions { get; set; } = new();
    }

    private sealed class MacroActionDto
    {
        public string Kind { get; set; } = string.Empty;
        public byte Usage { get; set; }
        public byte Op { get; set; }
        public ushort DelayMs { get; set; }
    }
}

/// <summary>One stored profile, as listed by <see cref="ProfileStore.List"/>.</summary>
/// <remarks>
/// <see cref="ToString"/> is what a screen reader announces for the list row (the automation peer
/// falls back to the item's text when no name is set), so it renders the profile the way the user
/// sees it and never dumps the record with VID/PID.
/// </remarks>
public sealed record ProfileInfo(string FileName, string ProfileName, ushort Vid, ushort Pid, DateTime SavedAtUtc)
{
    /// <summary>Name and local time, as shown (and announced) in the profile list.</summary>
    public override string ToString()
        => $"{ProfileName} · {SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}
