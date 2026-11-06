using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>Punto plano (X, Y) en coordenadas del dibujo.</summary>
public sealed class PuntoXY
{
    /// <summary>Coordenada Este (X).</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>Coordenada Norte (Y).</summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }
}

/// <summary>Punto tridimensional (X, Y, Z) para vértices de superficie.</summary>
public sealed class PuntoXYZ
{
    /// <summary>Coordenada Este (X).</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>Coordenada Norte (Y).</summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>Cota (Z).</summary>
    [JsonPropertyName("z")]
    public double Z { get; set; }
}

/// <summary>Solicitud para crear un alineamiento horizontal a partir de PIs.</summary>
public sealed class CrearAlineamientoRequest
{
    /// <summary>Nombre único del alineamiento.</summary>
    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }

    /// <summary>Puntos de inflexión (PI) en orden de progresiva (mínimo 2).</summary>
    [JsonPropertyName("puntos")]
    public List<PuntoXY>? Puntos { get; set; }

    /// <summary>Radio de curva circular en metros; 0 o ausente = sin curvas.</summary>
    [JsonPropertyName("radio")]
    public double Radio { get; set; }

    /// <summary>Capa destino; por defecto la capa "0".</summary>
    [JsonPropertyName("capa")]
    public string? Capa { get; set; }
}

/// <summary>Solicitud para crear una superficie TIN a partir de puntos topográficos.</summary>
public sealed class CrearSuperficieRequest
{
    /// <summary>Nombre único de la superficie.</summary>
    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }

    /// <summary>Puntos topográficos (mínimo 3, no colineales).</summary>
    [JsonPropertyName("puntos")]
    public List<PuntoXYZ>? Puntos { get; set; }
}
