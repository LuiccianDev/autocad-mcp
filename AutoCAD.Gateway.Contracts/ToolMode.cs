using System;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Controls how MCP tools are exposed to LLM clients via <see cref="ToolModeParser.EnvVarName"/>.
/// </summary>
public enum ToolMode
{
    /// <summary>Exposes all canonical and alias tools individually (default).</summary>
    Granular,

    /// <summary>Exposes grouped domain router tools with on-demand schema introspection.</summary>
    Compound,

    /// <summary>Exposes a curated subset of high-leverage standalone tools with zero aliases.</summary>
    Core,
}

/// <summary>
/// Parses and resolves <see cref="ToolMode"/> values from raw strings or process environment variables.
/// </summary>
public static class ToolModeParser
{
    /// <summary>Environment variable selecting the active MCP tool exposure mode.</summary>
    public const string EnvVarName = "AUTOCAD_MCP_TOOL_MODE";

    /// <summary>
    /// Parses a raw mode string into a <see cref="ToolMode"/> value.
    /// Null, empty, or whitespace-only values resolve to <see cref="ToolMode.Granular"/>.
    /// </summary>
    /// <param name="raw">Raw configuration string.</param>
    /// <returns>The parsed <see cref="ToolMode"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="raw"/> is non-empty and unrecognized.</exception>
    public static ToolMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ToolMode.Granular;
        }

        string normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "granular" => ToolMode.Granular,
            "compound" => ToolMode.Compound,
            "core" => ToolMode.Core,
            _ => throw new ArgumentException(
                $"Invalid {EnvVarName} value '{raw.Trim()}'. Valid modes: granular, compound, core.",
                nameof(raw)),
        };
    }

    /// <summary>
    /// Reads and parses <see cref="EnvVarName"/> from the current process environment.
    /// </summary>
    /// <returns>The configured <see cref="ToolMode"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when the environment variable contains an unrecognized non-empty value.</exception>
    public static ToolMode FromEnvironment() => Parse(Environment.GetEnvironmentVariable(EnvVarName));

    /// <summary>
    /// Converts a <see cref="ToolMode"/> enum value to its canonical lowercase wire/CLI identifier.
    /// </summary>
    /// <param name="mode">Mode value to format.</param>
    /// <returns><c>"granular"</c>, <c>"compound"</c>, or <c>"core"</c>.</returns>
    public static string ToWireName(this ToolMode mode) => mode switch
    {
        ToolMode.Granular => "granular",
        ToolMode.Compound => "compound",
        ToolMode.Core => "core",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ToolMode value."),
    };
}
