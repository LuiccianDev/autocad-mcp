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

    /// <summary>Legacy alias for 'name'.</summary>
    [JsonPropertyName("nombre")]
    public string Nombre => Name;

    /// <summary>Legacy alias for 'is_locked'.</summary>
    [JsonPropertyName("bloqueada")]
    public bool Bloqueada => IsLocked;

    /// <summary>Legacy alias for 'is_frozen'.</summary>
    [JsonPropertyName("congelada")]
    public bool Congelada => IsFrozen;

    /// <summary>Legacy alias for 'is_off'.</summary>
    [JsonPropertyName("apagada")]
    public bool Apagada => IsOff;
}
