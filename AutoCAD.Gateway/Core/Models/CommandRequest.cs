using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Representa la solicitud de ejecución de comando recibida por el Gateway.
/// </summary>
public sealed class CommandRequest
{
    /// <summary>
    /// Obtiene o establece la cadena de comandos o macro que se enviará a AutoCAD.
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}
