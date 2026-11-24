using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Manages AutoCAD multi-document lifecycle, command-line cancellation,
/// Edit Guard authorization, and safe system variable inspection/mutation.
/// </summary>
public static class DocumentControlService
{
    /// <summary>Lists all open AutoCAD documents with session IDs, revisions, and Edit Guard ownership.</summary>
    public static Task<string> ListDocumentsAsync()
    {
        return MainThread.InvokeAsync(() =>
        {
            Document? active = AcApp.DocumentManager.MdiActiveDocument;
            DocumentSessionManager.ObserveActiveDocument(active?.Name);
            var docs = new List<object>();

            foreach (Document doc in AcApp.DocumentManager)
            {
                DocumentSession session = DocumentSessionManager.GetOrCreate(doc);
                docs.Add(new
                {
                    session_id = session.Id,
                    name = doc.Name,
                    filename = Path.GetFileName(doc.Name),
                    is_active = ReferenceEquals(doc, active),
                    is_read_only = doc.IsReadOnly,
                    is_managed = session.IsManaged,
                    revision = session.Revision,
                });
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                edit_guard_enabled = DocumentSessionManager.EditGuardEnabled,
                count = docs.Count,
                documents = docs,
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>Queues <c>Ctrl+C</c> cancellation sequences and reads updated command-line state after one idle yield.</summary>
    public static async Task<string> CancelCommandAsync()
    {
        string docName = await MainThread.InvokeAsync(() =>
        {
            Document doc = Acad.ActiveDocument;
            doc.SendStringToExecute("\x03\x03", true, false, false);
            return doc.Name;
        }).ConfigureAwait(false);

        // Allow AutoCAD one command-loop pass to process the queued ^C^C before sampling CMDACTIVE.
        await Task.Delay(50).ConfigureAwait(false);
        var (active, names) = await MainThread.InvokeAsync(Acad.ReadCommandState).ConfigureAwait(false);
        CommandLineState? cmdState = CommandLineState.FromSystemVariables(active, names);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            document = docName,
            message = "Cancellation signal processed.",
            command_state = cmdState?.State ?? "idle",
            pending_command = cmdState?.PendingCommand,
            cmd_active = active,
        }, GatewayProtocol.WireJsonOptions);
    }

    /// <summary>Marks an existing open document as managed so mutations succeed when Edit Guard is active.</summary>
    public static Task<string> AllowEditDocumentAsync(string body)
    {
        JsonElement root = string.IsNullOrWhiteSpace(body) ? default : DatabaseHelpers.ParseJsonObject(body);
        string? documentName = root.ValueKind == JsonValueKind.Object ? DatabaseHelpers.GetStringOpt(root, "document") : null;
        bool allow = root.ValueKind == JsonValueKind.Object ? (DatabaseHelpers.GetBoolOpt(root, "allow") ?? true) : true;

        return MainThread.InvokeAsync(() =>
        {
            Document doc = Acad.ResolveDocument(documentName);
            DocumentSession session = DocumentSessionManager.GetOrCreate(doc, markManaged: allow);
            if (!allow)
            {
                session.IsManaged = false;
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                session_id = session.Id,
                document = doc.Name,
                is_managed = session.IsManaged,
                edit_guard_enabled = DocumentSessionManager.EditGuardEnabled,
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>Creates a new drawing document and marks it Gateway-managed so edits proceed under Edit Guard.</summary>
    public static Task<string> NewDrawingAsync(string body)
    {
        string? template = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            JsonElement root = DatabaseHelpers.ParseJsonObject(body);
            template = DatabaseHelpers.GetStringOpt(root, "template");
        }

        string resolvedTemplate = "acadiso.dwt";
        if (!string.IsNullOrWhiteSpace(template))
        {
            if (!SecurityPolicy.ValidateLocalFilePath(template, out string normTemplate, out string? pathErr, ".dwt"))
            {
                throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid template path.");
            }

            DatabaseHelpers.EnsureNoReparsePoints(normTemplate);

            if (!File.Exists(normTemplate))
            {
                throw new AcadGatewayException("not_found", $"Template file not found: '{normTemplate}'.");
            }

            resolvedTemplate = normTemplate;
        }

        return MainThread.InvokeAsync(() =>
        {
            Document newDoc = AcApp.DocumentManager.Add(resolvedTemplate);
            AcApp.DocumentManager.MdiActiveDocument = newDoc;
            DocumentSessionManager.ObserveActiveDocument(newDoc.Name);
            DocumentSession session = DocumentSessionManager.GetOrCreate(newDoc, markManaged: true);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                session_id = session.Id,
                document = newDoc.Name,
                template = resolvedTemplate,
                is_managed = session.IsManaged,
                revision = session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>Opens an existing <c>.dwg</c> file cleanly without automatically bypassing Edit Guard.</summary>
    public static Task<string> OpenDrawingAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string rawPath = DatabaseHelpers.GetRequiredString(root, "file_path");
        bool readOnly = DatabaseHelpers.GetBoolOpt(root, "read_only") ?? false;

        if (!SecurityPolicy.ValidateLocalFilePath(rawPath, out string normalized, out string? pathErr, ".dwg"))
        {
            throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid drawing path.");
        }

        DatabaseHelpers.EnsureNoReparsePoints(normalized);

        if (!File.Exists(normalized))
        {
            throw new AcadGatewayException("not_found", $"Drawing file not found: '{normalized}'.");
        }

        return MainThread.InvokeAsync(() =>
        {
            Document? alreadyOpen = DocumentSessionManager.FindOpenDocument(normalized);
            Document target = alreadyOpen ?? AcApp.DocumentManager.Open(normalized, readOnly);
            AcApp.DocumentManager.MdiActiveDocument = target;
            DocumentSessionManager.ObserveActiveDocument(target.Name);
            DocumentSession session = DocumentSessionManager.GetOrCreate(target);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                already_open = alreadyOpen is not null,
                session_id = session.Id,
                document = target.Name,
                is_read_only = target.IsReadOnly,
                is_managed = session.IsManaged,
                revision = session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>Switches AutoCAD's active MDI document by full path, unique filename, or session ID.</summary>
    public static Task<string> ActivateDocumentAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string targetName = DatabaseHelpers.GetRequiredString(root, "document");

        return MainThread.InvokeAsync(() =>
        {
            Document doc = Acad.ResolveDocument(targetName);
            AcApp.DocumentManager.MdiActiveDocument = doc;
            DocumentSessionManager.ObserveActiveDocument(doc.Name);
            DocumentSession session = DocumentSessionManager.GetOrCreate(doc);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                session_id = session.Id,
                document = doc.Name,
                is_read_only = doc.IsReadOnly,
                is_managed = session.IsManaged,
                revision = session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>
    /// Closes an open drawing with Edit Guard, quiescence, and explicit save/discard confirmation.
    /// </summary>
    public static Task<string> CloseDocumentAsync(string body)
    {
        JsonElement root = string.IsNullOrWhiteSpace(body) ? default : DatabaseHelpers.ParseJsonObject(body);
        string? targetName = root.ValueKind == JsonValueKind.Object ? DatabaseHelpers.GetStringOpt(root, "document") : null;
        bool save = root.ValueKind == JsonValueKind.Object && (DatabaseHelpers.GetBoolOpt(root, "save") ?? false);
        bool discardChanges = root.ValueKind == JsonValueKind.Object && (DatabaseHelpers.GetBoolOpt(root, "discard_changes") ?? false);
        string? saveAsPath = root.ValueKind == JsonValueKind.Object ? DatabaseHelpers.GetStringOpt(root, "save_as") : null;

        if (!string.IsNullOrWhiteSpace(saveAsPath))
        {
            if (!SecurityPolicy.ValidateLocalFilePath(saveAsPath, out string validatedSavePath, out string? pathErr, ".dwg"))
            {
                throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid 'save_as' drawing path.");
            }

            DatabaseHelpers.EnsureNoReparsePoints(validatedSavePath);

            saveAsPath = validatedSavePath;
            save = true;
        }

        if (save && discardChanges)
        {
            throw new AcadGatewayException("bad_argument", "Specify either 'save=true' or 'discard_changes=true', not both.");
        }

        return MainThread.InvokeAsync(() =>
        {
            Document doc = Acad.ResolveDocument(targetName);
            DocumentSession session = DocumentSessionManager.GetOrCreate(doc);
            if (DocumentSessionManager.EditGuardEnabled && !session.IsManaged)
            {
                throw new AcadGatewayException(
                    "edit_guard",
                    $"Drawing '{doc.Name}' is protected by Edit Guard. Call allow_edit_document before closing via automation.");
            }

            Acad.EnsureQuiescent(doc);
            string closedName = doc.Name;
            bool isActive = ReferenceEquals(doc, AcApp.DocumentManager.MdiActiveDocument);
            int dbmod = isActive ? Convert.ToInt32(AcApp.GetSystemVariable("DBMOD"), CultureInfo.InvariantCulture) : 1;

            if (!save && !discardChanges && dbmod != 0)
            {
                throw new AcadGatewayException(
                    "unsaved_changes",
                    $"Drawing '{closedName}' may have unsaved modifications. Pass save=true or discard_changes=true.");
            }

            if (save)
            {
                if (doc.IsReadOnly && string.IsNullOrWhiteSpace(saveAsPath))
                {
                    throw new AcadGatewayException("read_only", $"Drawing '{closedName}' is read-only. Supply 'save_as' or 'discard_changes=true'.");
                }

                string destination = !string.IsNullOrWhiteSpace(saveAsPath) ? saveAsPath : doc.Name;
                if (!Path.IsPathFullyQualified(destination))
                {
                    throw new AcadGatewayException(
                        "bad_argument",
                        $"Unsaved drawing '{closedName}' requires an explicit 'save_as' .dwg path to save before closing.");
                }

                doc.CloseAndSave(destination);
            }
            else
            {
                doc.CloseAndDiscard();
            }

            DocumentSessionManager.ObserveActiveDocument(AcApp.DocumentManager.MdiActiveDocument?.Name);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                closed_document = closedName,
                saved = save,
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>Reads an AutoCAD system variable while blocking security-sensitive variables.</summary>
    public static Task<string> GetSystemVariableAsync(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            throw new AcadGatewayException("bad_query", "Query parameter 'name' is required.");
        }

        string name = rawName.Trim();
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_query", $"Invalid system variable name '{name}'.");
        }

        if (SecurityPolicy.IsBlockedSystemVariable(name))
        {
            throw new AcadGatewayException("forbidden", $"Access to security-sensitive system variable '{name}' is blocked.");
        }

        return MainThread.InvokeAsync(() =>
        {
            object? value;
            try
            {
                value = AcApp.GetSystemVariable(name);
            }
            catch (Exception ex)
            {
                throw new AcadGatewayException("not_found", $"Unknown AutoCAD system variable '{name}'.", ex.Message);
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                name = name.ToUpperInvariant(),
                value = ConvertSysVarForJson(value),
            }, GatewayProtocol.WireJsonOptions);
        });
    }

    /// <summary>
    /// Sets an AutoCAD system variable on the active MDI document while blocking security-sensitive variables
    /// and enforcing Edit Guard protection.
    /// </summary>
    public static Task<string> SetSystemVariableAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string name = DatabaseHelpers.GetRequiredString(root, "name");
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid system variable name '{name}'.");
        }

        if (SecurityPolicy.IsBlockedSystemVariable(name))
        {
            throw new AcadGatewayException("forbidden", $"Modifying security-sensitive system variable '{name}' is blocked.");
        }

        if (!root.TryGetProperty("value", out JsonElement valEl) || valEl.ValueKind == JsonValueKind.Null)
        {
            throw new AcadGatewayException("bad_argument", "Parameter 'value' is required.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");

        return Acad.RunOnDocumentAsync((doc, _) =>
        {
            if (!ReferenceEquals(doc, AcApp.DocumentManager.MdiActiveDocument))
            {
                throw new AcadGatewayException(
                    "bad_argument",
                    $"System variable '{name}' can only be modified on the active drawing. Activate '{doc.Name}' first.");
            }

            object? current;
            try
            {
                current = AcApp.GetSystemVariable(name);
            }
            catch (Exception ex)
            {
                throw new AcadGatewayException("not_found", $"Unknown AutoCAD system variable '{name}'.", ex.Message);
            }

            try
            {
                object coerced = CoerceSysVarValue(name, valEl, current);
                AcApp.SetSystemVariable(name, coerced);
            }
            catch (AcadGatewayException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AcadGatewayException("bad_argument", $"Could not set system variable '{name}': {ex.Message}");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                name = name.ToUpperInvariant(),
                value = ConvertSysVarForJson(AcApp.GetSystemVariable(name)),
                document = doc.Name,
            }, GatewayProtocol.WireJsonOptions);
        }, write: true, requireIdle: true, documentName: documentName);
    }

    private static object? ConvertSysVarForJson(object? value) => value switch
    {
        null => null,
        Autodesk.AutoCAD.Geometry.Point2d p2 => new[] { p2.X, p2.Y },
        Autodesk.AutoCAD.Geometry.Point3d p3 => new[] { p3.X, p3.Y, p3.Z },
        double d => double.IsFinite(d) ? d : null,
        _ => value,
    };

    private static object CoerceSysVarValue(string name, JsonElement valEl, object? current)
    {
        if (current is string)
        {
            return valEl.ValueKind switch
            {
                JsonValueKind.String => valEl.GetString() ?? string.Empty,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => valEl.GetRawText(),
                _ => throw new AcadGatewayException("bad_argument", $"Expected string value for system variable '{name}'."),
            };
        }

        if (valEl.ValueKind == JsonValueKind.String)
        {
            string sVal = valEl.GetString() ?? string.Empty;
            if (current is short && short.TryParse(sVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out short sParsed))
            {
                return sParsed;
            }

            if (current is int && int.TryParse(sVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iParsed))
            {
                return iParsed;
            }

            if (current is double && double.TryParse(sVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double dParsed) && double.IsFinite(dParsed))
            {
                return dParsed;
            }

            return sVal;
        }

        if (valEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return valEl.GetBoolean() ? (short)1 : (short)0;
        }

        if (valEl.ValueKind == JsonValueKind.Number)
        {
            if (current is short && valEl.TryGetInt16(out short s))
            {
                return s;
            }

            if (current is int && valEl.TryGetInt32(out int i))
            {
                return i;
            }

            if (valEl.TryGetDouble(out double d) && double.IsFinite(d))
            {
                if (current is short)
                {
                    return Convert.ToInt16(d, CultureInfo.InvariantCulture);
                }

                if (current is int)
                {
                    return Convert.ToInt32(d, CultureInfo.InvariantCulture);
                }

                return d;
            }
        }

        throw new AcadGatewayException("bad_argument", $"Invalid value type for system variable '{name}'.");
    }
}
