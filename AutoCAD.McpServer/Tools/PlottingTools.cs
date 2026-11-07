using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for PaperSpace layouts, standardized sheet configuration (A0-A4),
/// viewport creation with metric scales, title block insertion, and PDF plotting.
/// </summary>
[McpServerToolType]
public sealed class PlottingTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlottingTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the AutoCAD Gateway.</param>
    public PlottingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    private static string Num(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Creates a new PaperSpace layout tab and switches the active view to that layout.
    /// </summary>
    /// <param name="layoutName">Unique name for the layout tab (e.g. 'SHEET-01' or 'GENERAL_PLAN').</param>
    /// <returns>Asynchronous task returning confirmation.</returns>
    [McpServerTool(Name = "create_layout")]
    [Description("Create a new PaperSpace Layout tab and switch the active view to that layout.")]
    public async Task<string> CreateLayout(string layoutName)
    {
        if (string.IsNullOrWhiteSpace(layoutName))
        {
            return "{\"status\": \"error\", \"message\": \"Layout name cannot be empty.\"}";
        }

        string cleanName = layoutName.Trim();
        string command = $"_.-LAYOUT\nN\n{cleanName}\n_.-LAYOUT\nS\n{cleanName}\n_.TILEMODE\n0\n";
        return await _gatewayClient.ExecuteCommandAsync(command);
    }

    /// <summary>
    /// Configures standardized sheet size (A0 to A4) of a layout for 1:1 metric PDF plotting.
    /// </summary>
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
        if (string.IsNullOrWhiteSpace(layoutName))
        {
            return "{\"status\": \"error\", \"message\": \"Layout name cannot be empty.\"}";
        }

        string mediaName = paperSize.ToUpperInvariant() switch
        {
            "A0" => "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
            "A1" => "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
            "A2" => "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
            "A3" => "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
            "A4" => "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
            _ => "ISO_full_bleed_A1_(841.00_x_594.00_MM)"
        };

        var sb = new StringBuilder();
        sb.Append($"_.-LAYOUT\nS\n{layoutName.Trim()}\n");
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        sb.Append($"_.-PLOT\nN\n{layoutName.Trim()}\nDWG To PDF.pc3\n{mediaName}\nM\nL\nN\nL\n1:1\n0.00,0.00\nY\n{plotStyle}\nY\nN\nN\nY\nN\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Creates a layout viewport (MVIEW) in active PaperSpace, configures metric scale, and centers model coordinates.
    /// </summary>
    /// <param name="lowerX">Lower left X coordinate on the sheet (mm).</param>
    /// <param name="lowerY">Lower left Y coordinate on the sheet (mm).</param>
    /// <param name="width">Viewport width on sheet (mm).</param>
    /// <param name="height">Viewport height on sheet (mm).</param>
    /// <param name="scale">Engineering metric scale denominator (e.g. 1000 for 1:1000, 500 for 1:500).</param>
    /// <param name="centerModelX">Center East (X) coordinate in ModelSpace in meters.</param>
    /// <param name="centerModelY">Center North (Y) coordinate in ModelSpace in meters.</param>
    /// <returns>Asynchronous task returning result.</returns>
    [McpServerTool(Name = "create_viewport")]
    [Description("Create a layout viewport (Viewport / MVIEW) in the active layout, calculate exact metric scale (1000/Scale XP), and center the model at given (X, Y) coordinates.")]
    public async Task<string> CreateViewport(
        double lowerX,
        double lowerY,
        double width,
        double height,
        double scale,
        double centerModelX,
        double centerModelY)
    {
        if (scale <= 0)
        {
            return "{\"status\": \"error\", \"message\": \"Metric scale denominator must be greater than zero (e.g. 1000, 500, 200).\"}";
        }

        double upperX = lowerX + width;
        double upperY = lowerY + height;
        double factorZoomXp = 1000.0 / scale;

        var sb = new StringBuilder();
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        sb.Append($"_.-MVIEW\n{Num(lowerX)},{Num(lowerY)}\n{Num(upperX)},{Num(upperY)}\n");
        sb.Append("_.MSPACE\n");
        sb.Append($"_.ZOOM\nC\n{Num(centerModelX)},{Num(centerModelY)}\n{factorZoomXp.ToString("F6", CultureInfo.InvariantCulture)}XP\n");
        sb.Append("_.PSPACE\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Draws a parametric engineering title block in PaperSpace with project metadata.
    /// </summary>
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
        sb.Append($"_.RECTANG\n{Num(x0)},{Num(y0)}\n{Num(x1)},{Num(y1)}\n");
        sb.Append($"_.LINE\n{Num(x0)},{Num(y0 + 25.0)}\n{Num(x1)},{Num(y0 + 25.0)}\n\n");
        sb.Append($"_.LINE\n{Num(x0)},{Num(y0 + 12.0)}\n{Num(x1)},{Num(y0 + 12.0)}\n\n");
        sb.Append($"_.LINE\n{Num(x0 + 120.0)},{Num(y0)}\n{Num(x0 + 120.0)},{Num(y0 + 25.0)}\n\n");

        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 35.0)}\n3.5\n0\nPROJECT: {projectTitle.ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 28.0)}\n3.0\n0\nDRAWING: {drawingName.ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 16.0)}\n2.5\n0\nAUTHOR / DISCIPLINE: {author.ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 4.0)}\n2.5\n0\nDATE: {finalDate}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 125.0)},{Num(y0 + 16.0)}\n2.5\n0\nSCALE: {scaleText}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 125.0)},{Num(y0 + 4.0)}\n4.5\n0\nSHEET: {sheetCode.ToUpperInvariant()}\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Plots and exports a layout tab to a physical PDF document using 'DWG To PDF.pc3'.
    /// </summary>
    /// <param name="layoutName">Name of the layout to plot (e.g. 'Layout1' or 'SHEET-01').</param>
    /// <param name="pdfPath">Absolute output path for the PDF file (e.g. 'C:/Drawings/Sheet01.pdf').</param>
    /// <param name="plotStyle">Plot style table file (.ctb), default 'monochrome.ctb'.</param>
    /// <returns>Asynchronous task returning PDF plot result.</returns>
    [McpServerTool(Name = "export_pdf")]
    [Description("Plot and export a layout directly to a physical PDF file using the 'DWG To PDF.pc3' virtual plotter.")]
    public async Task<string> ExportPdf(
        string layoutName,
        string pdfPath,
        string plotStyle = "monochrome.ctb")
    {
        if (string.IsNullOrWhiteSpace(layoutName) || string.IsNullOrWhiteSpace(pdfPath))
        {
            return "{\"status\": \"error\", \"message\": \"Layout name and PDF path are required.\"}";
        }

        string normalizedPath = pdfPath.Replace('\\', '/');

        var sb = new StringBuilder();
        sb.Append($"_.-LAYOUT\nS\n{layoutName.Trim()}\n");
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        sb.Append($"_.-PLOT\nY\n{layoutName.Trim()}\nDWG To PDF.pc3\n\n\n\n\n\n\n\nY\n{plotStyle}\nY\nN\n{normalizedPath}\nN\nY\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }
}
