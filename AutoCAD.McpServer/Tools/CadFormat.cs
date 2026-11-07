using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Shared formatting utilities for building safe AutoLISP expressions and CAD commands
/// (culture-invariant numbers, escaped LISP strings, point list parsing, and JSON responses).
/// </summary>
internal static class CadFormat
{
    /// <summary>Formats a floating-point number in culture-invariant notation with round-trip precision.</summary>
    /// <param name="value">Numeric value to format.</param>
    /// <returns>Textual representation with a decimal point.</returns>
    public static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Converts text into an escaped AutoLISP string literal enclosed in double quotes.
    /// </summary>
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

    /// <summary>
    /// Validates an AutoCAD layer, block, or linetype symbol name (non-empty, length &lt;= 255, no reserved characters).
    /// </summary>
    /// <param name="name">Symbol name to validate.</param>
    /// <returns><c>true</c> if valid for the symbol table.</returns>
    public static bool IsValidSymbolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return false;
        }

        return name.IndexOfAny(['<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`']) < 0;
    }

    /// <summary>Converts sexagesimal degrees to radians.</summary>
    /// <param name="degrees">Angle in degrees.</param>
    /// <returns>Angle in radians.</returns>
    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// Parses a string formatted as <c>"v1,v2[,v3];..."</c> into numeric coordinate rows. Returns <c>null</c> if invalid.
    /// </summary>
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
                    || double.IsNaN(result[i][j]) || double.IsInfinity(result[i][j]))
                {
                    return null;
                }
            }
        }

        return result;
    }

    /// <summary>Indicates whether all supplied numeric values are finite (not NaN or infinite).</summary>
    /// <param name="values">Values to check.</param>
    /// <returns><c>true</c> if all values are finite.</returns>
    public static bool AllFinite(params double[] values)
    {
        foreach (double v in values)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Serializes a standard error JSON response.</summary>
    /// <param name="message">Descriptive error message.</param>
    /// <returns>JSON <c>{"status":"error","message":...}</c>.</returns>
    public static string Error(string message) =>
        JsonSerializer.Serialize(new { status = "error", message });
}
