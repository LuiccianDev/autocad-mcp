using System;
using System.Collections.Generic;
using System.ComponentModel;
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

                if (!BatchElementNormalizer.TryNormalizeElement(item, defaultLayer, out Dictionary<string, object?>? op, out string err))
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

    /// <summary>Attaches and places a raster image (PNG/JPG/BMP/TIF/GIF) in ModelSpace or PaperSpace with IMAGEFRAME control.</summary>
    [McpServerTool(Name = "insert_raster_image")]
    [Description("Attach and place a raster image (PNG/JPG/BMP/TIF/GIF) in ModelSpace or PaperSpace with IMAGEFRAME control.")]
    public async Task<string> InsertRasterImage(
        string filePath,
        double x,
        double y,
        double z = 0.0,
        double? width = null,
        double? height = null,
        double scale = 1.0,
        double rotation = 0.0,
        int imageFrame = 0,
        string? space = null,
        string? layoutName = null,
        string layer = "0",
        long? expectedRevision = null,
        string? document = null)
    {
        if (!SecurityPolicy.ValidateLocalFilePath(
                filePath,
                out string normPath,
                out string? pathErr,
                SecurityPolicy.RasterImageExtensions))
        {
            return Error(pathErr ?? "Invalid raster image path.", code: "invalid_path");
        }

        if (!AllFinite(x, y, z, scale, rotation) || scale <= 0)
        {
            return Error("Coordinates and rotation must be finite and scale must be greater than zero.");
        }

        if (width.HasValue && (!double.IsFinite(width.Value) || width.Value <= 0))
        {
            return Error("'width' must be a finite number greater than zero.");
        }

        if (height.HasValue && (!double.IsFinite(height.Value) || height.Value <= 0))
        {
            return Error("'height' must be a finite number greater than zero.");
        }

        if (imageFrame is < 0 or > 2)
        {
            return Error("'imageFrame' must be 0 (off), 1 (above), or 2 (visible, no plot).");
        }

        if (!SecurityPolicy.TryNormalizeSpace(space, layoutName, out string normSpace, out string? spaceErr))
        {
            return Error(spaceErr ?? "'space' must be 'model' or 'paper'.");
        }

        if (HasControlChars(layoutName))
        {
            return Error("Layout name cannot contain control characters.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Error($"Invalid layer name: '{layer}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            file_path = normPath,
            x,
            y,
            z,
            width,
            height,
            scale,
            rotation,
            image_frame = imageFrame,
            space = normSpace,
            layout_name = string.IsNullOrWhiteSpace(layoutName) ? null : layoutName.Trim(),
            layer = layer.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return await _gatewayClient.PostJsonAsync("drafting/raster-image", payload);
    }
}
