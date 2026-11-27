using System;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Controls how MCP tool results render human/LLM-facing <c>content</c> blocks via <see cref="OutputFormatParser.EnvVarName"/>.
/// </summary>
public enum OutputFormat
{
    /// <summary>Renders compact high-signal text (and native ImageContent) in content alongside full JSON structuredContent (default).</summary>
    Compact = 0,

    /// <summary>Renders legacy raw JSON text in content while still populating structuredContent, isError, and ImageContent.</summary>
    Json = 1,
}

/// <summary>
/// Parses and resolves <see cref="OutputFormat"/> values from raw strings or process environment variables.
/// </summary>
public static class OutputFormatParser
{
    /// <summary>Environment variable selecting the active MCP output format.</summary>
    public const string EnvVarName = "AUTOCAD_MCP_OUTPUT_FORMAT";

    /// <summary>
    /// Parses a raw format string into an <see cref="OutputFormat"/> value.
    /// Null, empty, or whitespace-only values resolve to <see cref="OutputFormat.Compact"/>.
    /// </summary>
    /// <param name="raw">Raw configuration string.</param>
    /// <returns>The parsed <see cref="OutputFormat"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="raw"/> is non-empty and unrecognized.</exception>
    public static OutputFormat Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return OutputFormat.Compact;
        }

        string normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "compact" => OutputFormat.Compact,
            "json" => OutputFormat.Json,
            _ => throw new ArgumentException(
                $"Invalid {EnvVarName} value '{raw.Trim()}'. Expected one of: compact, json.",
                nameof(raw)),
        };
    }

    /// <summary>Reads and parses <see cref="EnvVarName"/> from the current process environment.</summary>
    /// <returns>The configured <see cref="OutputFormat"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when the environment variable contains an unrecognized non-empty value.</exception>
    public static OutputFormat FromEnvironment() => Parse(Environment.GetEnvironmentVariable(EnvVarName));

    /// <summary>Converts an <see cref="OutputFormat"/> value to its canonical lowercase wire/env identifier.</summary>
    public static string ToWireName(this OutputFormat format) => format switch
    {
        OutputFormat.Compact => "compact",
        OutputFormat.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown OutputFormat value."),
    };
}
