using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Native ObjectARX entity creation, modification, transformation, deletion,
/// and single-transaction batch execution service.
/// </summary>
public static class EntityService
{
    /// <summary>Creates a single native entity inside one <c>Acad.Write</c> transaction.</summary>
    /// <param name="body">JSON specification with entity <c>type</c>, geometry coordinates, and layer/color metadata.</param>
    /// <returns>Serialized JSON confirmation including created handle and document revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when geometry parameters, symbol names, or Edit Guard state are invalid.</exception>
    public static Task<string> CreateAsync(string body) =>
        DatabaseHelpers.WriteEnvelopeAsync(body, (ctx, root) => new
        {
            status = "success",
            ok = true,
            handle = CreateEntityInTransaction(ctx.Db, ctx.Tr, root, DatabaseHelpers.GetStringOpt(root, "type")),
            document = ctx.Doc.Name,
            revision = ctx.Session.Revision,
        });

    /// <summary>Modifies properties or coordinates of an existing entity identified by hexadecimal handle.</summary>
    /// <param name="body">JSON payload containing <c>handle</c> and updated entity fields.</param>
    /// <returns>Serialized JSON confirmation including handle and document revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when the handle is missing/invalid or property values are out of range.</exception>
    public static Task<string> ModifyAsync(string body) =>
        DatabaseHelpers.WriteEnvelopeAsync(body, (ctx, root) => new
        {
            status = "success",
            ok = true,
            handle = EntityModifier.ModifyEntityInTransaction(ctx.Db, ctx.Tr, root),
            document = ctx.Doc.Name,
            revision = ctx.Session.Revision,
        });

    /// <summary>Applies a geometric transform (move, rotate, scale, copy, mirror) to one or more handles atomically.</summary>
    /// <param name="body">JSON payload containing handles, <c>operation</c>, and transform parameters.</param>
    /// <returns>Serialized JSON summary with affected handles and document revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when operation parameters or handles are invalid.</exception>
    public static Task<string> TransformAsync(string body) =>
        DatabaseHelpers.WriteEnvelopeAsync(body, (ctx, root) =>
        {
            List<string> handles = EntityTransformer.TransformEntitiesInTransaction(ctx.Db, ctx.Tr, root);
            return new
            {
                status = "success",
                ok = true,
                count = handles.Count,
                handles,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            };
        });

    /// <summary>Erases one or more entities identified by hexadecimal handle in one transaction.</summary>
    /// <param name="body">JSON payload containing hexadecimal <c>handles</c> to erase.</param>
    /// <returns>Serialized JSON summary with deleted handle count, handles, and revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when any handle is invalid or non-erasable.</exception>
    public static Task<string> DeleteAsync(string body) =>
        DatabaseHelpers.WriteEnvelopeAsync(body, (ctx, root) =>
        {
            List<string> deleted = EntityTransformer.DeleteEntitiesInTransaction(ctx.Db, ctx.Tr, root);
            return new
            {
                status = "success",
                ok = true,
                deleted_count = deleted.Count,
                handles = deleted,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            };
        });

    /// <summary>
    /// Executes up to <see cref="SecurityPolicy.MaxBatchOperations"/> layer and entity mutations inside one
    /// <c>Acad.Write</c> transaction. When <c>rollback_on_error</c> is true (default), any error rolls back
    /// the entire batch atomically; when false, each failed item is rolled back via an isolated sub-transaction.
    /// </summary>
    /// <param name="body">JSON batch payload with <c>operations</c> array and optional <c>rollback_on_error</c>/<c>default_layer</c>.</param>
    /// <returns>Serialized JSON batch report including per-item results and optional partial failure detail.</returns>
    /// <exception cref="AcadGatewayException">Thrown when batch limits fail or an item fails under strict atomic rollback.</exception>
    public static Task<string> BatchAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        if (!root.TryGetProperty("operations", out JsonElement opsEl) || opsEl.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", "'operations' must be an array of batch operation objects.");
        }

        int count = opsEl.GetArrayLength();
        if (count == 0 || count > SecurityPolicy.MaxBatchOperations)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"'operations' must contain between 1 and {SecurityPolicy.MaxBatchOperations} items (got {count}).");
        }

        bool rollbackOnError = DatabaseHelpers.GetBoolOpt(root, "rollback_on_error") ?? true;
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");
        string defaultLayer = DatabaseHelpers.GetStringOpt(root, "default_layer") ?? "0";
        if (!SecurityPolicy.IsValidSymbolName(defaultLayer))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid default layer name '{defaultLayer}'.");
        }

        return Acad.Write(ctx =>
        {
            var results = new List<object>(count);
            var handles = new List<string>();
            int succeeded = 0;
            int failed = 0;
            int index = 0;

            foreach (JsonElement opSpec in opsEl.EnumerateArray())
            {
                if (opSpec.ValueKind != JsonValueKind.Object)
                {
                    if (rollbackOnError)
                    {
                        throw new AcadGatewayException("bad_argument", $"Operation at index {index} must be a JSON object.");
                    }

                    failed++;
                    results.Add(new { index, ok = false, error_code = "bad_argument", error = "Operation must be a JSON object." });
                    index++;
                    continue;
                }

                if (rollbackOnError)
                {
                    object opResult = ExecuteBatchItem(ctx.Db, ctx.Tr, opSpec, defaultLayer, handles);
                    succeeded++;
                    results.Add(new { index, ok = true, result = opResult });
                }
                else
                {
                    using Transaction subTr = ctx.Db.TransactionManager.StartTransaction();
                    try
                    {
                        object opResult = ExecuteBatchItem(ctx.Db, subTr, opSpec, defaultLayer, handles);
                        subTr.Commit();
                        succeeded++;
                        results.Add(new { index, ok = true, result = opResult });
                    }
                    catch (Exception ex)
                    {
                        subTr.Abort();
                        failed++;
                        string errCode = ex is AcadGatewayException age ? age.Code : "autocad_error";
                        results.Add(new { index, ok = false, error_code = errCode, error = ex.Message });
                    }
                }

                if (handles.Count > SecurityPolicy.MaxGeometryVertices)
                {
                    throw new AcadGatewayException(
                        "bad_argument",
                        $"Batch operations exceeded cumulative limit of {SecurityPolicy.MaxGeometryVertices} handles.");
                }

                index++;
            }

            string status = failed == 0 ? "success" : (succeeded == 0 ? "error" : "partial_error");
            string batchErrCode = succeeded == 0 ? "batch_failed" : "batch_partial_error";
            ToolError? errObj = failed > 0
                ? new ToolError(batchErrCode, $"{failed} of {count} batch operations failed.")
                : null;
            return JsonSerializer.Serialize(new
            {
                status,
                ok = failed == 0,
                rolled_back = false,
                succeeded,
                failed,
                handles,
                results,
                error = errObj,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    internal static string CreateEntityInTransaction(
        Database db,
        Transaction tr,
        JsonElement spec,
        string? explicitType,
        string fallbackLayer = "0",
        BlockTableRecord? targetSpace = null) =>
        EntityBuilder.CreateEntityInTransaction(db, tr, spec, explicitType, fallbackLayer, targetSpace);

    private static object ExecuteBatchItem(
        Database db,
        Transaction tr,
        JsonElement opSpec,
        string defaultLayer,
        List<string> createdOrAffectedHandles)
    {
        string rawOp = (DatabaseHelpers.GetStringOpt(opSpec, "op")
            ?? DatabaseHelpers.GetStringOpt(opSpec, "operation")
            ?? DatabaseHelpers.GetStringOpt(opSpec, "type")
            ?? string.Empty).Trim().ToLowerInvariant();

        switch (rawOp)
        {
            case "create_layer":
            case "layer_create":
            {
                string layerName = DatabaseHelpers.GetRequiredString(opSpec, "name");
                LayerService.ApplyLayerUpsert(db, tr, opSpec, layerName);
                return new { op = "create_layer", layer = layerName };
            }

            case "set_layer_state":
            case "layer_state":
            {
                string layerName = DatabaseHelpers.GetRequiredString(opSpec, "name");
                LayerService.ApplyLayerState(db, tr, opSpec, layerName);
                return new { op = "set_layer_state", layer = layerName };
            }

            case "modify":
            {
                string h = EntityModifier.ModifyEntityInTransaction(db, tr, opSpec);
                createdOrAffectedHandles.Add(h);
                return new { op = "modify", handle = h };
            }

            case "transform":
            {
                var transformed = EntityTransformer.TransformEntitiesInTransaction(db, tr, opSpec);
                createdOrAffectedHandles.AddRange(transformed);
                return new { op = "transform", handles = transformed };
            }

            case "delete":
            case "erase":
            {
                var deleted = EntityTransformer.DeleteEntitiesInTransaction(db, tr, opSpec);
                createdOrAffectedHandles.AddRange(deleted);
                return new { op = "delete", handles = deleted };
            }

            case "create":
            {
                string entityType = DatabaseHelpers.GetRequiredString(opSpec, "type");
                string h = CreateEntityInTransaction(db, tr, opSpec, entityType, defaultLayer);
                createdOrAffectedHandles.Add(h);
                return new { op = "create", handle = h };
            }

            default:
            {
                string h = CreateEntityInTransaction(db, tr, opSpec, rawOp, defaultLayer);
                createdOrAffectedHandles.Add(h);
                return new { op = "create", type = rawOp, handle = h };
            }
        }
    }
}
