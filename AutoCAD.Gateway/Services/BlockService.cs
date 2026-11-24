using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Native ObjectARX service for block definitions, block reference attributes, external references (Xrefs),
/// and safe entity exploding inside <c>Acad.Read</c>/<c>Acad.Write</c> transactions.
/// </summary>
public static class BlockService
{
    /// <summary>Creates or updates a reusable <see cref="BlockTableRecord"/> from entity handles or inline definitions.</summary>
    /// <param name="body">JSON payload containing block name, handles or inline operations, base point, attribute definitions, and overwrite flag.</param>
    /// <returns>Serialized JSON summary including block name, origin, and entity count.</returns>
    /// <exception cref="AcadGatewayException">Thrown when names, handles, operations, or overwrite constraints are invalid.</exception>
    public static Task<string> DefineBlockAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        if (!SecurityPolicy.IsValidSymbolName(name) ||
            name.StartsWith('*') ||
            string.Equals(name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, BlockTableRecord.PaperSpace, StringComparison.OrdinalIgnoreCase))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid block definition name '{name}'.");
        }

        bool overwrite = DatabaseHelpers.GetBoolOpt(root, "overwrite") ?? false;
        Point3d basePt = DatabaseHelpers.ReadPoint3dOpt(root, "base_point") ?? Point3d.Origin;
        bool hasHandles = root.TryGetProperty("handles", out JsonElement handlesEl) && handlesEl.ValueKind != JsonValueKind.Null;
        if (hasHandles)
        {
            if (handlesEl.ValueKind != JsonValueKind.Array)
            {
                throw new AcadGatewayException("bad_argument", "'handles' must be an array of hexadecimal entity handles.");
            }

            hasHandles = handlesEl.GetArrayLength() > 0;
        }

        bool hasOps = root.TryGetProperty("operations", out JsonElement opsEl) && opsEl.ValueKind != JsonValueKind.Null;
        if (!hasHandles && !hasOps)
        {
            throw new AcadGatewayException("bad_argument", "Supply either source 'handles' or inline 'operations' to define a block.");
        }

        if (hasOps && (opsEl.ValueKind != JsonValueKind.Array || opsEl.GetArrayLength() == 0 || opsEl.GetArrayLength() > SecurityPolicy.MaxBatchOperations))
        {
            throw new AcadGatewayException("bad_argument", $"'operations' must be an array of 1..{SecurityPolicy.MaxBatchOperations} objects.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            // Validate handle references BEFORE mutating or erasing any existing block definition.
            List<Entity>? clonedSources = null;
            if (hasHandles)
            {
                List<ObjectId> sourceIds = DatabaseHelpers.ResolveHandles(ctx.Db, root);
                clonedSources = new List<Entity>(sourceIds.Count);
                try
                {
                    Matrix3d shiftToOrigin = Matrix3d.Displacement(Point3d.Origin - basePt);
                    foreach (ObjectId srcId in sourceIds)
                    {
                        if (ctx.Tr.GetObject(srcId, OpenMode.ForRead, false) is not Entity srcEnt)
                        {
                            throw new AcadGatewayException("bad_argument", $"Handle '{srcId.Handle}' is not a top-level entity.");
                        }

                        var clone = (Entity)srcEnt.Clone();
                        if (!basePt.IsEqualTo(Point3d.Origin))
                        {
                            clone.TransformBy(shiftToOrigin);
                        }

                        clonedSources.Add(clone);
                    }
                }
                catch
                {
                    foreach (Entity c in clonedSources) c.Dispose();
                    throw;
                }
            }

            try
            {
                var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr;
                if (bt.Has(name))
                {
                    if (!overwrite)
                    {
                        throw new AcadGatewayException("already_exists", $"Block definition '{name}' already exists. Pass overwrite=true to redefine it.");
                    }

                    btr = (BlockTableRecord)ctx.Tr.GetObject(bt[name], OpenMode.ForWrite);
                    if (btr.IsFromExternalReference)
                    {
                        throw new AcadGatewayException("bad_argument", $"Cannot overwrite external reference '{name}'.");
                    }

                    foreach (ObjectId oldId in btr)
                    {
                        if (ctx.Tr.GetObject(oldId, OpenMode.ForWrite, false) is DBObject oldObj)
                        {
                            oldObj.Erase();
                        }
                    }
                }
                else
                {
                    bt.UpgradeOpen();
                    btr = new BlockTableRecord { Name = name };
                    bt.Add(btr);
                    ctx.Tr.AddNewlyCreatedDBObject(btr, true);
                }

                // Cloned handle geometry is shifted into block local origin (0,0,0), matching inline operations.
                btr.Origin = Point3d.Origin;
                int entityCount = 0;

                if (clonedSources is not null)
                {
                    for (int i = 0; i < clonedSources.Count; i++)
                    {
                        Entity clone = clonedSources[i];
                        clonedSources[i] = null!;
                        DatabaseHelpers.AppendEntity(btr, ctx.Tr, clone);
                        entityCount++;
                    }
                }

                if (hasOps)
                {
                    foreach (JsonElement opSpec in opsEl.EnumerateArray())
                    {
                        if (opSpec.ValueKind != JsonValueKind.Object)
                        {
                            throw new AcadGatewayException("bad_argument", "Each block definition operation must be a JSON object.");
                        }

                        string type = DatabaseHelpers.GetRequiredString(opSpec, "type");
                        EntityService.CreateEntityInTransaction(ctx.Db, ctx.Tr, opSpec, type, targetSpace: btr);
                        entityCount++;
                    }
                }

                if (root.TryGetProperty("attributes", out JsonElement attrsEl) && attrsEl.ValueKind != JsonValueKind.Null)
                {
                    AppendAttributeDefinitions(btr, ctx.Db, ctx.Tr, attrsEl);
                }

                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    ok = true,
                    block_name = btr.Name,
                    origin = new[] { btr.Origin.X, btr.Origin.Y, btr.Origin.Z },
                    entity_count = entityCount,
                    document = ctx.Doc.Name,
                    revision = ctx.Session.Revision,
                }, GatewayProtocol.WireJsonOptions);
            }
            finally
            {
                if (clonedSources is not null)
                {
                    foreach (Entity? unadded in clonedSources)
                    {
                        unadded?.Dispose();
                    }
                }
            }
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    /// <summary>Inspects entities, origin, Xref state, and attribute definitions of a named block.</summary>
    /// <param name="rawName">Block definition name.</param>
    /// <param name="documentName">Optional document path or basename.</param>
    /// <returns>Serialized JSON inspection payload.</returns>
    /// <exception cref="AcadGatewayException">Thrown when block name is missing, invalid, or not found.</exception>
    public static Task<string> GetBlockDefinitionAsync(string? rawName, string? documentName = null)
    {
        if (string.IsNullOrWhiteSpace(rawName) || !SecurityPolicy.IsValidSymbolName(rawName))
        {
            throw new AcadGatewayException("bad_query", "Valid query parameter 'name' is required.");
        }

        string name = rawName.Trim();
        return Acad.Read(ctx =>
        {
            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(name))
            {
                throw new AcadGatewayException("not_found", $"Block definition '{name}' does not exist in this drawing.");
            }

            var btr = (BlockTableRecord)ctx.Tr.GetObject(bt[name], OpenMode.ForRead);
            var entities = new List<object>();
            var attributes = new List<object>();

            foreach (ObjectId objId in btr)
            {
                if (objId.IsNull || objId.IsErased) continue;
                if (ctx.Tr.GetObject(objId, OpenMode.ForRead, false) is AttributeDefinition attDef)
                {
                    attributes.Add(new
                    {
                        tag = attDef.Tag,
                        prompt = attDef.Prompt,
                        default_value = attDef.TextString,
                        constant = attDef.Constant,
                        height = attDef.Height,
                    });
                }
                else if (ctx.Tr.GetObject(objId, OpenMode.ForRead, false) is Entity ent)
                {
                    entities.Add(new
                    {
                        handle = ent.Handle.ToString(),
                        type = ent.GetRXClass().DxfName,
                        layer = ent.Layer,
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                name = btr.Name,
                origin = new[] { btr.Origin.X, btr.Origin.Y, btr.Origin.Z },
                is_xref = btr.IsFromExternalReference,
                xref_path = btr.IsFromExternalReference ? btr.PathName : null,
                entity_count = entities.Count,
                entities,
                attributes,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    /// <summary>Reads all attributes on a <see cref="BlockReference"/> identified by handle.</summary>
    /// <param name="handle">Hexadecimal DXF handle of the block reference.</param>
    /// <param name="documentName">Optional document path or basename.</param>
    /// <returns>Serialized JSON dictionary of block attributes.</returns>
    /// <exception cref="AcadGatewayException">Thrown when the handle is invalid or not a BlockReference.</exception>
    public static Task<string> GetBlockAttributesAsync(string? handle, string? documentName = null)
    {
        if (string.IsNullOrWhiteSpace(handle) || !SecurityPolicy.IsValidEntityHandle(handle))
        {
            throw new AcadGatewayException("bad_handle", $"'{handle}' is not a valid hexadecimal handle.");
        }

        return Acad.Read(ctx =>
        {
            ObjectId id = Acad.ToObjectId(ctx.Db, handle);
            if (ctx.Tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference br)
            {
                throw new AcadGatewayException("bad_argument", $"Entity '{handle}' is not a BlockReference.");
            }

            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var details = new List<object>();
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (ctx.Tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference att)
                {
                    attrs[att.Tag] = att.TextString ?? string.Empty;
                    details.Add(new
                    {
                        tag = att.Tag,
                        value = att.TextString ?? string.Empty,
                        is_constant = att.IsConstant,
                        invisible = att.Invisible,
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = br.Handle.ToString(),
                block_name = br.Name,
                attributes = attrs,
                attribute_details = details,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);
    }

    /// <summary>Updates existing tag values on a <see cref="BlockReference"/> inside one <c>Acad.Write</c> transaction.</summary>
    /// <param name="body">JSON payload containing handle, attributes tag map, and optional revision/document.</param>
    /// <returns>Serialized JSON update summary.</returns>
    /// <exception cref="AcadGatewayException">Thrown when the handle is invalid or any supplied attribute tag is missing.</exception>
    public static Task<string> SetBlockAttributesAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string handle = DatabaseHelpers.GetRequiredString(root, "handle");
        if (!SecurityPolicy.IsValidEntityHandle(handle))
        {
            throw new AcadGatewayException("bad_handle", $"'{handle}' is not a valid hexadecimal handle.");
        }

        if (!root.TryGetProperty("attributes", out JsonElement attrsEl))
        {
            throw new AcadGatewayException("bad_argument", "'attributes' must be a JSON object of tag -> value pairs.");
        }

        Dictionary<string, string> updates = DatabaseHelpers.ReadStringDictionary(attrsEl, "attributes");
        if (updates.Count == 0)
        {
            throw new AcadGatewayException("bad_argument", "'attributes' cannot be empty.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            ObjectId id = Acad.ToObjectId(ctx.Db, handle);
            if (ctx.Tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference br)
            {
                throw new AcadGatewayException("bad_argument", $"Entity '{handle}' is not a BlockReference.");
            }

            var updatedTags = new List<string>();
            var remaining = new HashSet<string>(updates.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (ctx.Tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference attRead &&
                    updates.TryGetValue(attRead.Tag, out string? newVal))
                {
                    var attWrite = (AttributeReference)ctx.Tr.GetObject(attId, OpenMode.ForWrite);
                    attWrite.TextString = newVal;
                    updatedTags.Add(attWrite.Tag);
                    remaining.Remove(attWrite.Tag);
                }
            }

            if (remaining.Count > 0)
            {
                throw new AcadGatewayException(
                    "not_found",
                    $"BlockReference '{handle}' does not contain attribute tag(s): {string.Join(", ", remaining)}.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = br.Handle.ToString(),
                updated_tags = updatedTags,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    /// <summary>Lists external references (Xrefs) in the active drawing along with resolution status.</summary>
    /// <param name="documentName">Optional document path or basename.</param>
    /// <returns>Serialized JSON list of Xrefs.</returns>
    public static Task<string> ListXrefsAsync(string? documentName = null) =>
        Acad.Read(ctx =>
        {
            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            var xrefs = new List<object>();

            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)ctx.Tr.GetObject(id, OpenMode.ForRead);
                if (!btr.IsFromExternalReference)
                {
                    continue;
                }

                xrefs.Add(new
                {
                    name = btr.Name,
                    path = btr.PathName,
                    status = btr.XrefStatus.ToString(),
                    is_unloaded = btr.IsUnloaded,
                    is_resolved = btr.XrefStatus == XrefStatus.Resolved,
                });
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                count = xrefs.Count,
                xrefs,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName);

    /// <summary>Explodes compound entities (polylines, blocks, dimensions) inside one atomic transaction.</summary>
    /// <param name="body">JSON payload containing handles, <c>erase_source</c> flag, and optional revision/document.</param>
    /// <returns>Serialized JSON summary with created and erased handles.</returns>
    /// <exception cref="AcadGatewayException">Thrown when handles are invalid or yield no exploded pieces.</exception>
    public static Task<string> ExplodeEntitiesAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        bool eraseSource = DatabaseHelpers.GetBoolOpt(root, "erase_source") ?? true;
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            List<ObjectId> ids = DatabaseHelpers.ResolveHandles(ctx.Db, root);
            var createdHandles = new List<string>();
            var erasedHandles = new List<string>();

            foreach (ObjectId id in ids)
            {
                if (ctx.Tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                {
                    throw new AcadGatewayException("bad_argument", $"Handle '{id.Handle}' is not a top-level drawing entity.");
                }

                if (ctx.Tr.GetObject(ent.BlockId, OpenMode.ForWrite, false) is not BlockTableRecord owner)
                {
                    throw new AcadGatewayException("bad_argument", $"Entity '{ent.Handle}' cannot be exploded outside a block space.");
                }

                // Preserve visible AttributeReference values as DBText before Explode strips them into ATTDEFs.
                var attrTexts = new List<DBText>();
                using var pieces = new DBObjectCollection();
                try
                {
                    if (ent is BlockReference br)
                    {
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            if (ctx.Tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference attRef && !attRef.Invisible)
                            {
                                attrTexts.Add(ConvertAttributeToText(ctx.Db, attRef));
                            }
                        }
                    }

                    ent.Explode(pieces);
                    if (pieces.Count == 0 && attrTexts.Count == 0)
                    {
                        throw new AcadGatewayException("bad_argument", $"Entity '{ent.Handle}' ({ent.GetRXClass().DxfName}) produced no exploded geometry.");
                    }

                    var addedSet = new HashSet<DBObject>();
                    try
                    {
                        foreach (DBObject obj in pieces)
                        {
                            if (obj is AttributeDefinition)
                            {
                                continue;
                            }

                            if (obj is Entity pieceEnt)
                            {
                                ObjectId pieceId = owner.AppendEntity(pieceEnt);
                                ctx.Tr.AddNewlyCreatedDBObject(pieceEnt, true);
                                addedSet.Add(obj);
                                createdHandles.Add(pieceId.Handle.ToString());
                            }
                        }
                    }
                    finally
                    {
                        foreach (DBObject obj in pieces)
                        {
                            if (!addedSet.Contains(obj))
                            {
                                obj.Dispose();
                            }
                        }
                    }

                    for (int i = 0; i < attrTexts.Count; i++)
                    {
                        DBText txt = attrTexts[i];
                        attrTexts[i] = null!;
                        ObjectId txtId = DatabaseHelpers.AppendEntity(owner, ctx.Tr, txt);
                        createdHandles.Add(txtId.Handle.ToString());
                    }
                }
                finally
                {
                    foreach (DBText? leftover in attrTexts)
                    {
                        leftover?.Dispose();
                    }
                }

                if (eraseSource)
                {
                    var writeEnt = (Entity)ctx.Tr.GetObject(id, OpenMode.ForWrite);
                    string h = writeEnt.Handle.ToString();
                    writeEnt.Erase();
                    erasedHandles.Add(h);
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                created_count = createdHandles.Count,
                created_handles = createdHandles,
                erased_handles = erasedHandles,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    private static DBText ConvertAttributeToText(Database db, AttributeReference attRef)
    {
        var txt = new DBText();
        txt.SetDatabaseDefaults(db);
        txt.LayerId = attRef.LayerId;
        txt.ColorIndex = attRef.ColorIndex;
        txt.TextStyleId = attRef.TextStyleId;
        txt.Height = attRef.Height;
        txt.Rotation = attRef.Rotation;
        txt.WidthFactor = attRef.WidthFactor;
        txt.Oblique = attRef.Oblique;
        txt.TextString = attRef.TextString;
        txt.Position = attRef.Position;
        txt.HorizontalMode = attRef.HorizontalMode;
        txt.VerticalMode = attRef.VerticalMode;
        if (attRef.HorizontalMode != TextHorizontalMode.TextLeft || attRef.VerticalMode != TextVerticalMode.TextBase)
        {
            txt.AlignmentPoint = attRef.AlignmentPoint;
            txt.AdjustAlignment(db);
        }

        return txt;
    }

    private static void AppendAttributeDefinitions(BlockTableRecord btr, Database db, Transaction tr, JsonElement attrsEl)
    {
        if (attrsEl.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", "'attributes' must be an array of attribute definition objects.");
        }

        foreach (JsonElement item in attrsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new AcadGatewayException("bad_argument", "Each attribute definition must be a JSON object.");
            }

            string tag = DatabaseHelpers.GetRequiredString(item, "tag");
            if (!SecurityPolicy.IsValidSymbolName(tag) || tag.Any(char.IsWhiteSpace))
            {
                throw new AcadGatewayException("bad_argument", $"Invalid attribute tag '{tag}'.");
            }

            string prompt = DatabaseHelpers.GetStringOpt(item, "prompt") ?? tag;
            string defVal = DatabaseHelpers.GetStringOpt(item, "default_value")
                ?? DatabaseHelpers.GetStringOpt(item, "default")
                ?? string.Empty;
            Point3d pos = DatabaseHelpers.ReadPoint3dOpt(item, "position") ?? Point3d.Origin;
            double height = DatabaseHelpers.GetFiniteDoubleOpt(item, "height") ?? 0.25;
            if (height <= 0)
            {
                throw new AcadGatewayException("bad_argument", "Attribute height must be greater than zero.");
            }

            DatabaseHelpers.CreateAndAppend(
                btr,
                tr,
                db,
                () => new AttributeDefinition(pos, defVal, tag, prompt, db.Textstyle),
                attDef => attDef.Height = height);
        }
    }
}
