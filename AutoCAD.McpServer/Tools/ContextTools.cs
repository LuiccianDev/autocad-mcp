using System;
using System.ComponentModel;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides read-only MCP tools to inspect AutoCAD drawing state, entities, and context.
/// </summary>
[McpServerToolType]
[ToolDomain("context", "autocad_context", "Inspect drawing metadata, layers, entities, texts, extents, selection, blocks, screenshots, and Gateway catalog/guide.")]
public sealed class ContextTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the AutoCAD Gateway.</param>
    public ContextTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Retrieves metadata of the active AutoCAD drawing: DWG filename, read-only state, and configured units.
    /// </summary>
    /// <returns>Asynchronous task returning JSON with DWG document metadata.</returns>
    [McpServerTool(Name = "get_drawing_info")]
    [ToolExposure(Core = true)]
    [Description("Retrieve metadata of the active AutoCAD drawing: DWG filename, read-only state, and configured units.")]
    public async Task<string> GetDrawingInfo() => await _gatewayClient.GetContextAsync("context/info");

    /// <summary>
    /// Reads and returns a summary with counts by entity type of all entities in AutoCAD ModelSpace.
    /// </summary>
    /// <returns>Asynchronous task returning entity counts grouped by type.</returns>
    [McpServerTool(Name = "get_entities")]
    [Description("Read and return a summary (counts by type) of all entities and objects existing in AutoCAD ModelSpace.")]
    public async Task<string> GetEntities() => await _gatewayClient.GetContextAsync("context/entities");

    /// <summary>
    /// Retrieves the complete list of drawing layers with their states (locked, frozen, off) and ACI color code.
    /// </summary>
    /// <returns>Asynchronous task returning the layer list.</returns>
    [McpServerTool(Name = "get_layers")]
    [ToolExposure(Core = true)]
    [Description("Retrieve the complete list of drawing layers with their states (locked, frozen, off) and ACI color code.")]
    public async Task<string> GetLayers() => await _gatewayClient.GetContextAsync("context/layers");

    /// <summary>
    /// Lists all text entities (DBText and MText) in the drawing with content, exact coordinates, and layer.
    /// </summary>
    /// <returns>Asynchronous task returning text entities and their locations.</returns>
    [McpServerTool(Name = "get_texts")]
    [Description("List all text entities (DBText and MText) in the drawing with content, exact (X, Y) coordinates, and layer.")]
    public async Task<string> GetTexts() => await _gatewayClient.GetContextAsync("context/texts");

    /// <summary>
    /// Retrieves minimum (X, Y) and maximum (X, Y) bounding coordinates of the active drawing (Extmin and Extmax).
    /// </summary>
    /// <returns>Asynchronous task returning drawing bounding coordinates.</returns>
    [McpServerTool(Name = "get_drawing_extents")]
    [ToolExposure(Core = true)]
    [Description("Retrieve minimum (X, Y) and maximum (X, Y) coordinates of the active drawing (Extmin and Extmax).")]
    public async Task<string> GetDrawingExtents() => await _gatewayClient.GetContextAsync("context/extents");

    /// <summary>
    /// Gets the number of entities currently selected by the user in AutoCAD.
    /// </summary>
    /// <returns>Asynchronous task returning selected entity count.</returns>
    [McpServerTool(Name = "get_selection_count")]
    [Description("Get the number of entities currently selected by the user in AutoCAD.")]
    public async Task<string> GetSelectionCount() => await _gatewayClient.GetContextAsync("context/selection");

    /// <summary>
    /// Retrieves names of all block definitions loaded into the current drawing.
    /// </summary>
    /// <returns>Asynchronous task returning list of block names.</returns>
    [McpServerTool(Name = "list_blocks")]
    [Description("Retrieve names of all block definitions loaded into the current drawing.")]
    public async Task<string> ListBlocks() => await _gatewayClient.GetContextAsync("context/blocks");

    /// <summary>
    /// Captures a PNG screenshot of the active AutoCAD window and returns the file path and optional inline base64 PNG data.
    /// </summary>
    /// <param name="inline">When true, includes base64-encoded PNG data in addition to the saved file path (when within size limits).</param>
    /// <returns>Asynchronous task returning JSON with screenshot path, dimensions, and optional base64 payload.</returns>
    [McpServerTool(Name = "capture_screenshot")]
    [ToolExposure(Core = true)]
    [Description("Capture a PNG screenshot of the active AutoCAD window ('eyes for AI') and save it to disk. Set inline=true to include base64 PNG data.")]
    public async Task<string> CaptureScreenshot(bool inline = false) =>
        await _gatewayClient.GetContextAsync(inline ? "context/screenshot?inline=true" : "context/screenshot");

    /// <summary>Alias for get_layers.</summary>
    /// <returns>Asynchronous task returning drawing layer JSON.</returns>
    [McpServerTool(Name = "read_layers")]
    [ToolExposure(AliasOf = "get_layers")]
    [Description("Alias for get_layers: Retrieve the complete list of drawing layers with their states.")]
    public async Task<string> ReadLayers() => await GetLayers();

    /// <summary>Alias for get_entities.</summary>
    /// <returns>Asynchronous task returning ModelSpace entity counts JSON.</returns>
    [McpServerTool(Name = "read_objects")]
    [ToolExposure(AliasOf = "get_entities")]
    [Description("Alias for get_entities: Read and return a summary of all entities in ModelSpace.")]
    public async Task<string> ReadObjects() => await GetEntities();

    /// <summary>Alias for get_texts.</summary>
    /// <returns>Asynchronous task returning extracted text entities JSON.</returns>
    [McpServerTool(Name = "read_texts")]
    [ToolExposure(AliasOf = "get_texts")]
    [Description("Alias for get_texts: List all text entities in the drawing with coordinates.")]
    public async Task<string> ReadTexts() => await GetTexts();

    /// <summary>Alias for get_selection_count.</summary>
    /// <returns>Asynchronous task returning selection count JSON.</returns>
    [McpServerTool(Name = "read_selection")]
    [ToolExposure(AliasOf = "get_selection_count")]
    [Description("Alias for get_selection_count: Get the number of entities currently selected.")]
    public async Task<string> ReadSelection() => await GetSelectionCount();

    /// <summary>Alias for list_blocks.</summary>
    /// <returns>Asynchronous task returning loaded block definition names JSON.</returns>
    [McpServerTool(Name = "list_available_blocks")]
    [ToolExposure(AliasOf = "list_blocks")]
    [Description("Alias for list_blocks: Retrieve names of all block definitions loaded.")]
    public async Task<string> ListAvailableBlocks() => await ListBlocks();

    /// <summary>Alias for capture_screenshot.</summary>
    /// <param name="inline">When true, includes base64-encoded PNG data in addition to the saved file path.</param>
    /// <returns>Asynchronous task returning screenshot path and optional base64 payload.</returns>
    [McpServerTool(Name = "capture_screen")]
    [ToolExposure(AliasOf = "capture_screenshot")]
    [Description("Alias for capture_screenshot: Capture a screenshot of the active AutoCAD window.")]
    public async Task<string> CaptureScreen(bool inline = false) => await CaptureScreenshot(inline);

    /// <summary>Retrieves the dynamic Gateway endpoint catalog with undo semantics and Edit Guard metadata.</summary>
    /// <returns>Asynchronous task returning serialized JSON endpoint catalog.</returns>
    [McpServerTool(Name = "get_gateway_tools")]
    [Description("Query the live AutoCAD Gateway dynamic endpoint catalog (GET /_tools) with category, undo granularity, and Edit Guard flags.")]
    public async Task<string> GetGatewayTools() =>
        await _gatewayClient.GetContextAsync(GatewayProtocol.ToolsRoute);

    /// <summary>Retrieves operational guidelines and safety invariants from the Gateway.</summary>
    /// <returns>Asynchronous task returning serialized JSON agent guidelines.</returns>
    [McpServerTool(Name = "get_gateway_guide")]
    [Description("Retrieve AI agent operational rules, unit conventions, and undo/Edit Guard guidance from the Gateway (GET /_guide).")]
    public async Task<string> GetGatewayGuide() =>
        await _gatewayClient.GetContextAsync(GatewayProtocol.GuideRoute);
}
