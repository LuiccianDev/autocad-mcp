using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Builds native parametric PaperSpace engineering title blocks with configurable templates and optional raster logo.
/// </summary>
public static class TitleBlockBuilder
{
    /// <summary>
    /// Draws a native parametric engineering title block in PaperSpace with configurable templates and optional raster logo.
    /// </summary>
    public static Task<string> InsertTitleBlockAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        double cornerX = DatabaseHelpers.GetRequiredFiniteDouble(root, "corner_x");
        double cornerY = DatabaseHelpers.GetRequiredFiniteDouble(root, "corner_y");
        string projectTitle = (DatabaseHelpers.GetStringOpt(root, "project_title") ?? "ENGINEERING PROJECT").Trim();
        string drawingName = (DatabaseHelpers.GetStringOpt(root, "drawing_name") ?? "GENERAL PLAN").Trim();
        string sheetCode = (DatabaseHelpers.GetStringOpt(root, "sheet_code") ?? "L-01").Trim();
        string scaleText = (DatabaseHelpers.GetStringOpt(root, "scale_text") ?? "1:1000").Trim();
        string author = (DatabaseHelpers.GetStringOpt(root, "author") ?? "ENGINEERING").Trim();
        string rawDate = DatabaseHelpers.GetStringOpt(root, "date") ?? string.Empty;
        string finalDate = string.IsNullOrWhiteSpace(rawDate)
            ? DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : rawDate.Trim();

        foreach (string field in new[] { projectTitle, drawingName, sheetCode, scaleText, author, finalDate })
        {
            if (SecurityPolicy.HasControlChars(field))
            {
                throw new AcadGatewayException("bad_argument", "Title block text fields cannot contain control characters or newlines.");
            }
        }

        string template = (DatabaseHelpers.GetStringOpt(root, "template") ?? "iso_standard").Trim().ToLowerInvariant();
        (double boxW, double boxH, double rightColW) = template switch
        {
            "iso_standard" => (180.0, 45.0, 60.0),
            "compact" => (140.0, 36.0, 48.0),
            "detailed" => (210.0, 55.0, 70.0),
            _ => throw new AcadGatewayException("bad_argument", $"Unsupported title block template '{template}'. Expected: iso_standard, compact, detailed."),
        };

        string? rawLogoPath = DatabaseHelpers.GetStringOpt(root, "logo_path");
        string? normLogoPath = null;
        if (!string.IsNullOrWhiteSpace(rawLogoPath))
        {
            if (!SecurityPolicy.ValidateLocalFilePath(
                    rawLogoPath,
                    out string validatedLogo,
                    out string? logoErr,
                    SecurityPolicy.RasterImageExtensions))
            {
                throw new AcadGatewayException("invalid_path", logoErr ?? "Invalid logo image path.");
            }

            DatabaseHelpers.EnsureNoReparsePoints(validatedLogo);
            if (!File.Exists(validatedLogo))
            {
                throw new AcadGatewayException("not_found", $"Logo image file not found: '{validatedLogo}'.");
            }

            normLogoPath = validatedLogo;
        }

        string? layoutName = DatabaseHelpers.GetStringOpt(root, "layout_name");
        if (!string.IsNullOrWhiteSpace(layoutName) &&
            !PlottingPolicy.TryValidatePaperLayoutName(layoutName, out _, out string? layoutErr))
        {
            throw new AcadGatewayException("bad_argument", layoutErr ?? "'layout_name' must be a valid PaperSpace layout name.");
        }

        string layerName = (DatabaseHelpers.GetStringOpt(root, "layer") ?? "FORMAT_TITLEBLOCK").Trim();
        if (!SecurityPolicy.IsValidSymbolName(layerName))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid layer name '{layerName}'.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            ObjectId layerId = DatabaseHelpers.EnsureLayer(ctx.Db, ctx.Tr, layerName);
            if (string.Equals(layerName, "FORMAT_TITLEBLOCK", StringComparison.OrdinalIgnoreCase))
            {
                var ltr = (LayerTableRecord)ctx.Tr.GetObject(layerId, OpenMode.ForWrite);
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 7);
            }

            ObjectId spaceId = DatabaseHelpers.ResolvePaperSpaceId(ctx.Db, ctx.Tr, layoutName);
            var paperSpace = (BlockTableRecord)ctx.Tr.GetObject(spaceId, OpenMode.ForWrite);
            string resolvedLayoutName = !paperSpace.LayoutId.IsNull
                ? ((Layout)ctx.Tr.GetObject(paperSpace.LayoutId, OpenMode.ForRead)).LayoutName
                : (layoutName?.Trim() ?? "Layout");

            double x0 = cornerX - boxW;
            double y0 = cornerY;
            double x1 = cornerX;
            double y1 = cornerY + boxH;
            double logoColW = normLogoPath is not null ? (template == "compact" ? 28.0 : 36.0) : 0.0;
            double textX = x0 + logoColW + 4.0;
            double row1Y = y0 + boxH * 0.56;
            double row2Y = y0 + boxH * 0.27;
            double rightColX = x1 - rightColW;

            var handles = new List<string>(12);

            var border = new Polyline { Closed = true };
            border.SetDatabaseDefaults(ctx.Db);
            border.Layer = layerName;
            border.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            border.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            border.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            border.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            handles.Add(DatabaseHelpers.AppendEntity(paperSpace, ctx.Tr, border).Handle.ToString());

            void AddLine(double ax, double ay, double bx, double by)
            {
                var line = new Line(new Point3d(ax, ay, 0), new Point3d(bx, by, 0));
                line.SetDatabaseDefaults(ctx.Db);
                line.Layer = layerName;
                handles.Add(DatabaseHelpers.AppendEntity(paperSpace, ctx.Tr, line).Handle.ToString());
            }

            void AddText(double tx, double ty, double textH, string content)
            {
                var txt = new DBText
                {
                    Position = new Point3d(tx, ty, 0),
                    Height = textH,
                    TextString = content,
                };
                txt.SetDatabaseDefaults(ctx.Db);
                txt.Layer = layerName;
                handles.Add(DatabaseHelpers.AppendEntity(paperSpace, ctx.Tr, txt).Handle.ToString());
            }

            AddLine(x0 + logoColW, row1Y, x1, row1Y);
            AddLine(x0 + logoColW, row2Y, x1, row2Y);
            AddLine(rightColX, y0, rightColX, row1Y);
            if (logoColW > 0)
            {
                AddLine(x0 + logoColW, y0, x0 + logoColW, y1);
            }

            double scaleTextF = template == "compact" ? 0.85 : 1.0;
            AddText(textX, y0 + boxH * 0.78, 3.5 * scaleTextF, $"PROJECT: {projectTitle.ToUpperInvariant()}");
            AddText(textX, y0 + boxH * 0.62, 3.0 * scaleTextF, $"DRAWING: {drawingName.ToUpperInvariant()}");
            AddText(textX, y0 + boxH * 0.36, 2.5 * scaleTextF, $"AUTHOR / DISCIPLINE: {author.ToUpperInvariant()}");
            AddText(textX, y0 + boxH * 0.10, 2.5 * scaleTextF, $"DATE: {finalDate}");
            AddText(rightColX + 4.0, y0 + boxH * 0.36, 2.5 * scaleTextF, $"SCALE: {scaleText}");
            AddText(rightColX + 4.0, y0 + boxH * 0.10, 4.2 * scaleTextF, $"SHEET: {sheetCode.ToUpperInvariant()}");

            string? logoHandle = null;
            if (normLogoPath is not null)
            {
                const double pad = 2.0;
                double availW = Math.Max(4.0, logoColW - pad * 2.0);
                double availH = Math.Max(4.0, boxH - pad * 2.0);
                RasterImageService.RasterAttachmentResult placed = RasterImageService.AttachRasterImageInTransaction(
                    ctx.Doc,
                    ctx.Db,
                    ctx.Tr,
                    paperSpace,
                    normLogoPath,
                    new Point3d(x0 + pad, y0 + pad, 0.0),
                    availW,
                    null,
                    1.0,
                    0.0,
                    imageFrame: null,
                    explicitLayer: layerName);

                // Re-fit inside cell height if tall logo overflows availH.
                if (placed.Height > availH)
                {
                    double shrink = availH / placed.Height;
                    Vector3d u = placed.Raster.Orientation.Xaxis * shrink;
                    Vector3d v = placed.Raster.Orientation.Yaxis * shrink;
                    double ox = x0 + (logoColW - u.Length) * 0.5;
                    double oy = y0 + (boxH - v.Length) * 0.5;
                    placed.Raster.Orientation = new CoordinateSystem3d(new Point3d(ox, oy, 0.0), u, v);
                }

                logoHandle = placed.Raster.ObjectId.Handle.ToString();
                handles.Add(logoHandle);
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handles,
                logo_handle = logoHandle,
                count = handles.Count,
                template,
                layout = resolvedLayoutName,
                layer = layerName,
                bounds = new
                {
                    min_x = Math.Round(x0, 2),
                    min_y = Math.Round(y0, 2),
                    max_x = Math.Round(x1, 2),
                    max_y = Math.Round(y1, 2),
                },
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }
}
