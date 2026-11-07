# Tool Decision Matrix — AutoCAD & Civil 3D MCP

This matrix maps high-level engineering and drafting objectives to the optimal tool call sequence in `luiccian-autocad-mcp`.

---

| User / Workflow Objective | Primary Tool Sequence | Key Parameters / Notes |
|---|---|---|
| **Start any session / Verify setup** | `ping_autocad_gateway` &rarr; `get_drawing_info` &rarr; `configure_drawing_environment` | Mandatory pre-flight. Sets `INSUNITS = 4` (meters). |
| **Draw architectural floor plan / room** | `create_layer` &rarr; `draw_batch_geometry` &rarr; `zoom_extents` &rarr; `capture_screen` | **Do not loop single tools.** Pass arrays of lines, rects, polylines, and text in one atomic call. |
| **Inspect existing drawing layers & objects** | `read_layers` &rarr; `read_objects` &rarr; `get_drawing_extents` | Non-destructive database reads via transactions. |
| **Read texts / annotations in drawing** | `read_texts` | Returns text strings, styles, coordinates, and layer. |
| **Query ground elevation at coordinates** | `get_surface_elevation` | Parameters: `surface_name`, `x`, `y`. Returns exact $Z$ elevation. |
| **Sample longitudinal profile along road/axis** | `list_civil3d_alignments` &rarr; `get_longitudinal_profile` | Parameters: `alignment_name`, `surface_name`, `station_interval`. |
| **Calculate polygon area or boundary** | `calculate_polygon_area` | Returns area in $m^2$ and hectares, perimeter, and centroid (Gauss algorithm). |
| **Measure distance, slope and azimuth** | `measure_distance` | Local analytical calculation: 2D/3D distance, $\Delta X, \Delta Y, \Delta Z$, azimuth, slope %. |
| **Purge drawing and audit database** | `clean_and_purge_drawing` | Runs two passes of `_.-PURGE` and `_.AUDIT` to fix errors and reduce file size. |
| **Evaluate custom AutoLISP code** | `execute_lisp` (in-memory) or `load_lisp_file` (from disk) | Wrap strings carefully. Useful for complex `entmake` or legacy routines. |
| **Plot layout to physical PDF file** | `create_layout` &rarr; `configure_layout_sheet` &rarr; `create_viewport` &rarr; `insert_parametric_titleblock` &rarr; `export_pdf` | Full automated sheet generation with calibrated metric scale (`1000/Scale XP`). |
