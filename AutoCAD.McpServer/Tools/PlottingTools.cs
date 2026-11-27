using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for PaperSpace layouts, native page setups (A0-A4),
/// 2D/3D DCS viewports, native parametric title blocks with logos, and silent PDF plotting.
/// </summary>
[McpServerToolType]
[ToolDomain("plotting", "autocad_plotting", "Create PaperSpace layouts, configure page setups, create metric viewports, draw title blocks, and export PDFs.")]
public sealed class PlottingTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="PlottingTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with the AutoCAD Gateway.</param>
    public PlottingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Creates a PaperSpace layout tab natively and optionally removes its default viewport.</summary>
    [McpServerTool(Name = "create_layout")]
    [Description("Create a new PaperSpace Layout tab natively and optionally erase its auto-generated default viewport.")]
    public async Task<string> CreateLayout(
        string layoutName,
        bool clearDefaultViewport = true,
        bool activate = true,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!PlottingPolicy.TryValidatePaperLayoutName(layoutName, out string cleanName, out string? layoutErr))
        {
            return CadFormat.Error(layoutErr ?? "Invalid PaperSpace layout name.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            layout_name = cleanName,
            clear_default_viewport = clearDefaultViewport,
            activate,
            expected_revision = expectedRevision,
            document,
        });
        return await _gatewayClient.PostJsonAsync("plotting/layout", payload);
    }

    /// <summary>Configures PaperSpace layout PlotSettings (A0-A4, landscape/portrait, 1:1 mm) natively.</summary>
    [McpServerTool(Name = "configure_layout_page")]
    [Description("Configure standardized sheet size (A0 to A4) and orientation of a Layout natively for 1:1 metric PDF plotting.")]
    public async Task<string> ConfigureLayoutPage(
        string layoutName,
        string paperSize = "A1",
        string plotStyle = "monochrome.ctb",
        string orientation = "landscape",
        string plotDevice = "DWG To PDF.pc3",
        long? expectedRevision = null,
        string? document = null)
    {
        if (!PlottingPolicy.TryValidatePaperLayoutName(layoutName, out string cleanLayout, out string? layoutErr))
        {
            return CadFormat.Error(layoutErr ?? "Invalid PaperSpace layout name.");
        }

        if (!SecurityPolicy.IsSafePlotStyleName(plotStyle, allowSpaces: true))
        {
            return CadFormat.Error("Plot style must be a safe .ctb or .stb filename without path separators or '~'/'?'.");
        }

        string cleanPaper = (paperSize ?? string.Empty).Trim().ToUpperInvariant();
        if (!PlottingPolicy.ValidPaperSizes.Contains(cleanPaper))
        {
            return CadFormat.Error($"Unsupported paperSize '{paperSize}'. Expected one of: A0, A1, A2, A3, A4.");
        }

        string cleanOrientation = (orientation ?? "landscape").Trim().ToLowerInvariant();
        if (!PlottingPolicy.ValidOrientations.Contains(cleanOrientation))
        {
            return CadFormat.Error("'orientation' must be 'landscape' or 'portrait'.");
        }

        if (string.IsNullOrWhiteSpace(plotDevice) || CadFormat.HasControlChars(plotDevice))
        {
            return CadFormat.Error("'plotDevice' must be a non-empty plotter configuration name without control characters.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            layout_name = cleanLayout,
            paper_size = cleanPaper,
            plot_style = plotStyle.Trim(),
            orientation = cleanOrientation,
            plot_device = plotDevice.Trim(),
            expected_revision = expectedRevision,
            document,
        });
        return await _gatewayClient.PostJsonAsync("plotting/page-setup", payload);
    }

    /// <summary>Creates a native PaperSpace Viewport with 2D/3D DCS centering, metric scale, and visual controls.</summary>
    [McpServerTool(Name = "create_viewport")]
    [ToolExposure(Core = true)]
    [Description("Create a native PaperSpace Viewport entity with 2D/3D DCS centering, exact metric scale (1000/Scale), and visual style controls.")]
    public async Task<string> CreateViewport(
        double lowerX,
        double lowerY,
        double width,
        double height,
        double scale,
        double centerModelX,
        double centerModelY,
        double centerModelZ = 0.0,
        [CompoundParam("view_direction", StructuredJson = StructuredJsonKind.Array)] string? viewDirection = null,
        double twistAngle = 0.0,
        string? shadePlot = null,
        string? visualStyle = null,
        bool lockViewport = true,
        string layer = "0",
        string? layoutName = null,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.AllFinite(lowerX, lowerY, width, height, scale, centerModelX, centerModelY, centerModelZ, twistAngle) ||
            width <= 0 || height <= 0 || scale <= 0)
        {
            return CadFormat.Error("Viewport coordinates and positive width, height, and metric scale must be finite numbers.");
        }

        if (CadFormat.HasControlChars(layoutName))
        {
            return CadFormat.Error("Layout name cannot contain control characters.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return CadFormat.Error($"Invalid layer name: '{layer}'.");
        }

        if (!PlottingPolicy.TryParseViewDirectionInput(viewDirection, out object? parsedDir, out string? dirErr))
        {
            return CadFormat.Error(dirErr ?? "Invalid viewDirection.");
        }

        string? normShade = null;
        if (!string.IsNullOrWhiteSpace(shadePlot))
        {
            normShade = shadePlot.Trim().ToLowerInvariant();
            if (!PlottingPolicy.ValidShadePlots.Contains(normShade))
            {
                return CadFormat.Error($"Unsupported shadePlot '{shadePlot}'. Expected: as_displayed, wireframe, hidden, rendered.");
            }
        }

        if (CadFormat.HasControlChars(visualStyle))
        {
            return CadFormat.Error("Visual style name cannot contain control characters.");
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
            center_model_z = centerModelZ,
            view_direction = parsedDir,
            twist_angle = twistAngle,
            shade_plot = normShade,
            visual_style = string.IsNullOrWhiteSpace(visualStyle) ? null : visualStyle.Trim(),
            lock_viewport = lockViewport,
            layer = layer.Trim(),
            layout_name = string.IsNullOrWhiteSpace(layoutName) ? null : layoutName.Trim(),
            expected_revision = expectedRevision,
            document,
        });

        return await _gatewayClient.PostJsonAsync("plotting/viewport", payload);
    }

    /// <summary>Draws a native parametric engineering title block in PaperSpace with optional raster logo.</summary>
    [McpServerTool(Name = "insert_title_block")]
    [Description("Insert a native parametric engineering title block in PaperSpace with configurable template and optional raster logo.")]
    public async Task<string> InsertTitleBlock(
        double cornerX,
        double cornerY,
        string projectTitle = "ENGINEERING PROJECT",
        string drawingName = "GENERAL PLAN",
        string sheetCode = "L-01",
        string scaleText = "1:1000",
        string author = "ENGINEERING",
        string? date = null,
        string? layoutName = null,
        string template = "iso_standard",
        string? logoPath = null,
        string layer = "FORMAT_TITLEBLOCK",
        long? expectedRevision = null,
        string? document = null)
    {
        if (!CadFormat.AllFinite(cornerX, cornerY))
        {
            return CadFormat.Error("Title block corner coordinates must be finite numbers.");
        }

        if (CadFormat.HasControlChars(projectTitle) || CadFormat.HasControlChars(drawingName) || CadFormat.HasControlChars(sheetCode) ||
            CadFormat.HasControlChars(scaleText) || CadFormat.HasControlChars(author) || CadFormat.HasControlChars(date) ||
            CadFormat.HasControlChars(layoutName))
        {
            return CadFormat.Error("Title block text fields cannot contain control characters or newlines.");
        }

        if (!SecurityPolicy.IsValidSymbolName(layer))
        {
            return CadFormat.Error($"Invalid layer name: '{layer}'.");
        }

        string cleanTemplate = (template ?? "iso_standard").Trim().ToLowerInvariant();
        if (!PlottingPolicy.ValidTemplates.Contains(cleanTemplate))
        {
            return CadFormat.Error($"Unsupported template '{template}'. Expected: iso_standard, compact, detailed.");
        }

        string? normLogo = null;
        if (!string.IsNullOrWhiteSpace(logoPath))
        {
            if (!SecurityPolicy.ValidateLocalFilePath(
                    logoPath,
                    out normLogo,
                    out string? logoErr,
                    SecurityPolicy.RasterImageExtensions))
            {
                return CadFormat.Error(logoErr ?? "Invalid logo image path.", code: "invalid_path");
            }
        }

        string finalDate = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyy-MM") : date.Trim();
        string payload = JsonSerializer.Serialize(new
        {
            corner_x = cornerX,
            corner_y = cornerY,
            project_title = projectTitle.Trim(),
            drawing_name = drawingName.Trim(),
            sheet_code = sheetCode.Trim(),
            scale_text = scaleText.Trim(),
            author = author.Trim(),
            date = finalDate,
            layout_name = string.IsNullOrWhiteSpace(layoutName) ? null : layoutName.Trim(),
            template = cleanTemplate,
            logo_path = normLogo,
            layer = layer.Trim(),
            expected_revision = expectedRevision,
            document,
        });
        return await _gatewayClient.PostJsonAsync("plotting/title-block", payload);
    }

    /// <summary>Plots and exports a PaperSpace layout synchronously to PDF via PlotEngine.</summary>
    [McpServerTool(Name = "export_pdf")]
    [ToolExposure(Core = true)]
    [Description("Plot and export a PaperSpace layout synchronously to PDF via PlotEngine forcing BACKGROUNDPLOT=0.")]
    public async Task<string> ExportPdf(
        string layoutName,
        string pdfPath,
        string plotStyle = "monochrome.ctb",
        string? paperSize = null,
        string? orientation = null,
        long? expectedRevision = null,
        string? document = null)
    {
        if (!PlottingPolicy.TryValidatePaperLayoutName(layoutName, out string cleanLayout, out string? layoutErr))
        {
            return CadFormat.Error(layoutErr ?? "Export PDF requires a valid PaperSpace layout name; 'Model' is not supported.");
        }

        if (!SecurityPolicy.IsSafePlotStyleName(plotStyle, allowSpaces: true))
        {
            return CadFormat.Error("Plot style must be a safe .ctb or .stb filename without path separators, UNC prefixes, or '~'/'?'.");
        }

        if (!SecurityPolicy.ValidateLocalFilePath(pdfPath, out string normPdf, out string? pathErr, ".pdf"))
        {
            return CadFormat.Error(pathErr ?? "Invalid PDF output path.", code: "invalid_path");
        }

        string? cleanPaper = null;
        if (!string.IsNullOrWhiteSpace(paperSize))
        {
            cleanPaper = paperSize.Trim().ToUpperInvariant();
            if (!PlottingPolicy.ValidPaperSizes.Contains(cleanPaper))
            {
                return CadFormat.Error($"Unsupported paperSize '{paperSize}'. Expected one of: A0, A1, A2, A3, A4.");
            }
        }

        string? cleanOrientation = null;
        if (!string.IsNullOrWhiteSpace(orientation))
        {
            cleanOrientation = orientation.Trim().ToLowerInvariant();
            if (!PlottingPolicy.ValidOrientations.Contains(cleanOrientation))
            {
                return CadFormat.Error("'orientation' must be 'landscape' or 'portrait'.");
            }
        }

        string payload = JsonSerializer.Serialize(new
        {
            layout_name = cleanLayout,
            pdf_path = normPdf,
            plot_style = plotStyle.Trim(),
            paper_size = cleanPaper,
            orientation = cleanOrientation,
            expected_revision = expectedRevision,
            document,
        });

        return await _gatewayClient.PostJsonAsync("plotting/pdf", payload, timeoutMs: 60_000);
    }

    /// <summary>Alias for configure_layout_page.</summary>
    [McpServerTool(Name = "configure_layout_sheet")]
    [ToolExposure(AliasOf = "configure_layout_page")]
    [Description("Alias for configure_layout_page: Configure standardized sheet size (A0 to A4) of a Layout.")]
    public Task<string> ConfigureLayoutSheet(
        string layoutName,
        string paperSize = "A1",
        string plotStyle = "monochrome.ctb",
        string orientation = "landscape",
        string plotDevice = "DWG To PDF.pc3",
        long? expectedRevision = null,
        string? document = null) =>
        ConfigureLayoutPage(layoutName, paperSize, plotStyle, orientation, plotDevice, expectedRevision, document);

    /// <summary>Alias for insert_title_block.</summary>
    [McpServerTool(Name = "insert_parametric_titleblock")]
    [ToolExposure(AliasOf = "insert_title_block")]
    [Description("Alias for insert_title_block: Insert a native parametric engineering title block in PaperSpace.")]
    public Task<string> InsertParametricTitleblock(
        double cornerX,
        double cornerY,
        string projectTitle = "ENGINEERING PROJECT",
        string drawingName = "GENERAL PLAN",
        string sheetCode = "L-01",
        string scaleText = "1:1000",
        string author = "ENGINEERING",
        string? date = null,
        string? layoutName = null,
        string template = "iso_standard",
        string? logoPath = null,
        string layer = "FORMAT_TITLEBLOCK",
        long? expectedRevision = null,
        string? document = null) =>
        InsertTitleBlock(cornerX, cornerY, projectTitle, drawingName, sheetCode, scaleText, author, date, layoutName, template, logoPath, layer, expectedRevision, document);
}
