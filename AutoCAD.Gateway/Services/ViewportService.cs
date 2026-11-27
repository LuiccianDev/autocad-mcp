using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Creates native PaperSpace <see cref="Viewport"/> entities with 2D/3D DCS camera configuration.
/// </summary>
public static class ViewportService
{
    private static readonly Dictionary<string, ShadePlotType> ShadePlotMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["as_displayed"] = ShadePlotType.AsDisplayed,
        ["wireframe"] = ShadePlotType.Wireframe,
        ["hidden"] = ShadePlotType.Hidden,
        ["rendered"] = ShadePlotType.Rendered,
    };

    /// <summary>
    /// Creates a native PaperSpace <see cref="Viewport"/> entity with optional 3D DCS target, direction,
    /// visual style, shade plot, and immediate display regeneration.
    /// </summary>
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
        Point3d viewCenter3d = DatabaseHelpers.ReadRequiredPoint3d(root, "view_center", "center_model_x", "center_model_y", "center_model_z");

        root.TryGetProperty("view_direction", out JsonElement dirEl);
        if (!PlottingPolicy.TryParseViewDirectionElement(dirEl, out double dx, out double dy, out double dz, out string? dirErr))
        {
            throw new AcadGatewayException("bad_argument", dirErr ?? "Invalid 'view_direction'.");
        }

        var viewDir = new Vector3d(dx, dy, dz);
        double twistDeg = DatabaseHelpers.GetFiniteDoubleOpt(root, "twist_angle") ?? 0.0;
        ShadePlotType? shadePlot = ParseShadePlot(DatabaseHelpers.GetStringOpt(root, "shade_plot"));
        string? visualStyleName = DatabaseHelpers.GetStringOpt(root, "visual_style");
        bool locked = DatabaseHelpers.GetBoolOpt(root, "lock_viewport")
            ?? DatabaseHelpers.GetBoolOpt(root, "locked")
            ?? true;
        string? layoutName = DatabaseHelpers.GetStringOpt(root, "layout_name");
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            bool isActiveDoc = ReferenceEquals(ctx.Doc, AcApp.DocumentManager.MdiActiveDocument);
            if (isActiveDoc)
            {
                // Keep MAXACTVP >= 64 persistent for the drawing so newly activated PaperSpace viewports remain rendered and plottable.
                int maxActVp = Convert.ToInt32(AcApp.GetSystemVariable("MAXACTVP"), CultureInfo.InvariantCulture);
                if (maxActVp < 64)
                {
                    AcApp.SetSystemVariable("MAXACTVP", (short)64);
                }
            }

            ObjectId spaceId = DatabaseHelpers.ResolvePaperSpaceId(ctx.Db, ctx.Tr, layoutName);
            var paperSpace = (BlockTableRecord)ctx.Tr.GetObject(spaceId, OpenMode.ForWrite);
            string resolvedLayoutName = !paperSpace.LayoutId.IsNull
                ? ((Layout)ctx.Tr.GetObject(paperSpace.LayoutId, OpenMode.ForRead)).LayoutName
                : (layoutName?.Trim() ?? "Layout");

            ObjectId visualStyleId = ObjectId.Null;
            string? resolvedVisualStyle = null;
            if (!string.IsNullOrWhiteSpace(visualStyleName))
            {
                visualStyleId = ResolveVisualStyleId(ctx.Db, ctx.Tr, visualStyleName.Trim(), out resolvedVisualStyle);
            }

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
                    // Setting ViewTarget + ViewDirection with ViewCenter at DCS origin centers 2D and 3D views cleanly.
                    v.ViewTarget = viewCenter3d;
                    v.ViewDirection = viewDir;
                    v.ViewCenter = Point2d.Origin;
                    v.TwistAngle = DatabaseHelpers.ToRadians(twistDeg);
                    v.CustomScale = customScale;
                    if (shadePlot.HasValue)
                    {
                        v.ShadePlot = shadePlot.Value;
                    }

                    if (!visualStyleId.IsNull)
                    {
                        v.VisualStyleId = visualStyleId;
                    }

                    DatabaseHelpers.ValidateAndApplyEntityProperties(v, ctx.Db, ctx.Tr, root);
                });

            vp.On = true;
            vp.UpdateDisplay();
            vp.Locked = locked;

            if (isActiveDoc)
            {
                ctx.Doc.Editor.Regen();
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = vp.ObjectId.Handle.ToString(),
                layout = resolvedLayoutName,
                scale,
                custom_scale = Math.Round(vp.CustomScale, 6),
                view_target = new[] { Math.Round(viewCenter3d.X, 4), Math.Round(viewCenter3d.Y, 4), Math.Round(viewCenter3d.Z, 4) },
                view_direction = new[] { Math.Round(viewDir.X, 4), Math.Round(viewDir.Y, 4), Math.Round(viewDir.Z, 4) },
                twist_angle = twistDeg,
                shade_plot = vp.ShadePlot.ToString(),
                visual_style = resolvedVisualStyle,
                locked,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, requireIdle: true, expectedRevision: expectedRevision, documentName: documentName);
    }

    private static ShadePlotType? ParseShadePlot(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim();
        if (ShadePlotMap.TryGetValue(trimmed, out ShadePlotType mapped))
        {
            return mapped;
        }

        throw new AcadGatewayException("bad_argument", $"Unsupported 'shade_plot' '{raw}'. Expected: as_displayed, wireframe, hidden, rendered.");
    }

    private static ObjectId ResolveVisualStyleId(Database db, Transaction tr, string requestedName, out string resolvedName)
    {
        var dict = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
        var available = new List<string>();
        foreach (DBDictionaryEntry entry in dict)
        {
            available.Add(entry.Key);
            if (string.Equals(entry.Key, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedName = entry.Key;
                return entry.Value;
            }
        }

        throw new AcadGatewayException(
            "not_found",
            $"Visual style '{requestedName}' was not found. Available: {string.Join(", ", available)}.");
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
