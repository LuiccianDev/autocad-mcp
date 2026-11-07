using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contains metadata and basic information about the active AutoCAD drawing document.
/// </summary>
public sealed class DrawingInfo
{
    /// <summary>
    /// Gets or sets the file path or name of the active drawing.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the document is in read-only mode.
    /// </summary>
    [JsonPropertyName("is_read_only")]
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the drawing insertion units (INSUNITS).
    /// </summary>
    [JsonPropertyName("units")]
    public string Units { get; set; } = string.Empty;

    /// <summary>Legacy alias for 'name'.</summary>
    [JsonPropertyName("nombre")]
    public string Nombre => Name;

    /// <summary>Legacy alias for 'is_read_only'.</summary>
    [JsonPropertyName("soloLectura")]
    public bool SoloLectura => IsReadOnly;

    /// <summary>Legacy alias for 'units'.</summary>
    [JsonPropertyName("unidades")]
    public string Unidades => Units;
}
