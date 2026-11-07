using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Typed parametric drawing MCP tools (lines, polylines, rectangles, circles, arcs, texts, and blocks).
/// </summary>
/// <remarks>
/// Geometry is generated via AutoLISP <c>entmake</c> rather than interactive commands:
/// execution is OSNAP-independent, active-layer neutral, and automatically creates the target layer if it does not exist.
/// All coordinates are strictly interpreted in meters.
/// </remarks>
[McpServerToolType]
public sealed class DrawingTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the AutoCAD Gateway.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gatewayClient"/> is null.</exception>
    public DrawingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Wraps an entmake expression in a silent AutoLISP progn block and sends it to the Gateway.</summary>
    private Task<string> SendLispAsync(string body) =>
        _gatewayClient.ExecuteCommandAsync($"(progn {body} (princ))\n");

    /// <summary>Validated DXF layer pair, or <c>null</c> if the name is invalid.</summary>
    private static string? LayerPair(string layer) =>
        IsValidSymbolName(layer) ? $"(cons 8 {LispString(layer.Trim())})" : null;

    /// <summary>
    /// Draws a complete set of geometric entities (lines, polylines, rectangles, circles, arcs, texts)
    /// in AutoCAD through a single atomic AutoLISP injection, reducing roundtrips and token usage by up to 95%.
    /// </summary>
    /// <param name="elementsJson">
    /// JSON string containing an array of geometric entity objects to draw.
    /// Example: [{"type":"line","x1":0,"y1":0,"x2":10,"y2":0,"layer":"WALLS"},{"type":"circle","x":5,"y":5,"radius":2}]
    /// </param>
    /// <param name="defaultLayer">Default target layer if an entity does not specify 'layer'.</param>
    /// <returns>JSON summary of created entities or validation error message.</returns>
    [McpServerTool(Name = "draw_batch_geometry")]
    [Description("PREFERRED TOOL FOR DRAWING MULTIPLE ENTITIES IN A SINGLE CALL. Draws tens or hundreds of geometric entities (lines, polylines, rectangles, circles, arcs, texts) in a single atomic call into AutoCAD, reducing latency and token overhead by up to 95%.")]
    public async Task<string> DrawBatchGeometry(string elementsJson, string defaultLayer = "0")
    {
        if (string.IsNullOrWhiteSpace(elementsJson))
        {
            return Error("The 'elementsJson' parameter cannot be empty.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(elementsJson);
        }
        catch (JsonException ex)
        {
            return Error($"Invalid JSON format in 'elementsJson': {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Error("JSON must contain an array of geometric element objects.");
            }

            var elements = doc.RootElement;
            if (elements.GetArrayLength() == 0)
            {
                return Error("The geometric elements array is empty.");
            }

            var lispSb = new StringBuilder();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            foreach (var item in elements.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return Error($"Element at index {index} must be a JSON object.");
                }

                if (!TryCompileBatchItem(item, defaultLayer, lispSb, counts, out string? error))
                {
                    return Error($"Error in element {index}: {error}");
                }
                index++;
            }

            string fullLisp = $"(progn {lispSb} (princ))\n";
            string gatewayResult = await _gatewayClient.ExecuteCommandAsync(fullLisp);

            var resultObj = new
            {
                status = "success",
                total = index,
                breakdown = counts,
                desglose = counts,
                gateway_response = gatewayResult
            };

            return JsonSerializer.Serialize(resultObj);
        }
    }

    private static bool TryCompileBatchItem(
        JsonElement item, string defaultLayer, StringBuilder lispSb,
        Dictionary<string, int> counts, out string? error)
    {
        error = null;
        string entityType = "";
        foreach (var prop in item.EnumerateObject())
        {
            if (string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "tipo", StringComparison.OrdinalIgnoreCase))
            {
                entityType = prop.Value.GetString()?.Trim().ToLowerInvariant() ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(entityType))
        {
            error = "Element does not specify 'type' property.";
            return false;
        }

        string layerName = defaultLayer;
        foreach (var prop in item.EnumerateObject())
        {
            if (string.Equals(prop.Name, "layer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "capa", StringComparison.OrdinalIgnoreCase))
            {
                string? val = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    layerName = val.Trim();
                }
                break;
            }
        }

        string? layer = LayerPair(layerName);
        if (layer == null)
        {
            error = $"Invalid layer name: '{layerName}'.";
            return false;
        }

        switch (entityType)
        {
            case "line":
            case "linea":
            {
                double x1 = GetDouble(item, "x1", [], 0.0);
                double y1 = GetDouble(item, "y1", [], 0.0);
                double x2 = GetDouble(item, "x2", [], 0.0);
                double y2 = GetDouble(item, "y2", [], 0.0);
                double z1 = GetDouble(item, "z1", [], 0.0);
                double z2 = GetDouble(item, "z2", [], 0.0);

                if (!AllFinite(x1, y1, z1, x2, y2, z2))
                {
                    error = "Non-finite coordinates in line element.";
                    return false;
                }
                if (x1 == x2 && y1 == y2 && z1 == z2)
                {
                    error = "Line element has zero length (identical start and end points).";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"LINE\") {layer} (list 10 {Num(x1)} {Num(y1)} {Num(z1)}) (list 11 {Num(x2)} {Num(y2)} {Num(z2)}))) ");
                counts["line"] = counts.GetValueOrDefault("line") + 1;
                return true;
            }

            case "rectangle":
            case "rectangulo":
            {
                double x1 = GetDouble(item, "x1", [], 0.0);
                double y1 = GetDouble(item, "y1", [], 0.0);
                double x2 = GetDouble(item, "x2", [], 0.0);
                double y2 = GetDouble(item, "y2", [], 0.0);

                if (!AllFinite(x1, y1, x2, y2))
                {
                    error = "Non-finite coordinates in rectangle element.";
                    return false;
                }
                if (x1 == x2 || y1 == y2)
                {
                    error = "Rectangle element has zero area.";
                    return false;
                }

                double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
                double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);

                lispSb.Append($"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(100 . \"AcDbPolyline\") " +
                              $"{layer} '(90 . 4) '(70 . 1) " +
                              $"(list 10 {Num(minX)} {Num(minY)}) (list 10 {Num(maxX)} {Num(minY)}) " +
                              $"(list 10 {Num(maxX)} {Num(maxY)}) (list 10 {Num(minX)} {Num(maxY)}))) ");
                counts["rectangle"] = counts.GetValueOrDefault("rectangle") + 1;
                return true;
            }

            case "circle":
            case "circulo":
            {
                double cx = GetDouble(item, "center_x", ["centerX", "centroX", "centro_x", "x"], 0.0);
                double cy = GetDouble(item, "center_y", ["centerY", "centroY", "centro_y", "y"], 0.0);
                double cz = GetDouble(item, "center_z", ["centerZ", "centroZ", "centro_z", "z"], 0.0);
                double r = GetDouble(item, "radius", ["radio", "r"], 0.0);

                if (!AllFinite(cx, cy, cz, r) || r <= 0)
                {
                    error = "Invalid radius or coordinates in circle element.";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"CIRCLE\") {layer} (list 10 {Num(cx)} {Num(cy)} {Num(cz)}) (cons 40 {Num(r)}))) ");
                counts["circle"] = counts.GetValueOrDefault("circle") + 1;
                return true;
            }

            case "arc":
            case "arco":
            {
                double cx = GetDouble(item, "center_x", ["centerX", "centroX", "centro_x", "x"], 0.0);
                double cy = GetDouble(item, "center_y", ["centerY", "centroY", "centro_y", "y"], 0.0);
                double cz = GetDouble(item, "center_z", ["centerZ", "centroZ", "centro_z", "z"], 0.0);
                double r = GetDouble(item, "radius", ["radio", "r"], 0.0);
                double angStart = GetDouble(item, "start_angle", ["startAngle", "anguloInicio", "angulo_inicio"], 0.0);
                double angEnd = GetDouble(item, "end_angle", ["endAngle", "anguloFin", "angulo_fin"], 0.0);

                if (!AllFinite(cx, cy, cz, r, angStart, angEnd) || r <= 0)
                {
                    error = "Invalid parameters in arc element.";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"ARC\") {layer} (list 10 {Num(cx)} {Num(cy)} {Num(cz)}) (cons 40 {Num(r)}) " +
                              $"(cons 50 {Num(ToRadians(angStart))}) (cons 51 {Num(ToRadians(angEnd))}))) ");
                counts["arc"] = counts.GetValueOrDefault("arc") + 1;
                return true;
            }

            case "polyline":
            case "polilinea":
            {
                double[][]? pts = null;
                bool closed = false;

                foreach (var prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "points", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prop.Name, "puntos", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            pts = ParsePoints(prop.Value.GetString(), 2);
                        }
                    }
                    else if (string.Equals(prop.Name, "closed", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(prop.Name, "cerrada", StringComparison.OrdinalIgnoreCase))
                    {
                        closed = prop.Value.ValueKind == JsonValueKind.True;
                    }
                }

                if (pts == null || pts.Length < 2)
                {
                    error = "Polyline requires at least 2 valid vertices in 'x1,y1;x2,y2;...' format.";
                    return false;
                }

                lispSb.Append(BuildLwPolyline(pts, closed, layer)).Append(' ');
                counts["polyline"] = counts.GetValueOrDefault("polyline") + 1;
                return true;
            }

            case "text":
            case "texto":
            {
                string txt = "";
                double tx = GetDouble(item, "x", [], 0.0);
                double ty = GetDouble(item, "y", [], 0.0);
                double tz = GetDouble(item, "z", [], 0.0);
                double h = GetDouble(item, "height", ["altura"], 0.25);
                double rot = GetDouble(item, "rotation", ["rotacion"], 0.0);

                foreach (var prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "text", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prop.Name, "texto", StringComparison.OrdinalIgnoreCase))
                    {
                        txt = prop.Value.GetString() ?? "";
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(txt))
                {
                    error = "Text content cannot be empty.";
                    return false;
                }
                if (!AllFinite(tx, ty, tz, h, rot) || h <= 0)
                {
                    error = "Invalid coordinates or height in text element.";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"TEXT\") {layer} (list 10 {Num(tx)} {Num(ty)} {Num(tz)}) " +
                              $"(cons 40 {Num(h)}) (cons 1 {LispString(txt)}) (cons 50 {Num(ToRadians(rot))}))) ");
                counts["text"] = counts.GetValueOrDefault("text") + 1;
                return true;
            }

            default:
                error = $"Unsupported entity type: '{entityType}'.";
                return false;
        }
    }

    private static double GetDouble(JsonElement obj, string primaryName, string[] aliases, double defaultVal)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, primaryName, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double val))
                    return val;
                if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                    return parsed;
                return defaultVal;
            }
        }
        foreach (var alias in aliases)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, alias, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double val))
                        return val;
                    if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                        return parsed;
                    return defaultVal;
                }
            }
        }
        return defaultVal;
    }

    /// <summary>
    /// Draws a line segment between two 3D points in meters.
    /// </summary>
    /// <param name="x1">Start X coordinate (m).</param>
    /// <param name="y1">Start Y coordinate (m).</param>
    /// <param name="x2">End X coordinate (m).</param>
    /// <param name="y2">End Y coordinate (m).</param>
    /// <param name="z1">Start Z coordinate (m), default 0.</param>
    /// <param name="z2">End Z coordinate (m), default 0.</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "draw_line")]
    [Description("Draw a line between (x1, y1, z1) and (x2, y2, z2) in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawLine(
        double x1, double y1, double x2, double y2,
        double z1 = 0, double z2 = 0, string layer = "0")
    {
        if (!AllFinite(x1, y1, z1, x2, y2, z2))
        {
            return Error("Coordinates must be finite numbers.");
        }

        if (x1 == x2 && y1 == y2 && z1 == z2)
        {
            return Error("Start and end points are identical: line would have zero length.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"LINE\") {layerPair} (list 10 {Num(x1)} {Num(y1)} {Num(z1)}) (list 11 {Num(x2)} {Num(y2)} {Num(z2)})))");
    }

    /// <summary>
    /// Draws a 2D lightweight polyline (LWPOLYLINE) from a list of vertices.
    /// </summary>
    /// <param name="points">Vertices in 'x1,y1;x2,y2;...' format (minimum 2).</param>
    /// <param name="closed">If true, closes the polyline connecting last vertex to the first.</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "draw_polyline")]
    [Description("Draw a 2D polyline in meters. 'closed' connects the last vertex to the first. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawPolyline(string points, bool closed = false, string layer = "0")
    {
        var pts = ParsePoints(points, 2);
        if (pts == null || pts.Length < 2)
        {
            return Error("Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 2 numeric vertices.");
        }

        if (closed && pts.Length < 3)
        {
            return Error("A closed polyline requires at least 3 vertices.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        return await SendLispAsync(BuildLwPolyline(pts, closed, layerPair));
    }

    /// <summary>
    /// Draws a rectangle as a closed polyline defined by two opposite corners in meters.
    /// </summary>
    /// <param name="x1">X coordinate of the first corner (m).</param>
    /// <param name="y1">Y coordinate of the first corner (m).</param>
    /// <param name="x2">X coordinate of the opposite corner (m).</param>
    /// <param name="y2">Y coordinate of the opposite corner (m).</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "draw_rectangle")]
    [Description("Draw a rectangle as a closed polyline from two opposite corners in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawRectangle(double x1, double y1, double x2, double y2, string layer = "0")
    {
        if (!AllFinite(x1, y1, x2, y2))
        {
            return Error("Coordinates must be finite numbers.");
        }

        if (x1 == x2 || y1 == y2)
        {
            return Error("Corners must differ in both X and Y: rectangle would have zero area.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        double[][] pts = [[x1, y1], [x2, y1], [x2, y2], [x1, y2]];
        return await SendLispAsync(BuildLwPolyline(pts, closed: true, layerPair));
    }

    /// <summary>
    /// Draws a circle given its center and radius in meters.
    /// </summary>
    /// <param name="centerX">Center X coordinate (m).</param>
    /// <param name="centerY">Center Y coordinate (m).</param>
    /// <param name="radius">Radius in meters (must be > 0).</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "draw_circle")]
    [Description("Draw a circle with center and radius in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawCircle(double centerX, double centerY, double radius, string layer = "0")
    {
        if (!AllFinite(centerX, centerY, radius) || radius <= 0)
        {
            return Error("Center must be numeric and radius must be greater than zero.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"CIRCLE\") {layerPair} (list 10 {Num(centerX)} {Num(centerY)} 0.0) (cons 40 {Num(radius)})))");
    }

    /// <summary>
    /// Draws a circular arc CCW from start angle to end angle in degrees.
    /// </summary>
    /// <param name="centerX">Center X coordinate (m).</param>
    /// <param name="centerY">Center Y coordinate (m).</param>
    /// <param name="radius">Radius in meters (must be > 0).</param>
    /// <param name="startAngle">Start angle in degrees (0 = East, CCW).</param>
    /// <param name="endAngle">End angle in degrees (0 = East, CCW).</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "draw_arc")]
    [Description("Draw a circular arc centered at (centerX, centerY) with radius in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawArc(
        double centerX, double centerY, double radius,
        double startAngle, double endAngle, string layer = "0")
    {
        if (!AllFinite(centerX, centerY, radius, startAngle, endAngle) || radius <= 0)
        {
            return Error("Parameters must be numeric and radius must be greater than zero.");
        }

        if (Math.Abs(endAngle - startAngle) % 360.0 == 0)
        {
            return Error("Start and end angles are identical; use 'draw_circle' for a full circle.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"ARC\") {layerPair} (list 10 {Num(centerX)} {Num(centerY)} 0.0) (cons 40 {Num(radius)}) " +
            $"(cons 50 {Num(ToRadians(startAngle))}) (cons 51 {Num(ToRadians(endAngle))})))");
    }

    /// <summary>
    /// Inserts a single-line text entity (DBText) in ModelSpace.
    /// </summary>
    /// <param name="text">Text content.</param>
    /// <param name="x">Insertion X coordinate (m).</param>
    /// <param name="y">Insertion Y coordinate (m).</param>
    /// <param name="height">Text height in meters, default 0.25.</param>
    /// <param name="rotation">Rotation in degrees, default 0.</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "insert_text")]
    [Description("Insert a single-line text entity at (x, y) with height in meters and rotation in degrees. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> InsertText(
        string text, double x, double y,
        double height = 0.25, double rotation = 0.0, string layer = "0")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Error("Text content cannot be empty.");
        }

        if (!AllFinite(x, y, height, rotation) || height <= 0)
        {
            return Error("Coordinates must be numeric and height must be greater than zero.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"TEXT\") {layerPair} (list 10 {Num(x)} {Num(y)} 0.0) (cons 40 {Num(height)}) " +
            $"(cons 1 {LispString(text)}) (cons 50 {Num(ToRadians(rotation))})))");
    }

    /// <summary>
    /// Inserts a reference to an existing block definition in the drawing.
    /// </summary>
    /// <param name="blockName">Name of the existing block definition (see <c>list_blocks</c>).</param>
    /// <param name="x">Insertion X coordinate (m).</param>
    /// <param name="y">Insertion Y coordinate (m).</param>
    /// <param name="scale">Uniform scale factor (> 0), default 1.</param>
    /// <param name="rotation">Rotation in degrees, default 0.</param>
    /// <param name="layer">Target layer name (created automatically if missing).</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "insert_block")]
    [Description("Insert a reference of an EXISTING block definition into the drawing at (x, y) with uniform scale and rotation in degrees.")]
    public async Task<string> InsertBlock(
        string blockName, double x, double y,
        double scale = 1.0, double rotation = 0.0, string layer = "0")
    {
        if (!IsValidSymbolName(blockName))
        {
            return Error($"Invalid block name: '{blockName}'.");
        }

        if (!AllFinite(x, y, scale, rotation) || scale <= 0)
        {
            return Error("Coordinates must be numeric and scale must be greater than zero.");
        }

        if (LayerPair(layer) is not { } layerPair)
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string name = LispString(blockName.Trim());
        string s = Num(scale);
        return await SendLispAsync(
            $"(if (tblsearch \"BLOCK\" {name}) " +
            $"(entmake (list '(0 . \"INSERT\") (cons 2 {name}) {layerPair} (list 10 {Num(x)} {Num(y)} 0.0) " +
            $"(cons 41 {s}) (cons 42 {s}) (cons 43 {s}) (cons 50 {Num(ToRadians(rotation))}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] Block not found: \" {name})))");
    }

    /// <summary>Constructs the entmake expression for an LWPOLYLINE.</summary>
    private static string BuildLwPolyline(double[][] pts, bool closed, string layer)
    {
        var sb = new StringBuilder();
        sb.Append("(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") ")
          .Append(layer)
          .Append(" '(100 . \"AcDbPolyline\") (cons 90 ")
          .Append(pts.Length.ToString(CultureInfo.InvariantCulture))
          .Append(") (cons 70 ")
          .Append(closed ? '1' : '0')
          .Append(')');

        foreach (var p in pts)
        {
            sb.Append(" (list 10 ").Append(Num(p[0])).Append(' ').Append(Num(p[1])).Append(')');
        }

        sb.Append("))");
        return sb.ToString();
    }
}
