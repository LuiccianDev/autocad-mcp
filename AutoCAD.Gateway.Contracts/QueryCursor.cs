using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Typed error thrown when a pagination cursor fails validation, expires, or becomes stale.
/// </summary>
public sealed class CursorValidationException : Exception
{
    /// <summary>Stable machine-readable error code (CURSOR_INVALID, CURSOR_EXPIRED, CURSOR_STALE).</summary>
    public string Code { get; }

    /// <summary>Initializes a new instance of <see cref="CursorValidationException"/>.</summary>
    public CursorValidationException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// Signs and verifies HMAC-SHA256 pagination cursors bound to a single Gateway instance,
/// document ID, drawing revision, query fingerprint, and expiration time.
/// </summary>
public sealed class QueryCursor
{
    private const int MaxPayloadBytes = 1024;
    private const int MaxTokenLength = 2048;
    private static readonly JsonSerializerOptions ReadJsonOptions = new() { MaxDepth = 8 };

    private readonly byte[] _key;
    private readonly Func<long> _nowUnixSeconds;
    private readonly int _ttlSeconds;

    private sealed record Payload(
        int Version,
        string DocumentId,
        long Revision,
        int Offset,
        string Fingerprint,
        long Expires);

    /// <summary>Initializes a new <see cref="QueryCursor"/> signer/verifier.</summary>
    public QueryCursor(int ttlSeconds = 600, Func<long>? nowUnixSeconds = null, byte[]? key = null)
    {
        if (ttlSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "TTL must be greater than zero.");
        }

        _ttlSeconds = ttlSeconds;
        _nowUnixSeconds = nowUnixSeconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _key = key?.ToArray() ?? RandomNumberGenerator.GetBytes(32);

        if (_key.Length < 32)
        {
            throw new ArgumentException("Cursor signing key must be at least 32 bytes.", nameof(key));
        }
    }

    /// <summary>Creates a signed cursor token for the next query page.</summary>
    public string Create(string documentId, long revision, int offset, string fingerprint)
    {
        if (string.IsNullOrEmpty(documentId))
        {
            throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        var payload = new Payload(1, documentId, revision, offset, fingerprint ?? string.Empty, _nowUnixSeconds() + _ttlSeconds);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (data.Length > MaxPayloadBytes)
        {
            throw new ArgumentException("Cursor payload exceeds maximum allowed size.", nameof(fingerprint));
        }

        byte[] mac = HMACSHA256.HashData(_key, data);
        string token = $"{Encode(data)}.{Encode(mac)}";
        if (token.Length > MaxTokenLength)
        {
            throw new ArgumentException("Encoded cursor exceeds maximum token length.", nameof(fingerprint));
        }

        return token;
    }

    /// <summary>
    /// Validates a cursor token against the active document, revision, and query fingerprint,
    /// returning the page offset if valid.
    /// </summary>
    public int Read(string token, string documentId, long revision, string fingerprint)
    {
        Payload payload;
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            {
                throw new FormatException();
            }

            string[] parts = token.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException();
            }

            byte[] data = Decode(parts[0]);
            byte[] sig = Decode(parts[1]);
            if (data.Length > MaxPayloadBytes || sig.Length != 32)
            {
                throw new FormatException();
            }

            byte[] expected = HMACSHA256.HashData(_key, data);
            if (!CryptographicOperations.FixedTimeEquals(sig, expected))
            {
                throw new FormatException();
            }

            payload = JsonSerializer.Deserialize<Payload>(data, ReadJsonOptions)
                ?? throw new FormatException();

            if (payload.Version != 1 ||
                !string.Equals(payload.DocumentId, documentId, StringComparison.Ordinal) ||
                !string.Equals(payload.Fingerprint, fingerprint ?? string.Empty, StringComparison.Ordinal) ||
                payload.Offset < 0)
            {
                throw new FormatException();
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new CursorValidationException(
                "CURSOR_INVALID",
                "Cursor is malformed or does not match this instance, document, or query filter.");
        }

        if (payload.Expires <= _nowUnixSeconds())
        {
            throw new CursorValidationException(
                "CURSOR_EXPIRED",
                "Cursor expired; restart the query from the first page.");
        }

        if (payload.Revision != revision)
        {
            throw new CursorValidationException(
                "CURSOR_STALE",
                "Drawing revision changed; restart the query from the first page.");
        }

        return payload.Offset;
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
        {
            throw new FormatException();
        }

        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
