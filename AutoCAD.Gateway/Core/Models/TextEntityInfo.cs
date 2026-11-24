using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Represents text entity content and spatial position in AutoCAD (DBText / MText).
/// </summary>
public sealed class TextEntityInfo
{
    /// <summary>
    /// Gets or sets the entity type ("DBText" or "MText").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text string content.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the X insertion coordinate.
    /// </summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y insertion coordinate.
    /// </summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the layer name.
    /// </summary>
    [JsonPropertyName("layer")]
    public string Layer { get; set; } = string.Empty;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string Tipo => Type;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string Texto => Text;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string Capa => Layer;
}
