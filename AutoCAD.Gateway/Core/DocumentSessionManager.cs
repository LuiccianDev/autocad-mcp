using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Per-document state tracking unique session ID, live database mutation revision counter,
/// and Edit Guard ownership.
/// </summary>
public sealed class DocumentSession : IDisposable
{
    private long _revision;
    private bool _disposed;

    /// <summary>Unique identifier for this open document session.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Associated AutoCAD document instance.</summary>
    public Document Document { get; }

    /// <summary>True when created via new_drawing or unlocked via allow_edit_document.</summary>
    public bool IsManaged { get; set; }

    /// <summary>Monotonically incremented revision observed from AutoCAD database events.</summary>
    public long Revision => Interlocked.Read(ref _revision);

    internal DocumentSession(Document document, bool isManaged)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        IsManaged = isManaged;

        Database db = document.Database;
        db.ObjectAppended += OnDatabaseChanged;
        db.ObjectModified += OnDatabaseChanged;
        db.ObjectErased += OnDatabaseErased;
    }

    private void OnDatabaseChanged(object sender, ObjectEventArgs e) =>
        Interlocked.Increment(ref _revision);

    private void OnDatabaseErased(object sender, ObjectErasedEventArgs e) =>
        Interlocked.Increment(ref _revision);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Database db = Document.Database;
            db.ObjectAppended -= OnDatabaseChanged;
            db.ObjectModified -= OnDatabaseChanged;
            db.ObjectErased -= OnDatabaseErased;
        }
        catch
        {
            // Ignore disposal errors when AutoCAD has already closed the database
        }
    }
}

/// <summary>
/// Tracks open <see cref="DocumentSession"/> instances, observed active drawing name,
/// and configurable Edit Guard protection.
/// </summary>
public static class DocumentSessionManager
{
    private static readonly Lock Sync = new();
    private static readonly List<DocumentSession> Sessions = new();
    private static bool _hooked;
    private static string? _lastObservedDocumentName;
    private static volatile bool _editGuardEnabled =
        string.Equals(Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_EDIT_GUARD"), "1", StringComparison.Ordinal);
    private static volatile bool _restrictedExecutionEnabled =
        string.Equals(Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_RESTRICTED_MODE"), "1", StringComparison.Ordinal);

    /// <summary>
    /// When true, mutations are rejected unless the target drawing was created by the Gateway
    /// or explicitly unlocked with <c>allow_edit_document</c>. Defaults to false unless
    /// AUTOCAD_GATEWAY_EDIT_GUARD=1 is set or toggled via AI_EDIT_GUARD.
    /// </summary>
    public static bool EditGuardEnabled
    {
        get => _editGuardEnabled;
        set => _editGuardEnabled = value;
    }

    /// <summary>
    /// When true, raw CLI execution (<c>/execute</c>) and unrestricted AutoLISP (<c>/lisp/eval</c>)
    /// are disabled with <c>403 restricted_mode</c> to preserve system-variable hardening.
    /// Configured via <c>AUTOCAD_GATEWAY_RESTRICTED_MODE=1</c>.
    /// </summary>
    public static bool RestrictedExecutionEnabled
    {
        get => _restrictedExecutionEnabled;
        set => _restrictedExecutionEnabled = value;
    }

    /// <summary>Most recently observed active drawing path/name (thread-safe snapshot).</summary>
    public static string? LastObservedDocumentName => Volatile.Read(ref _lastObservedDocumentName);

    /// <summary>Updates the observed active document name safely across threads.</summary>
    public static void ObserveActiveDocument(string? name) =>
        Volatile.Write(ref _lastObservedDocumentName, string.IsNullOrWhiteSpace(name) ? null : name);

    /// <summary>Subscribes to DocumentCollection lifecycle events for deterministic session cleanup.</summary>
    public static void Initialize()
    {
        lock (Sync)
        {
            if (_hooked)
            {
                return;
            }

            AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
            AcApp.DocumentManager.DocumentDestroyed += OnDocumentDestroyed;
            _hooked = true;
        }

        try
        {
            ObserveActiveDocument(AcApp.DocumentManager.MdiActiveDocument?.Name);
        }
        catch
        {
            // Ignore when invoked before an active document exists
        }
    }

    /// <summary>Unsubscribes lifecycle hooks and disposes all tracked document sessions.</summary>
    public static void Shutdown()
    {
        lock (Sync)
        {
            if (_hooked)
            {
                try
                {
                    AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                    AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
                    AcApp.DocumentManager.DocumentDestroyed -= OnDocumentDestroyed;
                }
                catch
                {
                }

                _hooked = false;
            }

            foreach (DocumentSession session in Sessions)
            {
                session.Dispose();
            }

            Sessions.Clear();
        }

        ObserveActiveDocument(null);
    }

    /// <summary>Gets or creates a tracked <see cref="DocumentSession"/> for <paramref name="document"/>.</summary>
    public static DocumentSession GetOrCreate(Document document, bool markManaged = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        Initialize();

        lock (Sync)
        {
            DocumentSession? existing = Sessions.FirstOrDefault(s => ReferenceEquals(s.Document, document));
            if (existing is not null)
            {
                if (markManaged)
                {
                    existing.IsManaged = true;
                }

                return existing;
            }

            var created = new DocumentSession(document, markManaged);
            Sessions.Add(created);
            return created;
        }
    }

    /// <summary>
    /// Finds an open document matching <paramref name="nameOrPath"/> by exact full path first,
    /// then unique basename (throwing <c>ambiguous_document</c> on collision), then session ID.
    /// </summary>
    public static Document? FindOpenDocument(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return null;
        }

        string needle = nameOrPath.Trim();
        var basenameMatches = new List<Document>();

        foreach (Document doc in AcApp.DocumentManager)
        {
            if (string.Equals(doc.Name, needle, StringComparison.OrdinalIgnoreCase))
            {
                return doc;
            }

            if (string.Equals(Path.GetFileName(doc.Name), needle, StringComparison.OrdinalIgnoreCase))
            {
                basenameMatches.Add(doc);
            }
        }

        if (basenameMatches.Count > 1)
        {
            throw new AcadGatewayException(
                "ambiguous_document",
                $"Multiple open drawings match '{needle}'. Specify the full path or session ID.");
        }

        if (basenameMatches.Count == 1)
        {
            return basenameMatches[0];
        }

        lock (Sync)
        {
            DocumentSession? byId = Sessions.FirstOrDefault(s => string.Equals(s.Id, needle, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                foreach (Document doc in AcApp.DocumentManager)
                {
                    if (ReferenceEquals(doc, byId.Document))
                    {
                        return doc;
                    }
                }
            }
        }

        return null;
    }

    private static void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e)
    {
        try
        {
            ObserveActiveDocument(e.Document?.Name);
        }
        catch
        {
        }
    }

    private static void OnDocumentDestroyed(object? sender, DocumentDestroyedEventArgs e)
    {
        try
        {
            ObserveActiveDocument(AcApp.DocumentManager.MdiActiveDocument?.Name);
        }
        catch
        {
            ObserveActiveDocument(null);
        }
    }

    private static void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document is null)
        {
            return;
        }

        lock (Sync)
        {
            for (int i = Sessions.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(Sessions[i].Document, e.Document))
                {
                    Sessions[i].Dispose();
                    Sessions.RemoveAt(i);
                }
            }
        }
    }
}
