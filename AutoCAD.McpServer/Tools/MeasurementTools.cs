using System;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// MCP tools for geometric measurement (distances, areas, perimeters) and drawing maintenance (purge, audit, regen).
/// </summary>
/// <remarks>
/// Measurements are computed locally within the MCP server process: deterministic, zero latency, and requiring no active CAD instance.
/// </remarks>
[McpServerToolType]
public sealed class MeasurementTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeasurementTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the AutoCAD Gateway.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gatewayClient"/> is null.</exception>
    public MeasurementTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Calculates 2D and 3D Euclidean distances, deltas, azimuth, and slope between two points in meters.
    /// </summary>
    /// <param name="x1">Start point X (m).</param>
    /// <param name="y1">Start point Y (m).</param>
    /// <param name="x2">End point X (m).</param>
    /// <param name="y2">End point Y (m).</param>
    /// <param name="z1">Start point Z (m), default 0.</param>
    /// <param name="z2">End point Z (m), default 0.</param>
    /// <returns>JSON containing 2D/3D distances, coordinate deltas, angle, and slope.</returns>
    [McpServerTool(Name = "measure_distance")]
    [Description("Calculate (without CAD) 2D horizontal distance, 3D distance, delta X/Y/Z, angle in degrees (0 = East, CCW), and slope % between two points in meters.")]
    public string MeasureDistance(double x1, double y1, double x2, double y2, double z1 = 0, double z2 = 0)
    {
        if (!AllFinite(x1, y1, z1, x2, y2, z2))
        {
            return Error("Coordinates must be finite numbers.");
        }

        double dx = x2 - x1, dy = y2 - y1, dz = z2 - z1;
        double d2 = Math.Sqrt(dx * dx + dy * dy);
        double d3 = Math.Sqrt(d2 * d2 + dz * dz);
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360.0;
        }

        double? slope = d2 > 0 ? Math.Round(dz / d2 * 100.0, 4) : null;

        return JsonSerializer.Serialize(new
        {
            status = "success",
            distance_2d = Math.Round(d2, 4),
            distance_3d = Math.Round(d3, 4),
            delta_x = Math.Round(dx, 4),
            delta_y = Math.Round(dy, 4),
            delta_z = Math.Round(dz, 4),
            angle_degrees = Math.Round(angle, 4),
            slope_percent = slope,
            units = "meters",
            // Backward compatibility aliases
            distancia_2d = Math.Round(d2, 4),
            distancia_3d = Math.Round(d3, 4),
            angulo_grados = Math.Round(angle, 4),
            pendiente_porcentaje = slope
        });
    }

    /// <summary>
    /// Calculates polygon area (Gauss formula), perimeter, centroid, and winding order of a closed polygon.
    /// </summary>
    /// <param name="points">Vertices in 'x1,y1;x2,y2;...' format (minimum 3 vertices).</param>
    /// <returns>JSON with area in m2, perimeter, centroid, and orientation.</returns>
    [McpServerTool(Name = "calculate_polygon_area")]
    [Description("Calculate (without CAD) area in m2, hectares, perimeter in meters, centroid, and winding orientation of a closed polygon defined by vertices 'x1,y1;x2,y2;...' (minimum 3).")]
    public string CalculatePolygonArea(string points)
    {
        var pts = ParsePoints(points, 2);
        if (pts == null || pts.Length < 3)
        {
            return Error("Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 3 numeric vertices.");
        }

        int n = pts.Length;
        if (n > 3 && pts[0][0] == pts[n - 1][0] && pts[0][1] == pts[n - 1][1])
        {
            n--;
        }

        double ox = pts[0][0], oy = pts[0][1];
        double twiceArea = 0, perimeter = 0, cx = 0, cy = 0;
        for (int i = 0; i < n; i++)
        {
            double xi = pts[i][0] - ox, yi = pts[i][1] - oy;
            double xj = pts[(i + 1) % n][0] - ox, yj = pts[(i + 1) % n][1] - oy;
            double cross = xi * yj - xj * yi;
            twiceArea += cross;
            cx += (xi + xj) * cross;
            cy += (yi + yj) * cross;
            perimeter += Math.Sqrt((xj - xi) * (xj - xi) + (yj - yi) * (yj - yi));
        }

        if (Math.Abs(twiceArea) < 1e-12)
        {
            return Error("Degenerate polygon (collinear vertices or zero area).");
        }

        double area = twiceArea / 2.0;
        bool isCcw = area > 0;

        return JsonSerializer.Serialize(new
        {
            status = "success",
            area_m2 = Math.Round(Math.Abs(area), 4),
            area_hectares = Math.Round(Math.Abs(area) / 10000.0, 6),
            perimeter_m = Math.Round(perimeter, 4),
            centroid = new
            {
                x = Math.Round(cx / (3.0 * twiceArea) + ox, 4),
                y = Math.Round(cy / (3.0 * twiceArea) + oy, 4)
            },
            is_clockwise = !isCcw,
            orientation = isCcw ? "counterclockwise" : "clockwise",
            vertices = n,
            // Backward compatibility aliases
            perimetro_m = Math.Round(perimeter, 4),
            centroide = new
            {
                x = Math.Round(cx / (3.0 * twiceArea) + ox, 4),
                y = Math.Round(cy / (3.0 * twiceArea) + oy, 4)
            },
            sentido = isCcw ? "antihorario" : "horario"
        });
    }

    /// <summary>
    /// Purges unused symbol table definitions and optionally audits the drawing database.
    /// </summary>
    /// <param name="target">Purge scope: <c>all</c>, <c>blocks</c>, <c>layers</c>, <c>linetypes</c>, <c>text_styles</c>, or <c>dim_styles</c>.</param>
    /// <param name="audit">If true, runs AUDIT with fix enabled after purging.</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "purge_and_audit_drawing")]
    [Description("Purge unused definitions with -PURGE (two passes for nested items). 'target' accepts: all, blocks, layers, linetypes, text_styles, dim_styles. With 'audit'=true runs AUDIT correcting errors.")]
    public async Task<string> PurgeAndAuditDrawing(string target = "all", bool audit = true)
    {
        string? code = target?.Trim().ToLowerInvariant() switch
        {
            "all" or "todo" => "_A",
            "blocks" or "bloques" => "_B",
            "layers" or "capas" => "_LA",
            "linetypes" or "tipos_linea" => "_LT",
            "text_styles" or "estilos_texto" => "_ST",
            "dim_styles" or "estilos_cota" => "_D",
            _ => null
        };

        if (code == null)
        {
            return Error("Invalid target. Use: all, blocks, layers, linetypes, text_styles, or dim_styles.");
        }

        var sb = new StringBuilder();
        for (int pass = 0; pass < 2; pass++)
        {
            sb.Append($"_.-PURGE\n{code}\n*\n_N\n");
        }

        if (audit)
        {
            sb.Append("_.AUDIT\n_Y\n");
        }

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Regenerates all viewports in the current drawing (_.REGENALL).
    /// </summary>
    /// <returns>JSON response from Gateway.</returns>
    [McpServerTool(Name = "regen_all")]
    [Description("Regenerate all open viewports (_.REGENALL) to recalculate curve displays, text metrics, and layer state changes.")]
    public async Task<string> RegenAll() => await _gatewayClient.ExecuteCommandAsync("_.REGENALL\n");
}
