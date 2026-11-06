# AutoCAD AI Gateway — Technical Reference & Usage Guide

**Version:** 1.0.0  
**Target Framework:** .NET 10 (`net10.0-windows`)  
**Host Platform:** Autodesk AutoCAD 2027 & Civil 3D 2027 (x64)  
**Technology:** C# / AutoCAD Managed .NET API (`acmgd`, `acdbmgd`, `accoremgd`)  
**Listening Port:** `http://localhost:5055/`

---

## 1. Project Purpose & Architecture Context

The **AutoCAD AI Gateway** is an in-process native plugin for Autodesk AutoCAD 2027 and Civil 3D 2027. It provides a bidirectional communication bridge between Artificial Intelligence models (autonomous agents, MCP servers, external scripts) and the active AutoCAD session.

### Why This Solution Was Built
1. **AutoLISP Limitations:** Native AutoLISP (`.lsp`) routines cannot seamlessly drive commands defined by compiled third-party extensions or specialized CAD macros like **YQArch** (triggering *"Unknown command"* when invoked through `(command)`).
2. **Real-Time Agentic Automation:** Allows AI agents to query live AutoCAD state: inspect layers, count objects by type, determine drawing extents, and safely inject parametric drawing geometry into the primary UI loop.

### Binary Nature (.dll vs .exe)
This project is an in-process **managed Class Library** compiling exclusively to **`AutoCAD.Gateway.dll`**. **It does not generate a standalone `.exe` executable**, as it is engineered to execute inside the memory space of `acad.exe` once loaded via `NETLOAD`.

---

## 2. Gateway Modular Architecture

The plugin implements a decoupled service-based architecture with an HTTP listener and defensive synchronization against the AutoCAD UI thread:

```
[ AI Agent / MCP Server ]
              │
              │  HTTP REST (Port 5055)
              ▼
    [ HttpGatewayServer ]  ───> Background Thread (HttpListener + CORS)
              │
              ▼
       [ RequestRouter ]
        ├── POST /execute ──────────> [ AutoCADCommandDispatcher ]
        │                                      │ (ConcurrentQueue)
        │                                      ▼ (Application.Idle)
        │                              [ doc.SendStringToExecute ] (\x03\x03)
        │
        └── GET /context/* ─────────> [ AutoCADContextReader ]
                                               │ (RunOnIdleAsync / TCS)
                                               ▼ (Database Transaction)
                                       [ DWG Reads on UI Thread ]
```

### Core Components:
* **`Main.cs`**: Implements `IExtensionApplication`. Initializes the plugin, registers command listeners, and ensures graceful server teardown on AutoCAD exit.
* **`Commands/GatewayCommands.cs`**: Registers native AutoCAD commands: `AI_START_SERVER`, `AI_STOP_SERVER`, `AI_SHOW_CONFIG`, `AI_SET_TOKEN`, and `AI_GENERATE_TOKEN`.
* **`Core/AutoCADCommandDispatcher.cs`**:
  * Manages the thread-safe instruction queue (`ConcurrentQueue<string>`).
  * Attaches to `Application.Idle` to dispatch commands strictly within the primary UI thread.
  * Detaches immediately (`-= _idleHandler`) to prevent message-loop overhead.
  * Injects defensive `\x03\x03` (`ESC`/`Ctrl+C`) sequences to clear hanging prompts.
* **`Core/Models/`**: Strongly-typed DTOs (`CommandRequest`, `DrawingInfo`, `LayerInfo`, `TextEntityInfo`, `DrawingExtentsInfo`).
* **`Services/AutoCADContextReader.cs`**:
  * Executes read-only transactions against AutoCAD DWG database tables (`BlockTable`, `LayerTable`, etc.).
  * Synchronizes reads with the UI thread via `RunOnIdleAsync(Func<Document, string>)` and `TaskCompletionSource<string>`.
* **`Server/HttpGatewayServer.cs` & `RequestRouter.cs`**:
  * Lightweight HTTP listener listening on `http://localhost:5055/`.
  * Built-in CORS support (`Access-Control-Allow-Origin: *`, headers `X-Gateway-Token`, methods `GET, POST, OPTIONS`).

---

## 3. REST Endpoints Specification & Security

The Gateway exposes 16 REST endpoints at `http://localhost:5055/` (8 base AutoCAD and 8 Civil 3D endpoints).

### Security & Mandatory Authentication
* **Required HTTP Header:** `X-Gateway-Token: luiccian-autocad-token-2027` (configurable via `AUTOCAD_GATEWAY_TOKEN`).
* **Response (401 Unauthorized):** If the header is missing or invalid, the request is immediately rejected:
  ```json
  { "status": "error", "message": "Unauthorized: Invalid or missing X-Gateway-Token." }
  ```

### A. Command Execution
* **`POST /execute`**  
  Injects console commands, AutoLISP expressions, or plugin macros into AutoCAD.
  * **Payload:**
    ```json
    {
      "command": "_.RECTANG\n0,0\n25,15\n_.ZOOM\nE\n"
    }
    ```
  * **Response (200 OK):**
    ```json
    {
      "status": "success",
      "message": "Command enqueued successfully"
    }
    ```

### B. DWG Context Queries
* **`GET /context/info`**: Active drawing metadata (DWG filename, path, read-only status, and `INSUNITS`).
* **`GET /context/entities`**: Summary object count grouped by type (`Line`, `Polyline`, `Circle`, `BlockReference`, etc.).
* **`GET /context/layers`**: List of all drawing layers with visibility, freeze, lock, and color properties.
* **`GET /context/texts`**: All text entities (`DBText` and `MText`) with coordinates (X, Y) and layers.
* **`GET /context/extents`**: Drawing bounding box extents (`minX`, `minY`, `minZ`, `maxX`, `maxY`, `maxZ`).
* **`GET /context/selection`**: Count and types of objects currently selected by the user.
* **`GET /context/blocks`**: Available block definitions (excluding anonymous blocks and paper space).
* **`GET /context/screenshot`**: Active viewport PNG capture for AI vision models.

### C. Autodesk Civil 3D 2027 Endpoints (`/civil/*`)

The same `AutoCAD.Gateway.dll` runs seamlessly in both vanilla **AutoCAD 2027** and **Civil 3D 2027**. `Civil3DServiceFactory` checks runtime memory for `AeccDbMgd.dll`: if absent, it activates `NullCivil3DService` which responds with HTTP **409** (`civil_available: false`) without throwing exceptions.

**Read Endpoints (GET):**
* **`GET /civil/status`**: Civil 3D availability, active civil document, and surface/alignment count.
* **`GET /civil/surfaces`**: TIN surfaces with min/max elevation and triangle counts.
* **`GET /civil/surface/elevation?x=..&y=..&surface=..`**: Surface ground elevation Z at (X, Y).
* **`GET /civil/alignments`**: Horizontal alignments with start/end stations and lengths.
* **`GET /civil/alignment/station-offset?x=..&y=..&alignment=..`**: Station and lateral offset of point relative to alignment.
* **`GET /civil/alignment/point-at-station?station=..&offset=..&alignment=..`**: Metric coordinates (X, Y) of station and offset.

**Write Endpoints (POST, JSON):**
* **`POST /civil/alignment/create`**: Creates horizontal alignment from PIs with tangent circular curves.
* **`POST /civil/surface/create`**: Builds TIN surface from topographic points.

---

## 4. Drawing & Modeling Standards

When sending commands to AutoCAD via the Gateway:

1. **Strict Metric Units:**
   * `INSUNITS = 4` (Meters)
   * `MEASUREMENT = 1` (Metric)
   * `LUNITS = 2` (Decimal), `LUPREC = 3`
2. **Text & Dimension Calibration:**
   * Text height: `DIMTXT = 0.22` m
   * Arrowhead size: `DIMASZ = 0.18` m
   * Dimension line extension: `DIMEXE = 0.11` m, origin offset: `DIMEXO = 0.06` m
3. **Execution Modes:**
   * **Native Mode (Default):** Infrastructure geometry, excavation, and lining via native commands (`_.-PLINE`, `_.-ARC`) on standard layers.
   * **YQArch Mode (Architecture / Camp facilities):** High-speed architectural drafting (`ZXW` grid, `WW` walls, `AD`/`WD` openings) when requested.

---

## 5. Build & Setup

### Step 1: Build Solution (.NET 10)
```powershell
dotnet build AutoCAD.Gateway/AutoCAD.Gateway.csproj -c Release
```
Output assembly:  
`AutoCAD.Gateway/bin/Release/net10.0-windows/AutoCAD.Gateway.dll`

### Step 2: Load into AutoCAD 2027
1. In AutoCAD, open or create a metric drawing.
2. Type `NETLOAD` in the command prompt.
3. Select `AutoCAD.Gateway.dll`.

### Step 3: Verify Gateway Server
1. In the AutoCAD console, type:
   ```text
   AI_SHOW_CONFIG
   ```
2. You will see:
   ```text
   >>> [CONFIG] AI Gateway Server is RUNNING on port 5055. Active token: luiccian-autocad-token-2027
   ```

---

## 6. Connection Testing

### PowerShell:
```powershell
$headers = @{ "X-Gateway-Token" = "luiccian-autocad-token-2027" }
$payload = @{ command = "_.RECTANG`n0,0`n30,20`n_.ZOOM`nE`n" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5055/execute" -Method Post -Headers $headers -ContentType "application/json" -Body $payload
```

### Python:
```python
import requests

headers = {"X-Gateway-Token": "luiccian-autocad-token-2027"}
res = requests.post(
    "http://localhost:5055/execute",
    headers=headers,
    json={"command": "_.LINE\n0,0\n50,0\n50,20\n\n_.ZOOM\nE\n"},
)
print(res.json())
```
