using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Outcome of <see cref="HttpRequestGuard.Admit"/>.
/// When <see cref="Admitted"/> is false, <see cref="Refusal"/> is returned under <see cref="Status"/>
/// without reading the request body.
/// </summary>
public sealed record Admission(int Status, ToolEnvelope? Refusal, int ContentLength)
{
    /// <summary>True when the request passed authorization and framing checks.</summary>
    public bool Admitted => Refusal is null;

    internal static Admission Read(int contentLength) => new(200, null, contentLength);

    internal static Admission Refuse(int status, ToolEnvelope refusal) => new(status, refusal, 0);
}

/// <summary>
/// HTTP/1.x request head parser and admission guard protecting the loopback Gateway
/// against unauthenticated callers, cross-origin browser requests, DNS rebinding, header smuggling,
/// and oversized bodies.
/// </summary>
public static class HttpRequestGuard
{
    private static readonly HashSet<string> SingleValueCriticalHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Content-Type",
        "Transfer-Encoding",
        GatewayProtocol.TokenHeader,
    };

    /// <summary>
    /// Parses an HTTP/1.x request head into method, raw target, and case-insensitive headers.
    /// Rejects malformed request lines, bare LF, pre-colon whitespace, and duplicate critical headers.
    /// </summary>
    public static bool TryParseHead(
        string headerText,
        out string method,
        out string rawTarget,
        out Dictionary<string, string> headers)
    {
        method = string.Empty;
        rawTarget = string.Empty;
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(headerText))
        {
            return false;
        }

        string trimmedHead = headerText.EndsWith("\r\n\r\n", StringComparison.Ordinal)
            ? headerText[..^4]
            : headerText.EndsWith("\r\n", StringComparison.Ordinal)
                ? headerText[..^2]
                : headerText;

        string[] lines = trimmedHead.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || string.IsNullOrEmpty(lines[0]))
        {
            return false;
        }

        if (HasControlOrLineBreak(lines[0]))
        {
            return false;
        }

        string[] parts = lines[0].Split(' ', StringSplitOptions.None);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!string.Equals(parts[2], "HTTP/1.1", StringComparison.Ordinal) &&
            !string.Equals(parts[2], "HTTP/1.0", StringComparison.Ordinal))
        {
            return false;
        }

        method = parts[0].ToUpperInvariant();
        if (!IsValidMethod(method) || !parts[1].StartsWith('/'))
        {
            return false;
        }

        rawTarget = parts[1];

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line) || HasControlOrLineBreak(line))
            {
                return false;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                return false;
            }

            string name = line[..colon];
            if (!IsValidHeaderName(name))
            {
                return false;
            }

            string value = line[(colon + 1)..].Trim(' ', '\t');
            if (headers.ContainsKey(name) && SingleValueCriticalHeaders.Contains(name))
            {
                return false;
            }

            headers[name] = value;
        }

        return true;
    }

    /// <summary>
    /// Splits an HTTP request target into unescaped path and query dictionary.
    /// </summary>
    public static bool TrySplitTarget(
        string rawTarget,
        out string path,
        out Dictionary<string, string> query)
    {
        path = string.Empty;
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(rawTarget))
        {
            return false;
        }

        int qIndex = rawTarget.IndexOf('?');
        string rawPath = qIndex >= 0 ? rawTarget[..qIndex] : rawTarget;
        string rawQuery = qIndex >= 0 ? rawTarget[(qIndex + 1)..] : string.Empty;

        if (!HasValidPercentEncoding(rawPath))
        {
            return false;
        }

        try
        {
            path = Uri.UnescapeDataString(rawPath);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (rawQuery.Length > 0)
        {
            string[] pairs = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=');
                string rawKey = eq >= 0 ? pair[..eq] : pair;
                string rawVal = eq >= 0 ? pair[(eq + 1)..] : string.Empty;
                if (!HasValidPercentEncoding(rawKey) || !HasValidPercentEncoding(rawVal))
                {
                    return false;
                }

                try
                {
                    string key = Uri.UnescapeDataString(rawKey.Replace('+', ' '));
                    string val = Uri.UnescapeDataString(rawVal.Replace('+', ' '));
                    if (key.Length > 0)
                    {
                        query[key] = val;
                    }
                }
                catch (UriFormatException)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Validates authentication, loopback Host header, absence of browser Origin/Referer headers,
    /// and Content-Type for mutating requests. Returns null if authorized, or an HTTP status code to refuse with.
    /// </summary>
    public static int? Authorize(
        string method,
        IReadOnlyDictionary<string, string> headers,
        string expectedToken,
        int port)
    {
        if (string.IsNullOrEmpty(expectedToken))
        {
            return 403;
        }

        if (!headers.TryGetValue(GatewayProtocol.TokenHeader, out string? supplied) ||
            string.IsNullOrEmpty(supplied))
        {
            return 403;
        }

        if (!FixedTimeEquals(supplied, expectedToken))
        {
            return 403;
        }

        if (!headers.TryGetValue("Host", out string? host) || string.IsNullOrEmpty(host))
        {
            return 403;
        }

        if (!host.Equals($"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals($"localhost:{port}", StringComparison.OrdinalIgnoreCase))
        {
            return 403;
        }

        if (headers.ContainsKey("Origin") ||
            headers.ContainsKey("Referer") ||
            headers.ContainsKey("Sec-Fetch-Site") ||
            headers.ContainsKey("Sec-Fetch-Mode") ||
            headers.ContainsKey("Sec-Fetch-Dest"))
        {
            return 403;
        }

        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            !method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            return 405;
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            if (!headers.TryGetValue("Content-Type", out string? contentType) ||
                !IsJsonMediaType(contentType))
            {
                return 415;
            }
        }

        return null;
    }

    /// <summary>
    /// Decides from the request head alone whether to read the body and dispatch or refuse.
    /// Authorization executes before body-limit disclosure so unauthorized callers learn nothing.
    /// </summary>
    public static Admission Admit(
        string method,
        IReadOnlyDictionary<string, string> headers,
        string expectedToken,
        int port,
        int maxBodyBytes = GatewayProtocol.MaxBodyBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBodyBytes);

        int? denial = Authorize(method, headers, expectedToken, port);
        if (denial is not null)
        {
            return denial.Value switch
            {
                405 => Admission.Refuse(405, ToolEnvelope.Failure("method_not_allowed", "HTTP method not allowed.")),
                415 => Admission.Refuse(415, ToolEnvelope.Failure("unsupported_media_type", "POST requests require Content-Type: application/json.")),
                _ => Admission.Refuse(denial.Value, ToolEnvelope.Failure("forbidden", "Request rejected.")),
            };
        }

        if (headers.ContainsKey("Transfer-Encoding"))
        {
            return Admission.Refuse(400, ToolEnvelope.Failure(
                "bad_request",
                "Transfer-Encoding is not supported; send a fixed Content-Length body."));
        }

        long length = 0;
        if (headers.TryGetValue("Content-Length", out string? declared) &&
            !long.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out length))
        {
            return Admission.Refuse(400, ToolEnvelope.Failure(
                "bad_request",
                "Content-Length must be a non-negative whole number of bytes."));
        }

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && length > 0)
        {
            return Admission.Refuse(400, ToolEnvelope.Failure(
                "bad_request",
                "GET requests must not include a request body."));
        }

        if (length > maxBodyBytes)
        {
            return Admission.Refuse(413, ToolEnvelope.Failure(
                "payload_too_large",
                $"Request body exceeds the {maxBodyBytes} byte limit. The body was not read."));
        }

        return Admission.Read((int)length);
    }

    /// <summary>
    /// Constant-time UTF-8 string comparison for secret tokens.
    /// </summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        byte[] x = Encoding.UTF8.GetBytes(a);
        byte[] y = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(x, y);
    }

    private static bool IsJsonMediaType(string contentType)
    {
        string trimmed = contentType.TrimStart();
        if (!trimmed.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length == "application/json".Length)
        {
            return true;
        }

        char next = trimmed["application/json".Length];
        return next == ';' || char.IsWhiteSpace(next);
    }

    private static bool IsValidMethod(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidHeaderName(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (c <= 0x20 || c >= 0x7F || c == ':')
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasControlOrLineBreak(string s)
    {
        foreach (char c in s)
        {
            if (c == '\r' || c == '\n' || c == '\0')
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasValidPercentEncoding(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '%')
            {
                continue;
            }

            if (i + 2 >= s.Length ||
                !char.IsAsciiHexDigit(s[i + 1]) ||
                !char.IsAsciiHexDigit(s[i + 2]))
            {
                return false;
            }

            i += 2;
        }

        return true;
    }
}
