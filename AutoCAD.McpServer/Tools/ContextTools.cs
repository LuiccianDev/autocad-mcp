using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using AutoCAD.McpServer.Services;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides read-only MCP tools to inspect AutoCAD drawing state, entities, and context.
/// </summary>
[McpServerToolType]
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
    /// Captures a PNG screenshot of the active AutoCAD window and returns the generated file path.
    /// </summary>
    /// <returns>Asynchronous task returning JSON with screenshot path and dimensions.</returns>
    [McpServerTool(Name = "capture_screenshot")]
    [Description("Capture a PNG screenshot of the active AutoCAD window ('eyes for AI') and save it to disk. Returns absolute file path.")]
    public async Task<string> CaptureScreenshot() => await _gatewayClient.GetContextAsync("context/screenshot");
}
