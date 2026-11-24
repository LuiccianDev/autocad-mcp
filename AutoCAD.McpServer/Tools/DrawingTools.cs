using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for parametric 2D/3D drawing and native ObjectARX batch generation in AutoCAD (meters).
/// </summary>
[McpServerToolType]
[ToolDomain("drawing", "autocad_drawing", "Create parametric 2D/3D geometry and batch geometry in AutoCAD ModelSpace (meters).")]
public sealed class DrawingTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="DrawingTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with AutoCAD Gateway.</param>
    public DrawingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Draws multiple geometric entities (lines, polylines, rectangles, circles, arcs, texts) in one atomic transaction.
    /// </summary>
    [McpServerTool(Name = "draw_batch_geometry")]
    [ToolExposure(Core = true)]
    [Description("PREFERRED TOOL FOR DRAWING MULTIPLE ENTITIES IN A SINGLE CALL. Draws geometric entities (lines, polylines, rectangles, circles, arcs, texts) inside one atomic ObjectARX transaction.")]
    public async Task<string> DrawBatchGeometry(
        [CompoundParam("elements", StructuredJson = StructuredJsonKind.Array)] string elementsJson,
        string defaultLayer = "0")
    {
        if (string.IsNullOrWhiteSpace(elementsJson))
        {
            return Error("The 'elementsJson' parameter cannot be empty.");
        }

        if (!SecurityPolicy.IsValidSymbolName(defaultLayer))
        {
            return Error($"Invalid default layer name: '{defaultLayer}'.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(elementsJson);
        }
        catch (JsonException ex)
        {
            return Error($"Invalid JSON in 'elementsJson': {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Error("'elementsJson' must be a JSON array of element objects.");
            }

            int count = doc.RootElement.GetArrayLength();
            if (count == 0)
            {
                return Error("Element array cannot be empty.");
            }

            if (count > SecurityPolicy.MaxBatchOperations)
            {
                return Error($"Batch size {count} exceeds maximum {SecurityPolicy.MaxBatchOperations} operations per atomic transaction.");
            }

            var operations = new List<Dictionary<string, object?>>(count);
            int idx = 0;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return Error($"Element #{idx} is not a valid JSON object.");
                }

                if (!TryNormalizeElement(item, defaultLayer, out Dictionary<string, object?>? op, out string err))
                {
                    return Error($"Error in element #{idx}: {err}");
                }

                operations.Add(op!);
                idx++;
            }

            string payload = JsonSerializer.Serialize(new
            {
                operations,
                default_layer = defaultLayer.Trim(),
                rollback_on_error = true,
            });

            return await _gatewayClient.PostJsonAsync("batch", payload, timeoutMs: 60_000);
        }
    }

    /// <summary>Draws a 2D/3D line segment in meters.</summary>
    [McpServerTool(Name = "draw_line")]
    [Description("Draw a line between (x1, y1, z1) and (x2, y2, z2) in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawLine(
        double x1, double y1, double x2, double y2,
        double z1 = 0, double z2 = 0, string layer = "0")
    {
        if (!AllFinite(x1, y1, z1, x2, y2, z2))
        {
            return Error("Line coordinates must be finite numbers.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            type = "line",
            start = new[] { x1, y1, z1 },
            end = new[] { x2, y2, z2 },
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    /// <summary>Draws a 2D lightweight polyline in meters.</summary>
    [McpServerTool(Name = "draw_polyline")]
    [Description("Draw a 2D polyline in meters. 'closed' connects the last vertex to the first. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawPolyline(string points, bool closed = false, string layer = "0")
    {
        double[][]? pts = ParsePoints(points, 2);
        if (pts == null || pts.Length < 2)
        {
            return Error("At least 2 vertices in 'x1,y1;x2,y2;...' format are required.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        double[][] vertices = pts.Select(p => new[] { p[0], p[1] }).ToArray();
        string payload = JsonSerializer.Serialize(new
        {
            type = "polyline",
            points = vertices,
            closed,
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    /// <summary>Draws an axis-aligned rectangle from two opposite corners in meters.</summary>
    [McpServerTool(Name = "draw_rectangle")]
    [Description("Draw a rectangle as a closed polyline from two opposite corners in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawRectangle(double x1, double y1, double x2, double y2, string layer = "0")
    {
        if (!AllFinite(x1, y1, x2, y2))
        {
            return Error("Rectangle coordinates must be finite numbers.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            type = "rectangle",
            min = new[] { x1, y1 },
            max = new[] { x2, y2 },
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    /// <summary>Draws a circle in meters.</summary>
    [McpServerTool(Name = "draw_circle")]
    [Description("Draw a circle with center and radius in meters. (For multiple entities, prefer 'draw_batch_geometry').")]
    public async Task<string> DrawCircle(double centerX, double centerY, double radius, string layer = "0")
    {
        if (!AllFinite(centerX, centerY, radius) || radius <= 0)
        {
            return Error("Center coordinates must be finite and radius must be greater than zero.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            type = "circle",
            center = new[] { centerX, centerY, 0.0 },
            radius,
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    /// <summary>Draws a circular arc in meters using start and end angles in degrees.</summary>
    [McpServerTool(Name = "draw_arc")]
    [Description("Draw a circular arc centered at (centerX, centerY) with radius in meters and angles in degrees.")]
    public async Task<string> DrawArc(
        double centerX, double centerY, double radius,
        double startAngle, double endAngle, string layer = "0")
    {
        if (!AllFinite(centerX, centerY, radius, startAngle, endAngle) || radius <= 0)
        {
            return Error("Coordinates/angles must be finite and radius must be greater than zero.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            type = "arc",
            center = new[] { centerX, centerY, 0.0 },
            radius,
            start_angle = startAngle,
            end_angle = endAngle,
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    /// <summary>Inserts a single-line text annotation in meters.</summary>
    [McpServerTool(Name = "insert_text")]
    [Description("Insert a single-line text entity at (x, y) with height in meters and rotation in degrees.")]
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
            return Error("Coordinates/rotation must be finite and height must be greater than zero.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            type = "text",
            text,
            position = new[] { x, y, 0.0 },
            height,
            rotation,
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    /// <summary>Inserts a block reference from an existing block definition in the drawing.</summary>
    [McpServerTool(Name = "insert_block")]
    [Description("Insert a reference of an EXISTING block definition at (x, y) in meters with scale and rotation.")]
    public async Task<string> InsertBlock(
        string blockName, double x, double y,
        double scale = 1.0, double rotation = 0.0, string layer = "0")
    {
        if (!SecurityPolicy.IsValidSymbolName(blockName))
        {
            return Error($"Invalid block name: '{blockName}'.");
        }

        if (!AllFinite(x, y, scale, rotation) || scale <= 0)
        {
            return Error("Coordinates/rotation must be finite and scale must be greater than zero.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            type = "block_ref",
            block_name = blockName.Trim(),
            position = new[] { x, y, 0.0 },
            scale,
            rotation,
            layer = layer.Trim(),
        });

        return await _gatewayClient.PostJsonAsync("entities/create", payload);
    }

    private static bool TryNormalizeElement(
        JsonElement item,
        string defaultLayer,
        out Dictionary<string, object?>? op,
        out string error)
    {
        op = null;
        error = "";

        string rawType = GetStringOpt(item, "type") ?? GetStringOpt(item, "tipo") ?? "";
        string kind = rawType.Trim().ToLowerInvariant();
        if (kind.Length == 0)
        {
            error = "Missing 'type' field.";
            return false;
        }

        string layer = defaultLayer.Trim();
        string? rawLayer = GetStringOpt(item, "layer") ?? GetStringOpt(item, "capa");
        if (!string.IsNullOrWhiteSpace(rawLayer))
        {
            if (!SecurityPolicy.IsValidSymbolName(rawLayer))
            {
                error = $"Invalid layer name '{rawLayer}'.";
                return false;
            }

            layer = rawLayer.Trim();
        }

        switch (kind)
        {
            case "line" or "linea":
            {
                if (!TryGetDouble(item, "x1", [], 0.0, out double x1, out error) ||
                    !TryGetDouble(item, "y1", [], 0.0, out double y1, out error) ||
                    !TryGetDouble(item, "z1", [], 0.0, out double z1, out error) ||
                    !TryGetDouble(item, "x2", [], 0.0, out double x2, out error) ||
                    !TryGetDouble(item, "y2", [], 0.0, out double y2, out error) ||
                    !TryGetDouble(item, "z2", [], 0.0, out double z2, out error))
                {
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "line",
                    ["start"] = new[] { x1, y1, z1 },
                    ["end"] = new[] { x2, y2, z2 },
                    ["layer"] = layer,
                };
                return true;
            }

            case "rectangle" or "rectangulo" or "rect":
            {
                if (!TryGetDouble(item, "x1", [], 0.0, out double x1, out error) ||
                    !TryGetDouble(item, "y1", [], 0.0, out double y1, out error) ||
                    !TryGetDouble(item, "x2", [], 0.0, out double x2, out error) ||
                    !TryGetDouble(item, "y2", [], 0.0, out double y2, out error))
                {
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "rectangle",
                    ["min"] = new[] { x1, y1 },
                    ["max"] = new[] { x2, y2 },
                    ["layer"] = layer,
                };
                return true;
            }

            case "circle" or "circulo":
            {
                if (!TryGetDouble(item, "center_x", ["centerX", "centroX", "centro_x", "x"], 0.0, out double cx, out error) ||
                    !TryGetDouble(item, "center_y", ["centerY", "centroY", "centro_y", "y"], 0.0, out double cy, out error) ||
                    !TryGetDouble(item, "center_z", ["centerZ", "centroZ", "centro_z", "z"], 0.0, out double cz, out error) ||
                    !TryGetDouble(item, "radius", ["radio", "r"], 0.0, out double r, out error))
                {
                    return false;
                }

                if (r <= 0)
                {
                    error = "Invalid radius in circle element.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "circle",
                    ["center"] = new[] { cx, cy, cz },
                    ["radius"] = r,
                    ["layer"] = layer,
                };
                return true;
            }

            case "arc" or "arco":
            {
                if (!TryGetDouble(item, "center_x", ["centerX", "centroX", "centro_x", "x"], 0.0, out double cx, out error) ||
                    !TryGetDouble(item, "center_y", ["centerY", "centroY", "centro_y", "y"], 0.0, out double cy, out error) ||
                    !TryGetDouble(item, "center_z", ["centerZ", "centroZ", "centro_z", "z"], 0.0, out double cz, out error) ||
                    !TryGetDouble(item, "radius", ["radio", "r"], 0.0, out double r, out error) ||
                    !TryGetDouble(item, "start_angle", ["startAngle", "anguloInicio", "angulo_inicio"], 0.0, out double sa, out error) ||
                    !TryGetDouble(item, "end_angle", ["endAngle", "anguloFin", "angulo_fin"], 0.0, out double ea, out error))
                {
                    return false;
                }

                if (r <= 0)
                {
                    error = "Invalid radius in arc element.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "arc",
                    ["center"] = new[] { cx, cy, cz },
                    ["radius"] = r,
                    ["start_angle"] = sa,
                    ["end_angle"] = ea,
                    ["layer"] = layer,
                };
                return true;
            }

            case "polyline" or "polilinea" or "lwpolyline":
            {
                if (!ExtractBatchPoints(item, out double[][]? pts, out error))
                {
                    return false;
                }

                if (!TryGetOptionalBool(item, out bool? closedVal, out error, "closed", "cerrada"))
                {
                    return false;
                }

                if (pts == null || pts.Length < 2)
                {
                    error = "Polyline requires at least 2 valid vertices.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "polyline",
                    ["points"] = pts,
                    ["closed"] = closedVal ?? false,
                    ["layer"] = layer,
                };
                return true;
            }

            case "text" or "texto":
            {
                string txt = GetStringOpt(item, "text") ?? GetStringOpt(item, "texto") ?? "";
                if (string.IsNullOrWhiteSpace(txt))
                {
                    error = "Text element requires non-empty 'text'.";
                    return false;
                }

                if (!TryGetDouble(item, "x", [], 0.0, out double tx, out error) ||
                    !TryGetDouble(item, "y", [], 0.0, out double ty, out error) ||
                    !TryGetDouble(item, "z", [], 0.0, out double tz, out error) ||
                    !TryGetDouble(item, "height", ["altura"], 0.25, out double h, out error) ||
                    !TryGetDouble(item, "rotation", ["rotacion"], 0.0, out double rot, out error))
                {
                    return false;
                }

                if (h <= 0)
                {
                    error = "Invalid height in text element.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "text",
                    ["text"] = txt,
                    ["position"] = new[] { tx, ty, tz },
                    ["height"] = h,
                    ["rotation"] = rot,
                    ["layer"] = layer,
                };
                return true;
            }

            default:
                error = $"Unsupported element type '{rawType}'. Use: line, polyline, rectangle, circle, arc, text.";
                return false;
        }
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonProperty matched)
    {
        foreach (JsonProperty prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                matched = prop;
                return true;
            }
        }

        matched = default;
        return false;
    }

    private static bool TryParseFiniteNumber(JsonElement el, out double value)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value) && double.IsFinite(value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value))
        {
            return true;
        }

        value = 0.0;
        return false;
    }

    private static bool ExtractBatchPoints(JsonElement item, out double[][]? points, out string error)
    {
        points = null;
        error = "Polyline requires at least 2 valid vertices.";

        if (!TryGetPropertyCaseInsensitive(item, "points", out JsonProperty prop)
            && !TryGetPropertyCaseInsensitive(item, "puntos", out prop))
        {
            return false;
        }

        if (prop.Value.ValueKind == JsonValueKind.String)
        {
            points = ParsePoints(prop.Value.GetString(), 2);
            return points != null;
        }

        if (prop.Value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<double[]>();
        foreach (JsonElement pt in prop.Value.EnumerateArray())
        {
            if (pt.ValueKind == JsonValueKind.Array)
            {
                int len = pt.GetArrayLength();
                if (len is < 2 or > 3)
                {
                    return false;
                }

                var coords = new double[len];
                int k = 0;
                foreach (JsonElement numEl in pt.EnumerateArray())
                {
                    if (!TryParseFiniteNumber(numEl, out coords[k]))
                    {
                        return false;
                    }

                    k++;
                }

                list.Add(coords);
            }
            else if (pt.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetPropertyCaseInsensitive(pt, "x", out JsonProperty xProp)
                    || !TryGetPropertyCaseInsensitive(pt, "y", out JsonProperty yProp)
                    || !TryParseFiniteNumber(xProp.Value, out double vx)
                    || !TryParseFiniteNumber(yProp.Value, out double vy))
                {
                    return false;
                }

                if (TryGetPropertyCaseInsensitive(pt, "bulge", out JsonProperty bProp)
                    && bProp.Value.ValueKind != JsonValueKind.Null)
                {
                    if (!TryParseFiniteNumber(bProp.Value, out double vb))
                    {
                        return false;
                    }

                    list.Add([vx, vy, vb]);
                }
                else
                {
                    list.Add([vx, vy]);
                }
            }
            else
            {
                return false;
            }
        }

        if (list.Count < 2)
        {
            return false;
        }

        points = list.ToArray();
        return true;
    }

    private static string? GetStringOpt(JsonElement obj, string name)
    {
        return TryGetPropertyCaseInsensitive(obj, name, out JsonProperty prop) && prop.Value.ValueKind == JsonValueKind.String
            ? prop.Value.GetString()
            : null;
    }

    private static bool TryGetOptionalBool(JsonElement obj, out bool? value, out string error, params string[] names)
    {
        value = null;
        error = string.Empty;
        foreach (string name in names)
        {
            if (!TryGetPropertyCaseInsensitive(obj, name, out JsonProperty prop))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Null)
            {
                value = null;
                return true;
            }

            if (prop.Value.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (prop.Value.ValueKind == JsonValueKind.False)
            {
                value = false;
                return true;
            }

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                string raw = (prop.Value.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                if (raw is "true" or "1")
                {
                    value = true;
                    return true;
                }

                if (raw is "false" or "0")
                {
                    value = false;
                    return true;
                }
            }

            error = $"Field '{prop.Name}' must be a boolean.";
            return false;
        }

        return true;
    }

    private static bool TryGetDouble(
        JsonElement el,
        string primary,
        string[] aliases,
        double fallback,
        out double value,
        out string error)
    {
        error = string.Empty;
        JsonProperty matched;
        if (!TryGetPropertyCaseInsensitive(el, primary, out matched))
        {
            bool found = false;
            foreach (string alias in aliases)
            {
                if (TryGetPropertyCaseInsensitive(el, alias, out matched))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                value = fallback;
                return true;
            }
        }

        if (matched.Value.ValueKind == JsonValueKind.Null)
        {
            value = fallback;
            return true;
        }

        if (TryParseFiniteNumber(matched.Value, out value))
        {
            return true;
        }

        error = $"Field '{matched.Name}' must be a finite number.";
        return false;
    }
}
