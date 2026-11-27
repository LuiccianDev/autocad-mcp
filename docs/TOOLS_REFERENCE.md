# MCP Tools Technical Reference Manual

This manual documents all **90 tools across 10 domains** (**76 canonical actions** and **14 legacy aliases**) implemented in **`AutoCAD.McpServer`** (.NET 10) and **`AutoCAD.McpPython`** (FastMCP). Both servers support `AUTOCAD_MCP_TOOL_MODE` (`granular`, `compound`, `core`) and `AUTOCAD_MCP_OUTPUT_FORMAT` (`compact`, `json`).

All coordinates, dimensions, and elevations are strictly interpreted in **meters** (`INSUNITS = 4`).

---

## MCP Output Protocol (`v2.7.0`)

At the MCP server boundary (`McpResultAdapter` in C# and `mcp_result.py` in Python), every tool response maps into native MCP `CallToolResult`:

| Behavior | Environment / Condition | Output Envelope |
| :--- | :--- | :--- |
| Compact text + structured JSON | `AUTOCAD_MCP_OUTPUT_FORMAT=compact` (default) | Tabular/key-value summary in `content[0].text` with full JSON object in `structuredContent`. |
| Legacy raw JSON text | `AUTOCAD_MCP_OUTPUT_FORMAT=json` | Raw JSON string in `content[0].text` plus `structuredContent` on success. |
| Error propagation | `ok == false`, `status == "error"`, or `status == "partial_error"` | `isError = true`, concise error diagnostic text (`[code]`, `Detail:`, `Hint:`, or partial batch summary), and `structuredContent` omitted. |
| Native screenshots | `capture_screenshot(inline=true)` | `TextContent` metadata followed by `ImageContent` (`image/png`), with `image_base64` stripped from text and `structuredContent`. |
| Tool annotations | All modes (`tools/list`) | Deterministic `readOnlyHint`, `destructiveHint`, `idempotentHint`, and `openWorldHint` hints (`outputSchema` omitted in `tools/list` to preserve context budget). |

---

## Internal Execution Mechanisms

Each MCP tool runs through one of four execution channels:

| Mechanism | Description |
| :--- | :--- |
| **1. Local Analytical Computation** | Evaluated locally in server memory (.NET/Python) with zero CAD dependency (`measure_distance`, `calculate_polygon_area`). |
| **2. Native ObjectARX Transactions** | Executes via `Acad.Read` (`StartOpenCloseTransaction`) or single-transaction `Acad.Write` inside `AutoCAD.Gateway`, returning exact handles and monotonic `revision` values. |
| **3. Synchronous `LispBridge`** | Evaluates AutoLISP expressions or `.lsp` files synchronously on AutoCAD's command loop (`POST /lisp/eval`, `POST /lisp/load`). |
| **4. Non-Modal Command Dispatcher** | Enqueues `_.-` non-modal command strings onto `ConcurrentQueue<string>` drained on `Application.Idle` (`POST /execute`). |

---

## Domain Index (10 Domains, 90 Tools)

| # | Domain (`compound` Router) | Canonical / Aliases | Core Count | Jump |
| :--- | :--- | :--- | :--- | :--- |
| 1 | `autocad_context` | 10 + 6 aliases | 4 | [Domain 1](#domain-1-autocad_context-10-canonical--6-aliases) |
| 2 | `autocad_entities` | 6 + 0 aliases | 5 | [Domain 2](#domain-2-autocad_entities-6-canonical) |
| 3 | `autocad_drafting` | 10 + 0 aliases | 0 | [Domain 3](#domain-3-autocad_drafting-10-canonical) |
| 4 | `autocad_drawing` | 9 + 0 aliases | 1 | [Domain 4](#domain-4-autocad_drawing-9-canonical) |
| 5 | `autocad_layers` | 4 + 0 aliases | 2 | [Domain 5](#domain-5-autocad_layers-4-canonical) |
| 6 | `autocad_documents` | 9 + 0 aliases | 3 | [Domain 6](#domain-6-autocad_documents-9-canonical) |
| 7 | `autocad_measurement` | 3 + 1 alias | 2 | [Domain 7](#domain-7-autocad_measurement-3-canonical--1-alias) |
| 8 | `autocad_plotting` | 5 + 2 aliases | 2 | [Domain 8](#domain-8-autocad_plotting-5-canonical--2-aliases) |
| 9 | `autocad_civil3d` | 11 + 2 aliases | 5 | [Domain 9](#domain-9-autocad_civil3d-11-canonical--2-aliases) |
| 10 | `autocad_execution` | 9 + 3 aliases | 4 | [Domain 10](#domain-10-autocad_execution-9-canonical--3-aliases) |

> [!NOTE]
> In `compound` mode, invoke any domain router with `action="help"` for a concise domain overview, or `action="help", params={"action": "<tool_name>"}` for the full JSON parameter schema.

---

## Domain 1: `autocad_context` (10 Canonical + 6 Aliases)

C# Class: `AutoCAD.McpServer.Tools.ContextTools` | Python Module: `autocad_mcp.tools.context`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `get_drawing_info` | Yes | `GET /context/info` | `()` — Retrieve DWG name, path, read-only status, and units (`INSUNITS`). |
| `get_entities` | No | `GET /context/entities` | `()` — Count ModelSpace entities grouped by type. |
| `get_layers` | Yes | `GET /context/layers` | `()` — List drawing layers with ACI color, on/off, frozen, and locked states. |
| `get_texts` | No | `GET /context/texts` | `()` — List `DBText` and `MText` entities with contents, `(X, Y)` coordinates, and layers. |
| `get_drawing_extents` | Yes | `GET /context/extents` | `()` — Read ModelSpace world bounding extents (`Extmin` / `Extmax`). |
| `get_selection_count` | No | `GET /context/selection` | `()` — Return count of entities currently selected in AutoCAD. |
| `list_blocks` | No | `GET /context/blocks` | `()` — List non-anonymous, non-layout block definition names. |
| `capture_screenshot` | Yes | `GET /context/screenshot` | `(inline: bool = False)` — Capture PNG of active AutoCAD window. `inline=True` emits native `ImageContent`. |
| `get_gateway_tools` | No | `GET /_tools` | `()` — Query live Gateway endpoint catalog with undo granularity and Edit Guard metadata. |
| `get_gateway_guide` | No | `GET /_guide` | `()` — Retrieve operational working rules and Edit Guard conventions from the Gateway. |

**Legacy Aliases (6):** `read_layers` -> `get_layers`, `read_objects` -> `get_entities`, `read_texts` -> `get_texts`, `read_selection` -> `get_selection_count`, `list_available_blocks` -> `list_blocks`, `capture_screen` -> `capture_screenshot`.

---

## Domain 2: `autocad_entities` (6 Canonical)

C# Class: `AutoCAD.McpServer.Tools.EntityTools` | Python Module: `autocad_mcp.tools.entities`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `query_entities` | Yes | `POST /entities/query` | `(types=None, layers=None, space='model', min_x=None, min_y=None, max_x=None, max_y=None, text_contains=None, limit=100, cursor=None, document=None)` — Filtered/spatial query with revision-bound HMAC pagination cursors. |
| `get_entity` | Yes | `POST /entities/get` | `(handle, document=None)` — Inspect full geometry, layer, attributes, and revision of one entity by handle. |
| `modify_entity` | Yes | `POST /entities/modify` | `(handle, layer=None, color_index=None, linetype=None, text=None, radius=None, height=None, width=None, elevation=None, rotation=None, scale=None, closed=None, x=None, y=None, z=None, start_x=None, start_y=None, start_z=None, end_x=None, end_y=None, end_z=None, start_angle=None, end_angle=None, expected_revision=None, document=None)` — Patch entity properties or coordinates without overwriting omitted axes. |
| `transform_entities` | No | `POST /entities/transform` | `(handles, operation, dx=0.0, dy=0.0, dz=0.0, base_x=None, base_y=None, base_z=None, angle=None, factor=None, p1_x=None, p1_y=None, p2_x=None, p2_y=None, erase_source=False, expected_revision=None, document=None)` — Atomic `move`, `rotate`, `scale`, `copy`, or `mirror`. |
| `delete_entities` | Yes | `POST /entities/delete` | `(handles, expected_revision=None, document=None)` — Erase entity handles in one atomic transaction. |
| `execute_native_batch` | Yes | `POST /batch` | `(operations, default_layer='0', rollback_on_error=True, expected_revision=None, document=None)` — Execute up to 500 native layer/entity operations in a single `Ctrl+Z` undo step. |

---

## Domain 3: `autocad_drafting` (10 Canonical)

C# Class: `AutoCAD.McpServer.Tools.DraftingTools` | Python Module: `autocad_mcp.tools.drafting`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `create_dimension` | No | `POST /drafting/dimension` | `(type, x1=0.0, y1=0.0, x2=0.0, y2=0.0, dim_line_x=None, dim_line_y=None, center_x=None, center_y=None, angle=0.0, leader_length=0.5, text_override=None, dim_style=None, text_height=None, arrow_size=None, dim_scale=None, space='model', layer='0', expected_revision=None, document=None)` — Create native `aligned`, `rotated`, `radial`, `diametric`, or `angular` dimension with metric calibration. |
| `create_mleader` | No | `POST /drafting/mleader` | `(arrow_x, arrow_y, landing_x, landing_y, text, text_height=0.25, arrow_size=None, layer='0', expected_revision=None, document=None)` — Create native `MLeader` callout annotation. |
| `create_hatch` | No | `POST /drafting/hatch` | `(handles=None, points=None, pattern='ANSI31', scale=1.0, angle=0.0, associative=None, layer='0', expected_revision=None, document=None)` — Create solid or patterned `Hatch` from boundary handles or `"x1,y1;x2,y2;..."` vertices. |
| `create_table` | No | `POST /drafting/table` | `(x, y, rows, cols, cells_json, headers_json=None, title=None, row_height=1.0, col_width=5.0, text_height=0.35, layer='0', expected_revision=None, document=None)` — Create populated native AutoCAD `Table`. |
| `define_block` | No | `POST /blocks/define` | `(name, handles=None, operations_json=None, base_x=0.0, base_y=0.0, base_z=0.0, overwrite=False, attributes_json=None, expected_revision=None, document=None)` — Define reusable `BlockTableRecord` from handles or inline operations with optional attributes. |
| `get_block_definition` | No | `GET /blocks/definition` | `(name, document=None)` — Inspect entities, origin, Xref status, and attribute definitions of a named block. |
| `get_block_attributes` | No | `GET /blocks/attributes` | `(handle, document=None)` — Read `AttributeReference` tag values on an inserted `BlockReference` handle. |
| `set_block_attributes` | No | `POST /blocks/attributes` | `(handle, attributes_json, expected_revision=None, document=None)` — Update `AttributeReference` tag values on a `BlockReference` handle. |
| `list_xrefs` | No | `GET /blocks/xrefs` | `(document=None)` — List external references (Xrefs) with path, resolution status, and unloaded state. |
| `explode_entities` | No | `POST /entities/explode` | `(handles, erase_source=True, expected_revision=None, document=None)` — Explode compound entities (polylines, blocks, dimensions) atomically. |

---

## Domain 4: `autocad_drawing` (9 Canonical)

C# Class: `AutoCAD.McpServer.Tools.DrawingTools` | Python Module: `autocad_mcp.tools.drawing`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `draw_batch_geometry` | Yes | `POST /batch` | `(elements, default_layer='0')` — Preferred tool for drawing multiple lines, polylines, rectangles, circles, arcs, texts, and blocks in one atomic transaction. |
| `draw_line` | No | `POST /entities/create` | `(x1, y1, x2, y2, z1=0.0, z2=0.0, layer='0')` — Draw 2D/3D line segment in meters. |
| `draw_polyline` | No | `POST /entities/create` | `(points, closed=False, layer='0')` — Draw 2D polyline from `"x1,y1;x2,y2;..."`. |
| `draw_rectangle` | No | `POST /entities/create` | `(x1, y1, x2, y2, layer='0')` — Draw axis-aligned closed polyline rectangle. |
| `draw_circle` | No | `POST /entities/create` | `(center_x, center_y, radius, layer='0')` — Draw circle in meters. |
| `draw_arc` | No | `POST /entities/create` | `(center_x, center_y, radius, start_angle, end_angle, layer='0')` — Draw circular arc using start/end angles in degrees. |
| `insert_text` | No | `POST /entities/create` | `(text, x, y, height=0.25, rotation=0.0, layer='0')` — Insert single-line `DBText` at `(x, y)`. |
| `insert_block` | No | `POST /entities/create` | `(block_name, x, y, scale=1.0, rotation=0.0, layer='0')` — Insert `BlockReference` at `(x, y)`. |
| `insert_raster_image` | No | `POST /drafting/raster-image` | `(file_path, x, y, z=0.0, width=None, height=None, scale=1.0, rotation=0.0, image_frame=0, space=None, layout_name=None, layer='0', expected_revision=None, document=None)` — Attach and place a raster image (`PNG`/`JPG`/`BMP`/`TIF`/`GIF`) in ModelSpace or PaperSpace with `IMAGEFRAME` control. |

---

## Domain 5: `autocad_layers` (4 Canonical)

C# Class: `AutoCAD.McpServer.Tools.LayerTools` | Python Module: `autocad_mcp.tools.layers`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `create_layer` | Yes | `POST /layers/create` | `(name, color_aci=7, linetype='Continuous', lineweight=None, is_plottable=None, description='', set_current=True, document=None)` — Create or update a layer with ACI color (`1-255`), on-demand `.lin` linetype loading, ISO lineweight, and plottability. |
| `set_current_layer` | No | `POST /layers/state` | `(name)` — Set existing layer as active `CLAYER`. |
| `set_layer_state` | Yes | `POST /layers/state` | `(name, action=None, color_aci=None, linetype=None, lineweight=None, is_plottable=None, description=None, set_current=None, document=None)` — Modify `on`/`off`/`freeze`/`thaw`/`lock`/`unlock` and visual properties. |
| `delete_layer` | No | `POST /layers/delete` | `(name, document=None)` — Safely erase an unused layer using ObjectARX `Purge` verification. |

---

## Domain 6: `autocad_documents` (9 Canonical)

C# Class: `AutoCAD.McpServer.Tools.DocumentTools` | Python Module: `autocad_mcp.tools.documents`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `list_documents` | Yes | `GET /documents` | `()` — List open AutoCAD MDI drawings with session IDs, revisions, and Edit Guard flags. |
| `cancel_command` | No | `POST /session/cancel` | `()` — Send `Ctrl+C` cancellation and report updated command-line state. |
| `allow_edit_document` | Yes | `POST /session/allow-edit` | `(document=None, allow=True)` — Authorize or revoke Edit Guard management on an open drawing. |
| `new_drawing` | No | `POST /documents/new` | `(template=None)` — Create a new Gateway-managed drawing (optionally from a `.dwt` template). |
| `open_drawing` | No | `POST /documents/open` | `(file_path, read_only=False)` — Open a local `.dwg` file without automatically bypassing Edit Guard. |
| `activate_document` | Yes | `POST /documents/activate` | `(document)` — Switch active MDI drawing by path, unique filename, or session ID. |
| `close_document` | No | `POST /documents/close` | `(document=None, save=False, discard_changes=False, save_as=None)` — Close drawing with explicit save/discard confirmation. |
| `get_system_variable` | No | `GET /sysvar` | `(name)` — Safely read non-security system variables (blocks `TRUSTEDPATHS`, `SECURELOAD`, `SAFEMODE`, etc.). |
| `set_system_variable` | No | `POST /sysvar` | `(name, value, document=None)` — Set a non-security system variable under Edit Guard protection. |

---

## Domain 7: `autocad_measurement` (3 Canonical + 1 Alias)

C# Class: `AutoCAD.McpServer.Tools.MeasurementTools` | Python Module: `autocad_mcp.tools.measurement`

| Tool | Core | Mechanism / Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `measure_distance` | Yes | Local Analytical | `(x1, y1, x2, y2, z1=0.0, z2=0.0)` — Compute 2D/3D distance, deltas, azimuth, and slope `%` without CAD. |
| `calculate_polygon_area` | No | Local Analytical | `(points)` — Compute area (`m2` and hectares), perimeter, centroid, and orientation from `"x1,y1;x2,y2;..."` without CAD. |
| `purge_and_audit_drawing` | Yes | `POST /execute` | `(target='all', audit=True)` — Purge unused symbol table definitions and run `_.-AUDIT`. |

**Legacy Alias (1):** `clean_and_purge_drawing` -> `purge_and_audit_drawing`.

---

## Domain 8: `autocad_plotting` (5 Canonical + 2 Aliases)

C# Class: `AutoCAD.McpServer.Tools.PlottingTools` | Python Module: `autocad_mcp.tools.plotting`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `create_layout` | No | `POST /plotting/layout` | `(layout_name, clear_default_viewport=True, activate=True, expected_revision=None, document=None)` — Create a PaperSpace layout tab natively and optionally remove its default viewport. |
| `configure_layout_page` | No | `POST /plotting/page-setup` | `(layout_name, paper_size='A1', plot_style='monochrome.ctb', orientation='landscape', plot_device='DWG To PDF.pc3', expected_revision=None, document=None)` — Configure ISO `A0`–`A4` page setup (`1:1 mm`, `PlotRotation`, CTB/STB). |
| `create_viewport` | Yes | `POST /plotting/viewport` | `(lower_x, lower_y, width, height, scale, center_model_x, center_model_y, center_model_z=0.0, view_direction=None, twist_angle=0.0, shade_plot=None, visual_style=None, lock_viewport=True, layer='0', layout_name=None, expected_revision=None, document=None)` — Create scaled PaperSpace viewport with optional 3D DCS camera controls. |
| `insert_title_block` | No | `POST /plotting/title-block` | `(corner_x, corner_y, project_title='ENGINEERING PROJECT', drawing_name='GENERAL PLAN', sheet_code='L-01', scale_text='1:1000', author='ENGINEERING', date=None, layout_name=None, template='iso_standard', logo_path=None, layer='FORMAT_TITLEBLOCK', expected_revision=None, document=None)` — Draw parametric PaperSpace title block (`iso_standard`, `compact`, `detailed`) with optional raster logo. |
| `export_pdf` | Yes | `POST /plotting/pdf` | `(layout_name, pdf_path, plot_style='monochrome.ctb', paper_size=None, orientation=None, expected_revision=None, document=None)` — Plot layout synchronously to PDF via `PlotEngine` (`BACKGROUNDPLOT=0`). |

**Legacy Aliases (2):** `configure_layout_sheet` -> `configure_layout_page`, `insert_parametric_titleblock` -> `insert_title_block`.

---

## Domain 9: `autocad_civil3d` (11 Canonical + 2 Aliases)

C# Class: `AutoCAD.McpServer.Tools.Civil3DTools` | Python Module: `autocad_mcp.tools.civil3d`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `check_civil3d_status` | Yes | `GET /civil/status` | `()` — Verify Civil 3D 2027 runtime modules and summarize surface/alignment counts. |
| `list_civil3d_surfaces` | Yes | `GET /civil/surfaces` | `()` — List TIN surfaces with min/max elevations and triangle counts. |
| `get_surface_elevation` | Yes | `GET /civil/surface/elevation` | `(x, y, surface_name=None)` — Query exact ground elevation (`Z`) at `(x, y)`. |
| `get_civil3d_surface_stats` | No | `GET /civil/surface/stats` | `(surface_name=None)` — Query elevation stats, 2D/3D areas, point count, and triangle count. |
| `list_civil3d_alignments` | Yes | `GET /civil/alignments` | `()` — List horizontal alignments with start/end stations and lengths. |
| `get_alignment_station_offset` | No | `GET /civil/alignment/station-offset` | `(x, y, alignment_name=None)` — Compute station and lateral offset from `(x, y)`. |
| `get_point_at_station` | No | `GET /civil/alignment/point-at-station` | `(station, offset=0.0, alignment_name=None)` — Compute world `(X, Y)` coordinates from station and offset. |
| `sample_longitudinal_profile` | Yes | `GET /civil/profile` | `(interval=20.0, alignment_name=None, surface_name=None)` — Sample longitudinal profile coordinates and ground elevations atomically. |
| `sample_civil3d_cross_section` | No | `GET /civil/cross-section` | `(station, left_width=20.0, right_width=20.0, step=2.0, alignment_name=None, surface_name=None)` — Sample transverse terrain cross-section at a station. |
| `create_civil3d_alignment` | No | `POST /civil/alignment/create` | `(name, points, radius=0.0, layer=None)` — Create horizontal alignment from PI vertices `"x1,y1;x2,y2;..."` and fixed curve radius. |
| `create_civil3d_surface` | No | `POST /civil/surface/create` | `(name, points)` — Create TIN surface from 3D points `"x1,y1,z1;x2,y2,z2;..."` (minimum 3 points). |

**Legacy Aliases (2):** `get_station_coordinate` -> `get_point_at_station`, `get_longitudinal_profile` -> `sample_longitudinal_profile`.

---

## Domain 10: `autocad_execution` (9 Canonical + 3 Aliases)

C# Class: `AutoCAD.McpServer.Tools.ExecutionTools` | Python Module: `autocad_mcp.tools.execution`

| Tool | Core | Gateway Route | Signature & Purpose |
| :--- | :---: | :--- | :--- |
| `ping_gateway` | Yes | `GET /_ping` | `()` — Verify Gateway liveness, bound port, PID, active document, and Edit Guard state. |
| `execute_batch_commands` | No | `POST /execute` | `(commands)` — Enqueue a sequential list of AutoCAD command strings. |
| `execute_command` | Yes | `POST /execute` | `(command)` — Enqueue a non-modal AutoCAD command or plugin macro (`_.-` recommended). |
| `evaluate_lisp` | Yes | `POST /lisp/eval` | `(code, timeout_ms=15000)` — Evaluate AutoLISP synchronously and convert `ResultBuffer` output to JSON. |
| `load_lisp_file` | No | `POST /lisp/load` | `(file_path, timeout_ms=60000)` — Validate and load an external `.lsp` file synchronously (up to `16 MiB`). |
| `zoom_extents` | No | `POST /execute` | `()` — Run `_.ZOOM E` in AutoCAD. |
| `save_drawing` | Yes | `POST /documents/save` | `(file_path=None, overwrite=False, dwg_version='current', bak_and_rename=True, create_directories=False, expected_revision=None, document=None)` — Save active or target drawing in place or via `Database.SaveAs` with overwrite protection. |
| `regen_all` | No | `POST /session/regen` | `(document=None)` — Regenerate all viewports synchronously via native Gateway endpoint (moved from `autocad_measurement` in `v2.7.0`). |
| `configure_drawing_units` | No | `POST /execute` | `()` — Configure metric meters (`INSUNITS=4`, `MEASUREMENT=1`, `LUNITS=2`, `LUPREC=3`). |

**Legacy Aliases (3):** `ping_autocad_gateway` -> `ping_gateway`, `execute_lisp` -> `evaluate_lisp`, `configure_drawing_environment` -> `configure_drawing_units`.
