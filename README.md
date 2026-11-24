<div align="center">
<h1>AutoCAD MCP</h1>
<p><em>Bidirectional AI Gateway and Model Context Protocol (MCP) server for Autodesk AutoCAD 2027 and Civil 3D.</em></p>
</div>

<p align="center">
  <a href="https://pypi.org/project/luiccian-autocad-mcp/"><img src="https://img.shields.io/pypi/v/luiccian-autocad-mcp.svg?logo=pypi&logoColor=FFE873" alt="PyPI version" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet&logoColor=white" alt=".NET 10" /></a>
  <a href="https://www.python.org/"><img src="https://img.shields.io/badge/Python-3.10+-3776AB?style=flat&logo=python&logoColor=white" alt="Python 3.10+" /></a>
  <a href="https://www.autodesk.com/autocad"><img src="https://img.shields.io/badge/AutoCAD-2027-E51937?style=flat&logo=autodesk&logoColor=white" alt="AutoCAD 2027" /></a>
  <a href=".github/workflows/ci.yml"><img src="https://img.shields.io/badge/CI-GitHub_Actions-2ea44f?style=flat&logo=githubactions&logoColor=white" alt="CI Build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache_2.0-blue.svg" alt="License" /></a>
</p>

Connects AI coding agents (Antigravity, Claude Desktop, Cursor, VS Code) to a live **AutoCAD 2027** or **Civil 3D 2027** session with **89 tools across 10 domains** (75 canonical actions and 14 legacy aliases), selectable via `AUTOCAD_MCP_TOOL_MODE` (`granular`, `compound`, or `core`).

---

## Quick Start

### 1. Load and Start the Gateway in AutoCAD 2027
1. Build from source (`.\scripts\build.ps1`) or download [`AutoCAD.Gateway-Release.zip`](https://github.com/LuiccianDev/autocad-mcp/releases/latest).
2. In AutoCAD 2027 or Civil 3D 2027, run `NETLOAD` and select `AutoCAD.Gateway.dll`, then start the loopback listener:
   ```text
   AI_START_SERVER
   ```
   Available Gateway commands: `AI_START_SERVER`, `AI_STOP_SERVER`, `AI_SHOW_CONFIG`, `AI_SET_TOKEN`, `AI_GENERATE_TOKEN`, `AI_EDIT_GUARD`.
3. Optionally install as an Autodesk Autoloader `.bundle` (with SHA-256 verification) or uninstall cleanly:
   ```powershell
   .\scripts\install-bundle.ps1
   .\scripts\install-bundle.ps1 -Uninstall
   ```

### 2. Connect the MCP Server (`uvx`, Recommended)
Requires [`uv`](https://astral.sh/uv). Runs [`luiccian-autocad-mcp`](https://pypi.org/project/luiccian-autocad-mcp/) on demand with zero local compilation and automatic `{pid}.json` session discovery:

```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uvx",
      "args": ["luiccian-autocad-mcp"],
      "env": {
        "AUTOCAD_MCP_TOOL_MODE": "compound"
      }
    }
  }
}
```

> [!NOTE]
> Leave `AUTOCAD_GATEWAY_TOKEN` and `AUTOCAD_GATEWAY_PORT` unset for zero-config auto-discovery. Only set `AUTOCAD_GATEWAY_TOKEN` if you pinned a custom token via `AI_SET_TOKEN` inside AutoCAD.

---

## Architecture & Tool Modes

```mermaid
flowchart LR
    AI["AI Client (STDIO JSON-RPC)"] --> MCP["MCP Server (Python FastMCP or .NET 10)"]
    MCP -->|"HTTP Loopback 127.0.0.1 + X-Gateway-Token"| GW["AutoCAD.Gateway (.NET 10 TcpListener)"]
    GW -->|"Application.Idle Main Thread"| ACAD["AutoCAD 2027 / Civil 3D"]
```

### Selective Tool Loading (`AUTOCAD_MCP_TOOL_MODE`)

| Mode | Tools Exposed | Behavior |
| :--- | :--- | :--- |
| `granular` (default) | 89 tools | Exposes all 75 canonical tools and 14 legacy aliases directly. |
| `compound` | 10 domain routers | Exposes `autocad_context` through `autocad_execution` using `action` and `params` (with `action="help"` introspection). |
| `core` | 28 tools | Exposes high-leverage standalone tools with zero aliases for minimal context overhead. |

### Domain Catalog (89 Tools)

| Domain (`compound` Router) | Canonical / Alias Count | Representative Canonical Actions |
| :--- | :--- | :--- |
| `autocad_context` | 10 + 6 aliases | `get_drawing_info`, `get_layers`, `get_drawing_extents`, `capture_screenshot` |
| `autocad_entities` | 6 + 0 aliases | `query_entities`, `get_entity`, `modify_entity`, `transform_entities`, `delete_entities`, `execute_native_batch` |
| `autocad_drafting` | 10 + 0 aliases | `create_dimension`, `create_mleader`, `create_hatch`, `create_table`, `define_block`, `set_block_attributes`, `list_xrefs`, `explode_entities` |
| `autocad_drawing` | 8 + 0 aliases | `draw_batch_geometry`, `draw_line`, `draw_polyline`, `draw_rectangle`, `draw_circle`, `draw_arc`, `insert_text`, `insert_block` |
| `autocad_layers` | 4 + 0 aliases | `create_layer`, `set_current_layer`, `set_layer_state`, `delete_layer` |
| `autocad_documents` | 9 + 0 aliases | `list_documents`, `new_drawing`, `open_drawing`, `activate_document`, `close_document`, `allow_edit_document`, `cancel_command`, `get_system_variable`, `set_system_variable` |
| `autocad_measurement` | 4 + 1 alias | `measure_distance`, `calculate_polygon_area`, `purge_and_audit_drawing`, `regen_all` |
| `autocad_plotting` | 5 + 2 aliases | `create_layout`, `configure_layout_page`, `create_viewport`, `insert_title_block`, `export_pdf` |
| `autocad_civil3d` | 11 + 2 aliases | `check_civil3d_status`, `list_civil3d_surfaces`, `get_surface_elevation`, `get_civil3d_surface_stats`, `list_civil3d_alignments`, `sample_longitudinal_profile`, `sample_civil3d_cross_section`, `create_civil3d_alignment`, `create_civil3d_surface` |
| `autocad_execution` | 8 + 3 aliases | `ping_gateway`, `execute_command`, `execute_batch_commands`, `evaluate_lisp`, `load_lisp_file`, `configure_drawing_units`, `zoom_extents`, `save_drawing` |

---

## Alternative Execution Modes

<details>
<summary>Native C# Server (.NET 10) and Local Python Development (`uv run`)</summary>

### Option B: Native C# Server (`AutoCAD.McpServer.dll`)
Build the solution (`dotnet build AutoCAD_Integration.slnx -c Release`) or download `AutoCAD.McpServer-Release.zip` from [Releases](https://github.com/LuiccianDev/autocad-mcp/releases/latest), then configure:

```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "${workspaceFolder}/AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_MCP_TOOL_MODE": "compound"
      }
    }
  }
}
```

### Option C: Local Python Development (`uv run`)
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
        "AUTOCAD_MCP_TOOL_MODE": "compound"
      }
    }
  }
}
```

Run tests locally:

```powershell
dotnet run --project AutoCAD.Gateway.Tests -c Release
uv run --directory AutoCAD.McpPython --all-extras pytest
```

</details>

---

## Documentation, Skills & Governance

| Resource | Purpose |
| :--- | :--- |
| [Getting Started Guide](docs/GETTING_STARTED.md) | Step-by-step installation, Autoloader bundle, and client setup |
| [System Architecture](docs/ARCHITECTURE.md) | Loopback `TcpListener`, `Application.Idle` dispatcher, and security model |
| [MCP Tools Reference](docs/TOOLS_REFERENCE.md) | Parameter schemas and return payloads |
| [Tools Cheat Sheet](docs/TOOLS_CHEAT_SHEET.md) | Goal-to-tool decision matrix |
| [Troubleshooting Guide](docs/TROUBLESHOOTING.md) | Resolving `NETLOAD`, ports, Edit Guard, and Civil 3D issues |
| [PyPI Publishing Guide](docs/PYPI_PUBLISHING_GUIDE.md) | Trusted Publishing (OIDC) and PEP 740 attestations |
| [Agent Skills (`skills/`)](skills/) | [AutoCAD MCP Automation](skills/autocad-mcp-automation/SKILL.md), [YQArch Fast Drafting](skills/yqarch-fast-drafting/SKILL.md), [AutoLISP Drafting](skills/autolisp-drafting/SKILL.md) |
| [Examples (`examples/`)](examples/) | AutoLISP routines and gateway HTTP diagnostic scripts |

Licensed under the [Apache License 2.0](LICENSE). See [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), and [SECURITY.md](SECURITY.md).
