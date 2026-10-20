// SPDX-License-Identifier: GPL-3.0-or-later

using Retro87.Localization;

namespace Retro87.Services;

/// <summary>
/// Keeps the raw configuration read from a keyboard before a write, so a bad edit can be
/// restored by hand (project rule: back up before writing to the device).
/// </summary>
/// <remarks>
/// One binary file per write, next to the local profiles, named
/// <c>&lt;device&gt;-&lt;yyyymmdd-HHmmss&gt;.bin</c> and byte-for-byte the 1532-byte
/// configuration the device reported. Reusing <see cref="ProfileStore.FileNameFor"/> keeps the
/// file name valid on both Windows and Linux for the same device name.
/// </remarks>
public sealed class ConfigBackup
{
    /// <summary>Maximum number of backups kept; the oldest are deleted first.</summary>
    public const int MaxKept = 20;

    public ConfigBackup(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Retro87",
            "backups");
    }

    /// <summary>Directory holding the backups.</summary>
    public string RootDirectory { get; }

    /// <summary>Reason the last <see cref="Save"/> failed, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Saves <paramref name="raw"/> for <paramref name="deviceName"/> and returns the path, or
    /// <c>null</c> on failure (the caller must then refuse to write).
    /// </summary>
    public string? Save(string deviceName, ReadOnlySpan<byte> raw, string? suffix = null)
    {
        LastError = null;
        if (raw.Length == 0)
        {
            LastError = Loc.T("err_backup_empty");
            return null;
        }

        try
        {
            Directory.CreateDirectory(RootDirectory);
            string stem = ProfileStore.FileNameFor(deviceName);
            if (stem.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                stem = stem[..^".json".Length];

            string name = string.IsNullOrEmpty(suffix)
                ? $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}.bin"
                : $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}.bin";
            string path = Path.Combine(RootDirectory, name);
            File.WriteAllBytes(path, raw.ToArray());
            Prune();
            return path;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>Backups currently on disk, newest first.</summary>
    public IReadOnlyList<string> List()
    {
        try
        {
            if (!Directory.Exists(RootDirectory)) return Array.Empty<string>();
            return Directory.EnumerateFiles(RootDirectory, "*.bin")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Array.Empty<string>();
        }
    }

    private void Prune()
    {
        foreach (string path in List().Skip(MaxKept))
        {
            try { File.Delete(path); }
            catch { /* keeping an extra backup is not worth failing the write */ }
        }
    }
}
