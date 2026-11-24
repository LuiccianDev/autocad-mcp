using System;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Internal modifier updating properties and geometry of existing entities inside write transactions.
/// </summary>
internal static class EntityModifier
{
    internal static string ModifyEntityInTransaction(Database db, Transaction tr, JsonElement spec)
    {
        string handle = DatabaseHelpers.GetRequiredString(spec, "handle");
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            throw new AcadGatewayException("bad_handle", $"'{handle}' is not a valid hexadecimal entity handle.");
        }

        ObjectId id = Acad.ToObjectId(db, handle);
        if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent)
        {
            throw new AcadGatewayException("not_found", $"Object '{handle}' is not a mutable drawing entity.");
        }

        DatabaseHelpers.ValidateAndApplyEntityProperties(ent, db, tr, spec);

        switch (ent)
        {
            case Line line:
            {
                Point3d nextStart = DatabaseHelpers.ReadUpdatedPoint3d(line.StartPoint, spec, "start", "start_x", "start_y", "start_z");
                Point3d nextEnd = DatabaseHelpers.ReadUpdatedPoint3d(line.EndPoint, spec, "end", "end_x", "end_y", "end_z");
                if (nextStart.DistanceTo(nextEnd) < 1e-12)
                {
                    throw new AcadGatewayException("bad_argument", "Line start and end points cannot be identical.");
                }

                line.StartPoint = nextStart;
                line.EndPoint = nextEnd;
                break;
            }

            case Circle circle:
            {
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "radius") is double cr)
                {
                    if (cr <= 0) throw new AcadGatewayException("bad_argument", "Circle radius must be > 0.");
                    circle.Radius = cr;
                }

                circle.Center = DatabaseHelpers.ReadUpdatedPoint3d(circle.Center, spec, "center", "x", "y", "z");
                break;
            }

            case Arc arc:
            {
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "radius") is double ar)
                {
                    if (ar <= 0) throw new AcadGatewayException("bad_argument", "Arc radius must be > 0.");
                    arc.Radius = ar;
                }

                double nextStartRad = DatabaseHelpers.GetFiniteDoubleOpt(spec, "start_angle") is double sa
                    ? DatabaseHelpers.ToRadians(sa)
                    : arc.StartAngle;
                double nextEndRad = DatabaseHelpers.GetFiniteDoubleOpt(spec, "end_angle") is double ea
                    ? DatabaseHelpers.ToRadians(ea)
                    : arc.EndAngle;
                double sweepDiff = Math.Abs(nextEndRad - nextStartRad) % (2.0 * Math.PI);
                if (Math.Abs(nextEndRad - nextStartRad) < 1e-9 || sweepDiff < 1e-9)
                {
                    throw new AcadGatewayException("bad_argument", "Arc start_angle and end_angle must differ.");
                }

                arc.Center = DatabaseHelpers.ReadUpdatedPoint3d(arc.Center, spec, "center", "x", "y", "z");
                arc.StartAngle = nextStartRad;
                arc.EndAngle = nextEndRad;
                break;
            }

            case Polyline pl:
                if (DatabaseHelpers.GetBoolOpt(spec, "closed") is bool closed) pl.Closed = closed;
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "elevation") is double elev) pl.Elevation = elev;
                break;

            case DBText dbText:
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "height") is double th)
                {
                    if (th <= 0) throw new AcadGatewayException("bad_argument", "Text height must be > 0.");
                    dbText.Height = th;
                }

                if (DatabaseHelpers.GetStringOpt(spec, "text") is string ts) dbText.TextString = ts;
                dbText.Position = DatabaseHelpers.ReadUpdatedPoint3d(dbText.Position, spec, "position", "x", "y", "z");
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "rotation") is double trDeg) dbText.Rotation = DatabaseHelpers.ToRadians(trDeg);
                break;

            case MText mText:
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "height") is double mh)
                {
                    if (mh <= 0) throw new AcadGatewayException("bad_argument", "MText height must be > 0.");
                    mText.TextHeight = mh;
                }

                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "width") is double mw)
                {
                    if (mw < 0) throw new AcadGatewayException("bad_argument", "MText width must be >= 0.");
                    mText.Width = mw;
                }

                if (DatabaseHelpers.GetStringOpt(spec, "text") is string ms) mText.Contents = ms;
                mText.Location = DatabaseHelpers.ReadUpdatedPoint3d(mText.Location, spec, "position", "x", "y", "z");
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "rotation") is double mrDeg) mText.Rotation = DatabaseHelpers.ToRadians(mrDeg);
                break;

            case BlockReference br:
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "scale") is double bs)
                {
                    if (bs <= 0) throw new AcadGatewayException("bad_argument", "Block scale must be greater than zero.");
                    br.ScaleFactors = new Scale3d(bs);
                }

                br.Position = DatabaseHelpers.ReadUpdatedPoint3d(br.Position, spec, "position", "x", "y", "z");
                if (DatabaseHelpers.GetFiniteDoubleOpt(spec, "rotation") is double brDeg) br.Rotation = DatabaseHelpers.ToRadians(brDeg);
                break;

            default:
                if (HasGeometryFields(spec))
                {
                    throw new AcadGatewayException(
                        "bad_argument",
                        $"Geometry edits are not supported for entity type '{ent.GetRXClass().DxfName}'; only layer, color_index, and linetype can be modified.");
                }

                break;
        }

        return ent.Handle.ToString();
    }

    private static bool HasNonNull(JsonElement spec, string name) =>
        spec.TryGetProperty(name, out JsonElement el) && el.ValueKind != JsonValueKind.Null;

    private static bool HasGeometryFields(JsonElement spec) =>
        HasNonNull(spec, "start") || HasNonNull(spec, "end") ||
        HasNonNull(spec, "start_x") || HasNonNull(spec, "start_y") || HasNonNull(spec, "start_z") ||
        HasNonNull(spec, "end_x") || HasNonNull(spec, "end_y") || HasNonNull(spec, "end_z") ||
        HasNonNull(spec, "center") || HasNonNull(spec, "position") ||
        HasNonNull(spec, "radius") || HasNonNull(spec, "start_angle") ||
        HasNonNull(spec, "end_angle") || HasNonNull(spec, "closed") ||
        HasNonNull(spec, "elevation") || HasNonNull(spec, "height") ||
        HasNonNull(spec, "width") || HasNonNull(spec, "text") ||
        HasNonNull(spec, "rotation") || HasNonNull(spec, "scale") ||
        HasNonNull(spec, "x") || HasNonNull(spec, "y") || HasNonNull(spec, "z");
}
