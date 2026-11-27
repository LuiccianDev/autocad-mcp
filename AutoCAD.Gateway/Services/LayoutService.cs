using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Manages native PaperSpace layout creation, default viewport cleanup, and ISO page setup configuration.
/// </summary>
public static class LayoutService
{
    internal readonly record struct PageSetupSummary(
        string CanonicalMedia,
        string Orientation,
        string PlotRotationName,
        double WidthMm,
        double HeightMm,
        string PlotStyle,
        string PlotDevice);

    /// <summary>
    /// Creates a PaperSpace layout tab and optionally erases AutoCAD's auto-generated default viewport.
    /// </summary>
    public static Task<string> CreateLayoutAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        if (!PlottingPolicy.TryValidatePaperLayoutName(
                DatabaseHelpers.GetRequiredString(root, "layout_name"),
                out string cleanName,
                out string? layoutErr))
        {
            throw new AcadGatewayException("bad_argument", layoutErr ?? "'layout_name' must be a valid PaperSpace layout name (not 'Model').");
        }

        bool clearDefaultViewport = DatabaseHelpers.GetBoolOpt(root, "clear_default_viewport") ?? true;
        bool activate = DatabaseHelpers.GetBoolOpt(root, "activate") ?? true;
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.RunOnDocumentAsync((doc, session) =>
        {
            Acad.EnsureActiveDocument(doc, documentName, "Layout creation");
            LayoutManager lm = LayoutManager.Current;
            if (lm.LayoutExists(cleanName))
            {
                throw new AcadGatewayException("already_exists", $"Layout '{cleanName}' already exists in this drawing.");
            }

            string prevLayout = lm.CurrentLayout;
            ObjectId layoutId = lm.CreateLayout(cleanName);
            int clearedCount = 0;

            try
            {
                // Switching CurrentLayout initializes the PaperSpace BTR and spawns the default floating viewport (Number > 1).
                lm.CurrentLayout = cleanName;
                if (clearDefaultViewport)
                {
                    using Transaction tr = doc.TransactionManager.StartTransaction();
                    var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                    ObjectIdCollection vpIds = layout.GetViewports();
                    ObjectId paperSpaceVpId = vpIds.Count > 0 ? vpIds[0] : ObjectId.Null;

                    for (int i = 1; i < vpIds.Count; i++)
                    {
                        if (tr.GetObject(vpIds[i], OpenMode.ForRead, false) is Viewport vp &&
                            vp.ObjectId != paperSpaceVpId &&
                            vp.Number > 1)
                        {
                            vp.UpgradeOpen();
                            vp.Erase();
                            clearedCount++;
                        }
                    }

                    tr.Commit();
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(prevLayout) && !string.Equals(prevLayout, cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    lm.CurrentLayout = prevLayout;
                }

                throw;
            }

            if (!activate && !string.IsNullOrWhiteSpace(prevLayout) && !string.Equals(prevLayout, cleanName, StringComparison.OrdinalIgnoreCase))
            {
                lm.CurrentLayout = prevLayout;
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                layout = cleanName,
                handle = layoutId.Handle.ToString(),
                cleared_default_viewports = clearedCount,
                active = activate,
                document = doc.Name,
                revision = session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, write: true, requireIdle: true, expectedRevision: expectedRevision, documentName: documentName);
    }

    /// <summary>
    /// Configures PaperSpace layout <c>PlotSettings</c> (ISO full bleed A0-A4, landscape/portrait, 1:1 mm, CTB/STB) natively.
    /// </summary>
    public static Task<string> ConfigureLayoutPageAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        if (!PlottingPolicy.TryValidatePaperLayoutName(
                DatabaseHelpers.GetRequiredString(root, "layout_name"),
                out string cleanLayout,
                out string? layoutErr))
        {
            throw new AcadGatewayException("bad_argument", layoutErr ?? "'layout_name' must be a valid PaperSpace layout name (not 'Model').");
        }

        string rawPaper = (DatabaseHelpers.GetStringOpt(root, "paper_size") ?? "A1").Trim().ToUpperInvariant();
        if (!PlottingPolicy.ValidPaperSizes.Contains(rawPaper))
        {
            throw new AcadGatewayException("bad_argument", $"Unsupported paper_size '{rawPaper}'. Expected one of: A0, A1, A2, A3, A4.");
        }

        string rawOrientation = (DatabaseHelpers.GetStringOpt(root, "orientation") ?? "landscape").Trim().ToLowerInvariant();
        if (!PlottingPolicy.ValidOrientations.Contains(rawOrientation))
        {
            throw new AcadGatewayException("bad_argument", "'orientation' must be 'landscape' or 'portrait'.");
        }

        bool wantLandscape = rawOrientation == "landscape";
        string plotStyle = (DatabaseHelpers.GetStringOpt(root, "plot_style") ?? "monochrome.ctb").Trim();
        if (!SecurityPolicy.IsSafePlotStyleName(plotStyle, allowSpaces: true))
        {
            throw new AcadGatewayException("bad_argument", "Plot style must be a safe .ctb or .stb filename.");
        }

        string plotDevice = (DatabaseHelpers.GetStringOpt(root, "plot_device") ?? "DWG To PDF.pc3").Trim();
        if (SecurityPolicy.HasControlChars(plotDevice))
        {
            throw new AcadGatewayException("bad_argument", "'plot_device' cannot contain control characters.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            var layoutDict = (DBDictionary)ctx.Tr.GetObject(ctx.Db.LayoutDictionaryId, OpenMode.ForRead);
            if (!layoutDict.Contains(cleanLayout))
            {
                throw new AcadGatewayException("not_found", $"Layout '{cleanLayout}' does not exist in this drawing.");
            }

            var layout = (Layout)ctx.Tr.GetObject(layoutDict.GetAt(cleanLayout), OpenMode.ForWrite);
            using var ps = new PlotSettings(layout.ModelType);
            ps.CopyFrom(layout);

            var warnings = new List<string>();
            PageSetupSummary summary = ApplyPageSetup(ps, plotDevice, rawPaper, wantLandscape, plotStyle, warnings);
            layout.CopyFrom(ps);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                layout = layout.LayoutName,
                paper_size = rawPaper,
                canonical_media = summary.CanonicalMedia,
                orientation = summary.Orientation,
                plot_rotation = summary.PlotRotationName,
                paper_width_mm = summary.WidthMm,
                paper_height_mm = summary.HeightMm,
                plot_style = summary.PlotStyle,
                plot_device = summary.PlotDevice,
                scale = "1:1",
                units = "mm",
                warnings = warnings.Count > 0 ? warnings : null,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    internal static PageSetupSummary ApplyPageSetup(
        PlotSettings ps,
        string plotDevice,
        string paperSize,
        bool wantLandscape,
        string plotStyle,
        List<string> warnings)
    {
        PlotSettingsValidator psv = PlotSettingsValidator.Current;
        string resolvedDevice = plotDevice;
        try
        {
            psv.SetPlotConfigurationName(ps, plotDevice, null);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) when (!string.Equals(plotDevice, "DWG To PDF.pc3", StringComparison.OrdinalIgnoreCase))
        {
            resolvedDevice = "DWG To PDF.pc3";
            psv.SetPlotConfigurationName(ps, resolvedDevice, null);
            warnings.Add($"Plot device '{plotDevice}' was unavailable; fell back to 'DWG To PDF.pc3'.");
        }

        psv.RefreshLists(ps);
        StringCollection mediaList = psv.GetCanonicalMediaNameList(ps);
        string resolvedMedia = ResolveCanonicalMedia(mediaList, paperSize, wantLandscape, resolvedDevice);
        psv.SetCanonicalMediaName(ps, resolvedMedia);
        psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
        psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);

        // PlotRotation is relative to the media's native dimensions, not absolute page orientation.
        bool mediaIsLandscape = ps.PlotPaperSize.X >= ps.PlotPaperSize.Y;
        PlotRotation rotation = (wantLandscape == mediaIsLandscape) ? PlotRotation.Degrees000 : PlotRotation.Degrees090;
        psv.SetPlotRotation(ps, rotation);
        psv.SetUseStandardScale(ps, true);
        psv.SetStdScaleType(ps, StdScaleType.StdScale1To1);
        psv.SetPlotOrigin(ps, Point2d.Origin);

        ApplyStyleSheetIfPresent(psv, ps, plotStyle, warnings);
        ps.PlotPlotStyles = true;
        ps.PrintLineweights = true;
        ps.ScaleLineweights = false;

        return SummarizePlotSettings(ps, resolvedDevice);
    }

    internal static void ApplyStyleSheetIfPresent(PlotSettingsValidator psv, PlotSettings ps, string plotStyle, List<string> warnings)
    {
        foreach (string? candidate in psv.GetPlotStyleSheetList())
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(candidate, plotStyle, StringComparison.OrdinalIgnoreCase))
            {
                psv.SetCurrentStyleSheet(ps, candidate);
                return;
            }
        }

        warnings.Add($"Plot style sheet '{plotStyle}' was not found; kept existing style '{ps.CurrentStyleSheet}'.");
    }

    internal static PageSetupSummary SummarizePlotSettings(PlotSettings ps, string? deviceOverride = null)
    {
        bool rotated = ps.PlotRotation is PlotRotation.Degrees090 or PlotRotation.Degrees270;
        double w = rotated ? ps.PlotPaperSize.Y : ps.PlotPaperSize.X;
        double h = rotated ? ps.PlotPaperSize.X : ps.PlotPaperSize.Y;
        return new PageSetupSummary(
            ps.CanonicalMediaName,
            w >= h ? "landscape" : "portrait",
            ps.PlotRotation.ToString(),
            Math.Round(w, 2),
            Math.Round(h, 2),
            ps.CurrentStyleSheet,
            deviceOverride ?? ps.PlotConfigurationName);
    }

    internal static bool IsEffectiveLandscape(PlotSettings ps) =>
        SummarizePlotSettings(ps).Orientation == "landscape";

    internal static string? InferPaperSize(string? mediaName)
    {
        if (string.IsNullOrWhiteSpace(mediaName))
        {
            return null;
        }

        foreach (string size in PlottingPolicy.ValidPaperSizes)
        {
            if (mediaName.Contains($"_{size}_", StringComparison.OrdinalIgnoreCase) ||
                mediaName.Contains($" {size} ", StringComparison.OrdinalIgnoreCase))
            {
                return size;
            }
        }

        return null;
    }

    private static string ResolveCanonicalMedia(StringCollection mediaList, string paperSize, bool wantLandscape, string resolvedDevice)
    {
        string prefix = $"ISO_full_bleed_{paperSize}_";
        string? fallback = null;
        foreach (string? candidate in mediaList)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                fallback ??= candidate;
                if (TryParseMediaOrientation(candidate, out bool isLand) && isLand == wantLandscape)
                {
                    return candidate;
                }
            }
        }

        if (fallback is not null)
        {
            return fallback;
        }

        string altPrefix = $"ISO_{paperSize}_";
        foreach (string? candidate in mediaList)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Contains(altPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new AcadGatewayException("not_found", $"Canonical media for '{paperSize}' not found on plotter '{resolvedDevice}'.");
    }

    private static bool TryParseMediaOrientation(string mediaName, out bool isLandscape)
    {
        isLandscape = false;
        int open = mediaName.IndexOf('(');
        int xIdx = mediaName.IndexOf("_x_", StringComparison.OrdinalIgnoreCase);
        if (open < 0 || xIdx <= open)
        {
            return false;
        }

        string first = mediaName[(open + 1)..xIdx].Trim('_');
        string rest = mediaName[(xIdx + 3)..];
        int unitIdx = rest.IndexOf('_');
        string second = (unitIdx > 0 ? rest[..unitIdx] : rest).Trim(')', '_');

        if (double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
            double.TryParse(second, NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
        {
            isLandscape = w >= h;
            return true;
        }

        return false;
    }
}
