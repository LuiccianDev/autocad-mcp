using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contains layer state and property information in AutoCAD.
/// </summary>
public sealed class LayerInfo
{
    /// <summary>
    /// Gets or sets the layer name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the layer is locked.
    /// </summary>
    [JsonPropertyName("is_locked")]
    public bool IsLocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the layer is frozen.
    /// </summary>
    [JsonPropertyName("is_frozen")]
    public bool IsFrozen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the layer is turned off.
    /// </summary>
    [JsonPropertyName("is_off")]
    public bool IsOff { get; set; }

    /// <summary>
    /// Gets or sets the numeric ACI color index of the layer.
    /// </summary>
    [JsonPropertyName("color")]
    public short Color { get; set; }

    /// <summary>
    /// Gets or sets the linetype name assigned to the layer.
    /// </summary>
    [JsonPropertyName("linetype")]
    public string Linetype { get; set; } = "Continuous";

    /// <summary>
    /// Gets or sets the formatted lineweight in millimeters or <c>"default"</c>.
    /// </summary>
    [JsonPropertyName("lineweight")]
    public string Lineweight { get; set; } = "default";

    /// <summary>
    /// Gets or sets a value indicating whether objects on this layer are plottable.
    /// </summary>
    [JsonPropertyName("is_plottable")]
    public bool IsPlottable { get; set; } = true;

    /// <summary>
    /// Gets or sets the optional layer description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string Nombre => Name;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public bool Bloqueada => IsLocked;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public bool Congelada => IsFrozen;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public bool Apagada => IsOff;
}
