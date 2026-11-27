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
/// Provides MCP tools for native AutoCAD dimensions, multileaders, hatches, tables,
/// reusable block definitions, block attributes, Xrefs, and entity exploding.
/// </summary>
[McpServerToolType]
[ToolDomain("drafting", "autocad_drafting", "Create dimensions, multileaders, hatches, tables, block definitions, block attributes, Xrefs, and explode entities.")]
public sealed class DraftingTools
{
    private static readonly HashSet<string> ValidDimensionTypes =
        new(["aligned", "rotated", "radial", "diametric", "angular"], StringComparer.OrdinalIgnoreCase);

    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="DraftingTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with AutoCAD Gateway.</param>
    public DraftingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Creates a native aligned, rotated, radial, diametric, or angular dimension entity.</summary>
    /// <param name="type">Dimension type: <c>aligned</c>, <c>rotated</c>, <c>radial</c>, <c>diametric</c>, or <c>angular</c>.</param>
    /// <param name="x1">First definition X coordinate.</param>
    /// <param name="y1">First definition Y coordinate.</param>
    /// <param name="x2">Second definition X coordinate.</param>
    /// <param name="y2">Second definition Y coordinate.</param>
    /// <param name="dimLineX">Optional dimension line X coordinate (must be paired with <paramref name="dimLineY"/>).</param>
    /// <param name="dimLineY">Optional dimension line Y coordinate (must be paired with <paramref name="dimLineX"/>).</param>
    /// <param name="centerX">Optional center/vertex X coordinate (must be paired with <paramref name="centerY"/>).</param>
    /// <param name="centerY">Optional center/vertex Y coordinate (must be paired with <paramref name="centerX"/>).</param>
    /// <param name="angle">Dimension line rotation angle in degrees for rotated dimensions.</param>
    /// <param name="leaderLength">Leader length (&gt;= 0) for radial/diametric dimensions.</param>
    /// <param name="textOverride">Optional explicit text override string.</param>
    /// <param name="dimStyle">Optional existing AutoCAD dimension style name.</param>
    /// <param name="textHeight">Optional positive dimension text height in drawing units.</param>
    /// <param name="arrowSize">Optional positive dimension arrowhead size in drawing units.</param>
    /// <param name="dimScale">Optional positive overall dimension scale factor.</param>
    /// <param name="layer">Target layer name (default "0").</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created entity JSON.</returns>
    [McpServerTool(Name = "create_dimension")]
    [Description("Create a native dimension (aligned, rotated, radial, diametric, angular) inside one atomic transaction.")]
    public Task<string> CreateDimension(
        string type,
        double x1 = 0.0,
        double y1 = 0.0,
        double x2 = 0.0,
        double y2 = 0.0,
        double? dimLineX = null,
        double? dimLineY = null,
        double? centerX = null,
        double? centerY = null,
        double angle = 0.0,
        double leaderLength = 0.5,
        string? textOverride = null,
        string? dimStyle = null,
        double? textHeight = null,
        double? arrowSize = null,
        double? dimScale = null,
        string space = "model",
        string layer = "0",
        long? expectedRevision = null,
        string? document = null)
    {
        if (string.IsNullOrWhiteSpace(type) || !ValidDimensionTypes.Contains(type.Trim()))
        {
            return Task.FromResult(CadFormat.Error("'type' must be one of: aligned, rotated, radial, diametric, angular."));
        }

        if (!SecurityPolicy.TryNormalizeSpace(space, null, out string normSpace, out string? spaceErr))
        {
            return Task.FromResult(CadFormat.Error(spaceErr ?? "'space' must be 'model' or 'paper'."));
        }

        if (!string.IsNullOrWhiteSpace(dimStyle) && !SecurityPolicy.IsValidSymbolName(dimStyle))
        {
            return Task.FromResult(CadFormat.Error($"Invalid dimension style name '{dimStyle}'."));
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Task.FromResult(CadFormat.Error($"Invalid layer name '{layer}'."));
        }

        if (dimLineX.HasValue != dimLineY.HasValue)
        {
            return Task.FromResult(CadFormat.Error("Provide both 'dimLineX' and 'dimLineY', or neither."));
        }

        if (centerX.HasValue != centerY.HasValue)
        {
            return Task.FromResult(CadFormat.Error("Provide both 'centerX' and 'centerY', or neither."));
        }

        string normType = type.Trim().ToLowerInvariant();
        if (normType == "angular" && !centerX.HasValue)
        {
            return Task.FromResult(CadFormat.Error("Angular dimensions require explicit vertex coordinates ('centerX' and 'centerY')."));
        }

        if (!CadFormat.AllFinite(x1, y1, x2, y2, angle, leaderLength) ||
            leaderLength < 0 ||
            !CadFormat.AllOptionalFinite(dimLineX, dimLineY, centerX, centerY, textHeight, arrowSize, dimScale))
        {
            return Task.FromResult(CadFormat.Error("Dimension coordinates, angle, and non-negative leaderLength must be finite numbers."));
        }

        if ((textHeight.HasValue && textHeight.Value <= 0) ||
            (arrowSize.HasValue && arrowSize.Value <= 0) ||
            (dimScale.HasValue && dimScale.Value <= 0))
        {
            return Task.FromResult(CadFormat.Error("'textHeight', 'arrowSize', and 'dimScale' must be finite numbers greater than zero."));
        }

        double[]? dimLinePt = (dimLineX.HasValue && dimLineY.HasValue) ? [dimLineX.Value, dimLineY.Value, 0.0] : null;
        double[]? centerPt = (centerX.HasValue && centerY.HasValue) ? [centerX.Value, centerY.Value, 0.0] : null;

        string payload = JsonSerializer.Serialize(new
        {
            type = normType,
            p1 = new[] { x1, y1, 0.0 },
            p2 = new[] { x2, y2, 0.0 },
            dim_line_point = dimLinePt,
            center = centerPt ?? [x1, y1, 0.0],
            chord = new[] { x2, y2, 0.0 },
            vertex = centerPt ?? [x1, y1, 0.0],
            angle,
            leader_length = leaderLength,
            text_override = textOverride,
            dim_style = string.IsNullOrWhiteSpace(dimStyle) ? null : dimStyle.Trim(),
            text_height = textHeight,
            arrow_size = arrowSize,
            dim_scale = dimScale,
            space = normSpace,
            layer = layer.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("drafting/dimension", payload);
    }

    /// <summary>Creates a native multileader annotation pointing from arrow head to landing text.</summary>
    /// <param name="arrowX">Arrowhead tip X coordinate.</param>
    /// <param name="arrowY">Arrowhead tip Y coordinate.</param>
    /// <param name="landingX">Callout landing text X coordinate.</param>
    /// <param name="landingY">Callout landing text Y coordinate.</param>
    /// <param name="text">Callout text string.</param>
    /// <param name="textHeight">Positive text height in drawing units.</param>
    /// <param name="arrowSize">Optional positive arrowhead size.</param>
    /// <param name="layer">Target layer name (default "0").</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created entity JSON.</returns>
    [McpServerTool(Name = "create_mleader")]
    [Description("Create a native MLeader callout annotation from arrow point (arrowX, arrowY) to landing text point (landingX, landingY).")]
    public Task<string> CreateMLeader(
        double arrowX,
        double arrowY,
        double landingX,
        double landingY,
        string text,
        double textHeight = 0.25,
        double? arrowSize = null,
        string layer = "0",
        long? expectedRevision = null,
        string? document = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(CadFormat.Error("'text' cannot be empty."));
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Task.FromResult(CadFormat.Error($"Invalid layer name '{layer}'."));
        }

        if (!CadFormat.AllFinite(arrowX, arrowY, landingX, landingY, textHeight) ||
            textHeight <= 0 ||
            (arrowSize.HasValue && (!double.IsFinite(arrowSize.Value) || arrowSize.Value <= 0)))
        {
            return Task.FromResult(CadFormat.Error("Coordinates and positive textHeight/arrowSize must be finite numbers."));
        }

        string payload = JsonSerializer.Serialize(new
        {
            arrow_point = new[] { arrowX, arrowY, 0.0 },
            landing_point = new[] { landingX, landingY, 0.0 },
            text,
            text_height = textHeight,
            arrow_size = arrowSize,
            layer = layer.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("drafting/mleader", payload);
    }

    /// <summary>Creates a native solid or patterned hatch from closed boundary handles or polygon vertices.</summary>
    /// <param name="handles">Optional comma-separated or JSON array of closed boundary entity handles (mutually exclusive with <paramref name="points"/>).</param>
    /// <param name="points">Optional closed polygon vertices formatted as <c>"x1,y1;x2,y2;..."</c> (mutually exclusive with <paramref name="handles"/>).</param>
    /// <param name="pattern">Predefined AutoCAD hatch pattern name (e.g. ANSI31, SOLID).</param>
    /// <param name="scale">Positive pattern scale factor.</param>
    /// <param name="angle">Pattern rotation angle in degrees.</param>
    /// <param name="associative">Whether boundary handles stay associative.</param>
    /// <param name="layer">Target layer name (default "0").</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created entity JSON.</returns>
    [McpServerTool(Name = "create_hatch")]
    [Description("Create a native solid or patterned Hatch inside closed boundary handles or 'x1,y1;x2,y2;...' polygon vertices.")]
    public Task<string> CreateHatch(
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? handles = null,
        string? points = null,
        string pattern = "ANSI31",
        double scale = 1.0,
        double angle = 0.0,
        bool? associative = null,
        string layer = "0",
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.TryParseValidHandles(handles, out List<string> handleList, out string? handleErr))
        {
            return Task.FromResult(CadFormat.Error($"Invalid 'handles': {handleErr}"));
        }

        bool hasHandles = handleList.Count > 0;
        bool hasPoints = !string.IsNullOrWhiteSpace(points);
        if (hasHandles && hasPoints)
        {
            return Task.FromResult(CadFormat.Error("Provide either boundary 'handles' or polygon 'points', not both."));
        }

        if (!hasHandles && !hasPoints)
        {
            return Task.FromResult(CadFormat.Error("Provide either boundary 'handles' or closed 'points' vertices."));
        }

        if (!SecurityPolicy.IsValidSymbolName(pattern) || !SecurityPolicy.IsValidSymbolName(layer))
        {
            return Task.FromResult(CadFormat.Error("Invalid hatch pattern or layer name."));
        }

        if (!CadFormat.AllFinite(scale, angle) || scale <= 0)
        {
            return Task.FromResult(CadFormat.Error("Hatch scale must be > 0 and angle must be finite."));
        }

        double[][]? polyPts = null;
        if (hasPoints)
        {
            polyPts = CadFormat.ParsePoints(points, 2);
            if (polyPts == null || polyPts.Length < 3)
            {
                return Task.FromResult(CadFormat.Error("Hatch polygon boundary requires at least 3 'x,y' vertices."));
            }
        }

        string payload = JsonSerializer.Serialize(new
        {
            handles = hasHandles ? handleList : null,
            points = polyPts?.Select(p => new[] { p[0], p[1] }).ToArray(),
            pattern = pattern.Trim(),
            scale,
            angle,
            associative,
            layer = layer.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("drafting/hatch", payload);
    }

    /// <summary>Creates a structured AutoCAD Table entity from JSON cells and headers.</summary>
    /// <param name="x">Table top-left insertion X coordinate.</param>
    /// <param name="y">Table top-left insertion Y coordinate.</param>
    /// <param name="rows">Number of data rows (1..200).</param>
    /// <param name="cols">Number of columns (1..50).</param>
    /// <param name="cellsJson">JSON 2D array of cell values (strings, numbers, or booleans).</param>
    /// <param name="headersJson">Optional JSON array of column header labels.</param>
    /// <param name="title">Optional merged title row string.</param>
    /// <param name="rowHeight">Positive uniform row height.</param>
    /// <param name="colWidth">Positive uniform column width.</param>
    /// <param name="textHeight">Positive cell text height.</param>
    /// <param name="layer">Target layer name (default "0").</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created entity JSON.</returns>
    [McpServerTool(Name = "create_table")]
    [Description("Create a native AutoCAD Table entity at (x, y) populated with headers and a 2D JSON cells matrix.")]
    public Task<string> CreateTable(
        double x,
        double y,
        int rows,
        int cols,
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string cellsJson,
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? headersJson = null,
        string? title = null,
        double rowHeight = 1.0,
        double colWidth = 5.0,
        double textHeight = 0.35,
        string layer = "0",
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.AllFinite(x, y, rowHeight, colWidth, textHeight) || rowHeight <= 0 || colWidth <= 0 || textHeight <= 0)
        {
            return Task.FromResult(CadFormat.Error("Position and positive cell dimensions must be finite numbers."));
        }

        if (rows < 1 || rows > SecurityPolicy.MaxTableRows || cols < 1 || cols > SecurityPolicy.MaxTableCols)
        {
            return Task.FromResult(CadFormat.Error($"'rows' (1..{SecurityPolicy.MaxTableRows}) and 'cols' (1..{SecurityPolicy.MaxTableCols}) are required."));
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return Task.FromResult(CadFormat.Error($"Invalid layer name '{layer}'."));
        }

        if (!CadFormat.TryParseJsonArrayElement(cellsJson, out JsonElement cellsEl, out string? cellsErr))
        {
            return Task.FromResult(CadFormat.Error($"'cellsJson' invalid: {cellsErr}"));
        }

        JsonElement? headersEl = null;
        if (!string.IsNullOrWhiteSpace(headersJson))
        {
            if (!CadFormat.TryParseJsonArrayElement(headersJson, out JsonElement parsedHeaders, out string? headersErr))
            {
                return Task.FromResult(CadFormat.Error($"'headersJson' invalid: {headersErr}"));
            }

            headersEl = parsedHeaders;
        }

        string payload = JsonSerializer.Serialize(new
        {
            position = new[] { x, y, 0.0 },
            rows,
            cols,
            cells = cellsEl,
            headers = headersEl,
            title,
            row_height = rowHeight,
            col_width = colWidth,
            text_height = textHeight,
            layer = layer.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("drafting/table", payload);
    }

    /// <summary>Defines a reusable BlockTableRecord from entity handles or inline operation specs.</summary>
    /// <param name="name">Block definition name.</param>
    /// <param name="handles">Optional comma-separated or JSON array of source entity handles.</param>
    /// <param name="operationsJson">Optional JSON array of inline entity specs.</param>
    /// <param name="baseX">Block origin base point X.</param>
    /// <param name="baseY">Block origin base point Y.</param>
    /// <param name="baseZ">Block origin base point Z.</param>
    /// <param name="overwrite">Whether to replace an existing non-Xref block definition.</param>
    /// <param name="attributesJson">Optional JSON array of attribute definition specs.</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created block summary JSON.</returns>
    [McpServerTool(Name = "define_block")]
    [Description("Define a reusable Block definition from existing handles or a JSON array of inline geometry operations.")]
    public Task<string> DefineBlock(
        string name,
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? handles = null,
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? operationsJson = null,
        double baseX = 0.0,
        double baseY = 0.0,
        double baseZ = 0.0,
        bool overwrite = false,
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string? attributesJson = null,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Task.FromResult(CadFormat.Error($"Invalid block name '{name}'."));
        }

        if (!CadFormat.AllFinite(baseX, baseY, baseZ))
        {
            return Task.FromResult(CadFormat.Error("Block base point coordinates must be finite numbers."));
        }

        if (!CadFormat.TryParseValidHandles(handles, out List<string> handleList, out string? handleErr))
        {
            return Task.FromResult(CadFormat.Error($"Invalid 'handles': {handleErr}"));
        }

        bool hasHandles = handleList.Count > 0;
        bool hasOps = !string.IsNullOrWhiteSpace(operationsJson);
        if (!hasHandles && !hasOps)
        {
            return Task.FromResult(CadFormat.Error("Supply either non-empty source 'handles' or 'operationsJson' to define a block."));
        }

        JsonElement? opsEl = null;
        if (hasOps)
        {
            if (!CadFormat.TryParseJsonArrayElement(operationsJson, out JsonElement parsedOps, out string? opsErr))
            {
                return Task.FromResult(CadFormat.Error($"'operationsJson' invalid: {opsErr}"));
            }

            opsEl = parsedOps;
        }

        JsonElement? attrsEl = null;
        if (!string.IsNullOrWhiteSpace(attributesJson))
        {
            if (!CadFormat.TryParseJsonArrayElement(attributesJson, out JsonElement parsedAttrs, out string? attrsErr))
            {
                return Task.FromResult(CadFormat.Error($"'attributesJson' invalid: {attrsErr}"));
            }

            attrsEl = parsedAttrs;
        }

        string payload = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            handles = hasHandles ? handleList : null,
            operations = opsEl,
            base_point = new[] { baseX, baseY, baseZ },
            overwrite,
            attributes = attrsEl,
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("blocks/define", payload);
    }

    /// <summary>Inspects entities, origin, and attribute definitions of a named block definition.</summary>
    /// <param name="name">Block definition name.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning block definition inspection JSON.</returns>
    [McpServerTool(Name = "get_block_definition")]
    [Description("Inspect geometry summary, origin, Xref status, and attribute definitions of a named block.")]
    public Task<string> GetBlockDefinition(string name, string? document = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Task.FromResult(CadFormat.Error($"Invalid block name '{name}'."));
        }

        string q = $"blocks/definition?name={Uri.EscapeDataString(name.Trim())}" +
            (string.IsNullOrWhiteSpace(document) ? string.Empty : $"&document={Uri.EscapeDataString(document.Trim())}");
        return _gatewayClient.GetContextAsync(q);
    }

    /// <summary>Reads AttributeReference tag values of a BlockReference handle.</summary>
    /// <param name="handle">Hexadecimal BlockReference entity handle.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning block attribute values JSON.</returns>
    [McpServerTool(Name = "get_block_attributes")]
    [Description("Read all AttributeReference tag values on a BlockReference identified by hexadecimal handle.")]
    public Task<string> GetBlockAttributes(string handle, string? document = null)
    {
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            return Task.FromResult(CadFormat.Error($"Invalid hexadecimal handle: '{handle}'."));
        }

        string q = $"blocks/attributes?handle={Uri.EscapeDataString(handle.Trim())}" +
            (string.IsNullOrWhiteSpace(document) ? string.Empty : $"&document={Uri.EscapeDataString(document.Trim())}");
        return _gatewayClient.GetContextAsync(q);
    }

    /// <summary>Updates AttributeReference tag values on a BlockReference handle inside one transaction.</summary>
    /// <param name="handle">Hexadecimal BlockReference entity handle.</param>
    /// <param name="attributesJson">JSON object mapping attribute tags to replacement values.</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning attribute update summary JSON.</returns>
    [McpServerTool(Name = "set_block_attributes")]
    [Description("Update AttributeReference tag values on a BlockReference handle from a JSON object of {tag: value}.")]
    public Task<string> SetBlockAttributes(
        string handle,
        [CompoundParam(StructuredJson = StructuredJsonKind.Object)] string attributesJson,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            return Task.FromResult(CadFormat.Error($"Invalid hexadecimal handle: '{handle}'."));
        }

        Dictionary<string, object?>? attrs;
        try
        {
            attrs = JsonSerializer.Deserialize<Dictionary<string, object?>>(attributesJson);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(CadFormat.Error($"Invalid JSON in 'attributesJson': {ex.Message}"));
        }

        if (attrs == null || attrs.Count == 0)
        {
            return Task.FromResult(CadFormat.Error("'attributesJson' must be a non-empty JSON object."));
        }

        string payload = JsonSerializer.Serialize(new
        {
            handle = handle.Trim(),
            attributes = attrs,
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("blocks/attributes", payload);
    }

    /// <summary>Lists all external references (Xrefs) in the active or target drawing.</summary>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning Xref list JSON.</returns>
    [McpServerTool(Name = "list_xrefs")]
    [Description("List external references (Xrefs) with name, file path, resolution status, and unloaded state.")]
    public Task<string> ListXrefs(string? document = null)
    {
        string q = string.IsNullOrWhiteSpace(document)
            ? "blocks/xrefs"
            : $"blocks/xrefs?document={Uri.EscapeDataString(document.Trim())}";
        return _gatewayClient.GetContextAsync(q);
    }

    /// <summary>Explodes compound entities (polylines, block references, dimensions) atomically.</summary>
    /// <param name="handles">Comma-separated or JSON array of hexadecimal handles to explode.</param>
    /// <param name="eraseSource">When true, erases the original compound entity after exploding.</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created and erased handles JSON.</returns>
    [McpServerTool(Name = "explode_entities")]
    [Description("Explode one or more compound entities by handle inside a single atomic transaction.")]
    public Task<string> ExplodeEntities(
        [CompoundParam(StructuredJson = StructuredJsonKind.Array)] string handles,
        bool eraseSource = true,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.TryParseValidHandles(handles, out List<string> list, out string? handleErr, required: true))
        {
            return Task.FromResult(CadFormat.Error(handleErr ?? "'handles' must contain valid non-empty hexadecimal entity handles."));
        }

        string payload = JsonSerializer.Serialize(new
        {
            handles = list,
            erase_source = eraseSource,
            expected_revision = expectedRevision,
            document,
        });

        return _gatewayClient.PostJsonAsync("entities/explode", payload);
    }
}
