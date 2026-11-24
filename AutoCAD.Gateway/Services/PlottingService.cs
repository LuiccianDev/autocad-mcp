using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Safe native PaperSpace viewport creation and headless PDF plotting service
/// with synchronous <c>BACKGROUNDPLOT=0</c> restoration.
/// </summary>
public static class PlottingService
{
    /// <summary>
    /// Creates a native PaperSpace <see cref="Viewport"/> entity inside a target or active PaperSpace layout
    /// with exact metric scale (<c>1000 / scale</c>).
    /// </summary>
    /// <param name="body">JSON payload containing viewport bounds/center, scale, model view center, and optional layout/document.</param>
    /// <returns>Serialized JSON creation confirmation with handle and revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when arguments are invalid, Edit Guard is locked, or the layout does not exist.</exception>
    public static Task<string> CreateViewportAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        double width = DatabaseHelpers.GetRequiredFiniteDouble(root, "width");
        double height = DatabaseHelpers.GetRequiredFiniteDouble(root, "height");
        double scale = DatabaseHelpers.GetRequiredFiniteDouble(root, "scale");
        double customScale = 1000.0 / scale;
        if (width <= 0 || height <= 0 || scale <= 0 || !double.IsFinite(customScale))
        {
            throw new AcadGatewayException("bad_argument", "'width', 'height', and metric 'scale' must yield finite positive geometry.");
        }

        Point3d center = DatabaseHelpers.ReadPoint3dOpt(root, "center")
            ?? ResolveCenterFromLowerCorner(root, width, height);
        Point3d viewCenter3d = DatabaseHelpers.ReadRequiredPoint3d(root, "view_center", "center_model_x", "center_model_y");
        bool locked = DatabaseHelpers.GetBoolOpt(root, "locked") ?? true;
        string? layoutName = DatabaseHelpers.GetStringOpt(root, "layout_name");
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            ObjectId spaceId = DatabaseHelpers.ResolvePaperSpaceId(ctx.Db, ctx.Tr, layoutName);
            var paperSpace = (BlockTableRecord)ctx.Tr.GetObject(spaceId, OpenMode.ForWrite);

            Viewport vp = DatabaseHelpers.CreateAndAppend(
                paperSpace,
                ctx.Tr,
                ctx.Db,
                static () => new Viewport(),
                v =>
                {
                    v.CenterPoint = center;
                    v.Width = width;
                    v.Height = height;
                    v.ViewCenter = new Point2d(viewCenter3d.X, viewCenter3d.Y);
                    v.CustomScale = customScale;
                    DatabaseHelpers.ValidateAndApplyEntityProperties(v, ctx.Db, ctx.Tr, root);
                });

            vp.On = true;
            vp.Locked = locked;

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = vp.ObjectId.Handle.ToString(),
                scale,
                custom_scale = vp.CustomScale,
                locked = vp.Locked,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    /// <summary>
    /// Plots an existing PaperSpace layout synchronously to a local PDF file while forcing and restoring
    /// <c>BACKGROUNDPLOT = 0</c> inside <c>MainThread.InCommandContextAsync</c>.
    /// </summary>
    /// <param name="body">JSON payload containing <c>layout_name</c>, <c>pdf_path</c>, and optional <c>plot_style</c>, <c>document</c>, <c>expected_revision</c>.</param>
    /// <returns>Serialized JSON result confirming physical PDF output on disk.</returns>
    /// <exception cref="AcadGatewayException">Thrown on invalid paths, blocked prompt tokens, inactive target document, revision mismatch, or missing output file.</exception>
    public static Task<string> PlotPdfAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string layoutName = DatabaseHelpers.GetRequiredString(root, "layout_name");
        if (!SecurityPolicy.IsSafeLayoutName(layoutName, allowSpaces: true))
        {
            throw new AcadGatewayException("bad_argument", "'layout_name' must be a valid AutoCAD layout name free of prompt/control characters.");
        }

        if (string.Equals(layoutName.Trim(), "Model", StringComparison.OrdinalIgnoreCase))
        {
            throw new AcadGatewayException("bad_argument", "Export PDF requires a PaperSpace layout name; 'Model' is not supported.");
        }

        string rawPdf = DatabaseHelpers.GetRequiredString(root, "pdf_path");
        if (!SecurityPolicy.ValidateLocalFilePath(rawPdf, out string normPdf, out string? pathErr, ".pdf"))
        {
            throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid PDF output path.");
        }

        DatabaseHelpers.EnsureNoReparsePoints(normPdf);

        string plotStyle = (DatabaseHelpers.GetStringOpt(root, "plot_style") ?? "monochrome.ctb").Trim();
        if (!SecurityPolicy.IsSafePlotStyleName(plotStyle, allowSpaces: true))
        {
            throw new AcadGatewayException(
                "bad_argument",
                "Plot style must be a safe .ctb or .stb filename without path separators, UNC prefixes, or prompt characters.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return MainThread.InCommandContextAsync(() =>
        {
            Document doc = Acad.ActiveDocument;
            if (!string.IsNullOrWhiteSpace(documentName))
            {
                string target = documentName.Trim();
                bool matches = string.Equals(doc.Name, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(doc.Name), target, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    throw new AcadGatewayException(
                        "bad_argument",
                        $"PDF plotting requires document '{target}' to be active; call activate_document first.");
                }
            }

            DocumentSession session = DocumentSessionManager.GetOrCreate(doc);
            Acad.ValidateWritable(doc, session, requireIdle: false);

            if (expectedRevision.HasValue && expectedRevision.Value != session.Revision)
            {
                throw new AcadGatewayException(
                    "revision_conflict",
                    $"Document revision mismatch: expected {expectedRevision.Value}, current is {session.Revision}.");
            }

            using (var tr = doc.TransactionManager.StartOpenCloseTransaction())
            {
                var layoutDict = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
                if (!layoutDict.Contains(layoutName))
                {
                    tr.Abort();
                    throw new AcadGatewayException("not_found", $"Layout '{layoutName}' does not exist in active drawing.");
                }

                tr.Abort();
            }

            string? parentDir = Path.GetDirectoryName(normPdf);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            string tmpPdf = Path.Combine(
                parentDir ?? Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(normPdf)}.{Guid.NewGuid():N}.tmp.pdf");

            short prevBgPlot = Convert.ToInt16(AcApp.GetSystemVariable("BACKGROUNDPLOT"));
            AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
            try
            {
                LayoutManager.Current.CurrentLayout = layoutName;
                string fwdTmpPdf = tmpPdf.Replace('\\', '/');
                doc.Editor.Command(
                    "_.-PLOT", "Y", layoutName, "DWG To PDF.pc3",
                    string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty,
                    "Y", plotStyle, "Y", "N", "N", "N",
                    fwdTmpPdf, "N", "Y");
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

            try
            {
                if (!File.Exists(tmpPdf) || new FileInfo(tmpPdf).Length == 0)
                {
                    throw new AcadGatewayException(
                        "autocad_error",
                        $"AutoCAD did not generate a PDF file for layout '{layoutName}'. Verify plot style '{plotStyle}' and PC3 configuration.");
                }

                File.Move(tmpPdf, normPdf, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPdf))
                {
                    try
                    {
                        File.Delete(tmpPdf);
                    }
                    catch (Exception cleanupEx)
                    {
                        System.Diagnostics.Trace.TraceWarning("Failed to clean up temporary PDF '{0}': {1}", tmpPdf, cleanupEx);
                    }
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                layout = layoutName,
                pdf_path = normPdf,
                file_exists = File.Exists(normPdf),
                document = doc.Name,
                revision = session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, preflight: () => Acad.EnsureQuiescent(Acad.ActiveDocument), timeoutMs: 60_000);
    }

    private static Point3d ResolveCenterFromLowerCorner(JsonElement root, double width, double height)
    {
        double lx = DatabaseHelpers.GetRequiredFiniteDouble(root, "lower_x");
        double ly = DatabaseHelpers.GetRequiredFiniteDouble(root, "lower_y");
        double cx = lx + width * 0.5;
        double cy = ly + height * 0.5;
        if (!double.IsFinite(cx) || !double.IsFinite(cy))
        {
            throw new AcadGatewayException("bad_argument", "Viewport center coordinates overflowed.");
        }

        return new Point3d(cx, cy, 0.0);
    }
}
