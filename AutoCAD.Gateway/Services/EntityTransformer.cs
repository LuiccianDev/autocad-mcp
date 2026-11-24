using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Internal geometric transform and bulk deletion helpers executing inside write transactions.
/// </summary>
internal static class EntityTransformer
{
    internal static List<string> TransformEntitiesInTransaction(Database db, Transaction tr, JsonElement spec)
    {
        List<ObjectId> ids = DatabaseHelpers.ResolveHandles(db, spec);
        string op = DatabaseHelpers.GetRequiredString(spec, "operation").ToLowerInvariant();
        bool eraseSource = DatabaseHelpers.GetBoolOpt(spec, "erase_source") ?? false;

        Matrix3d matrix;
        bool cloneEntities = false;

        switch (op)
        {
            case "move":
            case "copy":
            {
                Vector3d delta = ReadDeltaVector(spec);
                matrix = Matrix3d.Displacement(delta);
                cloneEntities = op == "copy";
                break;
            }

            case "rotate":
            {
                Point3d basePt = DatabaseHelpers.ReadRequiredPoint3d(spec, "base_point", "base_x", "base_y", "base_z");
                double angleDeg = DatabaseHelpers.GetRequiredFiniteDouble(spec, "angle");
                matrix = Matrix3d.Rotation(DatabaseHelpers.ToRadians(angleDeg), Vector3d.ZAxis, basePt);
                break;
            }

            case "scale":
            {
                Point3d basePt = DatabaseHelpers.ReadRequiredPoint3d(spec, "base_point", "base_x", "base_y", "base_z");
                double factor = DatabaseHelpers.GetRequiredFiniteDouble(spec, "factor");
                if (factor <= 0)
                {
                    throw new AcadGatewayException("bad_argument", "Scale factor must be greater than zero.");
                }

                matrix = Matrix3d.Scaling(factor, basePt);
                break;
            }

            case "mirror":
            {
                Point3d p1 = DatabaseHelpers.ReadRequiredPoint3d(spec, "p1", "p1_x", "p1_y");
                Point3d p2 = DatabaseHelpers.ReadRequiredPoint3d(spec, "p2", "p2_x", "p2_y");
                if (p1.DistanceTo(p2) < 1e-12)
                {
                    throw new AcadGatewayException("bad_argument", "Mirror axis points p1 and p2 must be distinct.");
                }

                using var axisLine = new Line3d(p1, p2);
                matrix = Matrix3d.Mirroring(axisLine);
                cloneEntities = !eraseSource;
                break;
            }

            default:
                throw new AcadGatewayException("bad_argument", $"Unsupported transform operation '{op}'. Expected move, rotate, scale, copy, or mirror.");
        }

        var resultHandles = new List<string>(ids.Count);
        foreach (ObjectId id in ids)
        {
            if (cloneEntities)
            {
                resultHandles.Add(CloneAndTransform(tr, id, matrix));
            }
            else
            {
                if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent)
                {
                    throw new AcadGatewayException("bad_argument", $"Handle '{id.Handle}' is not a mutable drawing entity.");
                }

                ent.TransformBy(matrix);
                resultHandles.Add(ent.Handle.ToString());
            }
        }

        return resultHandles;
    }

    internal static List<string> DeleteEntitiesInTransaction(Database db, Transaction tr, JsonElement spec)
    {
        List<ObjectId> ids = DatabaseHelpers.ResolveHandles(db, spec);
        var deleted = new List<string>(ids.Count);
        foreach (ObjectId id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent)
            {
                throw new AcadGatewayException("bad_argument", $"Handle '{id.Handle}' is not a deletable drawing entity.");
            }

            string h = ent.Handle.ToString();
            ent.Erase();
            deleted.Add(h);
        }

        return deleted;
    }

    private static string CloneAndTransform(Transaction tr, ObjectId id, Matrix3d matrix)
    {
        if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity src)
        {
            throw new AcadGatewayException("bad_argument", $"Handle '{id.Handle}' is not a top-level drawing entity.");
        }

        if (tr.GetObject(src.BlockId, OpenMode.ForWrite, false) is not BlockTableRecord owner)
        {
            throw new AcadGatewayException("bad_argument", $"Entity '{src.Handle}' cannot be cloned outside a block space.");
        }

        var cloned = (Entity)src.Clone();
        ObjectId newId;
        try
        {
            cloned.TransformBy(matrix);
        }
        catch
        {
            cloned.Dispose();
            throw;
        }

        newId = DatabaseHelpers.AppendEntity(owner, tr, cloned);

        if (src is BlockReference origBr && cloned is BlockReference clonedBr)
        {
            foreach (ObjectId attId in origBr.AttributeCollection)
            {
                if (tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference origAtt)
                {
                    var attCopy = (AttributeReference)origAtt.Clone();
                    try
                    {
                        attCopy.TransformBy(matrix);
                    }
                    catch
                    {
                        attCopy.Dispose();
                        throw;
                    }

                    DatabaseHelpers.AppendAttribute(clonedBr, tr, attCopy);
                }
            }
        }

        return newId.Handle.ToString();
    }

    private static Vector3d ReadDeltaVector(JsonElement spec)
    {
        Point3d? vec = DatabaseHelpers.ReadPoint3dOpt(spec, "vector") ?? DatabaseHelpers.ReadPoint3dOpt(spec, "offset");
        if (vec.HasValue)
        {
            return new Vector3d(vec.Value.X, vec.Value.Y, vec.Value.Z);
        }

        double dx = DatabaseHelpers.GetFiniteDoubleOpt(spec, "dx") ?? 0.0;
        double dy = DatabaseHelpers.GetFiniteDoubleOpt(spec, "dy") ?? 0.0;
        double dz = DatabaseHelpers.GetFiniteDoubleOpt(spec, "dz") ?? 0.0;
        return new Vector3d(dx, dy, dz);
    }
}
