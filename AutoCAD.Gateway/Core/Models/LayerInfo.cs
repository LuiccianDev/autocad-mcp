using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>
/// Contiene información del estado y propiedades de una capa en AutoCAD.
/// </summary>
public sealed class LayerInfo
{
    /// <summary>
    /// Obtiene o establece el nombre de la capa.
    /// </summary>
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Obtiene o establece un valor que indica si la capa se encuentra bloqueada.
    /// </summary>
    [JsonPropertyName("bloqueada")]
    public bool Bloqueada { get; set; }

    /// <summary>
    /// Obtiene o establece un valor que indica si la capa se encuentra congelada.
    /// </summary>
    [JsonPropertyName("congelada")]
    public bool Congelada { get; set; }

    /// <summary>
    /// Obtiene o establece un valor que indica si la capa se encuentra apagada.
    /// </summary>
    [JsonPropertyName("apagada")]
    public bool Apagada { get; set; }

    /// <summary>
    /// Obtiene o establece el índice numérico de color ACI de la capa.
    /// </summary>
    [JsonPropertyName("color")]
    public short Color { get; set; }
}
