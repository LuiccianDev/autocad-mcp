using System.Text.Json.Serialization;

namespace AutoCAD.McpServer.Models;

/// <summary>
/// Representa el cuerpo de la petición JSON enviado al AutoCAD AI Gateway para ejecutar un comando.
/// </summary>
public sealed class CommandPayload
{
    /// <summary>
    /// Obtiene o establece el comando o secuencia de comandos a despachar en AutoCAD.
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}
