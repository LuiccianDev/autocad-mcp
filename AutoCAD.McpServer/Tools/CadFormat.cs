using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using AutoCAD.Gateway.Contracts;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Shared formatting and validation utilities for AutoLISP expressions, CAD commands,
/// coordinate lists, and structured JSON envelopes.
/// </summary>
internal static class CadFormat
{
    /// <summary>Formats a floating-point number in culture-invariant notation with round-trip precision.</summary>
    /// <param name="value">Numeric value to format.</param>
    /// <returns>Textual representation with a decimal point.</returns>
    public static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Converts text into an escaped AutoLISP string literal enclosed in double quotes.</summary>
    /// <param name="value">Text to escape.</param>
    /// <returns>LISP string literal, e.g. <c>"WALLS"</c>.</returns>
    public static string LispString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n':
                case '\r': sb.Append(' '); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Converts sexagesimal degrees to radians.</summary>
    /// <param name="degrees">Angle in degrees.</param>
    /// <returns>Angle in radians.</returns>
    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>Parses a string formatted as <c>"v1,v2[,v3];..."</c> into numeric coordinate rows. Returns <c>null</c> if invalid.</summary>
    /// <param name="points">Semicolon-separated coordinate list.</param>
    /// <param name="columns">Dimension count per vertex (2 for XY, 3 for XYZ).</param>
    /// <returns>Array of coordinate arrays, or <c>null</c> on syntax error.</returns>
    public static double[][]? ParsePoints(string? points, int columns)
    {
        if (string.IsNullOrWhiteSpace(points))
        {
            return null;
        }

        var rows = points.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length > SecurityPolicy.MaxGeometryVertices)
        {
            return null;
        }
        var result = new double[rows.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            var parts = rows[i].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != columns)
            {
                return null;
            }

            result[i] = new double[columns];
            for (int j = 0; j < columns; j++)
            {
                if (!double.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i][j])
                    || !double.IsFinite(result[i][j]))
                {
                    return null;
                }
            }
        }

        return result;
    }

    /// <summary>Parses a JSON array or comma/semicolon-separated string list without swallowing malformed JSON syntax.</summary>
    /// <param name="raw">Raw string input.</param>
    /// <param name="values">Parsed non-empty items, or null if input was blank/empty.</param>
    /// <param name="error">Error description when malformed JSON array syntax was provided.</param>
    /// <returns><c>true</c> if parsing succeeded (including empty); <c>false</c> on malformed JSON.</returns>
    public static bool TryParseStringList(string? raw, out string[]? values, out string? error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            values = null;
            error = null;
            return true;
        }

        string trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                string[]? parsed = JsonSerializer.Deserialize<string[]>(trimmed);
                if (parsed is null)
                {
                    values = null;
                    error = "JSON array cannot be null.";
                    return false;
                }

                string[] clean = parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                values = clean.Length > 0 ? clean : null;
                error = null;
                return true;
            }
            catch (JsonException ex)
            {
                values = null;
                error = $"Invalid JSON array syntax: {ex.Message}";
                return false;
            }
        }

        string[] parts = trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        values = parts.Length > 0 ? parts : null;
        error = null;
        return true;
    }

    /// <summary>Parses a handle list from JSON array or delimited string without crashing on malformed JSON.</summary>
    public static bool TryParseHandleList(string? raw, out List<string> handles, out string? error)
    {
        if (!TryParseStringList(raw, out string[]? arr, out error))
        {
            handles = [];
            return false;
        }

        handles = arr?.ToList() ?? [];
        if (handles.Count > SecurityPolicy.MaxGeometryVertices)
        {
            handles = [];
            error = $"Handle list cannot exceed {SecurityPolicy.MaxGeometryVertices} items.";
            return false;
        }

        return true;
    }

    /// <summary>Parses and validates a list of hexadecimal DXF entity handles without swallowing malformed JSON.</summary>
    public static bool TryParseValidHandles(
        string? raw,
        out List<string> handles,
        out string? error,
        bool required = false)
    {
        if (!TryParseHandleList(raw, out handles, out error))
        {
            return false;
        }

        if (required && handles.Count == 0)
        {
            error = "'handles' must contain valid non-empty hexadecimal entity handles.";
            return false;
        }

        if (handles.Any(h => !AutoCAD.Gateway.Contracts.SecurityPolicy.IsValidEntityHandle(h)))
        {
            error = "All handles must be valid non-empty hexadecimal entity handles.";
            return false;
        }

        return true;
    }

    /// <summary>Parses a JSON array string and returns a cloned <see cref="JsonElement"/> without leaking the underlying <see cref="JsonDocument"/>.</summary>
    public static bool TryParseJsonArrayElement(string? raw, out JsonElement element, out string? error)
    {
        element = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "JSON array cannot be empty.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "Expected a JSON array.";
                return false;
            }

            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Indicates whether <paramref name="value"/> contains any control characters.</summary>
    public static bool HasControlChars(string? value) => SecurityPolicy.HasControlChars(value);

    /// <summary>Indicates whether all supplied numeric values are finite (not NaN or infinite).</summary>
    /// <param name="values">Values to check.</param>
    /// <returns><c>true</c> if all values are finite.</returns>
    public static bool AllFinite(params double[] values) => values.All(double.IsFinite);

    /// <summary>Indicates whether all supplied optional numeric values are either null or finite.</summary>
    /// <param name="values">Optional values to check.</param>
    /// <returns><c>true</c> if all non-null values are finite.</returns>
    public static bool AllOptionalFinite(params double?[] values) =>
        values.All(v => !v.HasValue || double.IsFinite(v.Value));

    /// <summary>Serializes a standard structured error JSON response.</summary>
    public static string Error(
        string message,
        string code = "bad_argument",
        string? detail = null,
        string? hint = null) =>
        JsonSerializer.Serialize(new
        {
            status = "error",
            ok = false,
            message,
            error = new ToolError(code, message, detail, hint),
        }, GatewayProtocol.WireJsonOptions);
}
