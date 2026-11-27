using System;
using System.Collections.Generic;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// MCP tool behavioral hints describing read-only, destructive, idempotent, and open-world characteristics.
/// </summary>
public sealed record ToolAnnotationSpec(
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    bool OpenWorld);

/// <summary>
/// Deterministic MCP <c>ToolAnnotations</c> classifier shared across C# and Python tool catalogs.
/// </summary>
public static class ToolAnnotationPolicy
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        // context
        "get_drawing_info",
        "get_entities",
        "get_layers",
        "get_texts",
        "get_drawing_extents",
        "get_selection_count",
        "list_blocks",
        "capture_screenshot",
        "get_gateway_tools",
        "get_gateway_guide",
        // entities
        "query_entities",
        "get_entity",
        // drafting
        "get_block_definition",
        "get_block_attributes",
        "list_xrefs",
        // documents
        "list_documents",
        "get_system_variable",
        // measurement
        "measure_distance",
        "calculate_polygon_area",
        // execution
        "ping_gateway",
        // civil3d
        "check_civil3d_status",
        "list_civil3d_surfaces",
        "list_civil3d_alignments",
        "get_surface_elevation",
        "get_civil3d_surface_stats",
        "get_alignment_station_offset",
        "get_point_at_station",
        "sample_longitudinal_profile",
        "sample_civil3d_cross_section",
    };

    private static readonly HashSet<string> DestructiveTools = new(StringComparer.Ordinal)
    {
        "delete_entities",
        "execute_native_batch",
        "delete_layer",
        "explode_entities",
        "close_document",
        "purge_and_audit_drawing",
        "save_drawing",
        "execute_command",
        "execute_batch_commands",
        "evaluate_lisp",
        "load_lisp_file",
    };

    private static readonly HashSet<string> OpenWorldTools = new(StringComparer.Ordinal)
    {
        "new_drawing",
        "open_drawing",
        "save_drawing",
        "close_document",
        "export_pdf",
        "insert_raster_image",
        "insert_title_block",
        "execute_command",
        "execute_batch_commands",
        "evaluate_lisp",
        "load_lisp_file",
    };

    private static readonly HashSet<string> IdempotentMutators = new(StringComparer.Ordinal)
    {
        "create_layer",
        "set_layer_state",
        "set_current_layer",
        "set_block_attributes",
        "activate_document",
        "allow_edit_document",
        "cancel_command",
        "set_system_variable",
        "configure_layout_page",
        "zoom_extents",
        "regen_all",
        "configure_drawing_units",
    };

    /// <summary>Returns all canonical tool names explicitly classified across policy sets.</summary>
    public static IEnumerable<string> EnumerateClassifiedTools()
    {
        foreach (string t in ReadOnlyTools) yield return t;
        foreach (string t in DestructiveTools) yield return t;
        foreach (string t in OpenWorldTools) yield return t;
        foreach (string t in IdempotentMutators) yield return t;
    }

    /// <summary>Resolves behavioral hints for a canonical tool (or resolved alias target).</summary>
    public static ToolAnnotationSpec ResolveForTool(string canonicalToolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalToolName);

        string tool = canonicalToolName.Trim();
        bool openWorld = OpenWorldTools.Contains(tool);

        if (ReadOnlyTools.Contains(tool))
        {
            return new ToolAnnotationSpec(
                ReadOnly: true,
                Destructive: false,
                Idempotent: true,
                OpenWorld: openWorld);
        }

        if (DestructiveTools.Contains(tool))
        {
            return new ToolAnnotationSpec(
                ReadOnly: false,
                Destructive: true,
                Idempotent: false,
                OpenWorld: openWorld);
        }

        return new ToolAnnotationSpec(
            ReadOnly: false,
            Destructive: false,
            Idempotent: IdempotentMutators.Contains(tool),
            OpenWorld: openWorld);
    }

    /// <summary>
    /// Resolves conservative behavioral hints for a compound domain router by aggregating hints
    /// across its canonical member actions (eliminating parallel domain lookup tables).
    /// </summary>
    public static ToolAnnotationSpec ResolveForCompoundDomain(IEnumerable<string> canonicalToolNames)
    {
        ArgumentNullException.ThrowIfNull(canonicalToolNames);

        bool anyTool = false;
        bool allReadOnly = true;
        bool anyDestructive = false;
        bool allIdempotent = true;
        bool anyOpenWorld = false;

        foreach (string toolName in canonicalToolNames)
        {
            anyTool = true;
            ToolAnnotationSpec spec = ResolveForTool(toolName);
            allReadOnly &= spec.ReadOnly;
            anyDestructive |= spec.Destructive;
            allIdempotent &= spec.Idempotent;
            anyOpenWorld |= spec.OpenWorld;
        }

        return anyTool
            ? new ToolAnnotationSpec(allReadOnly, anyDestructive, allIdempotent, anyOpenWorld)
            : new ToolAnnotationSpec(ReadOnly: false, Destructive: true, Idempotent: false, OpenWorld: true);
    }
}
