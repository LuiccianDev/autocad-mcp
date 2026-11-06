# autocad-mcp (Python MCP Server)

Model Context Protocol (MCP) server connecting **Autodesk AutoCAD 2027** and **Autodesk Civil 3D 2027** with AI agents (Claude Desktop, Antigravity, Cursor, VS Code) via the **AutoCAD AI Gateway**.

Exposes **43 strongly-typed tools** covering parametric drawing, layer control, geometric calculations, layout/PDF generation, and native Civil 3D design (surfaces, alignments, profiles).

---

## ⚡ Quickstart with `uvx` (Zero-Config)

To run this server in **Claude Desktop** or **Cursor**, add this block to your MCP settings:

### Claude Desktop (`claude_desktop_config.json`)
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

### Local Development with `uv`
During local development, you can execute directly without publishing to PyPI:
```powershell
cd AutoCAD.McpPython
uv run autocad-mcp
```

Or configure your AI client to point directly to the local directory:
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

### Run Tests
```powershell
uv run --directory AutoCAD.McpPython --all-extras pytest
```

---

## 📋 Requirements
- **Python 3.10+** (or the modern package manager [uv](https://astral.sh/uv)).
- **AutoCAD 2027** or **Civil 3D 2027** with `AutoCAD.Gateway.dll` loaded and listening on port `5055`.
