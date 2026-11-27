using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Shared wire constants, discovery paths, URL/port resolution, and response envelopes
/// between AutoCAD.Gateway and MCP clients.
/// </summary>
public static class GatewayProtocol
{
    /// <summary>HTTP header carrying the session authentication token.</summary>
    public const string TokenHeader = "X-Gateway-Token";

    /// <summary>Default loopback port when AUTOCAD_GATEWAY_PORT is not set.</summary>
    public const int DefaultPort = 5055;

    /// <summary>Maximum allowed HTTP request body size in bytes (16 MiB).</summary>
    public const int MaxBodyBytes = 16 * 1024 * 1024;

    /// <summary>Maximum allowed HTTP request head size in bytes (64 KiB).</summary>
    public const int MaxHeaderBytes = 64 * 1024;

    /// <summary>Minimum permitted LISP evaluation timeout in milliseconds.</summary>
    public const int MinLispTimeoutMs = 250;

    /// <summary>Default LISP evaluation timeout in milliseconds.</summary>
    public const int DefaultLispTimeoutMs = 15_000;

    /// <summary>Default timeout in milliseconds for loading external AutoLISP files.</summary>
    public const int DefaultLispLoadTimeoutMs = 60_000;

    /// <summary>Maximum permitted LISP evaluation timeout in milliseconds.</summary>
    public const int MaxLispTimeoutMs = 300_000;

    /// <summary>Shared JSON serializer options across Gateway HTTP responses.</summary>
    public static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Clamps a requested LISP evaluation timeout into the supported safe bounds.</summary>
    /// <param name="requestedMs">Requested timeout in milliseconds.</param>
    /// <returns>Clamped timeout in milliseconds between <see cref="MinLispTimeoutMs"/> and <see cref="MaxLispTimeoutMs"/>.</returns>
    public static int ClampLispTimeoutMs(long requestedMs) =>
        (int)Math.Clamp(requestedMs, MinLispTimeoutMs, MaxLispTimeoutMs);

    /// <summary>Maps an internal gateway error code to its corresponding HTTP status code.</summary>
    /// <param name="code">Gateway machine-readable error code.</param>
    /// <returns>HTTP status code.</returns>
    public static int MapErrorCodeToHttpStatus(string? code) => code switch
    {
        "bad_argument" or "bad_handle" or "bad_json" or "bad_query" or "bad_request" or "bad_utf8" or "invalid_path" => 400,
        "edit_guard" or "forbidden" or "restricted_mode" => 403,
        "not_found" => 404,
        "busy" or "read_only" or "no_document" or "ambiguous_document" or "revision_conflict" or "already_running" or "already_exists" or "layer_in_use" or "unsaved_changes" or "autocad_error" or "civil_unavailable" => 409,
        "lisp_error" => 422,
        "gateway_stopped" => 503,
        "lisp_timeout" or "timeout" => 504,
        _ => 500,
    };

    /// <summary>Route for health and liveness checks.</summary>
    public const string PingRoute = "_ping";

    /// <summary>Route for dynamic tool catalog discovery.</summary>
    public const string ToolsRoute = "_tools";

    /// <summary>Route for CAD agent working rules.</summary>
    public const string GuideRoute = "_guide";

    /// <summary>
    /// Directory storing per-instance gateway descriptors ({pid}.json).
    /// Respects AUTOCAD_GATEWAY_INSTANCE_DIR when set.
    /// </summary>
    public static string InstanceDirectory
    {
        get
        {
            string? overrideDir = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_INSTANCE_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return overrideDir.Trim();
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "AutoCAD.Gateway", "instances");
        }
    }

    /// <summary>
    /// Resolves the configured Gateway port from explicit environment variables,
    /// returning <see cref="DefaultPort"/> when unset or invalid.
    /// </summary>
    public static int ResolveConfiguredPort(string? envPort = null, string? envUrl = null)
    {
        envPort ??= Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT");
        if (!string.IsNullOrWhiteSpace(envPort) &&
            int.TryParse(envPort.Trim(), out int parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            return parsedPort;
        }

        envUrl ??= Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL");
        if (TryNormalizeHttpUrl(envUrl, out Uri? uri) && uri is not null && uri.Port > 0)
        {
            return uri.Port;
        }

        return DefaultPort;
    }

    /// <summary>
    /// Validates that <paramref name="rawUrl"/> is an absolute loopback http or https URI.
    /// </summary>
    public static bool TryNormalizeHttpUrl(string? rawUrl, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!parsed.IsLoopback)
        {
            return false;
        }

        string normalized = parsed.AbsoluteUri.EndsWith('/') ? parsed.AbsoluteUri : parsed.AbsoluteUri + "/";
        uri = new Uri(normalized, UriKind.Absolute);
        return true;
    }
}

/// <summary>
/// Per-session descriptor written atomically by a running AutoCAD Gateway instance.
/// </summary>
public sealed class InstanceDescriptor
{
    /// <summary>AutoCAD OS process identifier.</summary>
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    /// <summary>Bound loopback TCP port.</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; }

    /// <summary>Per-session or configured authentication token.</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC timestamp when the gateway started.</summary>
    [JsonPropertyName("started_utc")]
    public string StartedUtc { get; set; } = string.Empty;

    /// <summary>Host product string reported by AutoCAD (if available).</summary>
    [JsonPropertyName("product")]
    public string? Product { get; set; }
}

/// <summary>
/// Structured error detail returned by the Gateway.
/// </summary>
public sealed record ToolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("detail")] string? Detail = null,
    [property: JsonPropertyName("hint")] string? Hint = null);

/// <summary>
/// Standardized Gateway JSON envelope compatible with both legacy status-based clients
/// and structured ok/error consumers.
/// </summary>
public sealed class ToolEnvelope
{
    /// <summary>Legacy status string ("success" or "error").</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "success";

    /// <summary>Boolean success indicator.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>Optional human-readable message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Structured result payload when Ok is true.</summary>
    [JsonPropertyName("result")]
    public object? Result { get; init; }

    /// <summary>Structured error object when Ok is false.</summary>
    [JsonPropertyName("error")]
    public ToolError? Error { get; init; }

    /// <summary>Elapsed execution duration in milliseconds when measured at the Gateway boundary.</summary>
    [JsonPropertyName("duration_ms")]
    public double? DurationMs { get; init; }

    /// <summary>Optional non-fatal warnings emitted during request execution.</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>Creates a success envelope.</summary>
    public static ToolEnvelope Success(object? result = null, string? message = null) => new()
    {
        Status = "success",
        Ok = true,
        Message = message,
        Result = result,
        Error = null,
    };

    /// <summary>Creates a failure envelope.</summary>
    public static ToolEnvelope Failure(
        string code,
        string message,
        string? detail = null,
        string? hint = null) => new()
    {
        Status = "error",
        Ok = false,
        Message = message,
        Result = null,
        Error = new ToolError(code, message, detail, hint),
    };
}
