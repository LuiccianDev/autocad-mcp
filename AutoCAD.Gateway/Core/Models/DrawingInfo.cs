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

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string Nombre => Name;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public bool SoloLectura => IsReadOnly;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string Unidades => Units;
}
