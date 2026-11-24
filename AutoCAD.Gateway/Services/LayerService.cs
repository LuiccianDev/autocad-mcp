using System;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Native ObjectARX layer management service operating inside atomic <c>Acad.Write</c> transactions.
/// </summary>
public static class LayerService
{
    /// <summary>Creates or updates a layer and optionally makes it the active drawing layer.</summary>
    public static Task<string> CreateOrUpdateAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.Write(ctx =>
        {
            ObjectId id = ApplyLayerUpsert(ctx.Db, ctx.Tr, root, name);
            var ltr = (LayerTableRecord)ctx.Tr.GetObject(id, OpenMode.ForRead);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                layer = ltr.Name,
                color_index = (int)ltr.Color.ColorIndex,
                is_current = ctx.Db.Clayer == id,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    /// <summary>Updates layer visibility, freeze, lock, color, or active status without command-line prompts.</summary>
    public static Task<string> SetStateAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.Write(ctx =>
        {
            var ltr = ApplyLayerState(ctx.Db, ctx.Tr, root, name);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                layer = ltr.Name,
                is_on = !ltr.IsOff,
                is_frozen = ltr.IsFrozen,
                is_locked = ltr.IsLocked,
                color_index = (int)ltr.Color.ColorIndex,
                is_current = ctx.Db.Clayer == ltr.ObjectId,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    /// <summary>Safely deletes an unused non-system, non-current layer using ObjectARX <c>Database.Purge</c>.</summary>
    public static Task<string> DeleteAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid layer name '{name}'.");
        }

        if (string.Equals(name, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Defpoints", StringComparison.OrdinalIgnoreCase))
        {
            throw new AcadGatewayException("bad_argument", $"Protected AutoCAD layer '{name}' cannot be deleted.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.Write(ctx =>
        {
            var lt = (LayerTable)ctx.Tr.GetObject(ctx.Db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                throw new AcadGatewayException("not_found", $"Layer '{name}' does not exist in this drawing.");
            }

            ObjectId id = lt[name];
            if (ctx.Db.Clayer == id)
            {
                throw new AcadGatewayException("layer_in_use", $"Cannot delete the active layer '{name}'. Switch CLAYER first.");
            }

            var purgeIds = new ObjectIdCollection { id };
            ctx.Db.Purge(purgeIds);
            if (purgeIds.Count == 0)
            {
                throw new AcadGatewayException("layer_in_use", $"Layer '{name}' is referenced by objects and cannot be deleted.");
            }

            var ltr = (LayerTableRecord)ctx.Tr.GetObject(id, OpenMode.ForWrite);
            ltr.Erase();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                deleted_layer = name,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    internal static ObjectId ApplyLayerUpsert(Database db, Transaction tr, JsonElement spec, string name)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid AutoCAD layer name '{name}'.");
        }

        int? colorIndex = DatabaseHelpers.GetIntOpt(spec, "color_index")
            ?? DatabaseHelpers.GetIntOpt(spec, "colorIndex")
            ?? DatabaseHelpers.GetIntOpt(spec, "color");
        if (colorIndex is < 1 or > 255)
        {
            throw new AcadGatewayException("bad_argument", "Layer color index must be between 1 and 255.");
        }

        ObjectId? linetypeId = null;
        string? linetype = DatabaseHelpers.GetStringOpt(spec, "linetype");
        if (!string.IsNullOrWhiteSpace(linetype))
        {
            string ltTrim = linetype.Trim();
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!ltt.Has(ltTrim))
            {
                throw new AcadGatewayException("not_found", $"Linetype '{ltTrim}' is not loaded in this drawing.");
            }

            linetypeId = ltt[ltTrim];
        }

        string? description = DatabaseHelpers.GetStringOpt(spec, "description");
        bool setCurrent = DatabaseHelpers.GetBoolOpt(spec, "set_current") ?? true;

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name) && setCurrent)
        {
            var existingRead = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
            if (existingRead.IsFrozen)
            {
                throw new AcadGatewayException("bad_argument", $"Cannot set frozen layer '{name}' as current. Thaw it first.");
            }
        }

        ObjectId id = DatabaseHelpers.EnsureLayer(db, tr, name);
        var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);

        if (colorIndex.HasValue)
        {
            ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, (short)colorIndex.Value);
        }

        if (linetypeId.HasValue)
        {
            ltr.LinetypeObjectId = linetypeId.Value;
        }

        if (description is not null)
        {
            ltr.Description = description;
        }

        if (setCurrent)
        {
            db.Clayer = id;
        }

        return id;
    }

    internal static LayerTableRecord ApplyLayerState(Database db, Transaction tr, JsonElement spec, string name)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid layer name '{name}'.");
        }

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(name))
        {
            throw new AcadGatewayException("not_found", $"Layer '{name}' does not exist in this drawing.");
        }

        ObjectId id = lt[name];
        bool? isOn = DatabaseHelpers.GetBoolOpt(spec, "on") ?? DatabaseHelpers.GetBoolOpt(spec, "is_on");
        bool? isFrozen = DatabaseHelpers.GetBoolOpt(spec, "frozen") ?? DatabaseHelpers.GetBoolOpt(spec, "is_frozen");
        bool? isLocked = DatabaseHelpers.GetBoolOpt(spec, "locked") ?? DatabaseHelpers.GetBoolOpt(spec, "is_locked");
        int? colorIndex = DatabaseHelpers.GetIntOpt(spec, "color_index") ?? DatabaseHelpers.GetIntOpt(spec, "color");
        bool setCurrent = DatabaseHelpers.GetBoolOpt(spec, "set_current") == true;

        if (colorIndex is < 1 or > 255)
        {
            throw new AcadGatewayException("bad_argument", "Layer color index must be between 1 and 255.");
        }

        if (isFrozen == true && (db.Clayer == id || setCurrent))
        {
            throw new AcadGatewayException("bad_argument", $"Cannot freeze the active layer '{name}'.");
        }

        var existingRead = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
        bool resultingFrozen = isFrozen ?? existingRead.IsFrozen;
        if (setCurrent && resultingFrozen)
        {
            throw new AcadGatewayException("bad_argument", $"Cannot set frozen layer '{name}' as current. Thaw it first.");
        }

        var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
        if (isOn.HasValue) ltr.IsOff = !isOn.Value;
        if (isFrozen.HasValue) ltr.IsFrozen = isFrozen.Value;
        if (isLocked.HasValue) ltr.IsLocked = isLocked.Value;
        if (colorIndex.HasValue) ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, (short)colorIndex.Value);
        if (setCurrent) db.Clayer = id;

        return ltr;
    }
}
