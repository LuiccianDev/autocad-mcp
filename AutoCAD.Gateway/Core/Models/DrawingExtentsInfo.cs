using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contiene las coordenadas tridimensionales de extensión mínima y máxima del dibujo.
/// </summary>
public sealed class DrawingExtentsInfo
{
    /// <summary>
    /// Obtiene o establece la coordenada X mínima del dibujo.
    /// </summary>
    [JsonPropertyName("minX")]
    public double MinX { get; set; }

    /// <summary>
    /// Obtiene o establece la coordenada Y mínima del dibujo.
    /// </summary>
    [JsonPropertyName("minY")]
    public double MinY { get; set; }

    /// <summary>
    /// Obtiene o establece la coordenada Z mínima del dibujo.
    /// </summary>
    [JsonPropertyName("minZ")]
    public double MinZ { get; set; }

    /// <summary>
    /// Obtiene o establece la coordenada X máxima del dibujo.
    /// </summary>
    [JsonPropertyName("maxX")]
    public double MaxX { get; set; }

    /// <summary>
    /// Obtiene o establece la coordenada Y máxima del dibujo.
    /// </summary>
    [JsonPropertyName("maxY")]
    public double MaxY { get; set; }

    /// <summary>
    /// Obtiene o establece la coordenada Z máxima del dibujo.
    /// </summary>
    [JsonPropertyName("maxZ")]
    public double MaxZ { get; set; }
}
