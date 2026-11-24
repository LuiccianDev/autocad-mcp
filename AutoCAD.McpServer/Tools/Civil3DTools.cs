using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// MCP tools for Autodesk Civil 3D 2027: surfaces, ground elevations, alignments,
/// longitudinal profiles, and perpendicular cross-sections. Requires Civil 3D with active Gateway.
/// </summary>
[McpServerToolType]
[ToolDomain("civil3d", "autocad_civil3d", "Query and create Civil 3D TIN surfaces, horizontal alignments, longitudinal profiles, and cross-sections.")]
public sealed class Civil3DTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="Civil3DTools"/> class.</summary>
    /// <param name="gatewayClient">The client for communicating with the Gateway.</param>
    public Civil3DTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Samples the longitudinal ground profile along a Civil 3D horizontal alignment.</summary>
    /// <param name="interval">Sampling step distance in meters (must be finite and &gt; 0).</param>
    /// <param name="alignmentName">Alignment name; defaults to first available alignment if omitted.</param>
    /// <param name="surfaceName">TIN surface name; defaults to first available surface if omitted.</param>
    /// <returns>Asynchronous task returning JSON with sampled stations, coordinates, elevations, and slope summary.</returns>
    [McpServerTool(Name = "sample_longitudinal_profile")]
    [ToolExposure(Core = true)]
    [Description("Sample terrain longitudinal profile along a Civil 3D alignment every 'interval' meters, returning station, X, Y, and elevation.")]
    public Task<string> SampleLongitudinalProfile(
        double interval = 20.0,
        string? alignmentName = null,
        string? surfaceName = null)
    {
        if (!double.IsFinite(interval) || interval <= 0)
        {
            return Task.FromResult(CadFormat.Error("The interval must be a finite number greater than zero."));
        }

        if (CadFormat.HasControlChars(alignmentName) || CadFormat.HasControlChars(surfaceName))
        {
            return Task.FromResult(CadFormat.Error("Alignment and surface names cannot contain control characters."));
        }

        string qAl = string.IsNullOrWhiteSpace(alignmentName) ? string.Empty : $"&alignment={Uri.EscapeDataString(alignmentName.Trim())}";
        string qSf = string.IsNullOrWhiteSpace(surfaceName) ? string.Empty : $"&surface={Uri.EscapeDataString(surfaceName.Trim())}";
        return _gatewayClient.GetContextAsync($"civil/profile?interval={CadFormat.Num(interval)}{qAl}{qSf}");
    }

    /// <summary>Checks whether active host is Civil 3D and counts surfaces and alignments.</summary>
    /// <returns>Asynchronous task returning Civil 3D availability status JSON.</returns>
    [McpServerTool(Name = "check_civil3d_status")]
    [ToolExposure(Core = true)]
    [Description("Check whether the active drawing runs under Civil 3D 2027 and count available surfaces and alignments.")]
    public Task<string> CheckCivil3DStatus() => _gatewayClient.GetContextAsync("civil/status");

    /// <summary>Lists TIN surfaces with minimum and maximum elevations.</summary>
    /// <returns>Asynchronous task returning JSON list of Civil 3D surfaces.</returns>
    [McpServerTool(Name = "list_civil3d_surfaces")]
    [ToolExposure(Core = true)]
    [Description("List TIN surfaces in Civil 3D with name, type, minimum elevation, maximum elevation, and triangle count.")]
    public Task<string> ListCivil3DSurfaces() => _gatewayClient.GetContextAsync("civil/surfaces");

    /// <summary>Queries ground elevation at a specific coordinate.</summary>
    /// <param name="x">East (X) coordinate in meters.</param>
    /// <param name="y">North (Y) coordinate in meters.</param>
    /// <param name="surfaceName">Surface name; first available surface is used if omitted.</param>
    /// <returns>Asynchronous task returning JSON elevation reading.</returns>
    [McpServerTool(Name = "get_surface_elevation")]
    [ToolExposure(Core = true)]
    [Description("Query exact ground elevation (Z coordinate) at point (x, y) on a Civil 3D TIN surface.")]
    public Task<string> GetSurfaceElevation(double x, double y, string? surfaceName = null)
    {
        if (!CadFormat.AllFinite(x, y))
        {
            return Task.FromResult(CadFormat.Error("Coordinates x and y must be finite numbers."));
        }

        if (CadFormat.HasControlChars(surfaceName))
        {
            return Task.FromResult(CadFormat.Error("Surface name cannot contain control characters."));
        }

        return _gatewayClient.GetContextAsync(
            $"civil/surface/elevation?x={CadFormat.Num(x)}&y={CadFormat.Num(y)}" +
            (string.IsNullOrWhiteSpace(surfaceName) ? string.Empty : $"&surface={Uri.EscapeDataString(surfaceName.Trim())}"));
    }

    /// <summary>Lists horizontal alignments with their stations.</summary>
    /// <returns>Asynchronous task returning JSON list of Civil 3D alignments.</returns>
    [McpServerTool(Name = "list_civil3d_alignments")]
    [ToolExposure(Core = true)]
    [Description("List horizontal alignments in Civil 3D with name, start station, end station, and length.")]
    public Task<string> ListCivil3DAlignments() => _gatewayClient.GetContextAsync("civil/alignments");

    /// <summary>Calculates station and lateral offset of a point relative to an alignment.</summary>
    /// <param name="x">East (X) coordinate in meters.</param>
    /// <param name="y">North (Y) coordinate in meters.</param>
    /// <param name="alignmentName">Alignment name; first available alignment used if omitted.</param>
    /// <returns>Asynchronous task returning JSON station and lateral offset.</returns>
    [McpServerTool(Name = "get_alignment_station_offset")]
    [Description("Calculate station and lateral offset of point (x, y) relative to a Civil 3D horizontal alignment.")]
    public Task<string> GetAlignmentStationOffset(double x, double y, string? alignmentName = null)
    {
        if (!CadFormat.AllFinite(x, y))
        {
            return Task.FromResult(CadFormat.Error("Coordinates x and y must be finite numbers."));
        }

        if (CadFormat.HasControlChars(alignmentName))
        {
            return Task.FromResult(CadFormat.Error("Alignment name cannot contain control characters."));
        }

        return _gatewayClient.GetContextAsync(
            $"civil/alignment/station-offset?x={CadFormat.Num(x)}&y={CadFormat.Num(y)}" +
            (string.IsNullOrWhiteSpace(alignmentName) ? string.Empty : $"&alignment={Uri.EscapeDataString(alignmentName.Trim())}"));
    }

    /// <summary>Calculates (X, Y) coordinate for a given station and offset.</summary>
    /// <param name="station">Station in meters (e.g. 1250.5 = 1+250.50).</param>
    /// <param name="offset">Lateral offset in meters (+ right, - left).</param>
    /// <param name="alignmentName">Alignment name; first available alignment used if omitted.</param>
    /// <returns>Asynchronous task returning JSON world (X, Y) coordinates.</returns>
    [McpServerTool(Name = "get_point_at_station")]
    [Description("Calculate (X, Y) coordinate given a station and lateral offset along a Civil 3D alignment.")]
    public Task<string> GetPointAtStation(double station, double offset = 0, string? alignmentName = null)
    {
        if (!CadFormat.AllFinite(station, offset))
        {
            return Task.FromResult(CadFormat.Error("Parameters station and offset must be finite numbers."));
        }

        if (CadFormat.HasControlChars(alignmentName))
        {
            return Task.FromResult(CadFormat.Error("Alignment name cannot contain control characters."));
        }

        return _gatewayClient.GetContextAsync(
            $"civil/alignment/point-at-station?station={CadFormat.Num(station)}&offset={CadFormat.Num(offset)}" +
            (string.IsNullOrWhiteSpace(alignmentName) ? string.Empty : $"&alignment={Uri.EscapeDataString(alignmentName.Trim())}"));
    }

    /// <summary>Creates a horizontal alignment from PI coordinates.</summary>
    /// <param name="name">Unique alignment name.</param>
    /// <param name="points">PIs formatted as 'x1,y1;x2,y2;...' (minimum 2 PIs).</param>
    /// <param name="radius">Fixed curve radius in meters; 0 = no curves.</param>
    /// <param name="layer">Optional target layer name.</param>
    /// <returns>Asynchronous task returning creation confirmation JSON.</returns>
    [McpServerTool(Name = "create_civil3d_alignment")]
    [Description("Create a horizontal alignment in Civil 3D from PI vertices 'x1,y1;x2,y2;...' with fixed radius tangent circular curves.")]
    public Task<string> CreateCivil3DAlignment(string name, string points, double radius = 0, string? layer = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Task.FromResult(CadFormat.Error($"Invalid alignment name '{name}'."));
        }

        if (!double.IsFinite(radius) || radius < 0)
        {
            return Task.FromResult(CadFormat.Error("'radius' must be a finite non-negative number."));
        }

        if (!string.IsNullOrWhiteSpace(layer) && !SecurityPolicy.IsValidSymbolName(layer))
        {
            return Task.FromResult(CadFormat.Error($"Invalid layer name '{layer}'."));
        }

        double[][]? pts = CadFormat.ParsePoints(points, 2);
        if (pts == null || pts.Length < 2)
        {
            return Task.FromResult(CadFormat.Error("Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 2 numeric PIs."));
        }

        string json = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            points = pts.Select(p => new { x = p[0], y = p[1] }),
            radius,
            layer = string.IsNullOrWhiteSpace(layer) ? null : layer.Trim(),
        });
        return _gatewayClient.PostJsonAsync("civil/alignment/create", json);
    }

    /// <summary>Creates a TIN surface from survey points.</summary>
    /// <param name="name">Unique surface name.</param>
    /// <param name="points">Points formatted as 'x1,y1,z1;x2,y2,z2;...' (minimum 3 points).</param>
    /// <returns>Asynchronous task returning surface creation confirmation JSON.</returns>
    [McpServerTool(Name = "create_civil3d_surface")]
    [Description("Create a TIN surface in Civil 3D from survey points 'x1,y1,z1;x2,y2,z2;...' (minimum 3 points).")]
    public Task<string> CreateCivil3DSurface(string name, string points)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Task.FromResult(CadFormat.Error($"Invalid surface name '{name}'."));
        }

        double[][]? pts = CadFormat.ParsePoints(points, 3);
        if (pts == null || pts.Length < 3)
        {
            return Task.FromResult(CadFormat.Error("Invalid 'points' format. Use 'x1,y1,z1;x2,y2,z2;...' with at least 3 numeric points."));
        }

        string json = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            points = pts.Select(p => new { x = p[0], y = p[1], z = p[2] }),
        });
        return _gatewayClient.PostJsonAsync("civil/surface/create", json);
    }

    /// <summary>Reads summary statistics of a Civil 3D surface (elevations, point count, triangle count, 2D/3D areas).</summary>
    /// <param name="surfaceName">Optional surface name; uses the first available surface when omitted.</param>
    /// <returns>Asynchronous task returning JSON surface statistics.</returns>
    [McpServerTool(Name = "get_civil3d_surface_stats")]
    [Description("Retrieve statistical properties of a Civil 3D surface (min/max/mean elevation, points, triangles, 2D/3D area).")]
    public Task<string> GetCivil3DSurfaceStats(string? surfaceName = null)
    {
        if (CadFormat.HasControlChars(surfaceName))
        {
            return Task.FromResult(CadFormat.Error("Surface name cannot contain control characters."));
        }

        string endpoint = string.IsNullOrWhiteSpace(surfaceName)
            ? "civil/surface/stats"
            : $"civil/surface/stats?surface={Uri.EscapeDataString(surfaceName.Trim())}";
        return _gatewayClient.GetContextAsync(endpoint);
    }

    /// <summary>Samples a perpendicular terrain cross-section at a station along a Civil 3D alignment.</summary>
    /// <param name="station">Alignment station in meters at which to cut the cross-section.</param>
    /// <param name="leftWidth">Sampling distance to the left of the centerline in meters (&gt;= 0).</param>
    /// <param name="rightWidth">Sampling distance to the right of the centerline in meters (&gt;= 0).</param>
    /// <param name="step">Lateral sampling interval in meters (&gt; 0).</param>
    /// <param name="alignmentName">Optional alignment name; defaults to the first alignment.</param>
    /// <param name="surfaceName">Optional surface name; defaults to the first surface.</param>
    /// <returns>Asynchronous task returning JSON cross-section samples and min/max elevations.</returns>
    [McpServerTool(Name = "sample_civil3d_cross_section")]
    [Description("Sample terrain cross-section elevations perpendicular to a Civil 3D alignment at a station across left/right swath widths.")]
    public Task<string> SampleCivil3DCrossSection(
        double station,
        double leftWidth = 20.0,
        double rightWidth = 20.0,
        double step = 2.0,
        string? alignmentName = null,
        string? surfaceName = null)
    {
        if (!CadFormat.AllFinite(station, leftWidth, rightWidth, step))
        {
            return Task.FromResult(CadFormat.Error("Parameters station, leftWidth, rightWidth, and step must be finite numbers."));
        }

        if (leftWidth < 0 || rightWidth < 0 || leftWidth + rightWidth <= 0 || step <= 0)
        {
            return Task.FromResult(CadFormat.Error("Require leftWidth >= 0, rightWidth >= 0, leftWidth + rightWidth > 0, and step > 0."));
        }

        if (CadFormat.HasControlChars(alignmentName) || CadFormat.HasControlChars(surfaceName))
        {
            return Task.FromResult(CadFormat.Error("Alignment and surface names cannot contain control characters."));
        }

        string query =
            $"civil/cross-section?station={CadFormat.Num(station)}&left_width={CadFormat.Num(leftWidth)}&right_width={CadFormat.Num(rightWidth)}&step={CadFormat.Num(step)}" +
            (string.IsNullOrWhiteSpace(alignmentName) ? string.Empty : $"&alignment={Uri.EscapeDataString(alignmentName.Trim())}") +
            (string.IsNullOrWhiteSpace(surfaceName) ? string.Empty : $"&surface={Uri.EscapeDataString(surfaceName.Trim())}");
        return _gatewayClient.GetContextAsync(query);
    }

    /// <summary>Alias for sample_longitudinal_profile.</summary>
    /// <param name="interval">Sampling step distance in meters (must be &gt; 0).</param>
    /// <param name="alignmentName">Alignment name; defaults to first available alignment if omitted.</param>
    /// <param name="surfaceName">TIN surface name; defaults to first available surface if omitted.</param>
    /// <returns>Asynchronous task returning longitudinal profile JSON.</returns>
    [McpServerTool(Name = "get_longitudinal_profile")]
    [ToolExposure(AliasOf = "sample_longitudinal_profile")]
    [Description("Alias for sample_longitudinal_profile: Sample terrain longitudinal profile along an alignment.")]
    public Task<string> GetLongitudinalProfile(
        double interval = 20.0, string? alignmentName = null, string? surfaceName = null) =>
        SampleLongitudinalProfile(interval, alignmentName, surfaceName);

    /// <summary>Alias for get_point_at_station.</summary>
    /// <param name="station">Station in meters.</param>
    /// <param name="offset">Lateral offset in meters (+ right, - left).</param>
    /// <param name="alignmentName">Alignment name; first available alignment used if omitted.</param>
    /// <returns>Asynchronous task returning world (X, Y) coordinates JSON.</returns>
    [McpServerTool(Name = "get_station_coordinate")]
    [ToolExposure(AliasOf = "get_point_at_station")]
    [Description("Alias for get_point_at_station: Calculate (X, Y) coordinate given a station and lateral offset.")]
    public Task<string> GetStationCoordinate(double station, double offset = 0, string? alignmentName = null) =>
        GetPointAtStation(station, offset, alignmentName);
}
