using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contiene metadatos e información básica sobre el documento activo de AutoCAD.
/// </summary>
public sealed class DrawingInfo
{
    /// <summary>
    /// Obtiene o establece el nombre o ruta del archivo de dibujo activo.
    /// </summary>
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Obtiene o establece un valor que indica si el documento está en modo de solo lectura.
    /// </summary>
    [JsonPropertyName("soloLectura")]
    public bool SoloLectura { get; set; }

    /// <summary>
    /// Obtiene o establece las unidades de inserción configuradas en el dibujo (INSUNITS).
    /// </summary>
    [JsonPropertyName("unidades")]
    public string Unidades { get; set; } = string.Empty;
}
