using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contains three-dimensional minimum and maximum bounding coordinates of the active drawing.
/// </summary>
public sealed class DrawingExtentsInfo
{
    /// <summary>
    /// Gets or sets the minimum X coordinate.
    /// </summary>
    [JsonPropertyName("minX")]
    public double MinX { get; set; }

    /// <summary>
    /// Gets or sets the minimum Y coordinate.
    /// </summary>
    [JsonPropertyName("minY")]
    public double MinY { get; set; }

    /// <summary>
    /// Gets or sets the minimum Z coordinate.
    /// </summary>
    [JsonPropertyName("minZ")]
    public double MinZ { get; set; }

    /// <summary>
    /// Gets or sets the maximum X coordinate.
    /// </summary>
    [JsonPropertyName("maxX")]
    public double MaxX { get; set; }

    /// <summary>
    /// Gets or sets the maximum Y coordinate.
    /// </summary>
    [JsonPropertyName("maxY")]
    public double MaxY { get; set; }

    /// <summary>
    /// Gets or sets the maximum Z coordinate.
    /// </summary>
    [JsonPropertyName("maxZ")]
    public double MaxZ { get; set; }
}
