using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contiene datos de una entidad de texto (DBText o MText) en el dibujo.
/// </summary>
public sealed class TextEntityInfo
{
    /// <summary>
    /// Obtiene o establece el tipo de texto ("DBText" o "MText").
    /// </summary>
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// Obtiene o establece el contenido textual.
    /// </summary>
    [JsonPropertyName("texto")]
    public string Texto { get; set; } = string.Empty;

    /// <summary>
    /// Obtiene o establece la posición X en coordenadas del dibujo.
    /// </summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>
    /// Obtiene o establece la posición Y en coordenadas del dibujo.
    /// </summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>
    /// Obtiene o establece la capa donde se ubica el texto.
    /// </summary>
    [JsonPropertyName("capa")]
    public string Capa { get; set; } = string.Empty;
}
