using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Plots an existing PaperSpace layout synchronously to PDF via native <see cref="PlotEngine"/>.
/// </summary>
public static class PlottingService
{
    private sealed record PlotExecutionInfo(
        string DocumentName,
        long Revision,
        string? PaperSize,
        LayoutService.PageSetupSummary Summary,
        List<string> Warnings);

    /// <summary>
    /// Plots an existing PaperSpace layout synchronously to a local PDF file using native <see cref="PlotEngine"/>
    /// with <c>BACKGROUNDPLOT=0</c>, direct output path, and validated layout/setup before touching disk.
    /// </summary>
    public static async Task<string> PlotPdfAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        if (!PlottingPolicy.TryValidatePaperLayoutName(
                DatabaseHelpers.GetRequiredString(root, "layout_name"),
                out string cleanLayout,
                out string? layoutErr))
        {
            throw new AcadGatewayException("bad_argument", layoutErr ?? "Export PDF requires a valid PaperSpace layout name; 'Model' is not supported.");
        }

        string rawPdf = DatabaseHelpers.GetRequiredString(root, "pdf_path");
        if (!SecurityPolicy.ValidateLocalFilePath(rawPdf, out string normPdf, out string? pathErr, ".pdf"))
        {
            throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid PDF output path.");
        }

        DatabaseHelpers.EnsureNoReparsePoints(normPdf);

        string? requestedPaper = DatabaseHelpers.GetStringOpt(root, "paper_size")?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(requestedPaper) && !PlottingPolicy.ValidPaperSizes.Contains(requestedPaper))
        {
            throw new AcadGatewayException("bad_argument", $"Unsupported paper_size '{requestedPaper}'. Expected: A0, A1, A2, A3, A4.");
        }

        string? requestedOrientation = DatabaseHelpers.GetStringOpt(root, "orientation")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(requestedOrientation) && !PlottingPolicy.ValidOrientations.Contains(requestedOrientation))
        {
            throw new AcadGatewayException("bad_argument", "'orientation' must be 'landscape' or 'portrait'.");
        }

        string? requestedStyle = DatabaseHelpers.GetStringOpt(root, "plot_style")?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedStyle) && !SecurityPolicy.IsSafePlotStyleName(requestedStyle, allowSpaces: true))
        {
            throw new AcadGatewayException("bad_argument", "Plot style must be a safe .ctb or .stb filename.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        PlotExecutionInfo plotInfo = await MainThread.InCommandContextAsync(() =>
        {
            Document doc = Acad.ActiveDocument;
            Acad.EnsureActiveDocument(doc, documentName, "PDF plotting");

            DocumentSession session = DocumentSessionManager.GetOrCreate(doc);
            Acad.ValidateWritable(doc, session, requireIdle: false, expectedRevision: expectedRevision);

            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
            {
                throw new AcadGatewayException("busy", "Another AutoCAD plot job is currently in progress.");
            }

            var warnings = new List<string>();
            LayoutService.PageSetupSummary summary;
            ObjectId layoutId;
            using var psOverride = new PlotSettings(false);

            using (var tr = doc.TransactionManager.StartOpenCloseTransaction())
            {
                var layoutDict = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
                if (!layoutDict.Contains(cleanLayout))
                {
                    tr.Abort();
                    throw new AcadGatewayException("not_found", $"Layout '{cleanLayout}' does not exist in active drawing.");
                }

                layoutId = layoutDict.GetAt(cleanLayout);
                var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                psOverride.CopyFrom(layout);

                string device = string.IsNullOrWhiteSpace(layout.PlotConfigurationName) ||
                    string.Equals(layout.PlotConfigurationName, "None", StringComparison.OrdinalIgnoreCase)
                    ? "DWG To PDF.pc3"
                    : layout.PlotConfigurationName;
                string style = !string.IsNullOrWhiteSpace(requestedStyle)
                    ? requestedStyle
                    : (!string.IsNullOrWhiteSpace(layout.CurrentStyleSheet) ? layout.CurrentStyleSheet : "monochrome.ctb");

                if (!string.IsNullOrWhiteSpace(requestedPaper) || !string.IsNullOrWhiteSpace(requestedOrientation))
                {
                    string paper = !string.IsNullOrWhiteSpace(requestedPaper)
                        ? requestedPaper
                        : LayoutService.InferPaperSize(layout.CanonicalMediaName) ?? "A3";
                    bool wantLand = requestedOrientation is not null
                        ? requestedOrientation == "landscape"
                        : LayoutService.IsEffectiveLandscape(layout);
                    summary = LayoutService.ApplyPageSetup(psOverride, device, paper, wantLand, style, warnings);
                }
                else
                {
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;
                    if (string.Equals(layout.PlotConfigurationName, "None", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(layout.PlotConfigurationName))
                    {
                        psv.SetPlotConfigurationName(psOverride, device, null);
                        psv.RefreshLists(psOverride);
                    }

                    if (!string.IsNullOrWhiteSpace(requestedStyle))
                    {
                        LayoutService.ApplyStyleSheetIfPresent(psv, psOverride, style, warnings);
                    }

                    summary = LayoutService.SummarizePlotSettings(psOverride);
                }

                tr.Abort();
            }

            using var pi = new PlotInfo { Layout = layoutId, OverrideSettings = psOverride };
            var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
            piv.Validate(pi);

            // Mutate disk only after all document, layout, and PlotInfo validations have passed.
            string? parentDir = Path.GetDirectoryName(normPdf);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
                DatabaseHelpers.EnsureNoReparsePoints(normPdf);
            }

            if (File.Exists(normPdf))
            {
                try
                {
                    using var lockProbe = new FileStream(normPdf, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex)
                {
                    throw new AcadGatewayException("busy", $"Cannot overwrite PDF '{normPdf}' because it is open in another application: {ex.Message}");
                }
            }

            short prevBgPlot = Convert.ToInt16(AcApp.GetSystemVariable("BACKGROUNDPLOT"), CultureInfo.InvariantCulture);
            AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
            try
            {
                LayoutManager.Current.CurrentLayout = cleanLayout;
                using PlotEngine engine = PlotFactory.CreatePublishEngine();
                using var progress = new PlotProgressDialog(false, 1, false) { IsVisible = false };
                engine.BeginPlot(progress, null);
                engine.BeginDocument(pi, doc.Name, null, 1, true, normPdf);
                using var pageInfo = new PlotPageInfo();
                engine.BeginPage(pageInfo, pi, true, null);
                engine.BeginGenerateGraphics(null);
                engine.EndGenerateGraphics(null);
                engine.EndPage(null);
                engine.EndDocument(null);
                engine.EndPlot(null);
            }
            finally
            {
                try
                {
                    AcApp.SetSystemVariable("BACKGROUNDPLOT", prevBgPlot);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Failed to restore BACKGROUNDPLOT system variable: {0}", ex);
                }
            }

            return new PlotExecutionInfo(
                doc.Name,
                session.Revision,
                LayoutService.InferPaperSize(summary.CanonicalMedia) ?? requestedPaper,
                summary,
                warnings);
        }, preflight: () => Acad.EnsureQuiescent(Acad.ActiveDocument), timeoutMs: 60_000).ConfigureAwait(false);

        long fileBytes = await WaitForPdfReadyAsync(normPdf).ConfigureAwait(false);
        if (fileBytes <= 0)
        {
            throw new AcadGatewayException(
                "autocad_error",
                $"AutoCAD did not produce a non-empty PDF file at '{normPdf}' for layout '{cleanLayout}'.");
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            layout = cleanLayout,
            pdf_path = normPdf,
            file_exists = true,
            file_size_bytes = fileBytes,
            paper_size = plotInfo.PaperSize,
            canonical_media = plotInfo.Summary.CanonicalMedia,
            paper_width_mm = plotInfo.Summary.WidthMm,
            paper_height_mm = plotInfo.Summary.HeightMm,
            orientation = plotInfo.Summary.Orientation,
            plot_style = plotInfo.Summary.PlotStyle,
            plot_device = plotInfo.Summary.PlotDevice,
            warnings = plotInfo.Warnings.Count > 0 ? plotInfo.Warnings : null,
            document = plotInfo.DocumentName,
            revision = plotInfo.Revision,
        }, GatewayProtocol.WireJsonOptions);
    }

    private static async Task<long> WaitForPdfReadyAsync(string pdfPath)
    {
        IOException? lastIo = null;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (File.Exists(pdfPath))
            {
                try
                {
                    using var stream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length > 0)
                    {
                        return stream.Length;
                    }
                }
                catch (IOException ex)
                {
                    lastIo = ex;
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        if (lastIo is not null)
        {
            System.Diagnostics.Trace.TraceWarning("Timed out waiting for plot output stream lock on '{0}': {1}", pdfPath, lastIo.Message);
        }

        return File.Exists(pdfPath) ? new FileInfo(pdfPath).Length : 0L;
    }
}
