# MCP Tools Technical Reference Manual (45 Tools)

This document is the comprehensive technical specification for all **45 tools** implemented in **`AutoCAD.McpServer`** and **`AutoCAD.McpPython`** for consumption by Artificial Intelligence Models (Claude, Antigravity, Cursor, ChatGPT) and developers under the Model Context Protocol (v2.2) standard.

All coordinates, dimensions, and elevations are strictly interpreted in **meters** (`INSUNITS = 4`).

---

## Internal Execution Mechanisms

Each MCP tool utilizes one of four execution channels engineered for maximum speed, predictability, and thread safety:

```
+-----------------------------------------------------------------------------+
|                          EXECUTION MODALITIES                               |
+--------------------------+--------------------------------------------------+
| 1. Local Analytical      | Evaluated locally in server memory (.NET/Python).|
|    Computation           | Zero latency, does not require running CAD.      |
+--------------------------+--------------------------------------------------+
| 2. Parametric AutoLISP   | Generates atomic (entmake ...) expressions.      |
|    Injection             | Atomic, OSNAP-independent, active-layer neutral. |
+--------------------------+--------------------------------------------------+
| 3. Non-Modal Command     | Enqueues non-modal (_.-) command strings into    |
|    (Application.Idle)    | ConcurrentQueue drained into the main UI thread. |
+--------------------------+--------------------------------------------------+
| 4. REST Gateway Endpoint | Queries transactional Gateway endpoints          |
|    (/civil, /context)    | (ObjectARX / AeccDbMgd at http://localhost:5055).|
+--------------------------+--------------------------------------------------+
```

---

## Module Index

- [Module 1: Inspection, Context & Vision (8 tools)](#module-1-inspection-context--vision)
- [Module 2: Parametric Geometry & Drawing (8 tools)](#module-2-parametric-geometry--drawing)
- [Module 3: Advanced Layer Management (3 tools)](#module-3-advanced-layer-management)
- [Module 4: Measurement, Computational Geometry & Audit (4 tools)](#module-4-measurement-computational-geometry--audit)
- [Module 5: Execution, Control & AutoLISP (8 tools)](#module-5-execution-control--autolisp)
- [Module 6: Sheet Layouts, Title Blocks & PDF Plotting (5 tools)](#module-6-sheet-layouts-title-blocks--pdf-plotting)
- [Module 7: Autodesk Civil 3D (9 tools)](#module-7-autodesk-civil-3d)

---

## Module 1: Inspection, Context & Vision

C# Class: `AutoCAD.McpServer.Tools.ContextTools`  
Mechanism: **REST Gateway Endpoints (`GET /context/*`)**

---

### 1. `get_drawing_info`
- **Purpose:** Inspect global metadata of the active DWG drawing in AutoCAD.
- **Mechanism:** `GET /context/info`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "get_drawing_info", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "drawing_name": "METRO_TUNNEL_SECTION1.dwg",
    "path": "C:\\Projects\\Metro\\METRO_TUNNEL_SECTION1.dwg",
    "is_read_only": false,
    "units_code": 4,
    "units_description": "Meters (INSUNITS = 4)"
  }
  ```

---

### 2. `read_layers`
- **Purpose:** Retrieve the full drawing layer catalog with colors, visibility, freeze, and lock states.
- **Mechanism:** `GET /context/layers`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "read_layers", "arguments": {} }
  ```
- **Success Response:**
  ```json
  [
    { "name": "0", "color": 7, "is_on": true, "is_frozen": false, "is_locked": false },
    { "name": "TUN-LINING", "color": 4, "is_on": true, "is_frozen": false, "is_locked": false }
  ]
  ```

---

### 3. `read_objects`
- **Purpose:** Count and summarize all geometric entities within `ModelSpace`.
- **Mechanism:** `GET /context/entities`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "read_objects", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "total_entities": 1420,
    "counts_by_type": {
      "AcDbLine": 450,
      "AcDbPolyline": 320,
      "AcDbCircle": 180,
      "AcDbText": 120,
      "AcDbBlockReference": 350
    }
  }
  ```

---

### 4. `get_drawing_extents`
- **Purpose:** Query drawing bounding box extents (`ExtMin`, `ExtMax`) and metric deltas.
- **Mechanism:** `GET /context/extents`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "get_drawing_extents", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "min_x": 500000.0, "min_y": 8000000.0, "min_z": 0.0,
    "max_x": 501250.0, "max_y": 8000850.0, "max_z": 45.2,
    "delta_x": 1250.0, "delta_y": 850.0
  }
  ```

---

### 5. `read_texts`
- **Purpose:** Search and list text strings in `DBText` and `MText` entities with (X, Y) coordinates.
- **Mechanism:** `GET /context/texts`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  |---|---|---|---|---|
  | `filter_text` | `string` | No | `""` | Substring filter for text search. |
- **Invocation Example:**
  ```json
  { "name": "read_texts", "arguments": { "filter_text": "STA." } }
  ```
- **Success Response:**
  ```json
  [
    { "text": "STA. 0+250", "layer": "TUN-ANNOT", "x": 500250.0, "y": 8000120.0, "z": 0.0, "height": 0.5 }
  ]
  ```

---

### 6. `list_available_blocks`
- **Purpose:** List block definition names registered in the current drawing.
- **Mechanism:** `GET /context/blocks`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "list_available_blocks", "arguments": {} }
  ```
- **Success Response:**
  ```json
  [ "SEGMENT_TYPE_A", "SEGMENT_TYPE_B", "ANCHOR_BOLT", "EMERGENCY_SIGN" ]
  ```

---

### 7. `read_selection`
- **Purpose:** Inspect count and entity types of objects currently selected by the user.
- **Mechanism:** `GET /context/selection`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "read_selection", "arguments": {} }
  ```
- **Success Response:**
  ```json
  { "selection_count": 3, "selected_types": [ "AcDbPolyline", "AcDbPolyline", "AcDbCircle" ] }
  ```

---

### 8. `capture_screen`
- **Purpose:** Capture a PNG screenshot of the active AutoCAD viewport for multimodal AI visual inspection.
- **Mechanism:** `GET /context/screenshot`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  |---|---|---|---|---|
  | `output_path` | `string` | No | `""` | Custom PNG destination path. |
- **Invocation Example:**
  ```json
  { "name": "capture_screen", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "image_path": "C:\\Users\\...\\AppData\\Local\\Temp\\acad_screen_20261106_113000.png",
    "width": 1920,
    "height": 1080
  }
  ```

---

## Module 2: Parametric Geometry & Drawing

C# Class: `AutoCAD.McpServer.Tools.DrawingTools`  
Mechanism: **AutoLISP `(entmake ...)` Injection**  
*Key Advantages:* Completely OSNAP-independent, active-layer neutral, and automatically creates the target layer if it does not exist.

---

### 9. `draw_batch_geometry`
- **Purpose:** **Preferred tool for multi-entity drafting.** Draws dozens or hundreds of geometric elements (lines, polylines, rectangles, circles, arcs, texts) in a single atomic AutoLISP call, reducing LLM token consumption and execution latency by ~95%.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `elements` | `string` / `array` | `string` / `array` | Yes | - | Array or JSON string containing entity objects to draw. |
  | `default_layer` | `string` | `string` | No | `"0"` | Fallback layer if an entity does not define `layer`. |
- **Supported Entity Types:**
  - `line`: `x1`, `y1`, `x2`, `y2`, `z1?`, `z2?`, `layer?`
  - `rectangle`: `x1`, `y1`, `x2`, `y2`, `layer?`
  - `circle`: `center_x` (or `x`), `center_y` (or `y`), `radius`, `center_z?`, `layer?`
  - `arc`: `center_x`, `center_y`, `radius`, `start_angle`, `end_angle` (degrees), `layer?`
  - `polyline`: `points` (`"x1,y1;x2,y2;..."` or `[[x,y],...]`), `closed?` (bool), `layer?`
  - `text`: `text`, `x`, `y`, `height?` (default 0.25), `rotation?` (degrees), `layer?`
- **Invocation Example:**
  ```json
  {
    "name": "draw_batch_geometry",
    "arguments": {
      "elements": [
        { "type": "line", "x1": 0, "y1": 0, "x2": 10, "y2": 0, "layer": "ARQ-WALLS" },
        { "type": "rectangle", "x1": 2, "y1": 2, "x2": 5, "y2": 5, "layer": "ARQ-WINDOWS" },
        { "type": "circle", "center_x": 8, "center_y": 8, "radius": 2.5, "layer": "ARQ-COLUMNS" },
        { "type": "text", "text": "MAIN HALL", "x": 5, "y": 5, "height": 0.35, "layer": "ARQ-TEXTS" }
      ]
    }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "total": 4,
    "breakdown": { "line": 1, "rectangle": 1, "circle": 1, "text": 1 }
  }
  ```

---

### 10. `draw_line`
- **Purpose:** Draw a 3D straight line segment between two metric points.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `x1` | `double` | `number` | Yes | - | Start point X (m). |
  | `y1` | `double` | `number` | Yes | - | Start point Y (m). |
  | `x2` | `double` | `number` | Yes | - | End point X (m). |
  | `y2` | `double` | `number` | Yes | - | End point Y (m). |
  | `z1` | `double` | `number` | No | `0.0` | Start point Z elevation (m). |
  | `z2` | `double` | `number` | No | `0.0` | End point Z elevation (m). |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "draw_line",
    "arguments": { "x1": 0.0, "y1": 0.0, "x2": 20.0, "y2": 0.0, "layer": "TUN-AXIS" }
  }
  ```

---

### 11. `draw_polyline`
- **Purpose:** Draw a 2D lightweight polyline (`LWPOLYLINE`), open or closed, from coordinate vertices.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `points` | `string` | `string` | Yes | - | Vertices in format `"x1,y1;x2,y2;..."` (min. 2). |
  | `closed` | `bool` | `boolean` | No | `false` | Closes polyline connecting last to first vertex. |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "draw_polyline",
    "arguments": {
      "points": "0,0;10,0;10,5;0,5",
      "closed": true,
      "layer": "TUN-LINING"
    }
  }
  ```

---

### 12. `draw_rectangle`
- **Purpose:** Draw a closed polyline rectangle defined by two opposite diagonal corner points.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `x1`, `y1` | `double` | `number` | Yes | - | First corner point (m). |
  | `x2`, `y2` | `double` | `number` | Yes | - | Opposite diagonal corner point (m). |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "draw_rectangle",
    "arguments": { "x1": 5.0, "y1": 2.0, "x2": 15.0, "y2": 8.0, "layer": "ARQ-WALLS" }
  }
  ```

---

### 13. `draw_circle`
- **Purpose:** Draw a parametric circle given center point and radius.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `center_x`, `center_y` | `double` | `number` | Yes | - | Center coordinates (m). |
  | `radius` | `double` | `number` | Yes | - | Radius in meters (> 0). |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "draw_circle",
    "arguments": { "center_x": 0.0, "center_y": 0.0, "radius": 4.50, "layer": "TUN-EXCAVATION" }
  }
  ```

---

### 14. `draw_arc`
- **Purpose:** Draw a counterclockwise circular arc.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `center_x`, `center_y` | `double` | `number` | Yes | - | Center coordinates (m). |
  | `radius` | `double` | `number` | Yes | - | Radius in meters (> 0). |
  | `start_angle` | `double` | `number` | Yes | - | Start angle in degrees (0 = East). |
  | `end_angle` | `double` | `number` | Yes | - | End angle in degrees. |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "draw_arc",
    "arguments": {
      "center_x": 0.0, "center_y": 0.0, "radius": 4.5,
      "start_angle": 0.0, "end_angle": 180.0, "layer": "TUN-VAULT"
    }
  }
  ```

---

### 15. `insert_text`
- **Purpose:** Insert single-line text (`DBText`) in model space.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `text` | `string` | `string` | Yes | - | Text string content. |
  | `x`, `y` | `double` | `number` | Yes | - | Insertion coordinates (m). |
  | `height` | `double` | `number` | No | `0.25` | Text height in meters (> 0). |
  | `rotation` | `double` | `number` | No | `0.0` | Rotation angle in degrees. |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "insert_text",
    "arguments": { "text": "STANDARD SECTION STA 1+200", "x": 10.0, "y": 2.0, "height": 0.35, "layer": "TUN-LABELS" }
  }
  ```

---

### 16. `insert_block`
- **Purpose:** Insert a reference of an existing block definition.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `block_name` | `string` | `string` | Yes | - | Existing block name. |
  | `x`, `y` | `double` | `number` | Yes | - | Insertion coordinates (m). |
  | `scale` | `double` | `number` | No | `1.0` | Uniform scale factor (> 0). |
  | `rotation` | `double` | `number` | No | `0.0` | Rotation angle in degrees. |
  | `layer` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "insert_block",
    "arguments": { "block_name": "SEGMENT_TYPE_A", "x": 0.0, "y": 4.5, "scale": 1.0, "rotation": 45.0 }
  }
  ```

---

## Module 3: Advanced Layer Management

C# Class: `AutoCAD.McpServer.Tools.LayerTools`  
Mechanism: **ActiveX AutoLISP (`vla-*`) with Safe Error Handling**

---

### 17. `create_layer`
- **Purpose:** Create or update a layer with ACI color, linetype, and description without modal dialogs.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `name` | `string` | `string` | Yes | - | Layer name (e.g. `STR-COLUMNS`). |
  | `color_aci` | `int` | `integer` | No | `7` | ACI color code (1–255). |
  | `linetype` | `string` | `string` | No | `"Continuous"` | Linetype name (loads from `acadiso.lin`). |
  | `description`| `string` | `string` | No | `""` | Technical description or note. |
- **Invocation Example:**
  ```json
  {
    "name": "create_layer",
    "arguments": { "name": "STR-BEAMS", "color_aci": 1, "linetype": "Continuous", "description": "Reinforced concrete beams" }
  }
  ```

---

### 18. `set_current_layer`
- **Purpose:** Set the active working layer (`CLAYER`).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `name` | `string` | `string` | Yes | - | Existing, un-frozen layer name. |
- **Invocation Example:**
  ```json
  { "name": "set_current_layer", "arguments": { "name": "TUN-LINING" } }
  ```

---

### 19. `set_layer_state`
- **Purpose:** Control layer visibility, freeze, or lock status and regenerate viewports.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `name` | `string` | `string` | Yes | - | Layer name. |
  | `action` | `string` | `string` | Yes | - | `on`, `off`, `freeze`, `thaw`, `lock`, `unlock`. |
- **Invocation Example:**
  ```json
  { "name": "set_layer_state", "arguments": { "name": "TUN-AUXILIARY", "action": "freeze" } }
  ```

---

## Module 4: Measurement, Computational Geometry & Audit

C# Class: `AutoCAD.McpServer.Tools.MeasurementTools`  
Mechanism: **Local Analytical Computation in .NET/Python** (for measurements and geometry) / **Non-Modal CAD Commands** (for purge and audit).

---

### 20. `measure_distance`
- **Purpose:** Compute 2D/3D distance, metric deltas, azimuth, and slope between two points. Evaluated instantly without requiring CAD to be open.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `x1`, `y1` | `double` | `number` | Yes | - | Start point coordinates (m). |
  | `x2`, `y2` | `double` | `number` | Yes | - | End point coordinates (m). |
  | `z1`, `z2` | `double` | `number` | No | `0.0` | Z elevations for both points (m). |
- **Invocation Example:**
  ```json
  {
    "name": "measure_distance",
    "arguments": { "x1": 0.0, "y1": 0.0, "z1": 0.0, "x2": 3.0, "y2": 4.0, "z2": 12.0 }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "distance_2d": 5.0,
    "distance_3d": 13.0,
    "delta_x": 3.0, "delta_y": 4.0, "delta_z": 12.0,
    "angle_degrees": 53.1301,
    "slope_percent": 240.0,
    "units": "meters"
  }
  ```

---

### 21. `calculate_polygon_area`
- **Purpose:** Compute area in $m^2$, hectares, perimeter, centroid, and winding order of a closed polygon from vertices (Gauss algorithm with coordinate translation for large UTM values).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `points` | `string` | `string` | Yes | - | Vertices `"x1,y1;x2,y2;..."` (min. 3). |
- **Invocation Example:**
  ```json
  {
    "name": "calculate_polygon_area",
    "arguments": { "points": "500000,8000000;500010,8000000;500010,8000005;500000,8000005" }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "area_m2": 50.0,
    "area_hectares": 0.005,
    "perimeter_m": 30.0,
    "centroid": { "x": 500005.0, "y": 8000002.5 },
    "orientation": "counterclockwise",
    "vertex_count": 4
  }
  ```

---

### 22. `clean_and_purge_drawing`
- **Purpose:** Purge unreferenced definitions in two consecutive passes via `_.-PURGE` and audit/repair the DWG database via `_.AUDIT`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `target` | `string` | `string` | No | `"all"` | `all`, `blocks`, `layers`, `linetypes`, `textstyles`, `dimensionstyles`. |
  | `audit` | `bool` | `boolean` | No | `true` | If `true`, executes `AUDIT` fixing detected errors. |
- **Invocation Example:**
  ```json
  { "name": "clean_and_purge_drawing", "arguments": { "target": "all", "audit": true } }
  ```

---

### 23. `regen_all`
- **Purpose:** Force complete graphics regeneration across all viewports (`_.REGENALL`).
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "regen_all", "arguments": {} }
  ```

---

## Module 5: Execution, Control & AutoLISP

C# Class: `AutoCAD.McpServer.Tools.ExecutionTools`  
Mechanism: **ConcurrentQueue (`Application.Idle`) with `\n` Terminators and `\x03\x03` Auto-Cancellation**

---

### 24. `ping_autocad_gateway`
- **Purpose:** Check whether the AutoCAD Gateway is active and listening on `http://localhost:5055/`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "ping_autocad_gateway", "arguments": {} }
  ```

---

### 25. `execute_batch_commands`
- **Purpose:** **Batch command runner.** Dispatches a sequential list of AutoCAD commands, scripts, or macros in a single execution payload without waiting for roundtrips.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `commands` | `string` / `array` | `string` / `array` | Yes | Array or JSON string of AutoCAD commands. |
- **Invocation Example:**
  ```json
  {
    "name": "execute_batch_commands",
    "arguments": {
      "commands": [
        "_.-LAYER\nM\nARQ-WALLS\nC\n1\n\n\n",
        "_.ZOOM\nE\n"
      ]
    }
  }
  ```

---

### 26. `execute_command`
- **Purpose:** Inject raw native CAD commands or plugin macros (e.g. YQArch) in non-modal text mode (`_.-`).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `command` | `string` | `string` | Yes | Command string with `\n` as Enter. |
- **Invocation Example:**
  ```json
  {
    "name": "execute_command",
    "arguments": { "command": "_.CIRCLE\n0,0\n5.0\n_.ZOOM\nE\n" }
  }
  ```

---

### 27. `execute_lisp`
- **Purpose:** Evaluate dynamic AutoLISP code directly in AutoCAD memory.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `code` | `string` | `string` | Yes | Complete AutoLISP expression. |
- **Invocation Example:**
  ```json
  {
    "name": "execute_lisp",
    "arguments": { "code": "(setvar \"OSMODE\" 0)" }
  }
  ```

---

### 28. `load_lisp_file`
- **Purpose:** Load and execute an external `.lsp` file from disk via `(load ...)`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `file_path`| `string`| `string` | Yes | Absolute Windows path to `.lsp` file. |
- **Invocation Example:**
  ```json
  {
    "name": "load_lisp_file",
    "arguments": { "file_path": "C:/scripts/sample_metro_tunnel.lsp" }
  }
  ```

---

### 29. `configure_drawing_environment`
- **Purpose:** Standardize the metric drafting environment: `INSUNITS = 4` (meters), `MEASUREMENT = 1`, `LUNITS = 2`, `LUPREC = 3`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "configure_drawing_environment", "arguments": {} }
  ```

---

### 30. `zoom_extents`
- **Purpose:** Trigger a full extents zoom (`_.ZOOM E`) in the active viewport.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "zoom_extents", "arguments": {} }
  ```

---

### 31. `save_drawing`
- **Purpose:** Save the active DWG file to disk (`_.QSAVE`).
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "save_drawing", "arguments": {} }
  ```

---

## Module 6: Sheet Layouts, Title Blocks & PDF Plotting

C# Class: `AutoCAD.McpServer.Tools.PlottingTools`  
Mechanism: **Paper Space (`PSPACE`), Scaled Viewports (`XP`), and Virtual Plotter `DWG To PDF.pc3`**

---

### 32. `create_layout`
- **Purpose:** Create a new paper space layout tab and set it active.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `layout_name` | `string` | `string` | Yes | Unique layout tab name (e.g. `SHEET-TUNNEL-01`). |
- **Invocation Example:**
  ```json
  { "name": "create_layout", "arguments": { "layout_name": "SHEET-01" } }
  ```

---

### 33. `configure_layout_sheet`
- **Purpose:** Configure sheet paper size (A0 to A4), virtual plotter `DWG To PDF.pc3`, and 1:1 scale in mm with `monochrome.ctb`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `layout_name` | `string` | `string` | Yes | - | Layout name. |
  | `sheet_size` | `string` | `string` | No | `"A1"` | `A0`, `A1`, `A2`, `A3`, `A4`. |
  | `plot_style` | `string` | `string` | No | `"monochrome.ctb"` | Plot style table. |
- **Invocation Example:**
  ```json
  {
    "name": "configure_layout_sheet",
    "arguments": { "layout_name": "SHEET-01", "sheet_size": "A1" }
  }
  ```

---

### 34. `create_viewport`
- **Purpose:** Insert a paper space viewport (`MVIEW`) calibrated to exact metric scale (`1000/Scale XP`) centered on model coordinates (X, Y).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `layout_name` | `string` | `string` | No | Active layout | Target layout tab name. |
  | `x`, `y` | `double` | `number` | Yes | Bottom-left corner on paper sheet (mm). |
  | `width`, `height` | `double` | `number` | Yes | Viewport width and height on paper (mm). |
  | `scale` | `double` | `number` | Yes | Metric scale denominator (e.g. `1000` for 1:1000, `500` for 1:500). |
  | `model_x`, `model_y` | `double` | `number` | Yes | Center coordinates in model space (m). |
- **Invocation Example:**
  ```json
  {
    "name": "create_viewport",
    "arguments": {
      "layout_name": "SHEET-01",
      "x": 25.0, "y": 25.0, "width": 500.0, "height": 350.0,
      "scale": 500.0, "model_x": 500100.0, "model_y": 8000050.0
    }
  }
  ```

---

### 35. `insert_parametric_titleblock`
- **Purpose:** Draw a formal standardized technical title block (180x45 mm) with metadata fields in paper space.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `layout_name` | `string` | `string` | No | Active layout | Target layout tab name. |
  | `corner_x`, `corner_y` | `double` | `number` | Yes | - | Bottom-right sheet margin coordinates (mm). |
  | `title` | `string` | `string` | No | `"ENGINEERING PROJECT"` | Project name. |
  | `drawing_number` | `string` | `string` | No | `"L-01"` | Drawing number / sheet code. |
  | `scale` | `string` | `string` | No | `"1:1000"` | Scale label. |
  | `author` | `string` | `string` | No | `"ENGINEERING"` | Designer / engineer. |
  | `date` | `string` | `string` | No | Current Date | Issue date. |
- **Invocation Example:**
  ```json
  {
    "name": "insert_parametric_titleblock",
    "arguments": {
      "layout_name": "SHEET-01",
      "corner_x": 820.0, "corner_y": 20.0,
      "title": "METRO LINE 1",
      "drawing_number": "TUN-01",
      "scale": "1:500",
      "author": "CIVIL ENGINEER"
    }
  }
  ```

---

### 36. `export_pdf`
- **Purpose:** Plot layout to a physical PDF file on disk using `DWG To PDF.pc3`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `layout_name` | `string` | `string` | Yes | - | Layout name to plot. |
  | `pdf_path` | `string` | `string` | Yes | - | Absolute output path for PDF. |
  | `plot_style` | `string` | `string` | No | `"monochrome.ctb"` | Plot style table. |
- **Invocation Example:**
  ```json
  {
    "name": "export_pdf",
    "arguments": {
      "layout_name": "SHEET-01",
      "pdf_path": "C:/Projects/Metro/Tunnel_Sheet_01.pdf"
    }
  }
  ```

---

## Module 7: Autodesk Civil 3D

C# Class: `AutoCAD.McpServer.Tools.Civil3DTools`  
Mechanism: **Dedicated REST Endpoints (`/civil/*`) and Dynamic Reflection over `AeccDbMgd.dll`**

---

### 37. `check_civil3d_status`
- **Purpose:** Verify if active session is running Civil 3D and report surface and alignment counts.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "check_civil3d_status", "arguments": {} }
  ```
- **Response:**
  ```json
  { "status": "success", "civil_available": true, "surfaces": 2, "alignments": 1 }
  ```

---

### 38. `list_civil3d_surfaces`
- **Purpose:** Inventory TIN surfaces with min/max elevations and triangle counts.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "list_civil3d_surfaces", "arguments": {} }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "data": [
      {
        "name": "NATURAL_GROUND",
        "type": "TinSurface",
        "min_elevation": 2450.2,
        "max_elevation": 2680.8,
        "triangles": 45200
      }
    ]
  }
  ```

---

### 39. `get_surface_elevation`
- **Purpose:** Return exact ground surface elevation (Z) at coordinates (X, Y).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `surface_name` | `string?` | `string` | No | TIN surface name (defaults to first surface). |
  | `x`, `y` | `double` | `number` | Yes | Easting and Northing coordinates (m). |
- **Invocation Example:**
  ```json
  {
    "name": "get_surface_elevation",
    "arguments": { "surface_name": "NATURAL_GROUND", "x": 500250.0, "y": 8000100.0 }
  }
  ```
- **Response:**
  ```json
  { "status": "success", "surface": "NATURAL_GROUND", "x": 500250.0, "y": 8000100.0, "elevation": 2514.85 }
  ```

---

### 40. `list_civil3d_alignments`
- **Purpose:** List horizontal alignments with station ranges and lengths.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "list_civil3d_alignments", "arguments": {} }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "data": [
      {
        "name": "MAIN_CORRIDOR_ALIGNMENT",
        "starting_station": 0.0,
        "ending_station": 2450.0,
        "initial_stake": "0+000.00",
        "final_stake": "2+450.00",
        "length": 2450.0
      }
    ]
  }
  ```

---

### 41. `get_alignment_station_offset`
- **Purpose:** Compute station (chainage) and lateral offset (+ right / - left) of a point relative to an alignment.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `alignment_name` | `string?` | `string` | No | Alignment name. |
  | `x`, `y` | `double` | `number` | Yes | Easting and Northing coordinates (m). |
- **Invocation Example:**
  ```json
  {
    "name": "get_alignment_station_offset",
    "arguments": { "alignment_name": "MAIN_CORRIDOR_ALIGNMENT", "x": 500248.0, "y": 8000105.0 }
  }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "alignment": "MAIN_CORRIDOR_ALIGNMENT",
    "station": 250.0,
    "station_formatted": "0+250.00",
    "offset": -3.5
  }
  ```

---

### 42. `get_station_coordinate`
- **Purpose:** Inverse operation: compute absolute coordinates (X, Y) given station and lateral offset.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `alignment_name` | `string?`| `string` | No | null | Alignment name. |
  | `station` | `double` | `number` | Yes | - | Station value in meters (e.g. `1250.5`). |
  | `offset` | `double` | `number` | No | `0.0` | Lateral offset in meters (+ right / - left). |
- **Invocation Example:**
  ```json
  {
    "name": "get_station_coordinate",
    "arguments": { "alignment_name": "MAIN_CORRIDOR_ALIGNMENT", "station": 500.0, "offset": 2.5 }
  }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "alignment": "MAIN_CORRIDOR_ALIGNMENT",
    "station": 500.0,
    "offset": 2.5,
    "x": 500498.25,
    "y": 8000102.10
  }
  ```

---

### 43. `get_longitudinal_profile`
- **Purpose:** Sample ground elevation profile along an alignment every `station_interval` meters. Returns minimum/maximum elevation, average slope %, and station/elevation samples.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `alignment_name` | `string?`| `string` | No | null | Alignment name. |
  | `surface_name` | `string?` | `string` | No | null | Surface name. |
  | `station_interval`| `double`| `number` | No | `20.0` | Sampling distance in meters (> 0). |
- **Invocation Example:**
  ```json
  {
    "name": "get_longitudinal_profile",
    "arguments": { "alignment_name": "MAIN_CORRIDOR_ALIGNMENT", "station_interval": 50.0 }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "alignment": "MAIN_CORRIDOR_ALIGNMENT",
    "surface": "NATURAL_GROUND",
    "station_interval": 50.0,
    "starting_station": 0.0,
    "ending_station": 250.0,
    "total_samples": 6,
    "min_elevation": 2450.0,
    "max_elevation": 2465.5,
    "average_slope_percent": 6.2,
    "samples": [
      { "station": 0.0, "x": 500000.0, "y": 8000000.0, "elevation": 2450.0 },
      { "station": 50.0, "x": 500050.0, "y": 8000000.0, "elevation": 2453.2 },
      { "station": 100.0, "x": 500100.0, "y": 8000000.0, "elevation": 2456.8 },
      { "station": 150.0, "x": 500150.0, "y": 8000000.0, "elevation": 2460.1 },
      { "station": 200.0, "x": 500200.0, "y": 8000000.0, "elevation": 2463.0 },
      { "station": 250.0, "x": 500250.0, "y": 8000000.0, "elevation": 2465.5 }
    ]
  }
  ```

---

### 44. `create_civil3d_alignment`
- **Purpose:** Parametrically create a horizontal alignment from PIs with tangent circular curves of fixed radius.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `name` | `string` | `string` | Yes | - | Unique alignment name. |
  | `points` | `string` | `string` | Yes | - | PIs format `"x1,y1;x2,y2;..."` (min. 2). |
  | `radius` | `double` | `number` | No | `0.0` | Curve radius in meters (0 = no curves). |
  | `layer` | `string?` | `string` | No | null | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "create_civil3d_alignment",
    "arguments": {
      "name": "NORTH_BYPASS",
      "points": "500000,8000000;500500,8000200;501000,8000100",
      "radius": 150.0
    }
  }
  ```

---

### 45. `create_civil3d_surface`
- **Purpose:** Build a new topographic TIN surface from a point cloud (X, Y, Z).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `name` | `string` | `string` | Yes | Unique surface name. |
  | `points` | `string` | `string` | Yes | Points format `"x1,y1,z1;x2,y2,z2;..."` (min. 3). |
- **Invocation Example:**
  ```json
  {
    "name": "create_civil3d_surface",
    "arguments": {
      "name": "QUARRY_SURFACE",
      "points": "500000,8000000,2450;500100,8000000,2455;500050,8000100,2462"
    }
  }
  ```
