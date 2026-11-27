using System;
using System.Collections.Generic;
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
    private readonly record struct LayerPropertyPatch(
        int? ColorIndex,
        ObjectId? LinetypeId,
        LineWeight? LineWeightValue,
        bool? IsPlottable,
        string? Description,
        bool? SetCurrent,
        bool LinetypeAutoLoaded);

    /// <summary>Creates or updates a layer and optionally makes it the active drawing layer.</summary>
    public static Task<string> CreateOrUpdateAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.Write(ctx =>
        {
            ObjectId id = ApplyLayerUpsert(ctx.Db, ctx.Tr, root, name, out bool ltAutoLoaded);
            var ltr = (LayerTableRecord)ctx.Tr.GetObject(id, OpenMode.ForRead);
            return SerializeLayerResult(ctx.Doc.Name, ctx.Session.Revision, ctx.Db, ctx.Tr, ltr, ltAutoLoaded);
        }, documentName: documentName);
    }

    /// <summary>Updates layer visibility, freeze, lock, color, linetype, lineweight, plottability, or active status.</summary>
    public static Task<string> SetStateAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.Write(ctx =>
        {
            LayerTableRecord ltr = ApplyLayerState(ctx.Db, ctx.Tr, root, name, out bool ltAutoLoaded);
            return SerializeLayerResult(ctx.Doc.Name, ctx.Session.Revision, ctx.Db, ctx.Tr, ltr, ltAutoLoaded);
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

    internal static ObjectId ApplyLayerUpsert(Database db, Transaction tr, JsonElement spec, string name) =>
        ApplyLayerUpsert(db, tr, spec, name, out _);

    internal static ObjectId ApplyLayerUpsert(
        Database db,
        Transaction tr,
        JsonElement spec,
        string name,
        out bool linetypeAutoLoaded)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid AutoCAD layer name '{name}'.");
        }

        LayerPropertyPatch patch = ParsePropertyPatch(db, tr, spec, defaultSetCurrent: true);
        linetypeAutoLoaded = patch.LinetypeAutoLoaded;
        bool setCurrent = patch.SetCurrent ?? true;

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
        ApplyPatch(db, ltr, patch, setCurrent);
        return id;
    }

    internal static LayerTableRecord ApplyLayerState(Database db, Transaction tr, JsonElement spec, string name) =>
        ApplyLayerState(db, tr, spec, name, out _);

    internal static LayerTableRecord ApplyLayerState(
        Database db,
        Transaction tr,
        JsonElement spec,
        string name,
        out bool linetypeAutoLoaded)
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

        LayerPropertyPatch patch = ParsePropertyPatch(db, tr, spec, defaultSetCurrent: false);
        linetypeAutoLoaded = patch.LinetypeAutoLoaded;
        bool setCurrent = patch.SetCurrent ?? false;

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
        ApplyPatch(db, ltr, patch, setCurrent);
        return ltr;
    }

    private static LayerPropertyPatch ParsePropertyPatch(
        Database db,
        Transaction tr,
        JsonElement spec,
        bool defaultSetCurrent)
    {
        int? colorIndex = DatabaseHelpers.GetIntOpt(spec, "color_index")
            ?? DatabaseHelpers.GetIntOpt(spec, "colorIndex")
            ?? DatabaseHelpers.GetIntOpt(spec, "color");
        if (colorIndex is < 1 or > 255)
        {
            throw new AcadGatewayException("bad_argument", "Layer color index must be between 1 and 255.");
        }

        ObjectId? linetypeId = null;
        bool autoLoaded = false;
        string? linetype = DatabaseHelpers.GetStringOpt(spec, "linetype");
        if (!string.IsNullOrWhiteSpace(linetype))
        {
            linetypeId = ResolveOrLoadLinetype(db, tr, linetype.Trim(), out autoLoaded);
        }

        LineWeight? lineWeightVal = ParseLineWeightOpt(spec);
        bool? isPlottable = DatabaseHelpers.GetBoolOpt(spec, "is_plottable");
        string? description = DatabaseHelpers.GetStringOpt(spec, "description");
        bool? setCurrent = DatabaseHelpers.GetBoolOpt(spec, "set_current") ?? defaultSetCurrent;

        return new LayerPropertyPatch(
            colorIndex,
            linetypeId,
            lineWeightVal,
            isPlottable,
            description,
            setCurrent,
            autoLoaded);
    }

    private static LineWeight? ParseLineWeightOpt(JsonElement spec)
    {
        if (!spec.TryGetProperty("lineweight", out JsonElement lwEl) &&
            !spec.TryGetProperty("line_weight", out lwEl))
        {
            return null;
        }

        if (lwEl.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        bool ok;
        int enumVal;
        string? error;
        if (lwEl.ValueKind == JsonValueKind.String)
        {
            ok = LineWeightPolicy.TryParseLineWeight(lwEl.GetString(), out enumVal, out _, out error);
        }
        else if (lwEl.ValueKind == JsonValueKind.Number && lwEl.TryGetDouble(out double d))
        {
            ok = LineWeightPolicy.TryFromNumeric(d, out enumVal, out _, out error);
        }
        else
        {
            throw new AcadGatewayException("bad_argument", "'lineweight' must be a millimeter string (e.g. '0.25', 'default') or finite number.");
        }

        if (!ok)
        {
            throw new AcadGatewayException("bad_argument", error ?? "Invalid layer lineweight.");
        }

        return (LineWeight)enumVal;
    }

    private static ObjectId ResolveOrLoadLinetype(Database db, Transaction tr, string ltTrim, out bool autoLoaded)
    {
        autoLoaded = false;
        if (!SecurityPolicy.IsValidLayerLinetypeName(ltTrim, out string? ltErr))
        {
            throw new AcadGatewayException("bad_argument", ltErr ?? $"Invalid linetype name '{ltTrim}'.");
        }

        var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (ltt.Has(ltTrim))
        {
            return ltt[ltTrim];
        }

        foreach (string linFile in new[] { "acadiso.lin", "acad.lin" })
        {
            try
            {
                db.LoadLineTypeFile(ltTrim, linFile);
                if (ltt.Has(ltTrim))
                {
                    autoLoaded = true;
                    return ltt[ltTrim];
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Linetype '{0}' failed to load from '{1}': {2}", ltTrim, linFile, ex.Message);
            }
        }

        throw new AcadGatewayException("not_found", $"Linetype '{ltTrim}' is not loaded and could not be found in acadiso.lin or acad.lin.");
    }

    private static void ApplyPatch(Database db, LayerTableRecord ltr, LayerPropertyPatch patch, bool setCurrent)
    {
        if (patch.ColorIndex.HasValue)
        {
            ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, (short)patch.ColorIndex.Value);
        }

        if (patch.LinetypeId.HasValue)
        {
            ltr.LinetypeObjectId = patch.LinetypeId.Value;
        }

        if (patch.LineWeightValue.HasValue)
        {
            ltr.LineWeight = patch.LineWeightValue.Value;
        }

        if (patch.IsPlottable.HasValue)
        {
            ltr.IsPlottable = patch.IsPlottable.Value;
        }

        if (patch.Description is not null)
        {
            ltr.Description = patch.Description;
        }

        if (setCurrent)
        {
            db.Clayer = ltr.ObjectId;
        }
    }

    private static string SerializeLayerResult(
        string docName,
        long revision,
        Database db,
        Transaction tr,
        LayerTableRecord ltr,
        bool linetypeAutoLoaded)
    {
        string ltName = "Continuous";
        if (!ltr.LinetypeObjectId.IsNull &&
            tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead, false) is LinetypeTableRecord ltRec)
        {
            ltName = ltRec.Name;
        }

        List<string>? warnings = linetypeAutoLoaded
            ? [$"Linetype '{ltName}' was auto-loaded from standard .lin definitions."]
            : null;

        return JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            layer = ltr.Name,
            is_on = !ltr.IsOff,
            is_frozen = ltr.IsFrozen,
            is_locked = ltr.IsLocked,
            color_index = (int)ltr.Color.ColorIndex,
            linetype = ltName,
            lineweight = LineWeightPolicy.FormatMillimeters((int)ltr.LineWeight),
            is_plottable = ltr.IsPlottable,
            description = ltr.Description ?? string.Empty,
            is_current = db.Clayer == ltr.ObjectId,
            warnings,
            document = docName,
            revision,
        }, GatewayProtocol.WireJsonOptions);
    }
}
