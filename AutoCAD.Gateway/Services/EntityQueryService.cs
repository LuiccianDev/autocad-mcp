using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Read-only ObjectARX entity query, spatial/filtered pagination, and detailed inspection service.
/// </summary>
public static class EntityQueryService
{
    private static readonly QueryCursor CursorCodec = new();

    /// <summary>
    /// Queries entities with optional type, layer, space, 2D bounding-box, and text substring filters,
    /// returning paginated summaries and an HMAC-signed revision-bound cursor.
    /// </summary>
    public static Task<string> QueryAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        string space = (DatabaseHelpers.GetStringOpt(root, "space") ?? "model").Trim().ToLowerInvariant();
        if (space is not ("model" or "paper" or "all"))
        {
            throw new AcadGatewayException("bad_argument", "'space' must be 'model', 'paper', or 'all'.");
        }

        HashSet<string>? typesFilter = ReadFilterSet(root, "types");
        HashSet<string>? layersFilter = ReadFilterSet(root, "layers");
        string? textContains = DatabaseHelpers.GetStringOpt(root, "text_contains") ?? DatabaseHelpers.GetStringOpt(root, "textContains");
        double[]? bbox = ReadBboxOpt(root);
        int limit = Math.Clamp(
            DatabaseHelpers.GetIntOpt(root, "limit") ?? SecurityPolicy.DefaultQueryLimit,
            1,
            SecurityPolicy.MaxQueryLimit);
        string? cursor = DatabaseHelpers.GetStringOpt(root, "cursor");

        string fingerprint = BuildFingerprint(space, typesFilter, layersFilter, bbox, textContains);

        return Acad.Read(ctx =>
        {
            int offset = 0;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                try
                {
                    offset = CursorCodec.Read(cursor.Trim(), ctx.Session.Id, ctx.Session.Revision, fingerprint);
                }
                catch (CursorValidationException ex)
                {
                    string code = ex.Code == "CURSOR_STALE" ? "revision_conflict" : "bad_argument";
                    throw new AcadGatewayException(code, ex.Message);
                }
            }

            var spacesToScan = space switch
            {
                "model" => new[] { (BlockTableRecord.ModelSpace, "model") },
                "paper" => new[] { (BlockTableRecord.PaperSpace, "paper") },
                _ => new[] { (BlockTableRecord.ModelSpace, "model"), (BlockTableRecord.PaperSpace, "paper") },
            };

            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            var matchedItems = new List<object>();
            int totalMatched = 0;

            foreach (var (btrName, spaceLabel) in spacesToScan)
            {
                var btr = (BlockTableRecord)ctx.Tr.GetObject(bt[btrName], OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsNull || id.IsErased)
                    {
                        continue;
                    }

                    if (ctx.Tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                    {
                        continue;
                    }

                    string dxfType = ent.GetRXClass().DxfName ?? ent.GetType().Name.ToUpperInvariant();
                    string clrType = ent.GetType().Name.ToUpperInvariant();

                    if (typesFilter is not null && !typesFilter.Contains(dxfType) && !typesFilter.Contains(clrType))
                    {
                        continue;
                    }

                    if (layersFilter is not null && !layersFilter.Contains(ent.Layer))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(textContains) && !MatchesText(ctx.Tr, ent, textContains))
                    {
                        continue;
                    }

                    double[]? extents = DatabaseHelpers.TryGetExtentsArray(ent);
                    if (bbox is not null)
                    {
                        if (extents is null || !SecurityPolicy.IntersectsBbox(
                            extents[0], extents[1], extents[2], extents[3],
                            bbox[0], bbox[1], bbox[2], bbox[3]))
                        {
                            continue;
                        }
                    }

                    if (totalMatched >= offset && matchedItems.Count < limit)
                    {
                        matchedItems.Add(BuildSummary(ctx.Tr, ent, dxfType, spaceLabel, extents));
                    }

                    totalMatched++;
                }
            }

            bool hasMore = offset + matchedItems.Count < totalMatched;
            string? nextCursor = hasMore
                ? CursorCodec.Create(ctx.Session.Id, ctx.Session.Revision, offset + matchedItems.Count, fingerprint)
                : null;

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
                offset,
                count = matchedItems.Count,
                total_matched = totalMatched,
                has_more = hasMore,
                next_cursor = nextCursor,
                entities = matchedItems,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    /// <summary>Returns detailed properties and geometry for a single entity identified by hexadecimal handle.</summary>
    public static Task<string> GetAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string handle = DatabaseHelpers.GetRequiredString(root, "handle");
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            throw new AcadGatewayException("bad_handle", $"'{handle}' is not a valid hexadecimal entity handle.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.Read(ctx =>
        {
            ObjectId id = Acad.ToObjectId(ctx.Db, handle);
            if (ctx.Tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
            {
                throw new AcadGatewayException("not_found", $"Object '{handle}' is not a drawing entity.");
            }

            string dxfType = ent.GetRXClass().DxfName ?? ent.GetType().Name.ToUpperInvariant();
            double[]? extents = DatabaseHelpers.TryGetExtentsArray(ent);
            object geometry = ExtractDetailedGeometry(ctx.Tr, ent);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
                entity = new
                {
                    handle = ent.Handle.ToString(),
                    type = dxfType,
                    clr_type = ent.GetType().Name,
                    layer = ent.Layer,
                    color_index = ent.ColorIndex,
                    linetype = ent.Linetype,
                    lineweight = ent.LineWeight.ToString(),
                    visible = ent.Visible,
                    extents,
                    geometry,
                },
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    private static HashSet<string>? ReadFilterSet(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out JsonElement el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", $"Filter '{key}' must be an array of strings.");
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                set.Add(item.GetString()!.Trim());
            }
        }

        return set.Count > 0 ? set : null;
    }

    private static double[]? ReadBboxOpt(JsonElement root)
    {
        if (!root.TryGetProperty("bbox", out JsonElement el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            if (el.GetArrayLength() != 4)
            {
                throw new AcadGatewayException("bad_argument", "'bbox' array must contain 4 finite numbers [min_x, min_y, max_x, max_y].");
            }

            double[] vals = new double[4];
            int i = 0;
            foreach (JsonElement item in el.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double d) || !double.IsFinite(d))
                {
                    throw new AcadGatewayException("bad_argument", "'bbox' must contain only finite numbers.");
                }

                vals[i++] = d;
            }

            return vals;
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            return
            [
                DatabaseHelpers.GetRequiredFiniteDouble(el, "min_x"),
                DatabaseHelpers.GetRequiredFiniteDouble(el, "min_y"),
                DatabaseHelpers.GetRequiredFiniteDouble(el, "max_x"),
                DatabaseHelpers.GetRequiredFiniteDouble(el, "max_y"),
            ];
        }

        throw new AcadGatewayException("bad_argument", "'bbox' must be [min_x, min_y, max_x, max_y] or {min_x, min_y, max_x, max_y}.");
    }

    private static string BuildFingerprint(
        string space,
        HashSet<string>? types,
        HashSet<string>? layers,
        double[]? bbox,
        string? textContains)
    {
        string typesStr = types is null ? "" : string.Join(",", types.Select(t => t.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal));
        string layersStr = layers is null ? "" : string.Join(",", layers.Select(l => l.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal));
        string bboxStr = bbox is null ? "" : string.Join(",", bbox.Select(d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        string raw = $"{space}|{typesStr}|{layersStr}|{bboxStr}|{textContains ?? ""}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static bool MatchesText(Transaction tr, Entity ent, string needle)
    {
        switch (ent)
        {
            case DBText dbText:
                return dbText.TextString?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
            case MText mText:
                return (mText.Text ?? mText.Contents)?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
            case BlockReference blockRef:
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    if (tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference att &&
                        att.TextString?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return true;
                    }
                }
                return blockRef.Name?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
            default:
                return false;
        }
    }

    private static object BuildSummary(Transaction tr, Entity ent, string dxfType, string spaceLabel, double[]? extents)
    {
        object? geometry = ent switch
        {
            Line l => new { start = Pt3(l.StartPoint), end = Pt3(l.EndPoint), length = Round4(l.Length) },
            Circle c => new { center = Pt3(c.Center), radius = Round4(c.Radius) },
            Arc a => new { center = Pt3(a.Center), radius = Round4(a.Radius), start_angle = Round4(DatabaseHelpers.ToDegrees(a.StartAngle)), end_angle = Round4(DatabaseHelpers.ToDegrees(a.EndAngle)) },
            Polyline pl => new { vertices = pl.NumberOfVertices, closed = pl.Closed, length = Round4(pl.Length) },
            DBText t => new { text = t.TextString, position = Pt3(t.Position), height = Round4(t.Height) },
            MText m => new { text = m.Text, position = Pt3(m.Location), height = Round4(m.TextHeight) },
            BlockReference br => new { block_name = br.Name, position = Pt3(br.Position), rotation = Round4(DatabaseHelpers.ToDegrees(br.Rotation)), attributes = br.AttributeCollection.Count },
            RasterImage ri => new { position = Pt3(ri.Orientation.Origin), width = Round4(ri.Orientation.Xaxis.Length), height = Round4(ri.Orientation.Yaxis.Length), image_def_handle = ri.ImageDefId.Handle.ToString() },
            _ => null,
        };

        return new
        {
            handle = ent.Handle.ToString(),
            type = dxfType,
            layer = ent.Layer,
            space = spaceLabel,
            color_index = ent.ColorIndex,
            extents,
            geometry,
        };
    }

    private static object ExtractDetailedGeometry(Transaction tr, Entity ent) => ent switch
    {
        Line l => new
        {
            start = Pt3(l.StartPoint),
            end = Pt3(l.EndPoint),
            length = Round4(l.Length),
        },
        Circle c => new
        {
            center = Pt3(c.Center),
            radius = Round4(c.Radius),
            circumference = Round4(c.Circumference),
            area = Round4(c.Area),
        },
        Arc a => new
        {
            center = Pt3(a.Center),
            radius = Round4(a.Radius),
            start_angle = Round4(DatabaseHelpers.ToDegrees(a.StartAngle)),
            end_angle = Round4(DatabaseHelpers.ToDegrees(a.EndAngle)),
            length = Round4(a.Length),
            start = Pt3(a.StartPoint),
            end = Pt3(a.EndPoint),
        },
        Polyline pl => new
        {
            closed = pl.Closed,
            elevation = Round4(pl.Elevation),
            length = Round4(pl.Length),
            area = Round4(pl.Area),
            vertices = Enumerable.Range(0, pl.NumberOfVertices).Select(i =>
            {
                var p = pl.GetPoint2dAt(i);
                return new { x = Round4(p.X), y = Round4(p.Y), bulge = Round4(pl.GetBulgeAt(i)) };
            }).ToArray(),
        },
        DBText t => new
        {
            text = t.TextString,
            position = Pt3(t.Position),
            height = Round4(t.Height),
            rotation = Round4(DatabaseHelpers.ToDegrees(t.Rotation)),
        },
        MText m => new
        {
            text = m.Text,
            contents = m.Contents,
            position = Pt3(m.Location),
            height = Round4(m.TextHeight),
            width = Round4(m.Width),
            rotation = Round4(DatabaseHelpers.ToDegrees(m.Rotation)),
        },
        BlockReference br => new
        {
            block_name = br.Name,
            position = Pt3(br.Position),
            rotation = Round4(DatabaseHelpers.ToDegrees(br.Rotation)),
            scale = new[] { Round4(br.ScaleFactors.X), Round4(br.ScaleFactors.Y), Round4(br.ScaleFactors.Z) },
            attributes = ReadBlockAttributes(tr, br),
        },
        RasterImage ri => new
        {
            position = Pt3(ri.Orientation.Origin),
            width = Round4(ri.Orientation.Xaxis.Length),
            height = Round4(ri.Orientation.Yaxis.Length),
            rotation = Round4(DatabaseHelpers.ToDegrees(ri.Rotation)),
            image_def_handle = ri.ImageDefId.Handle.ToString(),
            show_image = ri.ShowImage,
        },
        _ => new { dxf_name = ent.GetRXClass().DxfName },
    };

    private static Dictionary<string, string> ReadBlockAttributes(Transaction tr, BlockReference br)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId attId in br.AttributeCollection)
        {
            if (tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference att &&
                !string.IsNullOrEmpty(att.Tag))
            {
                attrs[att.Tag] = att.TextString ?? string.Empty;
            }
        }

        return attrs;
    }

    private static double Round4(double v) => double.IsFinite(v) ? Math.Round(v, 4) : v;

    private static double[] Pt3(Autodesk.AutoCAD.Geometry.Point3d p) => [Round4(p.X), Round4(p.Y), Round4(p.Z)];
}
