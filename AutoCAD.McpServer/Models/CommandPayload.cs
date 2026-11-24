using System.Text.Json.Serialization;

namespace AutoCAD.McpServer.Models;

/// <summary>
/// Represents the JSON payload sent to AutoCAD AI Gateway to enqueue a command sequence.
/// </summary>
public sealed class CommandPayload
{
    /// <summary>
    /// Gets or sets the AutoCAD command string or macro sequence (with newline terminators).
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}
