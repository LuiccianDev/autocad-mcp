# AutoCAD MCP — AI Gateway & MCP Server Integration

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Python 3.10+](https://img.shields.io/badge/Python-3.10+-3776AB?style=flat&logo=python&logoColor=white)](https://www.python.org/)
[![PyPI version](https://img.shields.io/pypi/v/autocad-mcp.svg?logo=pypi&logoColor=FFE873)](https://pypi.org/project/autocad-mcp/)
[![uv](https://img.shields.io/badge/managed%20by-uv-261230?style=flat&logo=uv&logoColor=white)](https://astral.sh/uv)
[![AutoCAD 2027](https://img.shields.io/badge/AutoCAD-2027-E51937?style=flat&logo=autodesk&logoColor=white)](https://www.autodesk.com/autocad)
[![MCP Protocol](https://img.shields.io/badge/MCP-v2.2-0052CC?style=flat)](https://modelcontextprotocol.io/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![CI Build](https://img.shields.io/badge/CI-GitHub_Actions-2ea44f?style=flat&logo=githubactions&logoColor=white)](.github/workflows/ci.yml)
[![Changelog](https://img.shields.io/badge/Changelog-Keep_a_Changelog-orange?style=flat)](CHANGELOG.md)

High-performance, bidirectional bridge connecting **Artificial Intelligence Models** (Claude, Antigravity, Cursor, ChatGPT, etc.) with **Autodesk AutoCAD 2027 & Civil 3D**. Features dual MCP client architectures (**Python with instant `uvx` support** or **native C# on .NET 10**) adhering to the **Model Context Protocol (MCP v2.2)** specification with 43 strongly-typed tools.

---

## 📚 Technical Documentation & Guides

To foster adoption, technical transparency, and open-source collaboration, explore our dedicated guides:

| Document | Description |
|---|---|
| 🚀 **[Getting Started Guide](docs/GETTING_STARTED.md)** | Step-by-step setup, compilation, and AI client configuration in under 5 minutes. |
| 🐍 **[Python MCP Server (`AutoCAD.McpPython/`)](AutoCAD.McpPython/README.md)** | Instant runtime with `uvx autocad-mcp` without manual compilation. |
| 📦 **[PyPI Publishing Guide](docs/PYPI_PUBLISHING_GUIDE.md)** | Official guide for publishing `autocad-mcp` via Trusted Publishing (OIDC). |
| 🏗️ **[System Architecture](docs/ARCHITECTURE.md)** | Concurrency model, UI thread dispatcher, secure queue, and C4/Mermaid diagrams. |
| 🛠️ **[MCP Tools Reference Manual](docs/TOOLS_REFERENCE.md)** | Comprehensive technical spec for all 43 tools (parameters, schemas, JSON-RPC, and returns). |
| ⚡ **[Tools Quick Reference & Cheat Sheet](docs/TOOLS_CHEAT_SHEET.md)** | Decision matrix ("Goal -> Tool to call") and chained prompting recipes for LLMs. |
| 🔧 **[Troubleshooting Guide](docs/TROUBLESHOOTING.md)** | Resolving issues with NETLOAD, ports, auth tokens, modal dialog freezes, and Civil 3D. |
| 🛣️ **[Project Roadmap](ROADMAP.md)** | Strategic vision and release milestones (Civil 3D surfaces, streaming, YQArch). |
| 📁 **[Practical Examples (`examples/`)](examples/)** | JSON configs, PowerShell/Bash test scripts, and AutoLISP routines. |

---

## 🏗️ System Architecture

The ecosystem uses a decoupled, two-layer architecture ensuring thread safety, high concurrency, and fault tolerance:

```
┌─────────────────────────────────────────────────────────────┐
│             AI Client (Antigravity / Claude / Cursor)       │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  STDIO (JSON-RPC Protocol)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│              MCP Servers (45 Available Tools)               │
│  Option A: Python FastMCP (`uvx autocad-mcp`)               │
│  Option B: AutoCAD.McpServer (.NET 10 Native)               │
│  - DI Container & Tool Registry                             │
│  - Typed HttpClient with Timeout & Token Authentication     │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  HTTP REST (http://localhost:5055/)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                 AutoCAD.Gateway (.NET 10)                   │
│  (C# Extension loaded inside acad.exe via NETLOAD)          │
│  - HttpGatewayServer (Background Listener Thread)           │
│  - RequestRouter (/execute, /context/*, /civil3d/*)         │
│  - AutoCADCommandDispatcher (ConcurrentQueue + Idle Event)  │
│  - AutoCADContextReader (Read-only Database Transactions)   │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  Main UI Thread Injection
                               ▼
┌─────────────────────────────────────────────────────────────┐
│          Active AutoCAD 2027 / Civil 3D Session             │
└─────────────────────────────────────────────────────────────┘
```

---

## 💡 Why Must Gateway and MCP Be Used Together?

For an AI model (or autonomous assistant) to operate safely with AutoCAD, **both components must be deployed and configured together**:

| Component | Ecosystem Role | If used in isolation... |
|---|---|---|
| **AutoCAD.Gateway** (.NET DLL in `acad.exe`) | **The Executor & Sensor:** Directly accesses the AutoCAD DWG database, queries entities/layers, and injects commands safely into the UI thread via `Application.Idle`. | Acts as a local REST API. While you can send raw commands via PowerShell or curl, the AI agent would have to write terminal scripts blindly and parse raw JSON responses, wasting context window and risking syntax errors. |
| **MCP Servers** (`uvx autocad-mcp` or `.NET 10`) | **The AI Translator:** Implements the Model Context Protocol (MCP v2.2), exposing 45 typed tools (`dibujar_geometria_lote`, `dibujar_linea`, `crear_capa`, `medir_distancia`, `consultar_perfil_longitudinal`, etc.) with strict JSON schemas and validation. | **Cannot function alone.** Requires an active Gateway connection to interact with a live AutoCAD session. |

> [!IMPORTANT]
> **Integration Policy:** The complete AI copilot experience requires building the solution, loading `AutoCAD.Gateway.dll` into AutoCAD via `NETLOAD`, and registering the MCP Server in your AI client configuration (`mcp_config.json` or `claude_desktop_config.json`). This enables the AI to inspect context and draw cleanly, parametrically, and bidirectionally without manual PowerShell scripts.

---

## 🚀 Key Features

1. **Thread-Safe UI Dispatcher:** Never blocks or corrupts the active AutoCAD document; all drawing operations synchronize with the `Application.Idle` event.
2. **Command Lockout Immunity:** Automatically prepends double cancellation sequences `\x03\x03` (`ESC`/`Ctrl+C`) before commands to clear hung prompts.
3. **45 Specialized MCP Tools:**
   - **Inspection & Vision:** Query layers, blocks, entities, text strings, drawing extents, and capture PNG screenshots for multimodal AI analysis.
   - **Parametric Geometry:** Batch drawing (`dibujar_geometria_lote`), lines, polylines, rectangles, circles, arcs, texts, and block insertions via atomic `entmake`.
   - **Layer Management:** Create and update layers with ACI colors and linetypes; toggle visibility, freeze/thaw, and lock/unlock states without modal dialogs.
   - **Measurement & Audit:** Compute 2D/3D distances, $\Delta X$, $\Delta Y$, $\Delta Z$, azimuth, slope %, polygon area and perimeter (Gauss algorithm), audit DWG database, and purge unreferenced definitions.
   - **Execution & AutoLISP:** Batch command dispatch (`ejecutar_comandos_lote`), native CAD commands, dynamic in-memory AutoLISP evaluation, and external `.lsp` loader.
   - **Layouts & PDF Export:** Create sheet layouts, configure sheet sizes (A0–A4), calibrate viewports to metric scale (`1000/Scale XP`), insert parametric title blocks, and plot to physical PDF files via `DWG To PDF.pc3`.
   - **Autodesk Civil 3D Native:** Query TIN surfaces, extract ground elevations (Z), list horizontal alignments, compute station and offset, sample longitudinal profiles, and create alignments or surfaces from point clouds.
4. **Zero-Secret Token Security:** Protected by the `X-Gateway-Token` HTTP header, configurable through environment variables.
5. **Strict `stdout` Silence:** Ensures complete JSON-RPC framing integrity for STDIO-based MCP clients.

---

## 📋 Prerequisites

- **Operating System:** Windows 10/11 (x64).
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` >= 10.0.100).
- **CAD Software:** Autodesk AutoCAD 2027 or Autodesk Civil 3D 2027 installed at:
  `C:\Program Files\Autodesk\AutoCAD 2027\`.
- **Python (Optional for Python MCP Server):** Python 3.10+ or [`uv`](https://astral.sh/uv).

---

## ⚙️ Build & Installation

### 1. Clone the Repository
```powershell
git clone https://github.com/LuiccianDev/autocad-mcp.git
cd autocad-mcp
```

### 2. Build Solution in Release Mode
You can use the included PowerShell script or the standard .NET CLI:
```powershell
.\scripts\build.ps1
# Or manually:
dotnet build AutoCAD_Integration.slnx -c Release
```

Build outputs:
- `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll` (Native AutoCAD Extension).
- `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.dll` (Portable .NET MCP Server).
- `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.exe` (Windows Executable).

---

## 🔌 Getting Started

### Step 1: Load the Gateway into AutoCAD 2027
1. Launch AutoCAD 2027 or Civil 3D 2027.
2. In the AutoCAD command line, type:
   ```text
   NETLOAD
   ```
3. Browse and select the built assembly:
   `...\autocad-mcp\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`
4. The AutoCAD command window will confirm:
   ```text
   >>> AutoCAD AI Gateway Plugin loaded successfully.
   >>> Available commands: AI_START_SERVER, AI_STOP_SERVER, AI_SHOW_CONFIG, AI_SET_TOKEN, AI_GENERATE_TOKEN
   ```

### Step 2: Connect the MCP Server to Your AI Client

You can use either MCP server implementation with identical 43-tool parity:

#### Option A: Instant Python Deployment with `uvx` (Recommended)
If using [`uv`](https://astral.sh/uv), no manual compilation is required:

**Claude Desktop (`claude_desktop_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uvx",
      "args": ["autocad-mcp"],
      "env": {
        "AUTOCAD_GATEWAY_PORT": "5055",
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

**Antigravity / Cursor / VS Code (`mcp_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uv",
      "args": [
        "--directory",
        "${workspaceFolder}/AutoCAD.McpPython",
        "run",
        "autocad-mcp"
      ],
      "env": {
        "AUTOCAD_GATEWAY_PORT": "5055",
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

#### Option B: Native C# Server (.NET 10)
For environments where native compiled binary performance is preferred:

**Claude Desktop (`claude_desktop_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "C:\\Path\\To\\autocad-mcp\\AutoCAD.McpServer\\bin\\Release\\net10.0\\AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

**Antigravity / Cursor / VS Code (`mcp_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "${workspaceFolder}/AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

---

## 🛠️ MCP Tools Catalog (45 Tools)

### 1. Inspection, Context & Vision Tools (8)
| Tool | Description |
|---|---|
| `obtener_info_dibujo` | Retrieves drawing name, DWG path, read-only status, and configured drawing units. |
| `leer_capas_autocad` | Returns complete layer catalog with ACI color, visibility, freeze, and lock status. |
| `leer_objetos_autocad` | Summarizes existing entities grouped by object type and count. |
| `obtener_limites_dibujo` | Queries drawing bounding box coordinates (`ExtMin`, `ExtMax`) and metric delta. |
| `leer_textos_autocad` | Searches and lists text content from `DBText` and `MText` entities with (X, Y) coordinates. |
| `listar_bloques_disponibles` | Lists block definition names available in the drawing. |
| `leer_seleccion_autocad` | Returns count and summary of currently selected objects in AutoCAD. |
| `capturar_pantalla_autocad` | **Eyes for AI:** Captures a PNG screenshot of the active AutoCAD viewport for visual reasoning. |

### 2. Parametric Geometry & Drawing Tools (8)
| Tool | Description |
|---|---|
| `dibujar_geometria_lote` | ⚡ **Preferred batch tool:** Draws entire floor plans and multiple entities (lines, rects, circles, polylines, texts) in one atomic call, slashing tokens and latency by 95%. |
| `dibujar_linea` | Draws a 3D straight line segment between two metric points (preserves active layer, independent of OSNAP). |
| `dibujar_polilinea` | Draws a 2D polyline (`LWPOLYLINE`), open or closed, from an array of metric vertices. |
| `dibujar_rectangulo` | Draws a closed polyline rectangle defined by two diagonal corner points. |
| `dibujar_circulo` | Draws a parametric circle given center point (X, Y) and radius in meters. |
| `dibujar_arco` | Draws a counterclockwise circular arc given center point, radius, and start/end angles in degrees. |
| `insertar_texto` | Inserts single-line text (`DBText`) in model space with metric height and rotation. |
| `insertar_bloque` | Inserts an existing block reference with uniform scale and rotation angle. |

### 3. Advanced Layer Management (3)
| Tool | Description |
|---|---|
| `crear_capa` | Creates or updates a layer with ACI color (1–255), linetype, and description (no modal dialogs). |
| `establecer_capa_actual` | Sets the active working layer (`CLAYER`). |
| `establecer_estado_capa` | Controls layer visibility (`activar`/`desactivar`), freeze (`congelar`/`descongelar`), or lock (`bloquear`/`desbloquear`). |

### 4. Measurement, Computational Geometry & Audit (4)
| Tool | Description |
|---|---|
| `medir_distancia` | Computes 2D/3D distances, coordinate deltas $\Delta X, \Delta Y, \Delta Z$, azimuth, and slope % (local analytical calculation). |
| `calcular_area_poligono` | Computes area in $m^2$, hectares, perimeter, centroid, and winding order for closed polygons (Gauss algorithm). |
| `limpiar_y_purgar_dibujo` | Purges unreferenced definitions in two passes (`_.-PURGE`) and audits/repairs DWG database (`_.AUDIT`). |
| `regenerar_todo` | Forces complete regeneration across all viewports (`_.REGENALL`). |

### 5. Execution, Control & AutoLISP (8)
| Tool | Description |
|---|---|
| `ping_autocad_gateway` | Performs an HTTP connectivity check against port 5055. |
| `ejecutar_comandos_lote` | ⚡ **Batch command runner:** Dispatches a sequential list of AutoCAD commands/macros in a single request. |
| `ejecutar_comando_autocad` | Injects non-modal text commands (`_.-`) with guaranteed `\n` terminators. |
| `ejecutar_lisp` | Evaluates dynamic in-memory AutoLISP expressions (e.g. `entmake`, variables, custom functions). |
| `cargar_archivo_lisp` | Loads and executes an external `.lsp` file from disk via `(load ...)`. |
| `configurar_entorno_dibujo` | Configures mandatory metric standard: `INSUNITS = 4` (meters), `MEASUREMENT = 1`. |
| `zoom_extension` | Triggers a full extents zoom (`_.ZOOM E`) in the active viewport. |
| `guardar_dibujo` | Saves the active DWG drawing file via `_.QSAVE`. |

### 6. Sheet Layouts, Title Blocks & PDF Plotting (5)
| Tool | Description |
|---|---|
| `crear_layout` | Creates a new paper space layout tab and sets it active. |
| `configurar_hoja_layout` | Configures layout paper size (A0 to A4), virtual plotter `DWG To PDF.pc3`, and plot style `monochrome.ctb`. |
| `crear_viewport` | Creates a paper space viewport calibrated to exact metric scale (`1000/Scale XP`) centered on (X, Y). |
| `insertar_cajetin_parametrico` | Draws a technical title block in paper space with parametric fields (Title, Drawing Number, Sheet, Scale, Designer, Date). |
| `exportar_pdf` | Plots and generates a physical PDF file from a layout using `DWG To PDF.pc3`. |

### 7. Autodesk Civil 3D Native Tools (9)
| Tool | Description |
|---|---|
| `verificar_estado_civil3d` | Verifies whether Civil 3D runtime is active and managed API (`AeccDbMgd`) is accessible. |
| `listar_superficies_civil3d` | Returns existing TIN surfaces with min/max elevation and triangle count. |
| `consultar_elevacion_terreno` | Returns exact ground surface elevation (Z) at any metric (X, Y) coordinate. |
| `listar_alineamientos_civil3d` | Returns horizontal alignments (`Alignment`), length, and station ranges. |
| `consultar_estacion_alineamiento` | Computes station (chainage) and lateral offset (+ right / - left) for a point (X, Y). |
| `obtener_coordenada_en_estacion` | Computes metric (X, Y) coordinates given a station and lateral offset. |
| `consultar_perfil_longitudinal` | Samples stations, coordinates, and ground elevations at regular intervals along an alignment. |
| `crear_alineamiento_civil3d` | Parametrically creates a horizontal alignment from PIs with fixed-radius tangent curves. |
| `crear_superficie_civil3d` | Parametrically builds a topographic TIN surface from a point cloud array (X, Y, Z). |

---

## 🔒 Safe Command Guidelines

1. **Avoid Modal Dialogs:** Always prepend interactive commands with `_.-` (e.g. `_.-LAYER` instead of `LAYER`, `_.-INSERT` instead of `INSERT`). If AutoCAD opens a Windows modal dialog, the Gateway connection will block until manually closed.
2. **Internationalization:** Use the `_.` command prefix to ensure compatibility across localized AutoCAD versions (Spanish, English, etc.), e.g. `_.LINE`, `_.RECTANG`.
3. **Units:** Always design and input coordinates in **meters**.

---

## 🤝 Community, Governance & Support

This project is built to serve as an open standard maintained by and for the CAD developer, civil engineering, and AI community.

- 📖 **[Code of Conduct](CODE_OF_CONDUCT.md):** Fostering an inclusive, welcoming community (Contributor Covenant v2.1).
- 💡 **[Contributing Guide](CONTRIBUTING.md):** Development guidelines, Git workflow, and Conventional Commits.
- 🛡️ **[Security Policy](SECURITY.md):** Responsible vulnerability disclosure and Gateway best practices.
- 💬 **[Support Channels](SUPPORT.md):** Where to ask questions, suggest features, and report bugs.
- 📝 **[Changelog](CHANGELOG.md):** Chronological history of releases and improvements.

---

## 👨‍💻 Author & Maintainer

Conceived, architected, and developed by:

- **Luiccian** ([@LuiccianDev](https://github.com/LuiccianDev))
  - Creator and Lead Architect of **AutoCAD MCP Server & Gateway** (`autocad-mcp`) and **Vulcan MCP Server** (`vulcan-mcp`).
  - Specialist in Artificial Intelligence integrations for engineering, mining, surveying, and AEC automation.

---

## 📄 License

This project is licensed under the **Apache License, Version 2.0** (Copyright 2026 LuiccianDev). See the [LICENSE](LICENSE) file for full details.
