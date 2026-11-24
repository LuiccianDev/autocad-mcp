using System;
using System.Collections.Generic;
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
/// Shared ObjectARX database, entity, symbol-table, and strict JSON argument helpers for Gateway services.
/// </summary>
internal static class DatabaseHelpers
{
    internal static JsonElement ParseJsonObject(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new AcadGatewayException("bad_request", "Request body cannot be empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AcadGatewayException("bad_request", "Request body must be a JSON object.");
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AcadGatewayException("bad_json", "Invalid or malformed JSON payload.");
        }
    }

    internal static string? GetStringOpt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.String)
        {
            throw new AcadGatewayException("bad_argument", $"Parameter '{name}' must be a JSON string.");
        }

        return prop.GetString();
    }

    internal static string GetRequiredString(JsonElement obj, string name)
    {
        string? val = GetStringOpt(obj, name);
        if (string.IsNullOrWhiteSpace(val))
        {
            throw new AcadGatewayException("bad_argument", $"Parameter '{name}' is required and cannot be empty.");
        }

        return val.Trim();
    }

    internal static bool? GetBoolOpt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new AcadGatewayException("bad_argument", $"Parameter '{name}' must be a JSON boolean.");
        }

        return prop.GetBoolean();
    }

    internal static int? GetIntOpt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out int val))
        {
            throw new AcadGatewayException("bad_argument", $"Parameter '{name}' must be a valid 32-bit integer.");
        }

        return val;
    }

    internal static long? GetLongOpt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt64(out long val))
        {
            throw new AcadGatewayException("bad_argument", $"Parameter '{name}' must be a valid integer.");
        }

        return val;
    }

    internal static double? GetFiniteDoubleOpt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetDouble(out double val) || !double.IsFinite(val))
        {
            throw new AcadGatewayException("bad_argument", $"Parameter '{name}' must be a finite number.");
        }

        return val;
    }

    internal static double GetRequiredFiniteDouble(JsonElement obj, string name) =>
        GetFiniteDoubleOpt(obj, name)
        ?? throw new AcadGatewayException("bad_argument", $"Parameter '{name}' must be a finite number.");

    internal static Point3d ReadPoint3d(JsonElement el, string fieldName, double defaultZ = 0.0)
    {
        if (el.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", $"'{fieldName}' must be a coordinate array [x, y] or [x, y, z].");
        }

        int len = el.GetArrayLength();
        if (len is < 2 or > 3)
        {
            throw new AcadGatewayException("bad_argument", $"'{fieldName}' must contain 2 or 3 numeric coordinates.");
        }

        double[] coords = [0.0, 0.0, defaultZ];
        int i = 0;
        foreach (JsonElement item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double d) || !double.IsFinite(d))
            {
                throw new AcadGatewayException("bad_argument", $"'{fieldName}' contains a non-finite coordinate.");
            }

            coords[i++] = d;
        }

        return new Point3d(coords[0], coords[1], coords[2]);
    }

    internal static Point3d? ReadPoint3dOpt(JsonElement obj, string name, double defaultZ = 0.0)
    {
        if (!obj.TryGetProperty(name, out JsonElement el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadPoint3d(el, name, defaultZ);
    }

    internal static Point3d ReadUpdatedPoint3d(
        Point3d current,
        JsonElement obj,
        string arrayName,
        string xName,
        string yName,
        string zName)
    {
        if (obj.TryGetProperty(arrayName, out JsonElement arrEl) && arrEl.ValueKind != JsonValueKind.Null)
        {
            return ReadPoint3d(arrEl, arrayName, current.Z);
        }

        double x = GetFiniteDoubleOpt(obj, xName) ?? current.X;
        double y = GetFiniteDoubleOpt(obj, yName) ?? current.Y;
        double z = GetFiniteDoubleOpt(obj, zName) ?? current.Z;
        return new Point3d(x, y, z);
    }

    internal static Point3d ReadRequiredPoint3d(JsonElement obj, string arrayName, string xName, string yName, string? zName = null)
    {
        Point3d? arrPt = ReadPoint3dOpt(obj, arrayName);
        if (arrPt.HasValue)
        {
            return arrPt.Value;
        }

        double x = GetRequiredFiniteDouble(obj, xName);
        double y = GetRequiredFiniteDouble(obj, yName);
        double z = zName is not null ? (GetFiniteDoubleOpt(obj, zName) ?? 0.0) : 0.0;
        return new Point3d(x, y, z);
    }

    internal static ObjectId EnsureLayer(Database db, Transaction tr, string layerName)
    {
        string trimmed = layerName.Trim();
        if (!SecurityPolicy.IsValidSymbolName(trimmed))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid AutoCAD layer name '{layerName}'.");
        }

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(trimmed))
        {
            return lt[trimmed];
        }

        lt.UpgradeOpen();
        var record = new LayerTableRecord { Name = trimmed };
        ObjectId id = lt.Add(record);
        tr.AddNewlyCreatedDBObject(record, true);
        return id;
    }

    internal static BlockTableRecord OpenSpace(Database db, Transaction tr, string? space, OpenMode mode)
    {
        string normalized = (space ?? "model").Trim().ToLowerInvariant();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        return normalized switch
        {
            "model" or "modelspace" => (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], mode),
            "paper" or "paperspace" => (BlockTableRecord)tr.GetObject(ResolvePaperSpaceId(db, tr, null, bt), mode),
            _ => throw new AcadGatewayException("bad_argument", "'space' must be 'model' or 'paper'."),
        };
    }

    internal static ObjectId ResolvePaperSpaceId(Database db, Transaction tr, string? layoutName, BlockTable? bt = null)
    {
        bt ??= (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!string.IsNullOrWhiteSpace(layoutName))
        {
            string clean = layoutName.Trim();
            if (string.Equals(clean, "Model", StringComparison.OrdinalIgnoreCase))
            {
                throw new AcadGatewayException("bad_argument", "PaperSpace layout required; 'Model' is not a PaperSpace layout.");
            }

            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            if (!dict.Contains(clean))
            {
                throw new AcadGatewayException("not_found", $"Layout '{clean}' does not exist in this drawing.");
            }

            var layout = (Layout)tr.GetObject(dict.GetAt(clean), OpenMode.ForRead);
            return layout.BlockTableRecordId;
        }

        ObjectId modelId = bt[BlockTableRecord.ModelSpace];
        if (!db.CurrentSpaceId.IsNull && db.CurrentSpaceId != modelId)
        {
            return db.CurrentSpaceId;
        }

        if (AcApp.DocumentManager.MdiActiveDocument?.Database == db)
        {
            string activeLayout = LayoutManager.Current.CurrentLayout;
            if (!string.IsNullOrWhiteSpace(activeLayout) &&
                !string.Equals(activeLayout, "Model", StringComparison.OrdinalIgnoreCase))
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                if (dict.Contains(activeLayout))
                {
                    var layout = (Layout)tr.GetObject(dict.GetAt(activeLayout), OpenMode.ForRead);
                    if (!layout.BlockTableRecordId.IsNull && layout.BlockTableRecordId != modelId)
                    {
                        return layout.BlockTableRecordId;
                    }
                }
            }
        }

        return bt[BlockTableRecord.PaperSpace];
    }

    internal static T CreateAndAppend<T>(
        BlockTableRecord space,
        Transaction tr,
        Database db,
        Func<T> factory,
        Action<T> configure) where T : Entity
    {
        T ent = factory();
        bool added = false;
        try
        {
            ent.SetDatabaseDefaults(db);
            configure(ent);
            space.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
            added = true;
            return ent;
        }
        finally
        {
            if (!added)
            {
                ent.Dispose();
            }
        }
    }

    internal static ObjectId AppendEntity(BlockTableRecord space, Transaction tr, Entity ent)
    {
        bool added = false;
        try
        {
            ObjectId id = space.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
            added = true;
            return id;
        }
        finally
        {
            if (!added)
            {
                ent.Dispose();
            }
        }
    }

    internal static ObjectId AppendAttribute(BlockReference br, Transaction tr, AttributeReference att)
    {
        bool added = false;
        try
        {
            ObjectId id = br.AttributeCollection.AppendAttribute(att);
            tr.AddNewlyCreatedDBObject(att, true);
            added = true;
            return id;
        }
        finally
        {
            if (!added)
            {
                att.Dispose();
            }
        }
    }

    internal static Dictionary<string, string> ReadStringDictionary(JsonElement objEl, string fieldName)
    {
        if (objEl.ValueKind != JsonValueKind.Object)
        {
            throw new AcadGatewayException("bad_argument", $"'{fieldName}' must be a JSON object mapping keys to string values.");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty prop in objEl.EnumerateObject())
        {
            string key = prop.Name.Trim();
            if (key.Length == 0)
            {
                throw new AcadGatewayException("bad_argument", $"'{fieldName}' cannot contain empty keys.");
            }

            map[key] = prop.Value.ValueKind == JsonValueKind.String
                ? (prop.Value.GetString() ?? string.Empty)
                : prop.Value.ToString();
        }

        return map;
    }

    internal static Task<string> WriteEnvelopeAsync(string body, Func<AcadContext, JsonElement, object> work)
    {
        JsonElement root = ParseJsonObject(body);
        string? documentName = GetStringOpt(root, "document");
        long? expectedRevision = GetLongOpt(root, "expected_revision");
        return Acad.Write(
            ctx => JsonSerializer.Serialize(work(ctx, root), GatewayProtocol.WireJsonOptions),
            documentName: documentName,
            expectedRevision: expectedRevision);
    }

    internal static void ValidateAndApplyEntityProperties(Entity ent, Database db, Transaction tr, JsonElement spec)
    {
        string? layer = GetStringOpt(spec, "layer");
        int? colorIndex = GetIntOpt(spec, "color_index") ?? GetIntOpt(spec, "colorIndex");
        string? linetype = GetStringOpt(spec, "linetype");

        if (colorIndex is < 0 or > 256)
        {
            throw new AcadGatewayException("bad_argument", "'color_index' must be between 0 and 256.");
        }

        string? validatedLinetype = null;
        if (!string.IsNullOrWhiteSpace(linetype))
        {
            validatedLinetype = linetype.Trim();
            if (!SecurityPolicy.IsValidSymbolName(validatedLinetype))
            {
                throw new AcadGatewayException("bad_argument", $"Invalid linetype name '{linetype}'.");
            }

            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!ltt.Has(validatedLinetype))
            {
                throw new AcadGatewayException("not_found", $"Linetype '{validatedLinetype}' is not loaded in this drawing.");
            }
        }

        if (!string.IsNullOrWhiteSpace(layer))
        {
            EnsureLayer(db, tr, layer);
            ent.Layer = layer.Trim();
        }

        if (colorIndex.HasValue)
        {
            ent.ColorIndex = colorIndex.Value;
        }

        if (validatedLinetype is not null)
        {
            ent.Linetype = validatedLinetype;
        }
    }

    internal static List<ObjectId> ResolveHandles(Database db, JsonElement spec, string propertyName = "handles")
    {
        if (!spec.TryGetProperty(propertyName, out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", $"'{propertyName}' must be a non-empty array of hexadecimal entity handles.");
        }

        var ids = new List<ObjectId>();
        var seen = new HashSet<ObjectId>();
        foreach (JsonElement item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new AcadGatewayException("bad_handle", "Every handle must be a hexadecimal string.");
            }

            string handle = item.GetString() ?? string.Empty;
            if (!SecurityPolicy.IsValidEntityHandle(handle))
            {
                throw new AcadGatewayException("bad_handle", $"'{handle}' is not a valid hexadecimal entity handle.");
            }

            if (ids.Count >= SecurityPolicy.MaxBatchOperations)
            {
                throw new AcadGatewayException("bad_argument", $"At most {SecurityPolicy.MaxBatchOperations} handles are allowed per call.");
            }

            ObjectId id = Acad.ToObjectId(db, handle);
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            throw new AcadGatewayException("bad_argument", $"'{propertyName}' cannot be empty.");
        }

        return ids;
    }

    internal static double[]? TryGetExtentsArray(Entity ent)
    {
        try
        {
            Extents3d ext = ent.GeometricExtents;
            double[] arr = [ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y];
            return Array.TrueForAll(arr, double.IsFinite) ? arr : null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static double ToDegrees(double radians) => radians * (180.0 / Math.PI);
    internal static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);

    /// <summary>
    /// Walks any existing ancestor directories and leaf file for <paramref name="fullPath"/> and rejects
    /// symbolic links or NTFS junctions (<see cref="FileAttributes.ReparsePoint"/>).
    /// </summary>
    internal static void EnsureNoReparsePoints(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        string current = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AcadGatewayException(
                        "invalid_path",
                        $"Symbolic links and NTFS junctions are not permitted along path '{fullPath}'.");
                }
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }
}
