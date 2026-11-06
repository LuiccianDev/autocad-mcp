# autocad-mcp (Python Server)

Model Context Protocol (MCP) server connecting **Autodesk AutoCAD 2027** and **Autodesk Civil 3D 2027** with AI agents (Claude Desktop, Antigravity, Cursor, VS Code) via the **AutoCAD AI Gateway**.

Exposes **43 strongly-typed tools** covering parametric drawing, layer control, geometric calculations, layout/PDF generation, and native Civil 3D design (surfaces, alignments, profiles).

---

## ⚡ Inicio Rápido con `uvx` (Zero-Config)

Para usar este servidor en **Claude Desktop** o **Cursor**, añade el bloque en tu configuración de MCP:

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

### Ejecución Local con `uv`
Durante desarrollo local, puedes ejecutar directamente sin publicar en PyPI:
```powershell
cd AutoCAD.McpPython
uv run autocad-mcp
```

O en tu cliente de IA referenciando la carpeta local:
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uv",
      "args": [
        "--directory",
        "C:\\Ruta\\A\\mcp-autocad\\AutoCAD.McpPython",
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

---

## 📋 Requisitos
- **Python 3.10+** (o el gestor ultra-rápido [uv](https://astral.sh/uv)).
- **AutoCAD 2027** o **Civil 3D 2027** con el plugin `AutoCAD.Gateway` activo en el puerto `5055`.
