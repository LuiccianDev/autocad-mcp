using System;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.Gateway.Contracts;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Active AutoCAD execution context passed into <see cref="Acad.Read{T}"/> and <see cref="Acad.Write{T}"/>.
/// </summary>
public sealed class AcadContext
{
    /// <summary>Target AutoCAD document.</summary>
    public required Document Doc { get; init; }

    /// <summary>Database belonging to <see cref="Doc"/>.</summary>
    public required Database Db { get; init; }

    /// <summary>Active transaction for the call.</summary>
    public required Transaction Tr { get; init; }

    /// <summary>Editor belonging to <see cref="Doc"/>.</summary>
    public required Editor Ed { get; init; }

    /// <summary>Tracked session metadata for <see cref="Doc"/>.</summary>
    public required DocumentSession Session { get; init; }
}

/// <summary>
/// Native ObjectARX transaction helpers executing safely on AutoCAD's main thread.
/// </summary>
public static class Acad
{
    /// <summary>Returns the active MDI drawing or throws <c>no_document</c>.</summary>
    public static Document ActiveDocument =>
        AcApp.DocumentManager.MdiActiveDocument
        ?? throw new AcadGatewayException("no_document", "No active drawing document open in AutoCAD.");

    /// <summary>Resolves the requested open drawing or falls back to <see cref="ActiveDocument"/>.</summary>
    /// <param name="documentName">Optional document path, unique filename, or session ID.</param>
    public static Document ResolveDocument(string? documentName = null) =>
        string.IsNullOrWhiteSpace(documentName)
            ? ActiveDocument
            : (DocumentSessionManager.FindOpenDocument(documentName)
               ?? throw new AcadGatewayException("not_found", $"Open document '{documentName}' not found."));

    /// <summary>
    /// Validates that <paramref name="doc"/> can be safely mutated (not read-only, allowed by Edit Guard,
    /// matching expected revision, and optionally quiescent).
    /// </summary>
    public static void ValidateWritable(
        Document doc,
        DocumentSession session,
        bool requireIdle = true,
        long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(session);

        if (doc.IsReadOnly)
        {
            throw new AcadGatewayException("read_only", $"Drawing '{doc.Name}' is open read-only.");
        }

        if (DocumentSessionManager.EditGuardEnabled && !session.IsManaged)
        {
            throw new AcadGatewayException(
                "edit_guard",
                $"Drawing '{doc.Name}' is protected by Edit Guard. Use new_drawing or allow_edit_document before mutating it.");
        }

        if (expectedRevision.HasValue && expectedRevision.Value != session.Revision)
        {
            throw new AcadGatewayException(
                "revision_conflict",
                $"Drawing revision mismatch (expected {expectedRevision.Value}, current {session.Revision}). Re-query before mutating.");
        }

        if (requireIdle)
        {
            EnsureQuiescent(doc);
        }
    }

    /// <summary>
    /// Executes a read-only database query inside an <c>OpenCloseTransaction</c> that never commits,
    /// guaranteeing safe inspection across active and background open documents.
    /// </summary>
    public static Task<T> Read<T>(
        Func<AcadContext, T> fn,
        string? documentName = null,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fn);
        return RunOnDocumentAsync((doc, session) =>
        {
            using Transaction tr = doc.TransactionManager.StartOpenCloseTransaction();
            var ctx = new AcadContext
            {
                Doc = doc,
                Db = doc.Database,
                Tr = tr,
                Ed = doc.Editor,
                Session = session,
            };

            T result = fn(ctx);
            tr.Abort();
            return result;
        }, write: false, requireIdle: false, expectedRevision: null, documentName, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Executes a mutating operation inside <c>Document.LockDocument()</c> and a single
    /// <c>Document.TransactionManager</c> commit (one Ctrl+Z undo step), enforcing ReadOnly, Busy, and Edit guards.
    /// </summary>
    public static Task<T> Write<T>(
        Func<AcadContext, T> fn,
        string? documentName = null,
        bool requireIdle = true,
        long? expectedRevision = null,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fn);
        return RunOnDocumentAsync((doc, session) =>
        {
            using Transaction tr = doc.TransactionManager.StartTransaction();
            var ctx = new AcadContext
            {
                Doc = doc,
                Db = doc.Database,
                Tr = tr,
                Ed = doc.Editor,
                Session = session,
            };

            T result = fn(ctx);
            tr.Commit();
            return result;
        }, write: true, requireIdle, expectedRevision, documentName, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Runs a callback on AutoCAD's main thread after resolving the target document, updating session
    /// tracking, and (when <paramref name="write"/> is true) validating writability and locking the document.
    /// </summary>
    public static Task<T> RunOnDocumentAsync<T>(
        Func<Document, DocumentSession, T> fn,
        bool write = false,
        bool requireIdle = false,
        long? expectedRevision = null,
        string? documentName = null,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fn);
        return MainThread.InvokeAsync(() =>
        {
            Document doc = ResolveDocument(documentName);
            DocumentSessionManager.ObserveActiveDocument(AcApp.DocumentManager.MdiActiveDocument?.Name);
            DocumentSession session = DocumentSessionManager.GetOrCreate(doc);

            if (write)
            {
                ValidateWritable(doc, session, requireIdle, expectedRevision);
            }

            using DocumentLock? docLock = write ? doc.LockDocument() : null;
            return fn(doc, session);
        }, timeoutMs, cancellationToken);
    }

    /// <summary>Validates that the resolved document revision matches <paramref name="expectedRevision"/> when supplied.</summary>
    public static void CheckRevision(DocumentSession session, long? expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (expectedRevision.HasValue && expectedRevision.Value != session.Revision)
        {
            throw new AcadGatewayException(
                "revision_conflict",
                $"Drawing revision mismatch (expected {expectedRevision.Value}, current {session.Revision}). Re-query before mutating.");
        }
    }

    /// <summary>Verifies that an optional <paramref name="documentName"/> (path, filename, or session ID) refers to <paramref name="activeDoc"/>.</summary>
    public static void EnsureActiveDocument(Document activeDoc, string? documentName, string operationName)
    {
        ArgumentNullException.ThrowIfNull(activeDoc);
        if (string.IsNullOrWhiteSpace(documentName))
        {
            return;
        }

        string target = documentName.Trim();
        Document? resolved = DocumentSessionManager.FindOpenDocument(target);
        if (resolved is not null && ReferenceEquals(resolved, activeDoc))
        {
            return;
        }

        throw new AcadGatewayException(
            "bad_argument",
            $"{operationName} requires document '{target}' to be active; call activate_document first.");
    }

    /// <summary>Throws <c>busy</c> if the target document (or AutoCAD command line when active) is mid-command.</summary>
    public static void EnsureQuiescent(Document doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        bool isActive = ReferenceEquals(doc, AcApp.DocumentManager.MdiActiveDocument);
        int cmdActive = isActive ? Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")) : 0;

        if (!doc.Editor.IsQuiescent || cmdActive != 0)
        {
            string? inProgress = !string.IsNullOrWhiteSpace(doc.CommandInProgress)
                ? doc.CommandInProgress
                : (isActive ? AcApp.GetSystemVariable("CMDNAMES")?.ToString() : null);
            throw new AcadGatewayException(
                "busy",
                "AutoCAD is busy inside an active command, prompt, or dialog.",
                string.IsNullOrWhiteSpace(inProgress) ? $"CMDACTIVE={cmdActive}" : inProgress);
        }
    }

    /// <summary>Reads CMDACTIVE and CMDNAMES safely on AutoCAD's main thread.</summary>
    public static (int CmdActive, string? CmdNames) ReadCommandState()
    {
        int cmdActive = Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE"));
        string? cmdNames = AcApp.GetSystemVariable("CMDNAMES") as string;
        return (cmdActive, cmdNames);
    }

    /// <summary>Converts a hexadecimal DXF handle string to an <see cref="ObjectId"/> in <paramref name="db"/>.</summary>
    public static ObjectId ToObjectId(Database db, string handle)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new AcadGatewayException("bad_handle", "Entity handle cannot be empty.");
        }

        if (!SecurityPolicy.TryParseHandleValue(handle, out long value))
        {
            throw new AcadGatewayException("bad_handle", $"'{handle}' is not a valid hexadecimal handle.");
        }

        if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsNull || id.IsErased)
        {
            throw new AcadGatewayException("not_found", $"No active object with handle '{handle}' exists in this drawing.");
        }

        return id;
    }
}
