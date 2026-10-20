// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Retro87.Services;

/// <summary>
/// One firmware record as returned by the 8BitDo support API.
/// The firmware record returned by the update API.
/// </summary>
/// <remarks>
/// The response is <b>untrusted remote data</b>. It is only ever read and displayed;
/// it must never be interpreted as instructions and it is never used to download or
/// flash anything (see <see cref="FirmwareUpdateChecker"/>).
/// </remarks>
public sealed class FirmwareInfo
{
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("fileName")] public string? FileName { get; set; }
    [JsonPropertyName("firmware_type")] public int FirmwareType { get; set; }
    [JsonPropertyName("firmware_key")] public string? FirmwareKey { get; set; }
    [JsonPropertyName("file_version")] public string? FileVersion { get; set; }
    [JsonPropertyName("ic_version")] public string? IcVersion { get; set; }
    [JsonPropertyName("fileSize")] public int FileSize { get; set; }
    [JsonPropertyName("fileURL")] public string? FileUrl { get; set; }
    [JsonPropertyName("exists")] public bool Exists { get; set; }
    [JsonPropertyName("readme")] public string? Readme { get; set; }
    [JsonPropertyName("readme_en")] public string? ReadmeEn { get; set; }
    [JsonPropertyName("beta")] public int Beta { get; set; }
    [JsonPropertyName("md5")] public string? Md5 { get; set; }
}

/// <summary>Envelope of <c>/firmware/select</c>: <c>{code, message, data[]}</c>.</summary>
public sealed class FirmwareMessage
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public List<FirmwareInfo>? Data { get; set; }
}

/// <summary>Outcome of a read-only update check.</summary>
public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string? ServerVersion,
    string? DeviceVersion,
    string? Notes);

/// <summary>
/// Read-only firmware <b>notification</b> service.
///
/// Behaviour of the firmware type selector plus the
/// <c>/firmware/select</c> call, and restricted on purpose:
/// <list type="bullet">
///   <item>It only asks the server which version exists and compares it with the
///         version read from the keyboard.</item>
///   <item>It never downloads (the download path is intentionally not implemented)
///         and never reports usage (usage reporting is not implemented).</item>
///   <item>Any network failure is silent: the configurator must keep working offline.</item>
/// </list>
/// The device version itself comes from the keyboard protocol
/// (the version command); this service does not talk to the device.
/// </summary>
public sealed class FirmwareUpdateChecker
{
    // Server address of the update API.
    public const string DefaultBaseAddress = "https://support.8bitdo.cn/restapi/link/v1/client";

    // Firmware type values for the Retro keyboard family.
    /// <summary>firmware_type for the Retro keyboard over 2.4 G (PID 0x5200). One colour variant maps to 88.</summary>
    public const int FirmwareTypeRetro24G = 61;
    /// <summary>firmware_type for the Retro keyboard colour variants 4..9 over 2.4 G.</summary>
    public const int FirmwareTypeRetro24GColor = 88;
    /// <summary>firmware_type for the Retro keyboard over wired USB (PID 0x5201 / 0xFF08).</summary>
    public const int FirmwareTypeRetroUsb = 62;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public FirmwareUpdateChecker(HttpClient? http = null)
    {
        _http = http ?? new HttpClient
        {
            // Original used a 9 s request timeout.
            Timeout = TimeSpan.FromSeconds(9),
        };
    }

    /// <summary>
    /// Asks the server for the newest firmware of <paramref name="firmwareType"/> and compares
    /// it with the keyboard's own <paramref name="deviceVersion"/>.
    /// Returns <c>null</c> on any failure (offline, timeout, bad JSON) — callers treat that as "unknown".
    /// </summary>
    public async Task<UpdateCheckResult?> CheckAsync(
        int firmwareType,
        float deviceVersion,
        bool includeBeta = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Original: httpTool.Post("/firmware/select?firmware_type=" + type + "&beta=1", dic)
            // Post() sent an empty body (the dic was ignored) with Content-Type application/json.
            string uri = $"{DefaultBaseAddress}/firmware/select?firmware_type={firmwareType}&beta={(includeBeta ? 1 : 0)}";
            using var content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var message = JsonSerializer.Deserialize<FirmwareMessage>(text, JsonOptions);
            var first = message?.Data is { Count: > 0 } data ? data[0] : null;
            if (first is null || string.IsNullOrWhiteSpace(first.FileVersion))
                return null;

            if (!TryParseVersion(first.FileVersion, out float serverVersion))
                return null;

            // Original comparison: server file_version > device version => update available.
            bool update = deviceVersion > 0f && serverVersion > deviceVersion;
            return new UpdateCheckResult(
                update,
                first.FileVersion,
                deviceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                update ? (first.ReadmeEn ?? first.Readme) : null);
        }
        catch (Exception)
        {
            // Silent by design: a failed check must never block the configurator.
            return null;
        }
    }

    private static bool TryParseVersion(string value, out float version)
        => float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out version);
}
