# MCP Tools Technical Reference Manual (45 Tools)

This document is the comprehensive technical specification for all **45 tools** implemented in **`AutoCAD.McpServer`** and **`AutoCAD.McpPython`** for consumption by Artificial Intelligence Models (Claude, Antigravity, Cursor, ChatGPT) and developers under the Model Context Protocol (v2.2) standard.

All coordinates, dimensions, and elevations are strictly interpreted in **meters** (`INSUNITS = 4`).

---

## 🧭 Internal Execution Mechanisms

Each MCP tool utilizes one of four execution channels engineered for maximum speed, predictability, and thread safety:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          EXECUTION MODALITIES                               │
├──────────────────────────┬──────────────────────────────────────────────────┤
│ 1. Local Analytical      │ Evaluated locally in server memory (.NET/Python).│
│    Computation           │ Zero latency, does not require running CAD.      │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ 2. Parametric AutoLISP   │ Generates atomic (entmake ...) expressions.      │
│    Injection             │ Atomic, OSNAP-independent, active-layer neutral. │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ 3. Non-Modal Command     │ Enqueues non-modal (_.-) command strings into    │
│    (Application.Idle)    │ ConcurrentQueue drained into the main UI thread. │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ 4. REST Gateway Endpoint │ Queries transactional Gateway endpoints          │
│    (/civil, /context)    │ (ObjectARX / AeccDbMgd at http://localhost:5055).│
└──────────────────────────┴──────────────────────────────────────────────────┘
```

---

## 📑 Module Index

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

### 1. `obtener_info_dibujo`
- **Purpose:** Inspect global metadata of the active DWG drawing in AutoCAD.
- **Mechanism:** `GET /context/info`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "obtener_info_dibujo", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "DrawingName": "METRO_TUNNEL_SECTION1.dwg",
    "Path": "C:\\Projects\\Metro\\METRO_TUNNEL_SECTION1.dwg",
    "IsReadOnly": false,
    "UnitsCode": 4,
    "UnitsDescription": "Meters (INSUNITS = 4)"
  }
  ```

---

### 2. `leer_capas_autocad`
- **Purpose:** Retrieve the full drawing layer catalog with colors, visibility, freeze, and lock states.
- **Mechanism:** `GET /context/layers`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "leer_capas_autocad", "arguments": {} }
  ```
- **Success Response:**
  ```json
  [
    { "Name": "0", "Color": 7, "IsOn": true, "IsFrozen": false, "IsLocked": false },
    { "Name": "TUN-LINING", "Color": 4, "IsOn": true, "IsFrozen": false, "IsLocked": false }
  ]
  ```

---

### 3. `leer_objetos_autocad`
- **Purpose:** Count and summarize all geometric entities within `ModelSpace`.
- **Mechanism:** `GET /context/entities`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "leer_objetos_autocad", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "TotalEntities": 1420,
    "CountsByType": {
      "AcDbLine": 450,
      "AcDbPolyline": 320,
      "AcDbCircle": 180,
      "AcDbText": 120,
      "AcDbBlockReference": 350
    }
  }
  ```

---

### 4. `obtener_limites_dibujo`
- **Purpose:** Query drawing bounding box extents (`ExtMin`, `ExtMax`) and metric deltas.
- **Mechanism:** `GET /context/extents`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "obtener_limites_dibujo", "arguments": {} }
  ```
- **Success Response:**
  ```json
  {
    "MinX": 500000.0, "MinY": 8000000.0, "MinZ": 0.0,
    "MaxX": 501250.0, "MaxY": 8000850.0, "MaxZ": 45.2,
    "DeltaX": 1250.0, "DeltaY": 850.0
  }
  ```

---

### 5. `leer_textos_autocad`
- **Purpose:** Search and list text strings in `DBText` and `MText` entities with (X, Y) coordinates.
- **Mechanism:** `GET /context/texts`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "leer_textos_autocad", "arguments": {} }
  ```
- **Success Response:**
  ```json
  [
    { "Text": "STA. 0+250", "Layer": "TUN-ANNOT", "X": 500250.0, "Y": 8000120.0, "Z": 0.0, "Height": 0.5 }
  ]
  ```

---

### 6. `listar_bloques_disponibles`
- **Purpose:** List block definition names registered in the current drawing.
- **Mechanism:** `GET /context/blocks`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "listar_bloques_disponibles", "arguments": {} }
  ```
- **Success Response:**
  ```json
  [ "SEGMENT_TYPE_A", "SEGMENT_TYPE_B", "ANCHOR_BOLT", "EMERGENCY_SIGN" ]
  ```

---

### 7. `leer_seleccion_autocad`
- **Purpose:** Inspect count and entity types of objects currently selected by the user.
- **Mechanism:** `GET /context/selection`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "leer_seleccion_autocad", "arguments": {} }
  ```
- **Success Response:**
  ```json
  { "Count": 3, "Types": [ "AcDbPolyline", "AcDbPolyline", "AcDbCircle" ] }
  ```

---

### 8. `capturar_pantalla_autocad`
- **Purpose:** Capture a PNG screenshot of the active AutoCAD viewport for multimodal AI visual inspection.
- **Mechanism:** `GET /context/screenshot`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "capturar_pantalla_autocad", "arguments": {} }
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

### 9. `dibujar_geometria_lote`
- **Purpose:** ⚡ **Preferred tool for multi-entity drafting.** Draws dozens or hundreds of geometric elements (lines, polylines, rectangles, circles, arcs, texts) in a single atomic AutoLISP call, reducing LLM token consumption and execution latency by ~95%.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `elementosJson` | `string` / `array` | `string` / `array` | Yes | - | Array or JSON string containing entity objects to draw. |
  | `capaPorDefecto` | `string` | `string` | No | `"0"` | Fallback layer if an entity does not define `capa`. |
- **Supported Entity Types:**
  - `linea`: `x1`, `y1`, `x2`, `y2`, `z1?`, `z2?`, `capa?`
  - `rectangulo`: `x1`, `y1`, `x2`, `y2`, `capa?`
  - `circulo`: `centroX` (or `x`), `centroY` (or `y`), `radio`, `centroZ?`, `capa?`
  - `arco`: `centroX`, `centroY`, `radio`, `anguloInicio`, `anguloFin` (degrees), `capa?`
  - `polilinea`: `puntos` (`"x1,y1;x2,y2;..."` or `[[x,y],...]`), `cerrada?` (bool), `capa?`
  - `texto`: `texto`, `x`, `y`, `altura?` (default 0.25), `rotacion?` (degrees), `capa?`
- **Invocation Example:**
  ```json
  {
    "name": "dibujar_geometria_lote",
    "arguments": {
      "elementosJson": [
        { "tipo": "linea", "x1": 0, "y1": 0, "x2": 10, "y2": 0, "capa": "ARQ-MUROS" },
        { "tipo": "rectangulo", "x1": 2, "y1": 2, "x2": 5, "y2": 5, "capa": "ARQ-VENTANAS" },
        { "tipo": "circulo", "centroX": 8, "centroY": 8, "radio": 2.5, "capa": "ARQ-COLUMNAS" },
        { "tipo": "texto", "texto": "SALA PRINCIPAL", "x": 5, "y": 5, "altura": 0.35, "capa": "ARQ-TEXTOS" }
      ]
    }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "total": 4,
    "desglose": { "linea": 1, "rectangulo": 1, "circulo": 1, "texto": 1 }
  }
  ```

---

### 10. `dibujar_linea`
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
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "dibujar_linea",
    "arguments": { "x1": 0.0, "y1": 0.0, "x2": 20.0, "y2": 0.0, "capa": "TUN-AXIS" }
  }
  ```

---

### 10. `dibujar_polilinea`
- **Purpose:** Draw a 2D lightweight polyline (`LWPOLYLINE`), open or closed, from coordinate vertices.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `puntos` | `string` | `string` | Yes | - | Vertices in format `"x1,y1;x2,y2;..."` (min. 2). |
  | `cerrada` | `bool` | `boolean` | No | `false` | Closes polyline connecting last to first vertex. |
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "dibujar_polilinea",
    "arguments": {
      "puntos": "0,0;10,0;10,5;0,5",
      "cerrada": true,
      "capa": "TUN-LINING"
    }
  }
  ```

---

### 11. `dibujar_rectangulo`
- **Purpose:** Draw a closed polyline rectangle defined by two opposite diagonal corner points.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `x1`, `y1` | `double` | `number` | Yes | - | First corner point (m). |
  | `x2`, `y2` | `double` | `number` | Yes | - | Opposite diagonal corner point (m). |
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "dibujar_rectangulo",
    "arguments": { "x1": 5.0, "y1": 2.0, "x2": 15.0, "y2": 8.0, "capa": "ARQ-WALLS" }
  }
  ```

---

### 12. `dibujar_circulo`
- **Purpose:** Draw a parametric circle given center point and radius.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `centroX`, `centroY` | `double` | `number` | Yes | - | Center coordinates (m). |
  | `radio` | `double` | `number` | Yes | - | Radius in meters (> 0). |
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "dibujar_circulo",
    "arguments": { "centroX": 0.0, "centroY": 0.0, "radio": 4.50, "capa": "TUN-EXCAVATION" }
  }
  ```

---

### 13. `dibujar_arco`
- **Purpose:** Draw a counterclockwise circular arc.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `centroX`, `centroY` | `double` | `number` | Yes | - | Center coordinates (m). |
  | `radio` | `double` | `number` | Yes | - | Radius in meters (> 0). |
  | `anguloInicio` | `double` | `number` | Yes | - | Start angle in degrees (0 = East). |
  | `anguloFin` | `double` | `number` | Yes | - | End angle in degrees. |
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "dibujar_arco",
    "arguments": {
      "centroX": 0.0, "centroY": 0.0, "radio": 4.5,
      "anguloInicio": 0.0, "anguloFin": 180.0, "capa": "TUN-VAULT"
    }
  }
  ```

---

### 14. `insertar_texto`
- **Purpose:** Insert single-line text (`DBText`) in model space.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `texto` | `string` | `string` | Yes | - | Text string content. |
  | `x`, `y` | `double` | `number` | Yes | - | Insertion coordinates (m). |
  | `altura` | `double` | `number` | No | `0.25` | Text height in meters (> 0). |
  | `rotacion` | `double` | `number` | No | `0.0` | Rotation angle in degrees. |
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "insertar_texto",
    "arguments": { "texto": "STANDARD SECTION STA 1+200", "x": 10.0, "y": 2.0, "altura": 0.35, "capa": "TUN-LABELS" }
  }
  ```

---

### 15. `insertar_bloque`
- **Purpose:** Insert a reference of an existing block definition.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombreBloque` | `string` | `string` | Yes | - | Existing block name. |
  | `x`, `y` | `double` | `number` | Yes | - | Insertion coordinates (m). |
  | `escala` | `double` | `number` | No | `1.0` | Uniform scale factor (> 0). |
  | `rotacion` | `double` | `number` | No | `0.0` | Rotation angle in degrees. |
  | `capa` | `string` | `string` | No | `"0"` | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "insertar_bloque",
    "arguments": { "nombreBloque": "SEGMENT_TYPE_A", "x": 0.0, "y": 4.5, "escala": 1.0, "rotacion": 45.0 }
  }
  ```

---

## Module 3: Advanced Layer Management

C# Class: `AutoCAD.McpServer.Tools.LayerTools`  
Mechanism: **ActiveX AutoLISP (`vla-*`) with Safe Error Handling**

---

### 16. `crear_capa`
- **Purpose:** Create or update a layer with ACI color, linetype, and description without modal dialogs.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Yes | - | Layer name (e.g. `STR-COLUMNS`). |
  | `colorAci` | `int` | `integer` | No | `7` | ACI color code (1–255). |
  | `tipoLinea` | `string` | `string` | No | `"Continuous"` | Linetype name (loads from `acadiso.lin`). |
  | `descripcion`| `string` | `string` | No | `""` | Technical description or note. |
- **Invocation Example:**
  ```json
  {
    "name": "crear_capa",
    "arguments": { "nombre": "STR-BEAMS", "colorAci": 1, "tipoLinea": "Continuous", "descripcion": "Reinforced concrete beams" }
  }
  ```

---

### 17. `establecer_capa_actual`
- **Purpose:** Set the active working layer (`CLAYER`).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Yes | - | Existing, un-frozen layer name. |
- **Invocation Example:**
  ```json
  { "name": "establecer_capa_actual", "arguments": { "nombre": "TUN-LINING" } }
  ```

---

### 18. `establecer_estado_capa`
- **Purpose:** Control layer visibility, freeze, or lock status and regenerate viewports.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Yes | - | Layer name. |
  | `accion` | `string` | `string` | Yes | - | `activar`, `desactivar`, `congelar`, `descongelar`, `bloquear`, `desbloquear`. |
- **Invocation Example:**
  ```json
  { "name": "establecer_estado_capa", "arguments": { "nombre": "TUN-AUXILIARY", "accion": "congelar" } }
  ```

---

## Module 4: Measurement, Computational Geometry & Audit

C# Class: `AutoCAD.McpServer.Tools.MeasurementTools`  
Mechanism: **Local Analytical Computation in .NET/Python** (for measurements and geometry) / **Non-Modal CAD Commands** (for purge and audit).

---

### 19. `medir_distancia`
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
    "name": "medir_distancia",
    "arguments": { "x1": 0.0, "y1": 0.0, "z1": 0.0, "x2": 3.0, "y2": 4.0, "z2": 12.0 }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "distancia_2d": 5.0,
    "distancia_3d": 13.0,
    "delta_x": 3.0, "delta_y": 4.0, "delta_z": 12.0,
    "angulo_grados": 53.1301,
    "pendiente_porcentaje": 240.0,
    "unidades": "metros"
  }
  ```

---

### 20. `calcular_area_poligono`
- **Purpose:** Compute area in $m^2$, hectares, perimeter, centroid, and winding order of a closed polygon from vertices (Gauss algorithm with coordinate translation for large UTM values).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `puntos` | `string` | `string` | Yes | - | Vertices `"x1,y1;x2,y2;..."` (min. 3). |
- **Invocation Example:**
  ```json
  {
    "name": "calcular_area_poligono",
    "arguments": { "puntos": "500000,8000000;500010,8000000;500010,8000005;500000,8000005" }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "area_m2": 50.0,
    "area_hectareas": 0.005,
    "perimetro_m": 30.0,
    "centroide": { "x": 500005.0, "y": 8000002.5 },
    "sentido": "antihorario",
    "vertices": 4
  }
  ```

---

### 21. `limpiar_y_purgar_dibujo`
- **Purpose:** Purge unreferenced definitions in two consecutive passes via `_.-PURGE` and audit/repair the DWG database via `_.AUDIT`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `tipo` | `string` | `string` | No | `"todo"` | `todo`, `bloques`, `capas`, `tipos_linea`, `estilos_texto`, `estilos_cota`. |
  | `auditar` | `bool` | `boolean` | No | `true` | If `true`, executes `AUDIT` fixing detected errors. |
- **Invocation Example:**
  ```json
  { "name": "limpiar_y_purgar_dibujo", "arguments": { "tipo": "todo", "auditar": true } }
  ```

---

### 22. `regenerar_todo`
- **Purpose:** Force complete graphics regeneration across all viewports (`_.REGENALL`).
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "regenerar_todo", "arguments": {} }
  ```

---

## Module 5: Execution, Control & AutoLISP

C# Class: `AutoCAD.McpServer.Tools.ExecutionTools`  
Mechanism: **ConcurrentQueue (`Application.Idle`) with `\n` Terminators and `\x03\x03` Auto-Cancellation**

---

### 23. `ping_autocad_gateway`
- **Purpose:** Check whether the AutoCAD Gateway is active and listening on `http://localhost:5055/`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "ping_autocad_gateway", "arguments": {} }
  ```

---

### 24. `ejecutar_comandos_lote`
- **Purpose:** ⚡ **Batch command runner.** Dispatches a sequential list of AutoCAD commands, scripts, or macros in a single execution payload without waiting for roundtrips.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `comandosJson` / `comandos` | `string` / `array` | `string` / `array` | Yes | Array or JSON string of AutoCAD commands. |
- **Invocation Example:**
  ```json
  {
    "name": "ejecutar_comandos_lote",
    "arguments": {
      "comandos": [
        "_.-LAYER\nM\nARQ-MUROS\nC\n1\n\n\n",
        "_.ZOOM\nE\n"
      ]
    }
  }
  ```

---

### 25. `ejecutar_comando_autocad`
- **Purpose:** Inject raw native CAD commands or plugin macros (e.g. YQArch) in non-modal text mode (`_.-`).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `command` | `string` | `string` | Yes | Command string with `\n` as Enter. |
- **Invocation Example:**
  ```json
  {
    "name": "ejecutar_comando_autocad",
    "arguments": { "command": "_.CIRCLE\n0,0\n5.0\n_.ZOOM\nE\n" }
  }
  ```

---

### 25. `ejecutar_lisp`
- **Purpose:** Evaluate dynamic AutoLISP code directly in AutoCAD memory.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `codigoLisp` | `string` | `string` | Yes | Complete AutoLISP expression. |
- **Invocation Example:**
  ```json
  {
    "name": "ejecutar_lisp",
    "arguments": { "codigoLisp": "(setvar \"OSMODE\" 0)" }
  }
  ```

---

### 26. `cargar_archivo_lisp`
- **Purpose:** Load and execute an external `.lsp` file from disk via `(load ...)`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `rutaArchivoLsp`| `string`| `string` | Yes | Absolute Windows path to `.lsp` file. |
- **Invocation Example:**
  ```json
  {
    "name": "cargar_archivo_lisp",
    "arguments": { "rutaArchivoLsp": "C:/scripts/sample_metro_tunnel.lsp" }
  }
  ```

---

### 27. `configurar_entorno_dibujo`
- **Purpose:** Standardize the metric drafting environment: `INSUNITS = 4` (meters), `MEASUREMENT = 1`, `LUNITS = 2`, `LUPREC = 3`.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "configurar_entorno_dibujo", "arguments": {} }
  ```

---

### 28. `zoom_extension`
- **Purpose:** Trigger a full extents zoom (`_.ZOOM E`) in the active viewport.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "zoom_extension", "arguments": {} }
  ```

---

### 29. `guardar_dibujo`
- **Purpose:** Save the active DWG file to disk (`_.QSAVE`).
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "guardar_dibujo", "arguments": {} }
  ```

---

## Module 6: Sheet Layouts, Title Blocks & PDF Plotting

C# Class: `AutoCAD.McpServer.Tools.PlottingTools`  
Mechanism: **Paper Space (`PSPACE`), Scaled Viewports (`XP`), and Virtual Plotter `DWG To PDF.pc3`**

---

### 30. `crear_layout`
- **Purpose:** Create a new paper space layout tab and set it active.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `nombreLayout` | `string` | `string` | Yes | Unique layout tab name (e.g. `SHEET-TUNNEL-01`). |
- **Invocation Example:**
  ```json
  { "name": "crear_layout", "arguments": { "nombreLayout": "SHEET-01" } }
  ```

---

### 31. `configurar_hoja_layout`
- **Purpose:** Configure sheet paper size (A0 to A4), virtual plotter `DWG To PDF.pc3`, and 1:1 scale in mm with `monochrome.ctb`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombreLayout` | `string` | `string` | Yes | - | Layout name. |
  | `tamanoHoja` | `string` | `string` | No | `"A1"` | `A0`, `A1`, `A2`, `A3`, `A4`. |
  | `estiloTrazado`| `string`| `string` | No | `"monochrome.ctb"` | Plot style table. |
- **Invocation Example:**
  ```json
  {
    "name": "configurar_hoja_layout",
    "arguments": { "nombreLayout": "SHEET-01", "tamanoHoja": "A1" }
  }
  ```

---

### 32. `crear_viewport`
- **Purpose:** Insert a paper space viewport (`MVIEW`) calibrated to exact metric scale (`1000/Scale XP`) centered on model coordinates (X, Y).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `xInferior`, `yInferior` | `double` | `number` | Yes | Bottom-left corner on paper sheet (mm). |
  | `ancho`, `alto` | `double` | `number` | Yes | Viewport width and height on paper (mm). |
  | `escala` | `double` | `number` | Yes | Metric scale denominator (e.g. `1000` for 1:1000, `500` for 1:500). |
  | `xCentroModelo`, `yCentroModelo` | `double` | `number` | Yes | Center coordinates in model space (m). |
- **Invocation Example:**
  ```json
  {
    "name": "crear_viewport",
    "arguments": {
      "xInferior": 25.0, "yInferior": 25.0, "ancho": 500.0, "alto": 350.0,
      "escala": 500.0, "xCentroModelo": 500100.0, "yCentroModelo": 8000050.0
    }
  }
  ```

---

### 33. `insertar_cajetin_parametrico`
- **Purpose:** Draw a formal standardized technical title block (180x45 mm) with metadata fields in paper space.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `xEsquina`, `yEsquina` | `double` | `number` | Yes | - | Bottom-right sheet margin coordinates (mm). |
  | `tituloProyecto` | `string` | `string` | No | `"PROYECTO DE INGENIERÍA"` | Project name. |
  | `nombrePlano` | `string` | `string` | No | `"PLANO GENERAL"` | Drawing sheet title. |
  | `codigoLamina` | `string` | `string` | No | `"L-01"` | Drawing number / sheet code. |
  | `escalaTexto` | `string` | `string` | No | `"1:1000"` | Scale label. |
  | `autor` | `string` | `string` | No | `"INGENIERÍA"` | Designer / engineer. |
  | `fecha` | `string` | `string` | No | Current Date | Issue date. |
- **Invocation Example:**
  ```json
  {
    "name": "insertar_cajetin_parametrico",
    "arguments": {
      "xEsquina": 820.0, "yEsquina": 20.0,
      "tituloProyecto": "METRO LINE 1",
      "nombrePlano": "STANDARD SECTION & DETAILS",
      "codigoLamina": "TUN-01",
      "escalaTexto": "1:500",
      "autor": "CIVIL ENGINEER"
    }
  }
  ```

---

### 34. `exportar_pdf`
- **Purpose:** Plot layout to a physical PDF file on disk using `DWG To PDF.pc3`.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombreLayout` | `string` | `string` | Yes | - | Layout name to plot. |
  | `rutaPdf` | `string` | `string` | Yes | - | Absolute output path for PDF. |
  | `estiloCtb` | `string` | `string` | No | `"monochrome.ctb"` | Plot style table. |
- **Invocation Example:**
  ```json
  {
    "name": "exportar_pdf",
    "arguments": {
      "nombreLayout": "SHEET-01",
      "rutaPdf": "C:/Projects/Metro/Tunnel_Sheet_01.pdf"
    }
  }
  ```

---

## Module 7: Autodesk Civil 3D

C# Class: `AutoCAD.McpServer.Tools.Civil3DTools`  
Mechanism: **Dedicated REST Endpoints (`/civil/*`) and Dynamic Reflection over `AeccDbMgd.dll`**

---

### 35. `verificar_estado_civil3d`
- **Purpose:** Verify if active session is running Civil 3D and report surface and alignment counts.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "verificar_estado_civil3d", "arguments": {} }
  ```
- **Response:**
  ```json
  { "status": "success", "civil_available": true, "surfaces_count": 2, "alignments_count": 1 }
  ```

---

### 36. `listar_superficies_civil3d`
- **Purpose:** Inventory TIN surfaces with min/max elevations and triangle counts.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "listar_superficies_civil3d", "arguments": {} }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "data": [
      {
        "nombre": "NATURAL_GROUND",
        "cota_minima": 2450.2,
        "cota_maxima": 2680.8,
        "triangulos": 45200
      }
    ]
  }
  ```

---

### 37. `consultar_elevacion_terreno`
- **Purpose:** Return exact ground surface elevation (Z) at coordinates (X, Y).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `x`, `y` | `double` | `number` | Yes | Easting and Northing coordinates (m). |
  | `superficie` | `string?` | `string` | No | TIN surface name (defaults to first surface). |
- **Invocation Example:**
  ```json
  {
    "name": "consultar_elevacion_terreno",
    "arguments": { "x": 500250.0, "y": 8000100.0, "superficie": "NATURAL_GROUND" }
  }
  ```
- **Response:**
  ```json
  { "status": "success", "superficie": "NATURAL_GROUND", "x": 500250.0, "y": 8000100.0, "elevacion": 2514.85 }
  ```

---

### 38. `listar_alineamientos_civil3d`
- **Purpose:** List horizontal alignments with station ranges and lengths.
- **Parameters:** None.
- **Invocation Example:**
  ```json
  { "name": "listar_alineamientos_civil3d", "arguments": {} }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "data": [
      {
        "nombre": "MAIN_CORRIDOR_ALIGNMENT",
        "progresiva_inicial": 0.0,
        "progresiva_final": 2450.0,
        "estaca_inicial": "0+000.00",
        "estaca_final": "2+450.00",
        "longitud": 2450.0
      }
    ]
  }
  ```

---

### 39. `consultar_estacion_alineamiento`
- **Purpose:** Compute station (chainage) and lateral offset (+ right / - left) of a point relative to an alignment.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `x`, `y` | `double` | `number` | Yes | Easting and Northing coordinates (m). |
  | `alineamiento` | `string?` | `string` | No | Alignment name. |
- **Invocation Example:**
  ```json
  {
    "name": "consultar_estacion_alineamiento",
    "arguments": { "x": 500248.0, "y": 8000105.0, "alineamiento": "MAIN_CORRIDOR_ALIGNMENT" }
  }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "alineamiento": "MAIN_CORRIDOR_ALIGNMENT",
    "progresiva": 250.0,
    "estaca": "0+250.00",
    "desfase": -3.5
  }
  ```

---

### 40. `obtener_coordenada_en_estacion`
- **Purpose:** Inverse operation: compute absolute coordinates (X, Y) given station and lateral offset.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `progresiva` | `double` | `number` | Yes | - | Station value (e.g. `1250.5`). |
  | `desfase` | `double` | `number` | No | `0.0` | Lateral offset in meters (+ right / - left). |
  | `alineamiento` | `string?`| `string` | No | null | Alignment name. |
- **Invocation Example:**
  ```json
  {
    "name": "obtener_coordenada_en_estacion",
    "arguments": { "progresiva": 500.0, "desfase": 2.5, "alineamiento": "MAIN_CORRIDOR_ALIGNMENT" }
  }
  ```
- **Response:**
  ```json
  {
    "status": "success",
    "alineamiento": "MAIN_CORRIDOR_ALIGNMENT",
    "progresiva": 500.0,
    "desfase": 2.5,
    "x": 500498.25,
    "y": 8000102.10
  }
  ```

---

### 41. `consultar_perfil_longitudinal`
- **Purpose:** Sample ground elevation profile along an alignment every `intervalo` meters. Returns minimum/maximum elevation, average slope %, and station/elevation samples.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `intervalo` | `double` | `number` | No | `20.0` | Sampling distance in meters (> 0). |
  | `alineamiento` | `string?`| `string` | No | null | Alignment name. |
  | `superficie` | `string?` | `string` | No | null | Surface name. |
- **Invocation Example:**
  ```json
  {
    "name": "consultar_perfil_longitudinal",
    "arguments": { "intervalo": 50.0, "alineamiento": "MAIN_CORRIDOR_ALIGNMENT" }
  }
  ```
- **Success Response:**
  ```json
  {
    "status": "success",
    "alineamiento": "MAIN_CORRIDOR_ALIGNMENT",
    "superficie": "NATURAL_GROUND",
    "intervalo": 50.0,
    "progresiva_inicial": 0.0,
    "progresiva_final": 250.0,
    "muestras_total": 6,
    "cota_minima": 2450.0,
    "cota_maxima": 2465.5,
    "pendiente_media_porcentaje": 6.2,
    "muestras": [
      { "progresiva": 0.0, "x": 500000.0, "y": 8000000.0, "cota": 2450.0 },
      { "progresiva": 50.0, "x": 500050.0, "y": 8000000.0, "cota": 2453.2 },
      { "progresiva": 100.0, "x": 500100.0, "y": 8000000.0, "cota": 2456.8 },
      { "progresiva": 150.0, "x": 500150.0, "y": 8000000.0, "cota": 2460.1 },
      { "progresiva": 200.0, "x": 500200.0, "y": 8000000.0, "cota": 2463.0 },
      { "progresiva": 250.0, "x": 500250.0, "y": 8000000.0, "cota": 2465.5 }
    ]
  }
  ```

---

### 42. `crear_alineamiento_civil3d`
- **Purpose:** Parametrically create a horizontal alignment from PIs with tangent circular curves of fixed radius.
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Default | Description |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Yes | - | Unique alignment name. |
  | `puntos` | `string` | `string` | Yes | - | PIs format `"x1,y1;x2,y2;..."` (min. 2). |
  | `radio` | `double` | `number` | No | `0.0` | Curve radius in meters (0 = no curves). |
  | `capa` | `string?` | `string` | No | null | Target layer. |
- **Invocation Example:**
  ```json
  {
    "name": "crear_alineamiento_civil3d",
    "arguments": {
      "nombre": "NORTH_BYPASS",
      "puntos": "500000,8000000;500500,8000200;501000,8000100",
      "radio": 150.0
    }
  }
  ```

---

### 43. `crear_superficie_civil3d`
- **Purpose:** Build a new topographic TIN surface from a point cloud (X, Y, Z).
- **Parameters:**
  | Parameter | C# Type | JSON Type | Required | Description |
  |---|---|---|---|---|
  | `nombre` | `string` | `string` | Yes | Unique surface name. |
  | `puntos` | `string` | `string` | Yes | Points format `"x1,y1,z1;x2,y2,z2;..."` (min. 3). |
- **Invocation Example:**
  ```json
  {
    "name": "crear_superficie_civil3d",
    "arguments": {
      "nombre": "QUARRY_SURFACE",
      "puntos": "500000,8000000,2450;500100,8000000,2455;500050,8000100,2462"
    }
  }
  ```
