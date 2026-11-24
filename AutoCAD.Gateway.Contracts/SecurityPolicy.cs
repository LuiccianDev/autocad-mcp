using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Pure validation and bounds policies enforced across Gateway endpoints and MCP tools.
/// </summary>
public static class SecurityPolicy
{
    /// <summary>Maximum number of operations allowed inside one atomic batch call.</summary>
    public const int MaxBatchOperations = 500;

    /// <summary>Default page size for filtered entity queries.</summary>
    public const int DefaultQueryLimit = 100;

    /// <summary>Maximum page size allowed per paginated entity query.</summary>
    public const int MaxQueryLimit = 500;

    /// <summary>Maximum table rows permitted by <c>/drafting/table</c>.</summary>
    public const int MaxTableRows = 200;

    /// <summary>Maximum table columns permitted by <c>/drafting/table</c>.</summary>
    public const int MaxTableCols = 50;

    /// <summary>Maximum longitudinal samples permitted by <c>/civil/profile</c>.</summary>
    public const int MaxProfileSamples = 500;

    /// <summary>Maximum transverse samples permitted by <c>/civil/cross-section</c>.</summary>
    public const int MaxCrossSectionSamples = 200;

    /// <summary>Maximum inline PNG payload size in bytes returned by <c>/context/screenshot?inline=true</c> (8 MiB).</summary>
    public const int MaxScreenshotInlineBytes = 8 * 1024 * 1024;

    /// <summary>Maximum geometry vertices or cumulative handle operations permitted per transaction.</summary>
    public const int MaxGeometryVertices = 10_000;

    private static readonly string[] BlockedSystemVariables =
    [
        "TRUSTEDPATHS",
        "TRUSTEDDOMAINS",
        "SECURELOAD",
        "LEGACYCODESEARCH",
        "SAFEMODE",
        "ACAD",
        "ACADPREFIX",
        "APPAUTOLOAD",
        "DEMANDLOAD",
        "LOGFILENAME",
        "LOGFILEMODE",
        "SAVEFILEPATH",
    ];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] InvalidSymbolChars =
    [
        '\\', '/', '<', '>', '?', '"', ':', ';', '*', '|', ',', '=', '`'
    ];

    /// <summary>Returns true when <paramref name="name"/> is a security-sensitive AutoCAD system variable.</summary>
    /// <param name="name">System variable name.</param>
    public static bool IsBlockedSystemVariable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim().TrimStart('_', '.');
        return BlockedSystemVariables.Any(blocked =>
            string.Equals(blocked, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Validates that an AutoCAD symbol name (layer, block, style) is non-empty and free of illegal characters.</summary>
    /// <param name="name">Candidate symbol table record name.</param>
    public static bool IsValidSymbolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (trimmed.Length > 255)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (c < 0x20 || Array.IndexOf(InvalidSymbolChars, c) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns true when <paramref name="plotStyle"/> is a plain <c>.ctb</c>/<c>.stb</c> table filename without path traversal or prompt tokens.</summary>
    /// <param name="plotStyle">Plot style filename.</param>
    /// <param name="allowSpaces">True when passed through typed ObjectARX parameters instead of space-delimited CLI scripts.</param>
    public static bool IsSafePlotStyleName(string? plotStyle, bool allowSpaces = false)
    {
        if (string.IsNullOrWhiteSpace(plotStyle))
        {
            return false;
        }

        string trimmed = plotStyle.Trim();
        if (trimmed.Length > 255 || trimmed.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (c < 0x20 ||
                c is '~' or '?' or '\\' or '/' or ':' or '"' or '<' or '>' or '|' or '*' or ';' or ',' or '=' or '`')
            {
                return false;
            }

            if (!allowSpaces && char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        int dotIdx = trimmed.IndexOf('.');
        string stem = dotIdx >= 0 ? trimmed[..dotIdx] : trimmed;
        if (ReservedDeviceNames.Contains(stem))
        {
            return false;
        }

        string ext = Path.GetExtension(trimmed);
        return string.Equals(ext, ".ctb", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".stb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns true when <paramref name="layoutName"/> is a valid AutoCAD layout symbol free of prompt tokens.</summary>
    /// <param name="layoutName">Candidate layout name.</param>
    /// <param name="allowSpaces">True when passed via ObjectARX API; false for CLI command scripts.</param>
    public static bool IsSafeLayoutName(string? layoutName, bool allowSpaces = false)
    {
        if (!IsValidSymbolName(layoutName))
        {
            return false;
        }

        string trimmed = layoutName!.Trim();
        if (trimmed.Contains('~') || trimmed.Contains('?'))
        {
            return false;
        }

        return allowSpaces || !trimmed.Any(char.IsWhiteSpace);
    }

    /// <summary>Validates that <paramref name="handle"/> is a non-empty 1..16 digit hexadecimal DXF handle.</summary>
    /// <param name="handle">Hexadecimal entity handle string.</param>
    public static bool IsValidEntityHandle(string? handle) =>
        TryParseHandleValue(handle, out _);

    /// <summary>Validates and parses a hexadecimal DXF entity handle into a non-zero 64-bit integer value.</summary>
    /// <param name="handle">Hexadecimal handle string.</param>
    /// <param name="value">Parsed non-zero 64-bit handle value.</param>
    public static bool TryParseHandleValue(string? handle, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(handle))
        {
            return false;
        }

        string trimmed = handle.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 16)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        if (!ulong.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong unsignedValue) ||
            unsignedValue == 0)
        {
            return false;
        }

        value = unchecked((long)unsignedValue);
        return true;
    }

    /// <summary>Returns true when two axis-aligned 2D bounding boxes overlap or touch.</summary>
    public static bool IntersectsBbox(
        double minX,
        double minY,
        double maxX,
        double maxY,
        double bMinX,
        double bMinY,
        double bMaxX,
        double bMaxY)
    {
        double a0x = Math.Min(minX, maxX);
        double a1x = Math.Max(minX, maxX);
        double a0y = Math.Min(minY, maxY);
        double a1y = Math.Max(minY, maxY);

        double b0x = Math.Min(bMinX, bMaxX);
        double b1x = Math.Max(bMinX, bMaxX);
        double b0y = Math.Min(bMinY, bMaxY);
        double b1y = Math.Max(bMinY, bMaxY);

        return a0x <= b1x && a1x >= b0x && a0y <= b1y && a1y >= b0y;
    }

    /// <summary>
    /// Validates and canonicalizes a local filesystem path, blocking control characters,
    /// parent directory traversal (<c>..</c>), UNC/device prefixes, alternate data streams,
    /// and disallowed file extensions.
    /// </summary>
    /// <param name="path">Input path supplied by caller.</param>
    /// <param name="normalized">Normalized full path on success.</param>
    /// <param name="error">Human-readable rejection reason on failure.</param>
    /// <param name="allowedExtensions">Optional extension allowlist (for example <c>.dwg</c>, <c>.lsp</c>).</param>
    public static bool ValidateLocalFilePath(
        string? path,
        out string normalized,
        out string? error,
        params string[] allowedExtensions)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "File path cannot be empty.";
            return false;
        }

        string trimmed = path.Trim();
        foreach (char c in trimmed)
        {
            if (c == '\0' || c < 0x20 || c is '"' or '<' or '>' or '|' or '*' or '?')
            {
                error = "File path contains invalid or control characters.";
                return false;
            }
        }

        if (trimmed.Length >= 2 && trimmed[0] is '\\' or '/' && trimmed[1] is '\\' or '/')
        {
            error = "UNC or device network paths are not permitted.";
            return false;
        }

        // Reject NTFS alternate data streams (any colon outside a Windows drive prefix like C:).
        int colonIdx = trimmed.IndexOf(':');
        if (colonIdx >= 0 && (colonIdx != 1 || !char.IsAsciiLetter(trimmed[0]) || trimmed.IndexOf(':', 2) >= 0))
        {
            error = "Path contains an unexpected colon specifier.";
            return false;
        }

        string[] segments = trimmed.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string seg in segments)
        {
            if (seg == ".." || seg.EndsWith('.'))
            {
                error = "Path traversal ('..') or trailing-dot segments are not permitted.";
                return false;
            }

            int dotIdx = seg.IndexOf('.');
            string stem = dotIdx >= 0 ? seg[..dotIdx] : seg;
            if (ReservedDeviceNames.Contains(stem))
            {
                error = $"Reserved Windows device name '{stem}' is not permitted in paths.";
                return false;
            }
        }

        bool isWinDriveAbs = trimmed.Length >= 3 &&
            char.IsAsciiLetter(trimmed[0]) &&
            trimmed[1] == ':' &&
            trimmed[2] is '\\' or '/';
        if (!isWinDriveAbs || !Path.IsPathFullyQualified(trimmed))
        {
            error = "File path must be an absolute local Windows path (for example C:\\folder\\file).";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (Exception ex)
        {
            error = $"Invalid file path: {ex.Message}";
            return false;
        }

        if (allowedExtensions is { Length: > 0 })
        {
            string ext = Path.GetExtension(normalized);
            bool allowed = allowedExtensions.Any(e =>
                string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                error = $"File extension '{ext}' is not allowed. Expected: {string.Join(", ", allowedExtensions)}.";
                return false;
            }
        }

        return true;
    }
}
