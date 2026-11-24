using System;
using System.Collections.Generic;
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
/// Provides MCP tools for native ObjectARX entity queries, inspection, modification,
/// geometric transforms, deletion, and atomic transactional batching.
/// </summary>
[McpServerToolType]
[ToolDomain("entities", "autocad_entities", "Query, inspect, modify, transform, delete, and transactionally batch-execute native AutoCAD entities.")]
public sealed class EntityTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="EntityTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with AutoCAD Gateway.</param>
    public EntityTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Queries entities with optional type, layer, space, 2D bounding-box, text, and cursor filters.</summary>
    [McpServerTool(Name = "query_entities")]
    [ToolExposure(Core = true)]
    [Description("Query drawing entities using optional filters (types, layers, space, 2D bounding box, text substring) with revision-bound pagination cursors.")]
    public async Task<string> QueryEntities(
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? types = null,
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? layers = null,
        string space = "model",
        double? minX = null,
        double? minY = null,
        double? maxX = null,
        double? maxY = null,
        string? textContains = null,
        int limit = 100,
        string? cursor = null,
        string? document = null)
    {
        if (limit is < 1 or > SecurityPolicy.MaxQueryLimit)
        {
            return CadFormat.Error($"'limit' must be between 1 and {SecurityPolicy.MaxQueryLimit}.");
        }

        double[]? bbox = null;
        if (minX.HasValue || minY.HasValue || maxX.HasValue || maxY.HasValue)
        {
            if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue ||
                !double.IsFinite(minX.Value) || !double.IsFinite(minY.Value) ||
                !double.IsFinite(maxX.Value) || !double.IsFinite(maxY.Value))
            {
                return CadFormat.Error("All four finite bounds (minX, minY, maxX, maxY) are required when filtering by bounding box.");
            }

            bbox = [minX.Value, minY.Value, maxX.Value, maxY.Value];
        }

        if (!CadFormat.TryParseStringList(types, out string[]? parsedTypes, out string? typesErr))
        {
            return CadFormat.Error($"Invalid 'types': {typesErr}");
        }

        if (!CadFormat.TryParseStringList(layers, out string[]? parsedLayers, out string? layersErr))
        {
            return CadFormat.Error($"Invalid 'layers': {layersErr}");
        }

        string payload = JsonSerializer.Serialize(new
        {
            types = parsedTypes,
            layers = parsedLayers,
            space,
            bbox,
            text_contains = textContains,
            limit,
            cursor,
            document,
        });

        return await _gatewayClient.PostJsonAsync("entities/query", payload);
    }

    /// <summary>Reads full geometry and properties of a single entity by hexadecimal handle.</summary>
    [McpServerTool(Name = "get_entity")]
    [ToolExposure(Core = true)]
    [Description("Inspect detailed geometry, layer, attributes, and revision metadata for a single entity by hexadecimal handle.")]
    public async Task<string> GetEntity(string handle, string? document = null)
    {
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            return CadFormat.Error($"Invalid hexadecimal handle: '{handle}'.");
        }

        string payload = JsonSerializer.Serialize(new { handle = handle.Trim(), document });
        return await _gatewayClient.PostJsonAsync("entities/get", payload);
    }

    /// <summary>Modifies properties or individual coordinates of an existing entity in one transaction.</summary>
    [McpServerTool(Name = "modify_entity")]
    [ToolExposure(Core = true)]
    [Description("Modify properties (layer, colorIndex, linetype, text, radius, height, width, elevation, rotation, block scale, coordinates, closed) by entity handle without overwriting omitted axes.")]
    public async Task<string> ModifyEntity(
        string handle,
        string? layer = null,
        int? colorIndex = null,
        string? linetype = null,
        string? text = null,
        double? radius = null,
        double? height = null,
        double? width = null,
        double? elevation = null,
        double? rotation = null,
        double? scale = null,
        bool? closed = null,
        double? x = null,
        double? y = null,
        double? z = null,
        double? startX = null,
        double? startY = null,
        double? startZ = null,
        double? endX = null,
        double? endY = null,
        double? endZ = null,
        double? startAngle = null,
        double? endAngle = null,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            return CadFormat.Error($"Invalid hexadecimal handle: '{handle}'.");
        }

        if (layer is not null && !string.IsNullOrWhiteSpace(layer) && !SecurityPolicy.IsValidSymbolName(layer))
        {
            return CadFormat.Error($"Invalid layer name: '{layer}'.");
        }

        if (linetype is not null && !string.IsNullOrWhiteSpace(linetype) && !SecurityPolicy.IsValidSymbolName(linetype))
        {
            return CadFormat.Error($"Invalid linetype name: '{linetype}'.");
        }

        if (colorIndex is < 0 or > 256)
        {
            return CadFormat.Error("'colorIndex' must be between 0 and 256.");
        }

        if (!CadFormat.AllOptionalFinite(radius, height, width, elevation, rotation, scale, x, y, z, startX, startY, startZ, endX, endY, endZ, startAngle, endAngle))
        {
            return CadFormat.Error("Numeric geometry parameters must be finite.");
        }

        var body = new Dictionary<string, object?>
        {
            ["handle"] = handle.Trim(),
            ["layer"] = layer?.Trim(),
            ["color_index"] = colorIndex,
            ["linetype"] = linetype?.Trim(),
            ["text"] = text,
            ["radius"] = radius,
            ["height"] = height,
            ["width"] = width,
            ["elevation"] = elevation,
            ["rotation"] = rotation,
            ["scale"] = scale,
            ["closed"] = closed,
            ["x"] = x,
            ["y"] = y,
            ["z"] = z,
            ["start_x"] = startX,
            ["start_y"] = startY,
            ["start_z"] = startZ,
            ["end_x"] = endX,
            ["end_y"] = endY,
            ["end_z"] = endZ,
            ["start_angle"] = startAngle,
            ["end_angle"] = endAngle,
            ["expected_revision"] = expectedRevision,
            ["document"] = document,
        }
        .Where(kv => kv.Value is not null)
        .ToDictionary(kv => kv.Key, kv => kv.Value);

        return await _gatewayClient.PostJsonAsync("entities/modify", JsonSerializer.Serialize(body));
    }

    /// <summary>
    /// Transforms one or more entities (move, rotate, scale, copy, mirror) atomically.
    /// For mirror, supply explicit axis endpoints (<paramref name="p1X"/>, <paramref name="p1Y"/>)
    /// and (<paramref name="p2X"/>, <paramref name="p2Y"/>).
    /// </summary>
    /// <param name="handles">Comma-separated handles or JSON array of handles.</param>
    /// <param name="operation">Transform kind: move, rotate, scale, copy, or mirror.</param>
    /// <param name="dx">Translation X offset for move/copy.</param>
    /// <param name="dy">Translation Y offset for move/copy.</param>
    /// <param name="dz">Translation Z offset for move/copy.</param>
    /// <param name="baseX">Pivot X coordinate for rotate/scale.</param>
    /// <param name="baseY">Pivot Y coordinate for rotate/scale.</param>
    /// <param name="baseZ">Pivot Z coordinate for rotate/scale.</param>
    /// <param name="angle">Rotation angle in degrees for rotate.</param>
    /// <param name="factor">Positive scale factor for scale.</param>
    /// <param name="p1X">Mirror axis start X coordinate.</param>
    /// <param name="p1Y">Mirror axis start Y coordinate.</param>
    /// <param name="p2X">Mirror axis end X coordinate.</param>
    /// <param name="p2Y">Mirror axis end Y coordinate.</param>
    /// <param name="eraseSource">When true on mirror, transforms entities in place instead of cloning.</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    [McpServerTool(Name = "transform_entities")]
    [Description("Apply move, rotate, scale, copy, or mirror (with explicit p1X/p1Y and p2X/p2Y axis) to one or more handles inside one atomic transaction.")]
    public async Task<string> TransformEntities(
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string handles,
        string operation,
        double dx = 0.0,
        double dy = 0.0,
        double dz = 0.0,
        double? baseX = null,
        double? baseY = null,
        double? baseZ = null,
        double? angle = null,
        double? factor = null,
        double? p1X = null,
        double? p1Y = null,
        double? p2X = null,
        double? p2Y = null,
        bool eraseSource = false,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.TryParseValidHandles(handles, out List<string> parsedHandles, out string? handleErr, required: true))
        {
            return CadFormat.Error(handleErr ?? "'handles' must contain valid non-empty hexadecimal entity handles.");
        }

        if (string.IsNullOrWhiteSpace(operation))
        {
            return CadFormat.Error("'operation' is required (move, rotate, scale, copy, mirror).");
        }

        if (!CadFormat.AllOptionalFinite(dx, dy, dz, baseX, baseY, baseZ, angle, factor, p1X, p1Y, p2X, p2Y))
        {
            return CadFormat.Error("All numeric transform parameters must be finite.");
        }

        string normOp = operation.Trim().ToLowerInvariant();
        if (normOp == "mirror" && (!p1X.HasValue || !p1Y.HasValue || !p2X.HasValue || !p2Y.HasValue))
        {
            return CadFormat.Error("Mirror operation requires explicit axis points (p1X, p1Y, p2X, p2Y).");
        }

        var payload = new Dictionary<string, object?>
        {
            ["handles"] = parsedHandles,
            ["operation"] = normOp,
            ["dx"] = dx,
            ["dy"] = dy,
            ["dz"] = dz,
            ["base_point"] = (baseX.HasValue && baseY.HasValue) ? new[] { baseX.Value, baseY.Value, baseZ ?? 0.0 } : null,
            ["angle"] = angle,
            ["factor"] = factor,
            ["p1"] = (p1X.HasValue && p1Y.HasValue) ? new[] { p1X.Value, p1Y.Value, 0.0 } : null,
            ["p2"] = (p2X.HasValue && p2Y.HasValue) ? new[] { p2X.Value, p2Y.Value, 0.0 } : null,
            ["erase_source"] = eraseSource,
            ["expected_revision"] = expectedRevision,
            ["document"] = document,
        };

        return await _gatewayClient.PostJsonAsync("entities/transform", JsonSerializer.Serialize(payload));
    }

    /// <summary>Erases one or more entities identified by handle in one atomic transaction.</summary>
    [McpServerTool(Name = "delete_entities")]
    [ToolExposure(Core = true)]
    [Description("Delete one or more entities by hexadecimal handle inside a single transaction.")]
    public async Task<string> DeleteEntities(
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string handles,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.TryParseValidHandles(handles, out List<string> parsedHandles, out string? handleErr, required: true))
        {
            return CadFormat.Error(handleErr ?? "'handles' must contain valid non-empty hexadecimal entity handles.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            handles = parsedHandles,
            expected_revision = expectedRevision,
            document,
        });

        return await _gatewayClient.PostJsonAsync("entities/delete", payload);
    }

    /// <summary>Executes up to 500 native layer and entity mutations in one atomic transaction (single Ctrl+Z undo step).</summary>
    [McpServerTool(Name = "execute_native_batch")]
    [ToolExposure(Core = true)]
    [Description("Execute up to 500 native ObjectARX operations (create, modify, transform, delete, create_layer, set_layer_state) in a single atomic transaction.")]
    public async Task<string> ExecuteNativeBatch(
        [CompoundParam("operations", StructuredJson = StructuredJsonKind.Array)] string operationsJson,
        string defaultLayer = "0",
        bool rollbackOnError = true,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(defaultLayer))
        {
            return CadFormat.Error($"Invalid default layer name: '{defaultLayer}'.");
        }

        if (string.IsNullOrWhiteSpace(operationsJson))
        {
            return CadFormat.Error("'operationsJson' cannot be empty.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(operationsJson);
        }
        catch (JsonException ex)
        {
            return CadFormat.Error($"Invalid JSON in 'operationsJson': {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return CadFormat.Error("'operationsJson' must be a JSON array of operation objects.");
            }

            int count = doc.RootElement.GetArrayLength();
            if (count == 0 || count > SecurityPolicy.MaxBatchOperations)
            {
                return CadFormat.Error($"'operationsJson' must contain between 1 and {SecurityPolicy.MaxBatchOperations} items.");
            }

            string payload = JsonSerializer.Serialize(new
            {
                operations = doc.RootElement,
                default_layer = defaultLayer.Trim(),
                rollback_on_error = rollbackOnError,
                expected_revision = expectedRevision,
                document,
            });

            return await _gatewayClient.PostJsonAsync("batch", payload, timeoutMs: 60_000);
        }
    }
}
