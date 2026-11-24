using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Honest undo granularity constants communicated by Gateway tool descriptors.
/// </summary>
public static class UndoKind
{
    /// <summary>Entire call commits inside one document transaction (one Ctrl+Z step).</summary>
    public const string SingleStep = "single-step";

    /// <summary>Fixed native factory executing multiple sequential database steps (more than one Ctrl+Z step).</summary>
    public const string MultiStep = "multi-step";

    /// <summary>Reversibility depends on arbitrary commands or AutoLISP supplied by caller.</summary>
    public const string ToolDefined = "tool-defined";

    /// <summary>Modifies files, session/document ownership, or application state outside the drawing undo stack.</summary>
    public const string NotUndoable = "not-undoable";

    /// <summary>Read-only inspection operation; makes no mutations.</summary>
    public const string None = "none";

    /// <summary>Canonical set of allowed <see cref="ToolDescriptor.Undo"/> values.</summary>
    public static readonly IReadOnlySet<string> ValidKinds =
        new HashSet<string>([None, SingleStep, MultiStep, ToolDefined, NotUndoable], StringComparer.Ordinal);
}

/// <summary>
/// Metadata describing a Gateway route exposed via <c>/_tools</c>.
/// Invariant: <paramref name="Mutating"/> is true whenever the endpoint modifies drawing entities,
/// system/session/MDI document state, or external disk files.
/// </summary>
/// <param name="Name">Stable machine-readable Gateway tool identifier.</param>
/// <param name="Category">Logical endpoint group (system, context, execution, entities, layers, documents, sysvar, civil3d).</param>
/// <param name="Description">Concise description of endpoint behavior.</param>
/// <param name="Method">HTTP method (GET or POST).</param>
/// <param name="Route">Canonical HTTP route path starting with <c>/</c>.</param>
/// <param name="Mutating">True when the endpoint mutates drawing, session, command, or disk state.</param>
/// <param name="Undo">Undo granularity constant from <see cref="UndoKind"/>.</param>
/// <param name="RequiresEditGuard">True when Edit Guard blocks writes to unmanaged drawings.</param>
public sealed record ToolDescriptor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("route")] string Route,
    [property: JsonPropertyName("mutating")] bool Mutating,
    [property: JsonPropertyName("undo")] string Undo,
    [property: JsonPropertyName("requires_edit_guard")] bool RequiresEditGuard);
