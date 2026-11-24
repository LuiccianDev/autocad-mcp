using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Internal builder constructing native AutoCAD entities and block attributes inside active write transactions.
/// </summary>
internal static class EntityBuilder
{
    internal static string CreateEntityInTransaction(
        Database db,
        Transaction tr,
        JsonElement spec,
        string? explicitType,
        string fallbackLayer = "0",
        BlockTableRecord? targetSpace = null)
    {
        string entityType = (explicitType ?? string.Empty).Trim().ToLowerInvariant();
        if (entityType.Length == 0)
        {
            throw new AcadGatewayException("bad_argument", "Entity 'type' is required.");
        }

        BlockTableRecord space = targetSpace ?? DatabaseHelpers.OpenSpace(db, tr, DatabaseHelpers.GetStringOpt(spec, "space"), OpenMode.ForWrite);
        Entity created = BuildEntity(db, tr, spec, entityType);
        bool added = false;

        try
        {
            created.SetDatabaseDefaults(db);

            string? explicitLayer = DatabaseHelpers.GetStringOpt(spec, "layer");
            if (string.IsNullOrWhiteSpace(explicitLayer) && !string.Equals(fallbackLayer.Trim(), "0", StringComparison.OrdinalIgnoreCase))
            {
                DatabaseHelpers.EnsureLayer(db, tr, fallbackLayer);
                created.Layer = fallbackLayer.Trim();
            }

            DatabaseHelpers.ValidateAndApplyEntityProperties(created, db, tr, spec);

            ObjectId id = DatabaseHelpers.AppendEntity(space, tr, created);
            added = true;

            if (created is BlockReference blockRef)
            {
                AppendBlockAttributesIfAny(tr, blockRef, spec);
            }

            return id.Handle.ToString();
        }
        finally
        {
            if (!added)
            {
                created.Dispose();
            }
        }
    }

    private static Entity BuildEntity(Database db, Transaction tr, JsonElement spec, string entityType)
    {
        return entityType switch
        {
            "line" => BuildLine(spec),
            "polyline" or "lwpolyline" => BuildPolyline(spec),
            "rectangle" or "rect" => BuildRectangle(spec),
            "circle" => BuildCircle(spec),
            "arc" => BuildArc(spec),
            "text" or "dbtext" => BuildDbText(spec),
            "mtext" => BuildMText(spec),
            "block" or "block_ref" or "insert" => BuildBlockReference(db, tr, spec),
            _ => throw new AcadGatewayException("bad_argument", $"Unsupported entity type '{entityType}'."),
        };
    }

    private static Line BuildLine(JsonElement spec)
    {
        Point3d p1 = DatabaseHelpers.ReadRequiredPoint3d(spec, "start", "x1", "y1", "z1");
        Point3d p2 = DatabaseHelpers.ReadRequiredPoint3d(spec, "end", "x2", "y2", "z2");
        if (p1.DistanceTo(p2) < 1e-12)
        {
            throw new AcadGatewayException("bad_argument", "Line start and end points cannot be identical.");
        }

        return new Line(p1, p2);
    }

    private static Polyline BuildPolyline(JsonElement spec)
    {
        if (!spec.TryGetProperty("points", out JsonElement ptsEl) || ptsEl.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", "Polyline requires a 'points' array of vertices.");
        }

        int vertCount = ptsEl.GetArrayLength();
        if (vertCount < 2 || vertCount > SecurityPolicy.MaxGeometryVertices)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"Polyline requires 'points' array with 2..{SecurityPolicy.MaxGeometryVertices} vertices.");
        }

        var pl = new Polyline();
        try
        {
            int idx = 0;
            foreach (JsonElement ptEl in ptsEl.EnumerateArray())
            {
                if (ptEl.ValueKind != JsonValueKind.Array || ptEl.GetArrayLength() < 2)
                {
                    throw new AcadGatewayException("bad_argument", "Each polyline vertex must be [x, y] or [x, y, bulge].");
                }

                double x = ReadArrayNum(ptEl, 0, "points");
                double y = ReadArrayNum(ptEl, 1, "points");
                double bulge = ptEl.GetArrayLength() >= 3 ? ReadArrayNum(ptEl, 2, "points") : 0.0;
                pl.AddVertexAt(idx++, new Point2d(x, y), bulge, 0, 0);
            }

            pl.Closed = DatabaseHelpers.GetBoolOpt(spec, "closed") ?? false;
            return pl;
        }
        catch
        {
            pl.Dispose();
            throw;
        }
    }

    private static Polyline BuildRectangle(JsonElement spec)
    {
        Point3d c1 = DatabaseHelpers.ReadRequiredPoint3d(spec, "min", "x1", "y1");
        Point3d c2 = DatabaseHelpers.ReadRequiredPoint3d(spec, "max", "x2", "y2");
        if (Math.Abs(c1.X - c2.X) < 1e-12 || Math.Abs(c1.Y - c2.Y) < 1e-12)
        {
            throw new AcadGatewayException("bad_argument", "Rectangle corners must differ in both X and Y.");
        }

        var rect = new Polyline(4);
        rect.AddVertexAt(0, new Point2d(c1.X, c1.Y), 0, 0, 0);
        rect.AddVertexAt(1, new Point2d(c2.X, c1.Y), 0, 0, 0);
        rect.AddVertexAt(2, new Point2d(c2.X, c2.Y), 0, 0, 0);
        rect.AddVertexAt(3, new Point2d(c1.X, c2.Y), 0, 0, 0);
        rect.Closed = true;
        return rect;
    }

    private static Circle BuildCircle(JsonElement spec)
    {
        Point3d center = DatabaseHelpers.ReadRequiredPoint3d(spec, "center", "center_x", "center_y", "center_z");
        double radius = DatabaseHelpers.GetRequiredFiniteDouble(spec, "radius");
        if (radius <= 0)
        {
            throw new AcadGatewayException("bad_argument", "Circle radius must be greater than zero.");
        }

        return new Circle(center, Vector3d.ZAxis, radius);
    }

    private static Arc BuildArc(JsonElement spec)
    {
        Point3d center = DatabaseHelpers.ReadRequiredPoint3d(spec, "center", "center_x", "center_y", "center_z");
        double radius = DatabaseHelpers.GetRequiredFiniteDouble(spec, "radius");
        double startDeg = DatabaseHelpers.GetRequiredFiniteDouble(spec, "start_angle");
        double endDeg = DatabaseHelpers.GetRequiredFiniteDouble(spec, "end_angle");
        if (radius <= 0)
        {
            throw new AcadGatewayException("bad_argument", "Arc radius must be greater than zero.");
        }

        double sweepDiff = Math.Abs(endDeg - startDeg) % 360.0;
        if (Math.Abs(endDeg - startDeg) < 1e-9 || sweepDiff < 1e-9)
        {
            throw new AcadGatewayException("bad_argument", "Arc start_angle and end_angle must differ.");
        }

        return new Arc(center, Vector3d.ZAxis, radius, DatabaseHelpers.ToRadians(startDeg), DatabaseHelpers.ToRadians(endDeg));
    }

    private static DBText BuildDbText(JsonElement spec)
    {
        string content = DatabaseHelpers.GetRequiredString(spec, "text");
        Point3d pos = DatabaseHelpers.ReadRequiredPoint3d(spec, "position", "x", "y", "z");
        double height = DatabaseHelpers.GetFiniteDoubleOpt(spec, "height") ?? 0.25;
        if (height <= 0)
        {
            throw new AcadGatewayException("bad_argument", "Text height must be greater than zero.");
        }

        double rotationDeg = DatabaseHelpers.GetFiniteDoubleOpt(spec, "rotation") ?? 0.0;
        return new DBText
        {
            TextString = content,
            Position = pos,
            Height = height,
            Rotation = DatabaseHelpers.ToRadians(rotationDeg),
        };
    }

    private static MText BuildMText(JsonElement spec)
    {
        string content = DatabaseHelpers.GetRequiredString(spec, "text");
        Point3d pos = DatabaseHelpers.ReadRequiredPoint3d(spec, "position", "x", "y", "z");
        double height = DatabaseHelpers.GetFiniteDoubleOpt(spec, "height") ?? 0.25;
        double width = DatabaseHelpers.GetFiniteDoubleOpt(spec, "width") ?? 0.0;
        double rotationDeg = DatabaseHelpers.GetFiniteDoubleOpt(spec, "rotation") ?? 0.0;
        if (height <= 0 || width < 0)
        {
            throw new AcadGatewayException("bad_argument", "MText height must be > 0 and width must be >= 0.");
        }

        return new MText
        {
            Contents = content,
            Location = pos,
            TextHeight = height,
            Width = width,
            Rotation = DatabaseHelpers.ToRadians(rotationDeg),
        };
    }

    private static BlockReference BuildBlockReference(Database db, Transaction tr, JsonElement spec)
    {
        string? rawBlockName = DatabaseHelpers.GetStringOpt(spec, "block_name");
        string blockName = !string.IsNullOrWhiteSpace(rawBlockName)
            ? rawBlockName.Trim()
            : DatabaseHelpers.GetRequiredString(spec, "name");
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(blockName))
        {
            throw new AcadGatewayException("not_found", $"Block definition '{blockName}' does not exist in this drawing.");
        }

        Point3d pos = DatabaseHelpers.ReadRequiredPoint3d(spec, "position", "x", "y", "z");
        double scale = DatabaseHelpers.GetFiniteDoubleOpt(spec, "scale") ?? 1.0;
        if (scale <= 0)
        {
            throw new AcadGatewayException("bad_argument", "Block scale factor must be greater than zero.");
        }

        double rotationDeg = DatabaseHelpers.GetFiniteDoubleOpt(spec, "rotation") ?? 0.0;
        return new BlockReference(pos, bt[blockName])
        {
            ScaleFactors = new Scale3d(scale),
            Rotation = DatabaseHelpers.ToRadians(rotationDeg),
        };
    }

    private static void AppendBlockAttributesIfAny(Transaction tr, BlockReference blockRef, JsonElement spec)
    {
        var btr = (BlockTableRecord)tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
        if (!btr.HasAttributeDefinitions)
        {
            return;
        }

        Dictionary<string, string>? overrides = null;
        if (spec.TryGetProperty("attributes", out JsonElement attrsEl) && attrsEl.ValueKind != JsonValueKind.Null)
        {
            overrides = DatabaseHelpers.ReadStringDictionary(attrsEl, "attributes");
        }

        foreach (ObjectId objId in btr)
        {
            if (tr.GetObject(objId, OpenMode.ForRead, false) is not AttributeDefinition attDef || attDef.Constant)
            {
                continue;
            }

            var attRef = new AttributeReference();
            try
            {
                attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                if (overrides is not null && overrides.TryGetValue(attDef.Tag, out string? customValue))
                {
                    attRef.TextString = customValue;
                }
            }
            catch
            {
                attRef.Dispose();
                throw;
            }

            DatabaseHelpers.AppendAttribute(blockRef, tr, attRef);
        }
    }

    private static double ReadArrayNum(JsonElement arrayEl, int index, string field)
    {
        if (index < 0 || index >= arrayEl.GetArrayLength())
        {
            throw new AcadGatewayException("bad_argument", $"Missing coordinate index {index} in '{field}'.");
        }

        JsonElement item = arrayEl[index];
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double d) || !double.IsFinite(d))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid non-finite coordinate in '{field}'.");
        }

        return d;
    }
}
