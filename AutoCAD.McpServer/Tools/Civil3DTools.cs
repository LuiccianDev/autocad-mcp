using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// MCP tools for Autodesk Civil 3D 2027: surfaces, ground elevations, and alignments
/// (inspection, sampling, and creation). Requires Civil 3D with active Gateway.
/// </summary>
[McpServerToolType]
public sealed class Civil3DTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="Civil3DTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the Gateway.</param>
    public Civil3DTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Err(string message) =>
        JsonSerializer.Serialize(new { status = "error", message });

    /// <summary>
    /// Parses "v1,v2[,v3];..." into numeric rows. Returns null if invalid.
    /// </summary>
    private static double[][]? ParsePoints(string points, int columns) => CadFormat.ParsePoints(points, columns);

    private const int MaxProfileSamples = 500;

    private static double? ReadNumber(JsonElement root, string property) =>
        root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool IsSuccess(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("status", out var s) && s.GetString() == "success";

    private static JsonDocument ParseSafe(string response)
    {
        try
        {
            return JsonDocument.Parse(response);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(Err(response));
        }
    }

    /// <summary>
    /// Samples the longitudinal ground profile along a Civil 3D horizontal alignment.
    /// </summary>
    /// <param name="interval">Sampling step distance in meters (must be > 0).</param>
    /// <param name="alignmentName">Alignment name; defaults to first available alignment if omitted.</param>
    /// <param name="surfaceName">TIN surface name; defaults to first available surface if omitted.</param>
    /// <returns>JSON containing sampled stations, coordinates, elevations, and slope summary.</returns>
    [McpServerTool(Name = "sample_longitudinal_profile")]
    [Description("Sample terrain longitudinal profile along a Civil 3D alignment every 'interval' meters, returning station, X, Y, and elevation.")]
    public async Task<string> SampleLongitudinalProfile(
        double interval = 20.0, string? alignmentName = null, string? surfaceName = null)
    {
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0)
        {
            return Err("The interval must be a number greater than zero.");
        }

        // Fast path: Query atomic profile endpoint first
        string qAlFast = string.IsNullOrWhiteSpace(alignmentName) ? "" : $"&alignment={Uri.EscapeDataString(alignmentName)}";
        string qSfFast = string.IsNullOrWhiteSpace(surfaceName) ? "" : $"&surface={Uri.EscapeDataString(surfaceName)}";
        try
        {
            string atomicRes = await _gatewayClient.GetContextAsync($"civil/profile?interval={Num(interval)}{qAlFast}{qSfFast}");
            using var atomicDoc = ParseSafe(atomicRes);
            if (IsSuccess(atomicDoc.RootElement))
            {
                return atomicRes;
            }
        }
        catch
        {
            // Fall back to sequential sampling if atomic route not available
        }

        using var listDoc = ParseSafe(await _gatewayClient.GetContextAsync("civil/alignments"));
        var listRoot = listDoc.RootElement;
        if (!IsSuccess(listRoot) || !listRoot.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return Err("No alignments found in Civil 3D.");
        }

        JsonElement? chosen = null;
        foreach (var a in data.EnumerateArray())
        {
            string currentName = a.TryGetProperty("name", out var np) ? np.GetString() ?? "" :
                                 a.TryGetProperty("nombre", out var npo) ? npo.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(alignmentName) ||
                string.Equals(currentName, alignmentName, StringComparison.OrdinalIgnoreCase))
            {
                chosen = a;
                break;
            }
        }

        if (chosen is not { } al)
        {
            return Err($"Alignment not found: '{alignmentName}'.");
        }

        string chosenName = al.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" :
                            al.TryGetProperty("nombre", out var nameOld) ? nameOld.GetString() ?? "" : "";

        double startStation = al.TryGetProperty("start_station", out var ss) ? ss.GetDouble() :
                              al.TryGetProperty("progresiva_inicial", out var sso) ? sso.GetDouble() : 0.0;

        double endStation = al.TryGetProperty("end_station", out var es) ? es.GetDouble() :
                            al.TryGetProperty("progresiva_final", out var eso) ? eso.GetDouble() : 0.0;

        var stations = new List<double>();
        for (double st = startStation; st < endStation - 1e-6; st += interval)
        {
            stations.Add(st);
        }
        stations.Add(endStation);

        if (stations.Count > MaxProfileSamples)
        {
            return Err($"Interval generates {stations.Count} samples (max {MaxProfileSamples}). Increase interval.");
        }

        string qAl = $"&alignment={Uri.EscapeDataString(chosenName)}";
        string qSup = string.IsNullOrWhiteSpace(surfaceName) ? string.Empty : $"&surface={Uri.EscapeDataString(surfaceName)}";

        var samples = new List<object>(stations.Count);
        double? zMin = null, zMax = null, zIni = null, zFin = null;

        foreach (double st in stations)
        {
            using var ptDoc = ParseSafe(await _gatewayClient.GetContextAsync(
                $"civil/alignment/point-at-station?station={Num(st)}&offset=0{qAl}"));
            var pt = ptDoc.RootElement;
            if (!IsSuccess(pt) || ReadNumber(pt, "x") is not { } x || ReadNumber(pt, "y") is not { } y)
            {
                samples.Add(new { station = st, x = (double?)null, y = (double?)null, elevation = (double?)null });
                continue;
            }

            using var zDoc = ParseSafe(await _gatewayClient.GetContextAsync(
                $"civil/surface/elevation?x={Num(x)}&y={Num(y)}{qSup}"));

            double? z = null;
            if (IsSuccess(zDoc.RootElement))
            {
                z = ReadNumber(zDoc.RootElement, "elevation") ?? ReadNumber(zDoc.RootElement, "elevacion");
            }

            if (z is { } zv)
            {
                zMin = zMin is null ? zv : Math.Min(zMin.Value, zv);
                zMax = zMax is null ? zv : Math.Max(zMax.Value, zv);
                zIni ??= zv;
                zFin = zv;
            }

            samples.Add(new
            {
                station = Math.Round(st, 3),
                x = Math.Round(x, 4),
                y = Math.Round(y, 4),
                elevation = z is null ? (double?)null : Math.Round(z.Value, 3)
            });
        }

        double length = endStation - startStation;
        return JsonSerializer.Serialize(new
        {
            status = "success",
            alignment = chosenName,
            surface = string.IsNullOrWhiteSpace(surfaceName) ? "(first available)" : surfaceName,
            interval,
            start_station = startStation,
            end_station = endStation,
            total_samples = samples.Count,
            min_elevation = zMin,
            max_elevation = zMax,
            average_slope_percent = zIni is { } a0 && zFin is { } a1 && length > 0
                ? Math.Round((a1 - a0) / length * 100.0, 4)
                : (double?)null,
            samples
        });
    }

    /// <summary>Checks whether active host is Civil 3D and counts surfaces and alignments.</summary>
    [McpServerTool(Name = "check_civil3d_status")]
    [Description("Check whether the active drawing runs under Civil 3D 2027 and count available surfaces and alignments.")]
    public async Task<string> CheckCivil3DStatus() => await _gatewayClient.GetContextAsync("civil/status");

    /// <summary>Lists TIN surfaces with minimum and maximum elevations.</summary>
    [McpServerTool(Name = "list_civil3d_surfaces")]
    [Description("List TIN surfaces in Civil 3D with name, type, minimum elevation, maximum elevation, and triangle count.")]
    public async Task<string> ListCivil3DSurfaces() => await _gatewayClient.GetContextAsync("civil/surfaces");

    /// <summary>Queries ground elevation at a specific coordinate.</summary>
    /// <param name="x">East (X) coordinate in meters.</param>
    /// <param name="y">North (Y) coordinate in meters.</param>
    /// <param name="surfaceName">Surface name; first available surface is used if omitted.</param>
    [McpServerTool(Name = "get_surface_elevation")]
    [Description("Query exact ground elevation (Z coordinate) at point (x, y) on a Civil 3D TIN surface.")]
    public async Task<string> GetSurfaceElevation(double x, double y, string? surfaceName = null) =>
        await _gatewayClient.GetContextAsync(
            $"civil/surface/elevation?x={Num(x)}&y={Num(y)}" +
            (string.IsNullOrWhiteSpace(surfaceName) ? string.Empty : $"&surface={Uri.EscapeDataString(surfaceName)}"));

    /// <summary>Lists horizontal alignments with their stations.</summary>
    [McpServerTool(Name = "list_civil3d_alignments")]
    [Description("List horizontal alignments in Civil 3D with name, start station, end station, and length.")]
    public async Task<string> ListCivil3DAlignments() => await _gatewayClient.GetContextAsync("civil/alignments");

    /// <summary>Calculates station and lateral offset of a point relative to an alignment.</summary>
    /// <param name="x">East (X) coordinate in meters.</param>
    /// <param name="y">North (Y) coordinate in meters.</param>
    /// <param name="alignmentName">Alignment name; first available alignment used if omitted.</param>
    [McpServerTool(Name = "get_alignment_station_offset")]
    [Description("Calculate station and lateral offset of point (x, y) relative to a Civil 3D horizontal alignment.")]
    public async Task<string> GetAlignmentStationOffset(double x, double y, string? alignmentName = null) =>
        await _gatewayClient.GetContextAsync(
            $"civil/alignment/station-offset?x={Num(x)}&y={Num(y)}" +
            (string.IsNullOrWhiteSpace(alignmentName) ? string.Empty : $"&alignment={Uri.EscapeDataString(alignmentName)}"));

    /// <summary>Calculates (X, Y) coordinate for a given station and offset.</summary>
    /// <param name="station">Station in meters (e.g. 1250.5 = 1+250.50).</param>
    /// <param name="offset">Lateral offset in meters (+ right, - left).</param>
    /// <param name="alignmentName">Alignment name; first available alignment used if omitted.</param>
    [McpServerTool(Name = "get_point_at_station")]
    [Description("Calculate (X, Y) coordinate given a station and lateral offset along a Civil 3D alignment.")]
    public async Task<string> GetPointAtStation(double station, double offset = 0, string? alignmentName = null) =>
        await _gatewayClient.GetContextAsync(
            $"civil/alignment/point-at-station?station={Num(station)}&offset={Num(offset)}" +
            (string.IsNullOrWhiteSpace(alignmentName) ? string.Empty : $"&alignment={Uri.EscapeDataString(alignmentName)}"));

    /// <summary>Creates a horizontal alignment from PI coordinates.</summary>
    /// <param name="name">Unique alignment name.</param>
    /// <param name="points">PIs formatted as 'x1,y1;x2,y2;...' (minimum 2 PIs).</param>
    /// <param name="radius">Fixed curve radius in meters; 0 = no curves.</param>
    /// <param name="layer">Target layer name.</param>
    [McpServerTool(Name = "create_civil3d_alignment")]
    [Description("Create a horizontal alignment in Civil 3D from PI vertices 'x1,y1;x2,y2;...' with fixed radius tangent circular curves.")]
    public async Task<string> CreateCivil3DAlignment(string name, string points, double radius = 0, string? layer = null)
    {
        var pts = ParsePoints(points, 2);
        if (pts == null || pts.Length < 2)
        {
            return Err("Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 2 numeric PIs.");
        }

        string json = JsonSerializer.Serialize(new
        {
            name,
            points = pts.Select(p => new { x = p[0], y = p[1] }),
            radius,
            layer
        });
        return await _gatewayClient.PostJsonAsync("civil/alignment/create", json);
    }

    /// <summary>Creates a TIN surface from survey points.</summary>
    /// <param name="name">Unique surface name.</param>
    /// <param name="points">Points formatted as 'x1,y1,z1;x2,y2,z2;...' (minimum 3 points).</param>
    [McpServerTool(Name = "create_civil3d_surface")]
    [Description("Create a TIN surface in Civil 3D from survey points 'x1,y1,z1;x2,y2,z2;...' (minimum 3 points).")]
    public async Task<string> CreateCivil3DSurface(string name, string points)
    {
        var pts = ParsePoints(points, 3);
        if (pts == null || pts.Length < 3)
        {
            return Err("Invalid 'points' format. Use 'x1,y1,z1;x2,y2,z2;...' with at least 3 numeric points.");
        }

        string json = JsonSerializer.Serialize(new
        {
            name,
            points = pts.Select(p => new { x = p[0], y = p[1], z = p[2] })
        });
        return await _gatewayClient.PostJsonAsync("civil/surface/create", json);
    }

    /// <summary>Alias for sample_longitudinal_profile.</summary>
    [McpServerTool(Name = "get_longitudinal_profile")]
    [Description("Alias for sample_longitudinal_profile: Sample terrain longitudinal profile along an alignment.")]
    public Task<string> GetLongitudinalProfile(
        double interval = 20.0, string? alignmentName = null, string? surfaceName = null) =>
        SampleLongitudinalProfile(interval, alignmentName, surfaceName);

    /// <summary>Alias for get_point_at_station.</summary>
    [McpServerTool(Name = "get_station_coordinate")]
    [Description("Alias for get_point_at_station: Calculate (X, Y) coordinate given a station and lateral offset.")]
    public Task<string> GetStationCoordinate(double station, double offset = 0, string? alignmentName = null) =>
        GetPointAtStation(station, offset, alignmentName);
}
