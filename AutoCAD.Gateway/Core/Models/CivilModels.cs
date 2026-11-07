using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Core.Models;

/// <summary>Planar 2D point (X, Y) in drawing coordinates.</summary>
public class PointXY
{
    /// <summary>East (X) coordinate.</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>North (Y) coordinate.</summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }
}

/// <summary>Three-dimensional point (X, Y, Z) for surface vertices.</summary>
public class PointXYZ
{
    /// <summary>East (X) coordinate.</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>North (Y) coordinate.</summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>Elevation (Z).</summary>
    [JsonPropertyName("z")]
    public double Z { get; set; }
}

/// <summary>Request payload for creating a horizontal alignment from PIs.</summary>
public class CreateAlignmentRequest
{
    /// <summary>Unique alignment name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Legacy alias for 'name'.</summary>
    [JsonPropertyName("nombre")]
    public string? LegacyNombre { set => Name ??= value; }

    /// <summary>Points of intersection (PI) in station order (minimum 2).</summary>
    [JsonPropertyName("points")]
    public List<PointXY>? Points { get; set; }

    /// <summary>Legacy alias for 'points'.</summary>
    [JsonPropertyName("puntos")]
    public List<PointXY>? LegacyPuntos { set => Points ??= value; }

    /// <summary>Circular curve radius in meters; 0 or omitted = no curves.</summary>
    [JsonPropertyName("radius")]
    public double Radius { get; set; }

    /// <summary>Legacy alias for 'radius'.</summary>
    [JsonPropertyName("radio")]
    public double LegacyRadio { set => Radius = value; }

    /// <summary>Target layer; defaults to layer "0".</summary>
    [JsonPropertyName("layer")]
    public string? Layer { get; set; }

    /// <summary>Legacy alias for 'layer'.</summary>
    [JsonPropertyName("capa")]
    public string? LegacyCapa { set => Layer ??= value; }

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string? Nombre => Name;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public List<PointXY>? Puntos => Points;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public double Radio => Radius;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string? Capa => Layer;
}

/// <summary>Request payload for creating a TIN surface from survey points.</summary>
public class CreateSurfaceRequest
{
    /// <summary>Unique surface name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Legacy alias for 'name'.</summary>
    [JsonPropertyName("nombre")]
    public string? LegacyNombre { set => Name ??= value; }

    /// <summary>Survey points (minimum 3, non-collinear).</summary>
    [JsonPropertyName("points")]
    public List<PointXYZ>? Points { get; set; }

    /// <summary>Legacy alias for 'points'.</summary>
    [JsonPropertyName("puntos")]
    public List<PointXYZ>? LegacyPuntos { set => Points ??= value; }

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public string? Nombre => Name;

    /// <summary>Backward-compatibility accessor.</summary>
    [JsonIgnore]
    public List<PointXYZ>? Puntos => Points;
}

// Backward-compatible type aliases
public sealed class PuntoXY : PointXY { }
public sealed class PuntoXYZ : PointXYZ { }
public sealed class CrearAlineamientoRequest : CreateAlignmentRequest { }
public sealed class CrearSuperficieRequest : CreateSurfaceRequest { }
