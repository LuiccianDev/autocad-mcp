using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Represents the command execution request payload received by the Gateway.
/// </summary>
public sealed class CommandRequest
{
    /// <summary>
    /// Gets or sets the command string or macro to enqueue into AutoCAD.
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}
