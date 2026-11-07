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

    /// <summary>Legacy alias for 'type'.</summary>
    [JsonPropertyName("tipo")]
    public string Tipo => Type;

    /// <summary>Legacy alias for 'text'.</summary>
    [JsonPropertyName("texto")]
    public string Texto => Text;

    /// <summary>Legacy alias for 'layer'.</summary>
    [JsonPropertyName("capa")]
    public string Capa => Layer;
}
