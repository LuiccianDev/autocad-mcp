using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Attaches and places <see cref="RasterImage"/> entities in ModelSpace or PaperSpace with <c>IMAGEFRAME</c> control.
/// </summary>
public static class RasterImageService
{
    internal readonly record struct RasterAttachmentResult(
        RasterImage Raster,
        ObjectId ImageDefId,
        double Width,
        double Height);

    /// <summary>Attaches and places a raster image entity in ModelSpace or a PaperSpace layout inside one transaction.</summary>
    public static Task<string> InsertRasterImageAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string rawPath = DatabaseHelpers.GetStringOpt(root, "file_path")
            ?? DatabaseHelpers.GetStringOpt(root, "image_path")
            ?? throw new AcadGatewayException("bad_argument", "'file_path' (or 'image_path') is required.");
        if (!SecurityPolicy.ValidateLocalFilePath(
                rawPath,
                out string normPath,
                out string? pathErr,
                SecurityPolicy.RasterImageExtensions))
        {
            throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid raster image path.");
        }

        DatabaseHelpers.EnsureNoReparsePoints(normPath);
        if (!File.Exists(normPath))
        {
            throw new AcadGatewayException("not_found", $"Raster image file not found: '{normPath}'.");
        }

        Point3d position = DatabaseHelpers.ReadRequiredPoint3d(root, "position", "x", "y", "z");
        double? width = DatabaseHelpers.GetFiniteDoubleOpt(root, "width");
        double? height = DatabaseHelpers.GetFiniteDoubleOpt(root, "height");
        double scale = DatabaseHelpers.GetFiniteDoubleOpt(root, "scale") ?? 1.0;
        double rotationDeg = DatabaseHelpers.GetFiniteDoubleOpt(root, "rotation") ?? 0.0;
        int imageFrame = DatabaseHelpers.GetIntOpt(root, "image_frame") ?? 0;

        if ((width.HasValue && width.Value <= 0) ||
            (height.HasValue && height.Value <= 0) ||
            scale <= 0)
        {
            throw new AcadGatewayException("bad_argument", "'width', 'height', and 'scale' must be greater than zero.");
        }

        if (imageFrame is < 0 or > 2)
        {
            throw new AcadGatewayException("bad_argument", "'image_frame' must be 0 (off), 1 (above), or 2 (visible, no plot).");
        }

        string? layoutName = DatabaseHelpers.GetStringOpt(root, "layout_name");
        if (!SecurityPolicy.TryNormalizeSpace(
                DatabaseHelpers.GetStringOpt(root, "space"),
                layoutName,
                out string canonicalSpace,
                out string? spaceErr))
        {
            throw new AcadGatewayException("bad_argument", spaceErr ?? "'space' must be 'model' or 'paper'.");
        }

        if (!string.IsNullOrWhiteSpace(layoutName) &&
            !PlottingPolicy.TryValidatePaperLayoutName(layoutName, out _, out string? layoutErr))
        {
            throw new AcadGatewayException("bad_argument", layoutErr ?? "'layout_name' must be a valid PaperSpace layout name.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            BlockTableRecord targetSpace = canonicalSpace == "paper" && !string.IsNullOrWhiteSpace(layoutName)
                ? (BlockTableRecord)ctx.Tr.GetObject(
                    DatabaseHelpers.ResolvePaperSpaceId(ctx.Db, ctx.Tr, layoutName),
                    OpenMode.ForWrite)
                : DatabaseHelpers.OpenSpace(ctx.Db, ctx.Tr, canonicalSpace, OpenMode.ForWrite);

            RasterAttachmentResult placed = AttachRasterImageInTransaction(
                ctx.Doc,
                ctx.Db,
                ctx.Tr,
                targetSpace,
                normPath,
                position,
                width,
                height,
                scale,
                rotationDeg,
                imageFrame,
                root);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = placed.Raster.ObjectId.Handle.ToString(),
                image_def_handle = placed.ImageDefId.Handle.ToString(),
                file_path = normPath,
                width = Math.Round(placed.Width, 4),
                height = Math.Round(placed.Height, 4),
                rotation = rotationDeg,
                image_frame = imageFrame,
                layer = placed.Raster.Layer,
                space = canonicalSpace,
                layout = layoutName?.Trim(),
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    internal static RasterAttachmentResult AttachRasterImageInTransaction(
        Autodesk.AutoCAD.ApplicationServices.Document doc,
        Database db,
        Transaction tr,
        BlockTableRecord space,
        string normImagePath,
        Point3d position,
        double? width,
        double? height,
        double scale,
        double rotationDeg,
        int? imageFrame,
        JsonElement? propSpec = null,
        string? explicitLayer = null)
    {
        ObjectId dictId = RasterImageDef.GetImageDictionary(db);
        if (dictId.IsNull)
        {
            dictId = RasterImageDef.CreateImageDictionary(db);
        }

        var imageDict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
        ObjectId imageDefId = ObjectId.Null;
        RasterImageDef? imageDef = null;

        foreach (DBDictionaryEntry entry in imageDict)
        {
            if (tr.GetObject(entry.Value, OpenMode.ForRead, false) is RasterImageDef existing &&
                string.Equals(existing.SourceFileName, normImagePath, StringComparison.OrdinalIgnoreCase))
            {
                imageDefId = entry.Value;
                imageDef = existing;
                break;
            }
        }

        if (imageDef is null)
        {
            var newDef = new RasterImageDef();
            bool addedDef = false;
            try
            {
                newDef.SourceFileName = normImagePath;
                newDef.Load();

                string baseKey = SanitizeImageDictKey(Path.GetFileNameWithoutExtension(normImagePath));
                string uniqueKey = baseKey;
                int suffix = 1;
                while (imageDict.Contains(uniqueKey))
                {
                    uniqueKey = $"{baseKey}_{suffix++}";
                }

                imageDefId = imageDict.SetAt(uniqueKey, newDef);
                tr.AddNewlyCreatedDBObject(newDef, true);
                addedDef = true;
                imageDef = newDef;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new AcadGatewayException("bad_argument", $"Failed to load raster image '{normImagePath}': {ex.Message}");
            }
            finally
            {
                if (!addedDef)
                {
                    newDef.Dispose();
                }
            }
        }
        else
        {
            // AssociateRasterDef attaches a persistent reactor onto imageDef, requiring ForWrite even if already loaded.
            if (!imageDef.IsWriteEnabled)
            {
                imageDef.UpgradeOpen();
            }

            if (!imageDef.IsLoaded)
            {
                imageDef.Load();
            }
        }

        Vector2d pxSize = imageDef.Size;
        double pxW = pxSize.X > 0 ? pxSize.X : 1.0;
        double pxH = pxSize.Y > 0 ? pxSize.Y : 1.0;
        double aspect = pxH / pxW;

        double finalWidth;
        double finalHeight;
        if (width.HasValue && height.HasValue)
        {
            finalWidth = width.Value;
            finalHeight = height.Value;
        }
        else if (width.HasValue)
        {
            finalWidth = width.Value;
            finalHeight = finalWidth * aspect;
        }
        else if (height.HasValue)
        {
            finalHeight = height.Value;
            finalWidth = finalHeight / aspect;
        }
        else
        {
            // Keep default placement in drawing units so a 1000px image defaults to scale drawing units wide.
            finalWidth = scale;
            finalHeight = scale * aspect;
        }

        double rotRad = DatabaseHelpers.ToRadians(rotationDeg);
        Vector3d uVec = new Vector3d(finalWidth, 0.0, 0.0).RotateBy(rotRad, Vector3d.ZAxis);
        Vector3d vVec = new Vector3d(0.0, finalHeight, 0.0).RotateBy(rotRad, Vector3d.ZAxis);

        var raster = new RasterImage();
        bool addedRaster = false;
        try
        {
            raster.SetDatabaseDefaults(db);
            raster.ImageDefId = imageDefId;
            if (propSpec.HasValue)
            {
                DatabaseHelpers.ValidateAndApplyEntityProperties(raster, db, tr, propSpec.Value);
            }

            if (!string.IsNullOrWhiteSpace(explicitLayer))
            {
                DatabaseHelpers.EnsureLayer(db, tr, explicitLayer);
                raster.Layer = explicitLayer.Trim();
            }

            raster.Orientation = new CoordinateSystem3d(position, uVec, vVec);
            space.AppendEntity(raster);
            tr.AddNewlyCreatedDBObject(raster, true);
            addedRaster = true;

            // EnableReactors and AssociateRasterDef require the RasterImage to already be database-resident.
            RasterImage.EnableReactors(true);
            raster.AssociateRasterDef(imageDef);
            raster.ShowImage = true;
            raster.DisplayOptions = ImageDisplayOptions.Show | ImageDisplayOptions.ShowUnaligned;
        }
        finally
        {
            if (!addedRaster)
            {
                raster.Dispose();
            }
        }

        ApplyImageFrameSetting(doc, db, tr, imageFrame);
        return new RasterAttachmentResult(raster, imageDefId, finalWidth, finalHeight);
    }

    private static string SanitizeImageDictKey(string rawStem)
    {
        if (string.IsNullOrWhiteSpace(rawStem))
        {
            return "RASTER_IMAGE";
        }

        Span<char> buffer = stackalloc char[Math.Min(rawStem.Length, 48)];
        int idx = 0;
        foreach (char ch in rawStem)
        {
            if (idx >= buffer.Length)
            {
                break;
            }

            buffer[idx++] = char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_';
        }

        return idx > 0 ? new string(buffer[..idx]) : "RASTER_IMAGE";
    }

    private static void ApplyImageFrameSetting(
        Autodesk.AutoCAD.ApplicationServices.Document doc,
        Database db,
        Transaction tr,
        int? requestedFrameMode)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        const string varsKey = "ACAD_IMAGE_VARS";

        if (!requestedFrameMode.HasValue && nod.Contains(varsKey))
        {
            return;
        }

        int effectiveMode = requestedFrameMode ?? 0;
        FrameSetting setting = effectiveMode switch
        {
            0 => FrameSetting.ImageFrameOff,
            2 => FrameSetting.ImageFrameOnNoPlot,
            _ => FrameSetting.ImageFrameAbove,
        };

        if (nod.Contains(varsKey))
        {
            if (tr.GetObject(nod.GetAt(varsKey), OpenMode.ForWrite, false) is RasterVariables rv)
            {
                rv.ImageFrame = setting;
            }
        }
        else
        {
            nod.UpgradeOpen();
            var rv = new RasterVariables { ImageFrame = setting };
            bool added = false;
            try
            {
                nod.SetAt(varsKey, rv);
                tr.AddNewlyCreatedDBObject(rv, true);
                added = true;
            }
            finally
            {
                if (!added)
                {
                    rv.Dispose();
                }
            }
        }

        if (requestedFrameMode.HasValue && ReferenceEquals(doc, AcApp.DocumentManager.MdiActiveDocument))
        {
            try
            {
                AcApp.SetSystemVariable("IMAGEFRAME", (short)effectiveMode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Best-effort IMAGEFRAME sync failed: {0}", ex.Message);
            }
        }
    }
}
