<div align="center">
<h1>luiccian-autocad-mcp</h1>
<p><em>Model Context Protocol (MCP) server connecting Autodesk AutoCAD 2027 and Civil 3D with AI agents via AutoCAD AI Gateway.</em></p>
</div>

[![PyPI version](https://img.shields.io/pypi/v/luiccian-autocad-mcp.svg?logo=pypi&logoColor=FFE873)](https://pypi.org/project/luiccian-autocad-mcp/)
[![Python versions](https://img.shields.io/pypi/pyversions/luiccian-autocad-mcp.svg?logo=python&logoColor=white)](https://pypi.org/project/luiccian-autocad-mcp/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/LuiccianDev/autocad-mcp/blob/master/LICENSE)

Exposes **90 tools across 10 domains** (76 canonical actions and 14 legacy aliases) covering parametric batch drafting, layer control, entity inspection, layouts/PDF plotting, AutoLISP execution, and native Civil 3D surfaces and alignments.

## Quick Start (`uvx`)

1. In **AutoCAD 2027** or **Civil 3D 2027**, load `AutoCAD.Gateway.dll` (`NETLOAD` or Autoloader `.bundle`) and run `AI_START_SERVER`.
2. Add the server to your MCP client (`claude_desktop_config.json` or `mcp_config.json`):

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

> **Note:** Leave `AUTOCAD_GATEWAY_TOKEN` and `AUTOCAD_GATEWAY_PORT` unset for zero-config auto-discovery via the Gateway `{pid}.json` session file. Only set `AUTOCAD_GATEWAY_TOKEN` if you pinned a custom token in AutoCAD using `AI_SET_TOKEN`.

## Tool Loading & Output Format (`AUTOCAD_MCP_TOOL_MODE`, `AUTOCAD_MCP_OUTPUT_FORMAT`)

| Mode | Tools Exposed | Behavior |
| :--- | :--- | :--- |
| `granular` (default) | 90 tools | Exposes all 76 canonical tools and 14 legacy aliases directly. |
| `compound` | 10 domain routers | Exposes `autocad_context` through `autocad_execution` with `action="help"` schema introspection. |
| `core` | 28 tools | Exposes high-leverage standalone tools with zero aliases. |

- **`AUTOCAD_MCP_OUTPUT_FORMAT`:** `compact` (default) emits concise tabular/key-value summaries in `content[0].text` paired with full typed JSON in `structuredContent`. Set `json` for legacy raw JSON text. Invalid values fail fast to `stderr` (`exit code 1`).
- **Native Protocol Semantics:** Tool failures set `CallToolResult.isError = true`; `capture_screenshot(inline=true)` emits native `image/png` `ImageContent` without duplicating base64 in text or `structuredContent`; all tools advertise MCP `ToolAnnotations` (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`).

<details>
<summary>Local Development and Testing (`uv`)</summary>

Run directly from a cloned repository without publishing to PyPI:

```powershell
cd AutoCAD.McpPython
uv run autocad-mcp
```

Client configuration pointing to a local checkout:

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

Run the test suite:

```powershell
uv run --directory AutoCAD.McpPython --all-extras pytest
```

</details>

## Requirements and Links

- **Python:** 3.10+ (or [`uv`](https://astral.sh/uv) for `uvx` zero-install execution).
- **CAD Host:** Autodesk AutoCAD 2027 or Civil 3D 2027 with `AutoCAD.Gateway.dll` loaded and started (`AI_START_SERVER`).
- **Repository:** [GitHub (`LuiccianDev/autocad-mcp`)](https://github.com/LuiccianDev/autocad-mcp)
- **Full Documentation:** [Getting Started](https://github.com/LuiccianDev/autocad-mcp/blob/master/docs/GETTING_STARTED.md) | [Tools Reference](https://github.com/LuiccianDev/autocad-mcp/blob/master/docs/TOOLS_REFERENCE.md) | [Changelog](https://github.com/LuiccianDev/autocad-mcp/blob/master/CHANGELOG.md)
