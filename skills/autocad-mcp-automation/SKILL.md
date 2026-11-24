---
name: autocad-mcp-automation
description: "Master AI automation guide for Autodesk AutoCAD 2027 & Civil 3D: Gateway handshake, metric standards, atomic batch geometry, Civil 3D surface/alignment tools, and safe non-modal command execution."
---

# AutoCAD & Civil 3D MCP Automation Guide

Use this skill whenever an AI agent, contributor, or developer needs to interact with, draw in, query, or automate **Autodesk AutoCAD 2027** or **Autodesk Civil 3D 2027** using the **AutoCAD AI Gateway** and **AutoCAD MCP Server** (`luiccian-autocad-mcp`).

---

## 1. Pre-Flight Gateway Handshake & Verification

Before attempting any CAD operations, the agent **MUST** verify communication with AutoCAD using canonical tool names (compatible with `granular`, `compound`, and `core` modes):

1. **Ping the Gateway:** Call `ping_gateway`.
   - If it fails, verify that `AutoCAD.Gateway.dll` is loaded in AutoCAD via `NETLOAD` (or Autoloader `.bundle`) and started via `AI_START_SERVER`.
2. **Inspect Drawing Context:** Call `get_drawing_info` to retrieve:
   - Drawing filename, DWG file path, read-only status, and configured drawing units.
3. **Edit Guard Authorization (Existing DWGs):**
   - If a write operation returns `403 edit_guard` on a pre-existing drawing, ask before modifying existing client files or call `allow_edit_document` (or create a fresh drawing with `new_drawing` / toggle via `AI_EDIT_GUARD`).
4. **Enforce Metric Standards (MANDATORY):**
   - Call `configure_drawing_units` (`execute_command` is available when using custom unit scripts, or `configure_drawing_units` directly in `granular`/`compound`) at the start of any drawing session.
   - Ensures `INSUNITS = 4` (Meters), `MEASUREMENT = 1` (Metric ISO), `LUNITS = 2` (Decimal), `LUPREC = 3` (Millimeter precision).
   - Prohibited: Never work in imperial units (inches/feet) or assume unverified coordinate spaces.

## 1.1 Tool Loading Modes (`AUTOCAD_MCP_TOOL_MODE`)

The MCP server exposes the same underlying Gateway capabilities across three modes:

- **`granular` (default, 89 tools):** Calls canonical tool names directly (75 canonical + 14 legacy aliases).
- **`compound` (10 domain routers):** Calls `autocad_context`, `autocad_entities`, `autocad_drafting`, `autocad_drawing`, `autocad_layers`, `autocad_documents`, `autocad_measurement`, `autocad_plotting`, `autocad_civil3d`, or `autocad_execution`. Inspect schemas in 2 steps when needed:
  1. `autocad_<domain>(action="help")` or `autocad_<domain>(action="help", params={"action": "<name>"})`.
  2. `autocad_<domain>(action="<name>", params={...})`.
- **`core` (28 tools):** Minimal direct set with zero aliases (always use canonical names such as `ping_gateway`, `capture_screenshot`, `sample_longitudinal_profile`, `allow_edit_document`).

---

## 2. Drawing Strategy: Prefer Batch Geometry

To minimize network latency, avoid context window bloat, and prevent UI thread thrashing:

### Priority 1: Atomic Batch Drawing (`draw_batch_geometry`)
Whenever drawing multiple elements (e.g. walls, grids, structural elements, labels, or complete floor plans), **always prefer** `draw_batch_geometry`.
- Dispatches all lines, rectangles, circles, polylines, and text annotations in a **single atomic call**.
- Bypasses individual roundtrips, reducing execution time and token consumption by over 90%.
- **Architectural Door Openings & Metric Dimensioning:**
  - When drawing doors inside walls or partitions, split the wall/partition line segments around every door opening (`vano`) and add short jamb caps rather than drawing door arcs across continuous wall lines.
  - When calling `create_dimension` in metric drawings (`INSUNITS = 4`), pass `text_height` (e.g. `0.25` to `0.28`), `arrow_size` (e.g. `0.18` to `0.20`), or `dim_scale` (`1.0`), or rely on the Gateway's automatic metric calibration, then verify readability with `zoom_extents` + `capture_screenshot`.

### Priority 2: Atomic Primitives via `entmake`
For single, standalone adjustments:
- `draw_line`: 3D straight line between two metric points.
- `draw_polyline`: 2D lightweight polyline (`LWPOLYLINE`), open or closed.
- `draw_rectangle`: Closed rectangular boundary between two diagonal metric points.
- `draw_circle`: Parametric circle from center point and radius in meters.
- `draw_arc`: Counterclockwise circular arc.
- `insert_text`: Single-line `DBText` in model space with metric height and rotation.
- `insert_block`: Block reference insertion with uniform scale and rotation.

---

## 3. Safe Command Execution Rules

When injecting native AutoCAD commands via `execute_command` or `execute_batch_commands`:

1. **Strict Non-Modal Mode (`_.-` Prefix):**
   - Commands that open Windows dialog boxes **FREEZE** the background Gateway thread waiting for human mouse clicks.
   - **Always** use the command-line text version with `_.-`:
     - Use `_.-LAYER` instead of `LAYER`.
     - Use `_.-INSERT` instead of `INSERT`.
     - Use `_.-PURGE` instead of `PURGE`.
2. **Internationalization (`_.` Prefix):**
   - Always prefix native commands with `_.` (e.g. `_.LINE`, `_.RECTANG`, `_.ZOOM`, `_.QSAVE`) to ensure identical behavior on localized Spanish or English AutoCAD installations.
3. **Line Terminators:**
   - Every prompt parameter must end with `\n` to simulate the `Enter` key.
4. **Auto-Cancellation:**
   - The Gateway automatically sends `\x03\x03` (`Ctrl+C` / `ESC`) before and after execution to clear active command loops.

---

## 4. Visual Verification (Multimodal Feedback)

Whenever you finish creating or modifying geometry:
1. **Recenter Viewport:** Call `zoom_extents` (`_.ZOOM E`) to ensure all geometry is framed.
2. **Visual Inspection:** Call `capture_screenshot` to obtain a high-resolution PNG screenshot of the active AutoCAD viewport.
3. Inspect the image to verify geometry alignment, text readability, layer visibility, and structural correctness before reporting completion to the user.

---

## 5. Autodesk Civil 3D Native Operations

When connected to an active Civil 3D 2027 session:

1. **Runtime Verification:** Call `check_civil3d_status` to confirm that the managed Civil 3D API (`AeccDbMgd.dll`) is loaded.
2. **Surface Analysis:**
   - Call `list_civil3d_surfaces` to list TIN surfaces, bounds, and triangle counts.
   - Call `get_surface_elevation` to query exact terrain elevation ($Z$) at any $(X, Y)$ coordinate.
3. **Alignments & Profiles:**
   - Call `list_civil3d_alignments` to discover horizontal centerlines and station ranges.
   - Call `get_alignment_station_offset` to compute chainage station and lateral offset.
   - Call `sample_longitudinal_profile` to sample ground elevation profiles along an alignment at custom station intervals.
4. **Parametric Civil Design:**
   - Call `create_civil3d_alignment` to generate horizontal alignments with tangent curves.
   - Call `create_civil3d_surface` to build topographic TIN surfaces from coordinate clouds.

---

## 6. Layouts, Title Blocks & PDF Plotting

To produce physical, print-ready engineering drawings:

1. **Create Paper Space Layout:** Call `create_layout` with desired tab name.
2. **Configure Sheet & Plotter:** Call `configure_layout_page` (Sheet sizes: `A0` to `A4`, virtual plotter: `DWG To PDF.pc3`, plot style: `monochrome.ctb`).
3. **Scale Viewport:** Call `create_viewport` centered on model geometry with exact metric scale (`1000 / Scale XP`, e.g. `Scale = 50` for $1:50$).
4. **Parametric Title Block:** Call `insert_title_block` to draw standard borders and fill title block attributes (Project, Sheet, Scale, Designer, Date).
5. **Physical PDF Plot:** Call `export_pdf` to render the layout directly to disk.

---

## 7. Reference Tool Decision Matrix

For a complete decision matrix mapping user intents to the optimal tool sequence, see:
[tool_decision_matrix.md](./references/tool_decision_matrix.md)
