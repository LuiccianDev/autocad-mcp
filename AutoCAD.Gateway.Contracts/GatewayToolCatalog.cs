using System.Collections.Generic;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Canonical Gateway HTTP route constants shared between <see cref="GatewayToolCatalog"/> and the Gateway router.
/// </summary>
public static class GatewayRoutes
{
    public const string Ping = $"/{GatewayProtocol.PingRoute}";
    public const string Tools = $"/{GatewayProtocol.ToolsRoute}";
    public const string Guide = $"/{GatewayProtocol.GuideRoute}";

    public const string ContextSelection = "/context/selection";
    public const string ContextEntities = "/context/entities";
    public const string ContextInfo = "/context/info";
    public const string ContextLayers = "/context/layers";
    public const string ContextExtents = "/context/extents";
    public const string ContextTexts = "/context/texts";
    public const string ContextBlocks = "/context/blocks";
    public const string ContextScreenshot = "/context/screenshot";

    public const string Execute = "/execute";
    public const string LispEval = "/lisp/eval";
    public const string LispLoad = "/lisp/load";

    public const string EntitiesQuery = "/entities/query";
    public const string EntitiesGet = "/entities/get";
    public const string EntitiesCreate = "/entities/create";
    public const string EntitiesModify = "/entities/modify";
    public const string EntitiesTransform = "/entities/transform";
    public const string EntitiesDelete = "/entities/delete";
    public const string EntitiesExplode = "/entities/explode";
    public const string Batch = "/batch";

    public const string DraftingDimension = "/drafting/dimension";
    public const string DraftingMLeader = "/drafting/mleader";
    public const string DraftingHatch = "/drafting/hatch";
    public const string DraftingTable = "/drafting/table";
    public const string DraftingRasterImage = "/drafting/raster-image";

    public const string BlocksDefine = "/blocks/define";
    public const string BlocksDefinition = "/blocks/definition";
    public const string BlocksAttributes = "/blocks/attributes";
    public const string BlocksXrefs = "/blocks/xrefs";

    public const string LayersCreate = "/layers/create";
    public const string LayersState = "/layers/state";
    public const string LayersDelete = "/layers/delete";

    public const string PlottingLayout = "/plotting/layout";
    public const string PlottingPageSetup = "/plotting/page-setup";
    public const string PlottingViewport = "/plotting/viewport";
    public const string PlottingTitleBlock = "/plotting/title-block";
    public const string PlottingPdf = "/plotting/pdf";

    public const string Documents = "/documents";
    public const string SessionCancel = "/session/cancel";
    public const string SessionAllowEdit = "/session/allow-edit";
    public const string SessionRegen = "/session/regen";
    public const string DocumentsNew = "/documents/new";
    public const string DocumentsOpen = "/documents/open";
    public const string DocumentsSave = "/documents/save";
    public const string DocumentsActivate = "/documents/activate";
    public const string DocumentsClose = "/documents/close";
    public const string SysVar = "/sysvar";

    public const string CivilStatus = "/civil/status";
    public const string CivilSurfaces = "/civil/surfaces";
    public const string CivilAlignments = "/civil/alignments";
    public const string CivilSurfaceElevation = "/civil/surface/elevation";
    public const string CivilSurfaceStats = "/civil/surface/stats";
    public const string CivilStationOffset = "/civil/alignment/station-offset";
    public const string CivilPointAtStation = "/civil/alignment/point-at-station";
    public const string CivilProfile = "/civil/profile";
    public const string CivilCrossSection = "/civil/cross-section";
    public const string CivilCreateAlignment = "/civil/alignment/create";
    public const string CivilCreateSurface = "/civil/surface/create";
}

/// <summary>
/// Canonical catalog of Gateway HTTP endpoints, undo semantics, and working guidelines for AI agents.
/// </summary>
public static class GatewayToolCatalog
{
    /// <summary>All active Gateway HTTP endpoints with honest undo and Edit Guard metadata.</summary>
    public static IReadOnlyList<ToolDescriptor> All { get; } =
    [
        // Health & Catalog Discovery
        new("ping", "system", "Verify Gateway liveness, bound port, process ID, active document, and Edit Guard state.", "GET", GatewayRoutes.Ping, false, UndoKind.None, false),
        new("gateway_tools", "system", "List all Gateway HTTP endpoints with category, HTTP method, mutating flag, and undo granularity.", "GET", GatewayRoutes.Tools, false, UndoKind.None, false),
        new("gateway_guide", "system", "Return concise operational and safety guidelines for AI agents controlling AutoCAD.", "GET", GatewayRoutes.Guide, false, UndoKind.None, false),

        // Context Inspection
        new("context_selection", "context", "Read current implied selection count in the active drawing.", "GET", GatewayRoutes.ContextSelection, false, UndoKind.None, false),
        new("context_entities", "context", "Count ModelSpace entities grouped by type.", "GET", GatewayRoutes.ContextEntities, false, UndoKind.None, false),
        new("context_info", "context", "Inspect drawing metadata, filename, read-only status, and units.", "GET", GatewayRoutes.ContextInfo, false, UndoKind.None, false),
        new("context_layers", "context", "List drawing layers with color index, on/off, frozen, and locked states.", "GET", GatewayRoutes.ContextLayers, false, UndoKind.None, false),
        new("context_extents", "context", "Read ModelSpace world bounding extents (Extmin and Extmax).", "GET", GatewayRoutes.ContextExtents, false, UndoKind.None, false),
        new("context_texts", "context", "Extract DBText and MText entities with text strings and coordinates.", "GET", GatewayRoutes.ContextTexts, false, UndoKind.None, false),
        new("context_blocks", "context", "List non-anonymous, non-layout block definition names.", "GET", GatewayRoutes.ContextBlocks, false, UndoKind.None, false),
        new("context_screenshot", "context", "Capture PNG screenshot of AutoCAD window to disk (optionally inline base64 via ?inline=true).", "GET", GatewayRoutes.ContextScreenshot, true, UndoKind.NotUndoable, false),

        // Execution & AutoLISP
        new("execute_command", "execution", "Enqueue non-modal AutoCAD command text strings under Edit Guard validation.", "POST", GatewayRoutes.Execute, true, UndoKind.ToolDefined, true),
        new("evaluate_lisp", "execution", "Evaluate AutoLISP synchronously on the command loop and convert ResultBuffer output to JSON.", "POST", GatewayRoutes.LispEval, true, UndoKind.ToolDefined, true),
        new("load_lisp_file", "execution", "Validate and load an external AutoLISP (.lsp) script file synchronously with configurable timeout and duration metrics.", "POST", GatewayRoutes.LispLoad, true, UndoKind.ToolDefined, true),

        // Native Entity CRUD, Spatial Query & Atomic Batch
        new("query_entities", "entities", "Filter entities by type, layer, space, 2D bbox, and text with revision-bound pagination cursors.", "POST", GatewayRoutes.EntitiesQuery, false, UndoKind.None, false),
        new("get_entity", "entities", "Inspect full geometry, layer, attributes, and revision of one entity by handle.", "POST", GatewayRoutes.EntitiesGet, false, UndoKind.None, false),
        new("create_entity", "entities", "Create a native entity (line, polyline, rectangle, circle, arc, text, mtext, block_ref) inside one transaction.", "POST", GatewayRoutes.EntitiesCreate, true, UndoKind.SingleStep, true),
        new("modify_entity", "entities", "Modify properties or individual coordinates of an entity by handle inside one transaction.", "POST", GatewayRoutes.EntitiesModify, true, UndoKind.SingleStep, true),
        new("transform_entities", "entities", "Move, rotate, scale, copy, or mirror entity handles atomically inside one transaction.", "POST", GatewayRoutes.EntitiesTransform, true, UndoKind.SingleStep, true),
        new("delete_entities", "entities", "Erase one or more entity handles atomically inside one transaction.", "POST", GatewayRoutes.EntitiesDelete, true, UndoKind.SingleStep, true),
        new("explode_entities", "entities", "Explode compound entities (polylines, blocks, dimensions) atomically inside one transaction.", "POST", GatewayRoutes.EntitiesExplode, true, UndoKind.SingleStep, true),
        new("execute_batch", "entities", "Execute up to 500 native layer/entity operations inside one transaction with rollback controls.", "POST", GatewayRoutes.Batch, true, UndoKind.SingleStep, true),

        // Annotation, Drafting & Blocks
        new("create_dimension", "drafting", "Create aligned, rotated, radial, diametric, or angular native dimensions inside one transaction.", "POST", GatewayRoutes.DraftingDimension, true, UndoKind.SingleStep, true),
        new("create_mleader", "drafting", "Create a native MLeader callout annotation inside one transaction.", "POST", GatewayRoutes.DraftingMLeader, true, UndoKind.SingleStep, true),
        new("create_hatch", "drafting", "Create a solid or patterned associative hatch from handles or closed polygon points.", "POST", GatewayRoutes.DraftingHatch, true, UndoKind.SingleStep, true),
        new("create_table", "drafting", "Create a populated native AutoCAD Table entity inside one transaction.", "POST", GatewayRoutes.DraftingTable, true, UndoKind.SingleStep, true),
        new("insert_raster_image", "drafting", "Attach and place a raster image entity (PNG/JPG/BMP/TIF) with IMAGEFRAME control inside one transaction.", "POST", GatewayRoutes.DraftingRasterImage, true, UndoKind.SingleStep, true),
        new("define_block", "blocks", "Define a reusable BlockTableRecord from handles or inline operations and optional attributes.", "POST", GatewayRoutes.BlocksDefine, true, UndoKind.SingleStep, true),
        new("get_block_definition", "blocks", "Inspect entities, origin, Xref status, and attribute definitions of a named block.", "GET", GatewayRoutes.BlocksDefinition, false, UndoKind.None, false),
        new("get_block_attributes", "blocks", "Read AttributeReference tag values of an inserted BlockReference handle.", "GET", GatewayRoutes.BlocksAttributes, false, UndoKind.None, false),
        new("set_block_attributes", "blocks", "Update AttributeReference values on a BlockReference handle inside one transaction.", "POST", GatewayRoutes.BlocksAttributes, true, UndoKind.SingleStep, true),
        new("list_xrefs", "blocks", "List external reference (Xref) definitions, paths, and resolution statuses.", "GET", GatewayRoutes.BlocksXrefs, false, UndoKind.None, false),

        // Native Layer Management
        new("create_layer", "layers", "Create or update a layer and optionally set CLAYER inside one transaction.", "POST", GatewayRoutes.LayersCreate, true, UndoKind.SingleStep, true),
        new("set_layer_state", "layers", "Modify layer on/off, frozen, locked, color, or active status inside one transaction.", "POST", GatewayRoutes.LayersState, true, UndoKind.SingleStep, true),
        new("delete_layer", "layers", "Safely erase an unused non-system, non-current layer using ObjectARX Purge verification.", "POST", GatewayRoutes.LayersDelete, true, UndoKind.SingleStep, true),

        // Plotting & Viewports
        new("create_layout", "plotting", "Create a PaperSpace layout tab and optionally erase its auto-generated default viewport.", "POST", GatewayRoutes.PlottingLayout, true, UndoKind.SingleStep, true),
        new("configure_layout_page", "plotting", "Configure PaperSpace layout PlotSettings (ISO full bleed A0-A4, landscape/portrait, 1:1 mm, CTB/STB) natively.", "POST", GatewayRoutes.PlottingPageSetup, true, UndoKind.SingleStep, true),
        new("create_viewport", "plotting", "Create a scaled PaperSpace Viewport entity with optional 3D DCS target, direction, and visual style inside one atomic transaction.", "POST", GatewayRoutes.PlottingViewport, true, UndoKind.SingleStep, true),
        new("insert_title_block", "plotting", "Draw a native parametric PaperSpace title block with configurable template and optional raster logo.", "POST", GatewayRoutes.PlottingTitleBlock, true, UndoKind.SingleStep, true),
        new("plot_pdf", "plotting", "Plot a layout synchronously to PDF via PlotEngine forcing and restoring BACKGROUNDPLOT=0.", "POST", GatewayRoutes.PlottingPdf, true, UndoKind.NotUndoable, true),

        // Document Session & System Variables
        new("list_documents", "documents", "List all open AutoCAD MDI drawings with session IDs, revisions, and Edit Guard flags.", "GET", GatewayRoutes.Documents, false, UndoKind.None, false),
        new("cancel_command", "documents", "Send Ctrl+C cancellation sequence and report updated command-line state.", "POST", GatewayRoutes.SessionCancel, true, UndoKind.NotUndoable, false),
        new("allow_edit_document", "documents", "Authorize or revoke Edit Guard management status on an existing open drawing.", "POST", GatewayRoutes.SessionAllowEdit, true, UndoKind.NotUndoable, false),
        new("regen_all", "documents", "Regenerate all viewports in the active drawing synchronously.", "POST", GatewayRoutes.SessionRegen, true, UndoKind.NotUndoable, false),
        new("new_drawing", "documents", "Create a new drawing (optionally from a .dwt template) marked as Gateway-managed.", "POST", GatewayRoutes.DocumentsNew, true, UndoKind.NotUndoable, false),
        new("open_drawing", "documents", "Open an existing local .dwg drawing file and focus it without bypassing Edit Guard.", "POST", GatewayRoutes.DocumentsOpen, true, UndoKind.NotUndoable, false),
        new("save_drawing", "documents", "Save the active or target drawing in place or to a validated .dwg path via Database.SaveAs.", "POST", GatewayRoutes.DocumentsSave, true, UndoKind.NotUndoable, true),
        new("activate_document", "documents", "Switch AutoCAD active MDI drawing by path, unique filename, or session ID.", "POST", GatewayRoutes.DocumentsActivate, true, UndoKind.NotUndoable, false),
        new("close_document", "documents", "Close an open drawing requiring explicit save or discard confirmation and Edit Guard check.", "POST", GatewayRoutes.DocumentsClose, true, UndoKind.NotUndoable, true),
        new("get_system_variable", "sysvar", "Read a non-security AutoCAD system variable safely.", "GET", GatewayRoutes.SysVar, false, UndoKind.None, false),
        new("set_system_variable", "sysvar", "Set a non-security system variable on the active MDI drawing under Edit Guard (may touch profile/session scope).", "POST", GatewayRoutes.SysVar, true, UndoKind.NotUndoable, true),

        // Autodesk Civil 3D 2027
        new("civil_status", "civil3d", "Check whether Civil 3D 2027 runtime modules are loaded and summarize TIN/Alignment counts.", "GET", GatewayRoutes.CivilStatus, false, UndoKind.None, false),
        new("civil_surfaces", "civil3d", "List Civil 3D TIN surfaces with elevation bounds and triangle counts.", "GET", GatewayRoutes.CivilSurfaces, false, UndoKind.None, false),
        new("civil_alignments", "civil3d", "List Civil 3D horizontal alignments with stations and lengths.", "GET", GatewayRoutes.CivilAlignments, false, UndoKind.None, false),
        new("civil_surface_elevation", "civil3d", "Query exact TIN surface Z elevation at (x, y).", "GET", GatewayRoutes.CivilSurfaceElevation, false, UndoKind.None, false),
        new("civil_surface_stats", "civil3d", "Query TIN surface elevation stats, 2D/3D areas, point count, and triangle count.", "GET", GatewayRoutes.CivilSurfaceStats, false, UndoKind.None, false),
        new("civil_station_offset", "civil3d", "Compute station and lateral offset of (x, y) relative to an alignment.", "GET", GatewayRoutes.CivilStationOffset, false, UndoKind.None, false),
        new("civil_point_at_station", "civil3d", "Compute world (x, y) coordinates at alignment station and offset.", "GET", GatewayRoutes.CivilPointAtStation, false, UndoKind.None, false),
        new("civil_profile", "civil3d", "Sample longitudinal profile coordinates and ground elevations along an alignment.", "GET", GatewayRoutes.CivilProfile, false, UndoKind.None, false),
        new("civil_cross_section", "civil3d", "Sample transverse ground elevations perpendicular to an alignment at a station.", "GET", GatewayRoutes.CivilCrossSection, false, UndoKind.None, false),
        new("civil_create_alignment", "civil3d", "Create a Civil 3D horizontal alignment from PI vertices and fixed curve radius across multiple DB steps.", "POST", GatewayRoutes.CivilCreateAlignment, true, UndoKind.MultiStep, true),
        new("civil_create_surface", "civil3d", "Create a Civil 3D TIN surface from 3D points across multiple DB steps.", "POST", GatewayRoutes.CivilCreateSurface, true, UndoKind.MultiStep, true),
    ];

    /// <summary>Returns concise operational rules and safety invariants for AI agents driving AutoCAD.</summary>
    /// <returns>Multi-line operational guideline text for AI agents.</returns>
    public static string BuildAgentGuideText() =>
        """
        AutoCAD AI Gateway Agent Working Rules:
        1. Units & Precision: Work in metric meters (INSUNITS=4, MEASUREMENT=1). Always pass finite coordinates.
        2. Edit Guard Safety: Existing drawings opened by the user may be protected when Edit Guard is active. Call allow_edit_document or create a fresh sheet with new_drawing before mutating.
        3. Prefer Native Transactions: Use POST /batch, POST /entities/*, POST /drafting/*, and POST /blocks/* rather than command-line strings so mutations commit in a single Ctrl+Z undo step with exact handles.
        4. Paginated Queries & Optimistic Concurrency: Use POST /entities/query with filters (types, layers, bbox) and next_cursor. Re-query when a mutation or cursor check returns error code revision_conflict.
        5. Undo Honesty: single-step operations commit in one Ctrl+Z transaction; multi-step factories (/civil/*/create) span multiple internal transactions; tool-defined calls (/execute, /lisp/eval) depend on supplied code; session, sysvar, screenshot, and PDF plot operations are not-undoable.
        6. Synchronous LISP Escape Hatch: Use POST /lisp/eval only for operations without a native endpoint, and call POST /session/cancel if command_state reports a pending prompt.
        """;
}
