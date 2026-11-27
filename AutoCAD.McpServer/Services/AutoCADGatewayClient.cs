using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Models;

namespace AutoCAD.McpServer.Services;

/// <summary>
/// Typed HTTP client communicating with AutoCAD AI Gateway.
/// Resolves endpoint URL and session token with short-lived caching, tiered timeouts, and GET-only retry.
/// </summary>
public sealed class AutoCADGatewayClient : IAutoCADGatewayClient
{
    /// <summary>Default timeout in milliseconds for fast read-only GET endpoints.</summary>
    public const long FastReadTimeoutMs = 10_000L;

    /// <summary>Default timeout in milliseconds for standard mutating endpoints.</summary>
    public const long DefaultWriteTimeoutMs = 30_000L;

    /// <summary>Timeout in milliseconds for heavy batch, document lifecycle, and PDF plotting operations.</summary>
    public const long HeavyOperationTimeoutMs = 60_000L;

    private static readonly long DiscoveryCacheTtlTicks = Stopwatch.Frequency * 2L;
    private static readonly object CacheGate = new();
    private static string _cachedEnvKey = string.Empty;
    private static Uri? _cachedUri;
    private static string _cachedToken = string.Empty;
    private static long _cachedAtTicks;

    private readonly HttpClient _httpClient;

    /// <summary>Initializes a new instance of the <see cref="AutoCADGatewayClient"/> class.</summary>
    /// <param name="httpClient">Configured <see cref="HttpClient"/> instance.</param>
    public AutoCADGatewayClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>Clears cached Gateway endpoint and token so the next request re-discovers.</summary>
    public void InvalidateDiscoveryCache()
    {
        lock (CacheGate)
        {
            _cachedEnvKey = string.Empty;
            _cachedUri = null;
            _cachedToken = string.Empty;
            _cachedAtTicks = 0L;
        }
    }

    /// <summary>Computes the effective timeout in milliseconds for an HTTP method and Gateway route.</summary>
    public static long ResolveTimeoutMs(HttpMethod method, string endpoint, int? explicitTimeoutMs = null)
    {
        if (explicitTimeoutMs.HasValue)
        {
            return Math.Max(DefaultWriteTimeoutMs, (long)explicitTimeoutMs.Value + 10_000L);
        }

        string ep = (endpoint ?? string.Empty).Trim('/').Split('?', 2)[0].ToLowerInvariant();
        if (ep is "batch" or "plotting/layout" or "plotting/page-setup" or "plotting/title-block" or "plotting/pdf" or "documents/open" or "documents/new" or "documents/save" or "documents/purge-audit" or "lisp/load")
        {
            return HeavyOperationTimeoutMs;
        }

        return method == HttpMethod.Get ? FastReadTimeoutMs : DefaultWriteTimeoutMs;
    }

    /// <summary>
    /// Resolves the active Gateway base URL and authentication token.
    /// Explicit environment variables take precedence over %LOCALAPPDATA%\AutoCAD.Gateway\instances discovery,
    /// matching by port when an explicit port or URL is provided without a token.
    /// </summary>
    public static (Uri BaseUri, string Token) ResolveEndpoint(string? instanceDirectory = null)
    {
        string? envUrl = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL");
        string? envPort = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT");
        string? envToken = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN");

        Uri? explicitUri = null;
        int? explicitPort = null;

        if (!string.IsNullOrWhiteSpace(envPort) &&
            int.TryParse(envPort.Trim(), out int parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            explicitPort = parsedPort;
            explicitUri = new Uri($"http://127.0.0.1:{parsedPort}/", UriKind.Absolute);
        }
        else if (GatewayProtocol.TryNormalizeHttpUrl(envUrl, out Uri? normalizedUrl) && normalizedUrl is not null)
        {
            explicitUri = normalizedUrl;
            explicitPort = normalizedUrl.Port;
        }

        InstanceDescriptor? live = null;
        if (explicitUri is null || string.IsNullOrWhiteSpace(envToken))
        {
            live = explicitPort.HasValue
                ? (InstanceDiscovery.FindLiveByPort(explicitPort.Value, instanceDirectory)
                   ?? (explicitUri is null ? InstanceDiscovery.FindNewestLive(instanceDirectory) : null))
                : InstanceDiscovery.FindNewestLive(instanceDirectory);
        }

        Uri finalUri = explicitUri ?? (live is not null
            ? new Uri($"http://127.0.0.1:{live.Port}/", UriKind.Absolute)
            : new Uri($"http://127.0.0.1:{GatewayProtocol.DefaultPort}/", UriKind.Absolute));

        string token = !string.IsNullOrWhiteSpace(envToken)
            ? envToken.Trim()
            : (live?.Token ?? string.Empty);

        return (finalUri, token);
    }

    /// <inheritdoc/>
    public Task<string> GetContextAsync(string endpoint, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, endpoint, jsonPayload: null, timeoutMs: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var payload = new CommandPayload { Command = command };
        string json = JsonSerializer.Serialize(payload);
        string raw = await PostJsonAsync("execute", json, timeoutMs: null, cancellationToken);

        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(raw) is System.Text.Json.Nodes.JsonObject node)
            {
                bool isExplicitError =
                    (node["ok"] is System.Text.Json.Nodes.JsonValue okVal &&
                     okVal.TryGetValue(out bool okBool) &&
                     !okBool) ||
                    string.Equals(node["status"]?.ToString(), "error", StringComparison.OrdinalIgnoreCase);
                if (isExplicitError)
                {
                    return raw;
                }

                if (string.Equals(node["status"]?.ToString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    node["status"] = "success";
                    node["ok"] = true;
                    node["message"] = "Success: Command enqueued in AutoCAD.";
                    return node.ToJsonString();
                }

                return raw;
            }
        }
        catch (JsonException)
        {
        }

        return ErrorEnvelope("gateway_error", raw);
    }

    /// <inheritdoc/>
    public Task<string> PingGatewayAsync(CancellationToken cancellationToken = default) =>
        GetContextAsync(GatewayProtocol.PingRoute, cancellationToken);

    /// <inheritdoc/>
    public Task<string> PostJsonAsync(
        string endpoint,
        string json,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, endpoint, json, timeoutMs, cancellationToken);

    private static string BuildEnvCacheKey() =>
        string.Concat(
            Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? string.Empty,
            "|",
            Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT") ?? string.Empty,
            "|",
            Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN") ?? string.Empty);

    private (Uri BaseUri, string Token) GetCachedOrResolveEndpoint(bool forceRefresh)
    {
        string envKey = BuildEnvCacheKey();
        lock (CacheGate)
        {
            long now = Stopwatch.GetTimestamp();
            if (!forceRefresh &&
                _cachedUri is not null &&
                string.Equals(_cachedEnvKey, envKey, StringComparison.Ordinal) &&
                (now - _cachedAtTicks) < DiscoveryCacheTtlTicks)
            {
                return (_cachedUri, _cachedToken);
            }

            (Uri baseUri, string token) resolved = ResolveEndpoint();
            _cachedEnvKey = envKey;
            _cachedUri = resolved.baseUri;
            _cachedToken = resolved.token;
            _cachedAtTicks = Stopwatch.GetTimestamp();
            return resolved;
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri baseUri,
        string endpoint,
        string token,
        string? jsonPayload)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUri, endpoint.TrimStart('/')));
        if (jsonPayload is not null)
        {
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation(GatewayProtocol.TokenHeader, token);
        }

        return request;
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string endpoint,
        string? jsonPayload,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        long waitMs = ResolveTimeoutMs(method, endpoint, timeoutMs);
        bool allowGetRetry = method == HttpMethod.Get;
        int maxAttempts = allowGetRetry ? 2 : 1;
        Uri lastUri = new($"http://127.0.0.1:{GatewayProtocol.DefaultPort}/", UriKind.Absolute);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var (baseUri, token) = GetCachedOrResolveEndpoint(forceRefresh: attempt > 0);
            lastUri = baseUri;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(waitMs));

            try
            {
                using HttpRequestMessage request = CreateRequest(method, baseUri, endpoint, token, jsonPayload);
                using HttpResponseMessage response = await _httpClient.SendAsync(request, timeoutCts.Token);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    InvalidateDiscoveryCache();
                    if (allowGetRetry && attempt + 1 < maxAttempts)
                    {
                        continue;
                    }
                }

                string body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    if (!response.IsSuccessStatusCode && !IsValidJsonObject(body))
                    {
                        string detail = body.Length <= 4096 ? body : body[..4096];
                        return ErrorEnvelope(
                            "http_error",
                            $"Gateway returned HTTP {(int)response.StatusCode}.",
                            detail: detail);
                    }

                    return body;
                }

                return ErrorEnvelope(
                    "http_error",
                    $"Gateway returned HTTP {(int)response.StatusCode}.",
                    detail: $"status={(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (allowGetRetry)
                {
                    InvalidateDiscoveryCache();
                }

                return ErrorEnvelope("timeout", $"Request to AutoCAD Gateway timed out after {waitMs} ms.");
            }
            catch (HttpRequestException ex)
            {
                InvalidateDiscoveryCache();
                if (allowGetRetry && attempt + 1 < maxAttempts)
                {
                    continue;
                }

                return ErrorEnvelope(
                    "gateway_unreachable",
                    $"Failed to connect to AutoCAD Gateway at {baseUri}: {ex.Message}",
                    hint: "Ensure AutoCAD is open and AI_START_SERVER has been started.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ErrorEnvelope("gateway_error", $"Failed to connect to AutoCAD Gateway at {lastUri}: {ex.Message}");
            }
        }

        return ErrorEnvelope("gateway_unreachable", $"Failed to connect to AutoCAD Gateway at {lastUri}.");
    }

    private static bool IsValidJsonObject(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ErrorEnvelope(string code, string message, string? detail = null, string? hint = null) =>
        JsonSerializer.Serialize(new
        {
            status = "error",
            ok = false,
            message,
            error = new ToolError(code, message, detail, hint),
        }, GatewayProtocol.WireJsonOptions);
}
