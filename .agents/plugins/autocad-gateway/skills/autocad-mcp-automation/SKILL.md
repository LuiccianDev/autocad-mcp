---
name: autocad-mcp-automation
description: "Master AI automation guide for Autodesk AutoCAD 2027 & Civil 3D: Gateway handshake, metric standards, atomic batch geometry, Civil 3D surface/alignment tools, and safe non-modal command execution."
---

# AutoCAD & Civil 3D MCP Automation Guide

Use this skill whenever an AI agent, contributor, or developer needs to interact with, draw in, query, or automate **Autodesk AutoCAD 2027** or **Autodesk Civil 3D 2027** using the **AutoCAD AI Gateway** and **AutoCAD MCP Server** (`luiccian-autocad-mcp`).

---

## 1. Pre-Flight Gateway Handshake & Verification

Before attempting any CAD operations, the agent **MUST** verify communication with AutoCAD:

1. **Ping the Gateway:** Call `ping_autocad_gateway`.
   - If it fails, verify that `AutoCAD.Gateway.dll` is loaded in AutoCAD via `NETLOAD` and listening on port `5055`.
2. **Inspect Drawing Context:** Call `get_drawing_info` to retrieve:
   - Drawing filename, DWG file path, read-only status, and configured drawing units.
3. **Enforce Metric Standards (MANDATORY):**
   - Call `configure_drawing_environment` at the start of any drawing session.
   - Ensures `INSUNITS = 4` (Meters), `MEASUREMENT = 1` (Metric ISO), `LUNITS = 2` (Decimal), `LUPREC = 3` (Millimeter precision).
   - Prohibited: Never work in imperial units (inches/feet) or assume unverified coordinate spaces.

---

## 2. Drawing Strategy: Prefer Batch Geometry

To minimize network latency, avoid context window bloat, and prevent UI thread thrashing:

### Priority 1: Atomic Batch Drawing (`draw_batch_geometry`)
Whenever drawing multiple elements (e.g. walls, grids, structural elements, labels, or complete floor plans), **always prefer** `draw_batch_geometry`.
- Dispatches all lines, rectangles, circles, polylines, and text annotations in a **single atomic call**.
- Bypasses individual roundtrips, reducing execution time and token consumption by over 90%.

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
2. **Visual Inspection:** Call `capture_screen` to obtain a high-resolution PNG screenshot of the active AutoCAD viewport.
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
   - Call `get_longitudinal_profile` to sample ground elevation profiles along an alignment at custom station intervals.
4. **Parametric Civil Design:**
   - Call `create_civil3d_alignment` to generate horizontal alignments with tangent curves.
   - Call `create_civil3d_surface` to build topographic TIN surfaces from coordinate clouds.

---

## 6. Layouts, Title Blocks & PDF Plotting

To produce physical, print-ready engineering drawings:

1. **Create Paper Space Layout:** Call `create_layout` with desired tab name.
2. **Configure Sheet & Plotter:** Call `configure_layout_sheet` (Sheet sizes: `A0` to `A4`, virtual plotter: `DWG To PDF.pc3`, style: `monochrome.ctb`).
3. **Scale Viewport:** Call `create_viewport` centered on model geometry with exact metric scale (`1000 / Scale XP`, e.g. `Scale = 50` for $1:50$).
4. **Parametric Title Block:** Call `insert_parametric_titleblock` to draw standard borders and fill title block attributes (Project, Sheet, Scale, Designer, Date).
5. **Physical PDF Plot:** Call `export_pdf` to render the layout directly to disk.

---

## 7. Reference Tool Decision Matrix

For a complete decision matrix mapping user intents to the optimal tool sequence, see:
[tool_decision_matrix.md](./references/tool_decision_matrix.md)
