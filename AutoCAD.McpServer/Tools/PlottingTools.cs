using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for PaperSpace layouts, standardized sheet configuration (A0-A4),
/// viewport creation with metric scales, title block insertion, and PDF plotting.
/// </summary>
[McpServerToolType]
[ToolDomain("plotting", "autocad_plotting", "Create PaperSpace layouts, configure page setups, create metric viewports, draw title blocks, and export PDFs.")]
public sealed class PlottingTools
{
    private static readonly Dictionary<string, string> PaperMediaMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A0"] = "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
        ["A1"] = "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
        ["A2"] = "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
        ["A3"] = "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
        ["A4"] = "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
    };

    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="PlottingTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with the AutoCAD Gateway.</param>
    public PlottingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Creates a new PaperSpace layout tab and switches the active view to that layout.</summary>
    /// <param name="layoutName">Unique name for the layout tab (e.g. 'SHEET-01').</param>
    /// <returns>Asynchronous task returning confirmation.</returns>
    [McpServerTool(Name = "create_layout")]
    [Description("Create a new PaperSpace Layout tab and switch the active view to that layout.")]
    public async Task<string> CreateLayout(string layoutName)
    {
        if (!SecurityPolicy.IsSafeLayoutName(layoutName, allowSpaces: false))
        {
            return CadFormat.Error("Layout name must be a valid AutoCAD symbol free of spaces, control characters, or '~'/'?'.");
        }

        string cleanName = layoutName.Trim();
        string command = $"_.-LAYOUT\nN\n{cleanName}\n_.-LAYOUT\nS\n{cleanName}\n_.TILEMODE\n0\n";
        return await _gatewayClient.ExecuteCommandAsync(command);
    }

    /// <summary>Configures standardized sheet size (A0 to A4) of a layout for 1:1 metric PDF plotting.</summary>
    /// <param name="layoutName">Name of the layout to configure.</param>
    /// <param name="paperSize">Standardized sheet size: 'A0', 'A1', 'A2', 'A3', or 'A4'.</param>
    /// <param name="plotStyle">Plot style table file (.ctb), default 'monochrome.ctb'.</param>
    /// <returns>Asynchronous task returning page configuration result.</returns>
    [McpServerTool(Name = "configure_layout_page")]
    [Description("Configure standardized sheet size (A0 to A4) of a Layout for 1:1 metric PDF plotting with monochrome plot styles.")]
    public async Task<string> ConfigureLayoutPage(
        string layoutName,
        string paperSize = "A1",
        string plotStyle = "monochrome.ctb")
    {
        if (!SecurityPolicy.IsSafeLayoutName(layoutName, allowSpaces: false))
        {
            return CadFormat.Error("Layout name must be a valid AutoCAD symbol free of spaces, control characters, or '~'/'?'.");
        }

        if (!SecurityPolicy.IsSafePlotStyleName(plotStyle, allowSpaces: false))
        {
            return CadFormat.Error("Plot style must be a safe .ctb or .stb filename without spaces, paths, or '~'/'?'.");
        }

        string cleanPaper = (paperSize ?? string.Empty).Trim();
        if (!PaperMediaMap.TryGetValue(cleanPaper, out string? mediaName))
        {
            return CadFormat.Error($"Unsupported paperSize '{paperSize}'. Expected one of: A0, A1, A2, A3, A4.");
        }

        string cleanLayout = layoutName.Trim();
        string cleanStyle = plotStyle.Trim();
        var sb = new StringBuilder();
        sb.Append($"_.-LAYOUT\nS\n{cleanLayout}\n");
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        sb.Append($"_.-PLOT\nY\n{cleanLayout}\nDWG To PDF.pc3\n{mediaName}\nM\nL\nN\nL\n1:1\n0.00,0.00\nY\n{cleanStyle}\nY\nN\nN\nN\n\nY\nN\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Creates a native PaperSpace Viewport entity in active or target PaperSpace, configures metric scale, and centers model coordinates.
    /// </summary>
    /// <param name="lowerX">Lower left X coordinate on the sheet (mm).</param>
    /// <param name="lowerY">Lower left Y coordinate on the sheet (mm).</param>
    /// <param name="width">Viewport width on sheet (mm, must be &gt; 0).</param>
    /// <param name="height">Viewport height on sheet (mm, must be &gt; 0).</param>
    /// <param name="scale">Engineering metric scale denominator (e.g. 1000 for 1:1000, 500 for 1:500).</param>
    /// <param name="centerModelX">Center East (X) coordinate in ModelSpace in meters.</param>
    /// <param name="centerModelY">Center North (Y) coordinate in ModelSpace in meters.</param>
    /// <param name="layoutName">Optional PaperSpace layout name; uses active PaperSpace when omitted.</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name or session ID.</param>
    /// <returns>Asynchronous task returning created Viewport result.</returns>
    [McpServerTool(Name = "create_viewport")]
    [ToolExposure(Core = true)]
    [Description("Create a native PaperSpace Viewport entity with exact metric scale (1000/Scale) and target ModelSpace center inside one transaction.")]
    public async Task<string> CreateViewport(
        double lowerX,
        double lowerY,
        double width,
        double height,
        double scale,
        double centerModelX,
        double centerModelY,
        string? layoutName = null,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.AllFinite(lowerX, lowerY, width, height, scale, centerModelX, centerModelY) ||
            width <= 0 || height <= 0 || scale <= 0)
        {
            return CadFormat.Error("Viewport coordinates and positive width, height, and metric scale must be finite numbers.");
        }

        if (CadFormat.HasControlChars(layoutName))
        {
            return CadFormat.Error("Layout name cannot contain control characters.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            lower_x = lowerX,
            lower_y = lowerY,
            width,
            height,
            scale,
            center_model_x = centerModelX,
            center_model_y = centerModelY,
            layout_name = string.IsNullOrWhiteSpace(layoutName) ? null : layoutName.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return await _gatewayClient.PostJsonAsync("plotting/viewport", payload);
    }

    /// <summary>Draws a parametric engineering title block in PaperSpace with project metadata.</summary>
    /// <param name="cornerX">Bottom-right X coordinate on the sheet (mm) where title block is anchored.</param>
    /// <param name="cornerY">Bottom-right Y coordinate on the sheet (mm) where title block is anchored.</param>
    /// <param name="projectTitle">Main project title.</param>
    /// <param name="drawingName">Drawing sheet content / title.</param>
    /// <param name="sheetCode">Sheet identifier code (e.g. 'L-01').</param>
    /// <param name="scaleText">Printed scale text (e.g. '1:1000', 'AS SHOWN').</param>
    /// <param name="author">Author or designer name.</param>
    /// <param name="date">Drawing date string.</param>
    /// <returns>Asynchronous task returning title block creation result.</returns>
    [McpServerTool(Name = "insert_title_block")]
    [Description("Insert and draw a parametric engineering title block in PaperSpace with metadata fields (Title, Drawing, Sheet, Scale, Designer, Date).")]
    public async Task<string> InsertTitleBlock(
        double cornerX,
        double cornerY,
        string projectTitle = "ENGINEERING PROJECT",
        string drawingName = "GENERAL PLAN",
        string sheetCode = "L-01",
        string scaleText = "1:1000",
        string author = "ENGINEERING",
        string? date = null)
    {
        if (!CadFormat.AllFinite(cornerX, cornerY))
        {
            return CadFormat.Error("Title block corner coordinates must be finite numbers.");
        }

        if (CadFormat.HasControlChars(projectTitle) || CadFormat.HasControlChars(drawingName) || CadFormat.HasControlChars(sheetCode) ||
            CadFormat.HasControlChars(scaleText) || CadFormat.HasControlChars(author) || CadFormat.HasControlChars(date))
        {
            return CadFormat.Error("Title block text fields cannot contain control characters or newlines.");
        }

        string finalDate = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyy-MM") : date.Trim();
        double titleWidth = 180.0;
        double titleHeight = 45.0;

        double x0 = cornerX - titleWidth;
        double y0 = cornerY;
        double x1 = cornerX;
        double y1 = cornerY + titleHeight;

        var sb = new StringBuilder();
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        sb.Append("_.-LAYER\nM\nFORMAT_TITLEBLOCK\nC\n7\nFORMAT_TITLEBLOCK\n\n");
        sb.Append($"_.RECTANG\n{CadFormat.Num(x0)},{CadFormat.Num(y0)}\n{CadFormat.Num(x1)},{CadFormat.Num(y1)}\n");
        sb.Append($"_.LINE\n{CadFormat.Num(x0)},{CadFormat.Num(y0 + 25.0)}\n{CadFormat.Num(x1)},{CadFormat.Num(y0 + 25.0)}\n\n");
        sb.Append($"_.LINE\n{CadFormat.Num(x0)},{CadFormat.Num(y0 + 12.0)}\n{CadFormat.Num(x1)},{CadFormat.Num(y0 + 12.0)}\n\n");
        sb.Append($"_.LINE\n{CadFormat.Num(x0 + 120.0)},{CadFormat.Num(y0)}\n{CadFormat.Num(x0 + 120.0)},{CadFormat.Num(y0 + 25.0)}\n\n");

        sb.Append($"_.-TEXT\n{CadFormat.Num(x0 + 5.0)},{CadFormat.Num(y0 + 35.0)}\n3.5\n0\nPROJECT: {projectTitle.Trim().ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{CadFormat.Num(x0 + 5.0)},{CadFormat.Num(y0 + 28.0)}\n3.0\n0\nDRAWING: {drawingName.Trim().ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{CadFormat.Num(x0 + 5.0)},{CadFormat.Num(y0 + 16.0)}\n2.5\n0\nAUTHOR / DISCIPLINE: {author.Trim().ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{CadFormat.Num(x0 + 5.0)},{CadFormat.Num(y0 + 4.0)}\n2.5\n0\nDATE: {finalDate}\n");
        sb.Append($"_.-TEXT\n{CadFormat.Num(x0 + 125.0)},{CadFormat.Num(y0 + 16.0)}\n2.5\n0\nSCALE: {scaleText.Trim()}\n");
        sb.Append($"_.-TEXT\n{CadFormat.Num(x0 + 125.0)},{CadFormat.Num(y0 + 4.0)}\n4.5\n0\nSHEET: {sheetCode.Trim().ToUpperInvariant()}\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>Plots and exports a PaperSpace layout synchronously to a physical PDF document using 'DWG To PDF.pc3'.</summary>
    /// <param name="layoutName">Name of the PaperSpace layout to plot (e.g. 'Layout1' or 'SHEET-01').</param>
    /// <param name="pdfPath">Absolute output path for the PDF file (e.g. 'C:/Drawings/Sheet01.pdf').</param>
    /// <param name="plotStyle">Plot style table file (.ctb), default 'monochrome.ctb'.</param>
    /// <param name="expectedRevision">Optional expected drawing revision for optimistic concurrency.</param>
    /// <param name="document">Optional target open drawing name (must already be active).</param>
    /// <returns>Asynchronous task returning PDF plot result.</returns>
    [McpServerTool(Name = "export_pdf")]
    [ToolExposure(Core = true)]
    [Description("Plot and export a PaperSpace layout directly to a physical PDF file using the 'DWG To PDF.pc3' virtual plotter.")]
    public async Task<string> ExportPdf(
        string layoutName,
        string pdfPath,
        string plotStyle = "monochrome.ctb",
        long? expectedRevision = null,
        string? document = null)
    {
        if (!SecurityPolicy.IsSafeLayoutName(layoutName, allowSpaces: true))
        {
            return CadFormat.Error("Layout name must be a valid AutoCAD symbol free of control characters or '~'/'?'.");
        }

        if (string.Equals(layoutName.Trim(), "Model", StringComparison.OrdinalIgnoreCase))
        {
            return CadFormat.Error("Export PDF requires a PaperSpace layout name; 'Model' is not supported.");
        }

        if (!SecurityPolicy.IsSafePlotStyleName(plotStyle, allowSpaces: true))
        {
            return CadFormat.Error("Plot style must be a safe .ctb or .stb filename without path separators, UNC prefixes, or '~'/'?'.");
        }

        if (!SecurityPolicy.ValidateLocalFilePath(pdfPath, out string normPdf, out string? pathErr, ".pdf"))
        {
            return CadFormat.Error(pathErr ?? "Invalid PDF output path.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            layout_name = layoutName.Trim(),
            pdf_path = normPdf,
            plot_style = plotStyle.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return await _gatewayClient.PostJsonAsync("plotting/pdf", payload, timeoutMs: 60_000);
    }

    /// <summary>Alias for configure_layout_page.</summary>
    /// <param name="layoutName">Name of the layout to configure.</param>
    /// <param name="paperSize">Standardized sheet size: 'A0', 'A1', 'A2', 'A3', or 'A4'.</param>
    /// <param name="plotStyle">Plot style table file (.ctb), default 'monochrome.ctb'.</param>
    /// <returns>Asynchronous task returning page configuration result.</returns>
    [McpServerTool(Name = "configure_layout_sheet")]
    [ToolExposure(AliasOf = "configure_layout_page")]
    [Description("Alias for configure_layout_page: Configure standardized sheet size (A0 to A4) of a Layout.")]
    public async Task<string> ConfigureLayoutSheet(
        string layoutName,
        string paperSize = "A1",
        string plotStyle = "monochrome.ctb") =>
        await ConfigureLayoutPage(layoutName, paperSize, plotStyle);

    /// <summary>Alias for insert_title_block.</summary>
    /// <param name="cornerX">Bottom-right X coordinate on the sheet (mm).</param>
    /// <param name="cornerY">Bottom-right Y coordinate on the sheet (mm).</param>
    /// <param name="projectTitle">Main project title.</param>
    /// <param name="drawingName">Drawing sheet content / title.</param>
    /// <param name="sheetCode">Sheet identifier code (e.g. 'L-01').</param>
    /// <param name="scaleText">Printed scale text.</param>
    /// <param name="author">Author or designer name.</param>
    /// <param name="date">Drawing date string.</param>
    /// <returns>Asynchronous task returning title block creation result.</returns>
    [McpServerTool(Name = "insert_parametric_titleblock")]
    [ToolExposure(AliasOf = "insert_title_block")]
    [Description("Alias for insert_title_block: Insert and draw a parametric engineering title block in PaperSpace.")]
    public async Task<string> InsertParametricTitleblock(
        double cornerX,
        double cornerY,
        string projectTitle = "ENGINEERING PROJECT",
        string drawingName = "GENERAL PLAN",
        string sheetCode = "L-01",
        string scaleText = "1:1000",
        string author = "ENGINEERING",
        string? date = null) =>
        await InsertTitleBlock(cornerX, cornerY, projectTitle, drawingName, sheetCode, scaleText, author, date);
}
