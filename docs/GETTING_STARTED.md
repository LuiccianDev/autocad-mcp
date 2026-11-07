# Getting Started Guide — AutoCAD MCP (`autocad-mcp`)

This step-by-step guide walks you through deploying the native Gateway plugin inside AutoCAD 2027 and connecting your Artificial Intelligence client (Antigravity, Claude Desktop, Cursor, or VS Code) in under 2 minutes.

---

## Prerequisites

1. **Operating System:** Windows 10 or Windows 11 (64-bit).
2. **Autodesk AutoCAD 2027 or Autodesk Civil 3D 2027:**
   - Installed at the default location: `C:\Program Files\Autodesk\AutoCAD 2027\`.
3. **MCP Runner (Recommended):**
   - [`uv`](https://astral.sh/uv) installed (enables instant zero-compile execution via `uvx`).
4. **Developer SDK (Only if compiling from source):**
   - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` >= 10.0.100).

---

## Step 1: Load the Gateway Plugin into AutoCAD 2027

To enable AI access to your active AutoCAD drawing, the native Gateway extension must run inside `acad.exe`:

### Option A: Use Pre-Built Binary (Fastest)
1. Download the latest release asset [`AutoCAD.Gateway-Release.zip`](https://github.com/LuiccianDev/autocad-mcp/releases/latest).
2. Extract the archive to a local folder (e.g., `C:\Tools\AutoCAD.Gateway\`).
3. If downloaded from the internet, unblock the DLL in PowerShell:
   ```powershell
   Unblock-File -Path "C:\Tools\AutoCAD.Gateway\AutoCAD.Gateway.dll"
   ```

### Option B: Build from Source
If building locally with the .NET 10 SDK:
```powershell
git clone https://github.com/LuiccianDev/autocad-mcp.git
cd autocad-mcp
dotnet build AutoCAD_Integration.slnx -c Release
```
The output assembly is located at `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`.

### Load into AutoCAD
1. Launch **AutoCAD 2027** or **Autodesk Civil 3D 2027**.
2. Open any drawing (or create a new metric drawing).
3. In the AutoCAD command prompt, type:
   ```text
   NETLOAD
   ```
4. Select `AutoCAD.Gateway.dll`.
5. The command window will confirm:
   ```text
   >>> AutoCAD AI Gateway Plugin loaded successfully.
   >>> Available commands:
       - AI_START_SERVER   : Starts the HTTP Gateway server.
       - AI_STOP_SERVER    : Stops the HTTP Gateway server.
       - AI_SHOW_CONFIG    : Displays server status and active token.
       - AI_SET_TOKEN      : Updates the active token in memory.
       - AI_GENERATE_TOKEN : Generates a cryptographically secure token.
   ```
   *(The server starts automatically listening on `http://localhost:5055/`).*

---

## Step 2: Connect the MCP Server to Your AI Client

### Primary Option: Zero-Install with `uvx` (Recommended)

Requires only [`uv`](https://astral.sh/uv). Runs the official PyPI distribution [`luiccian-autocad-mcp`](https://pypi.org/project/luiccian-autocad-mcp/) on demand without cloning or building:

#### Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`)
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uvx",
      "args": ["luiccian-autocad-mcp"],
      "env": {
        "AUTOCAD_GATEWAY_PORT": "5055",
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

#### Antigravity / Cursor / VS Code (`mcp_config.json`)
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uvx",
      "args": ["luiccian-autocad-mcp"],
      "env": {
        "AUTOCAD_GATEWAY_PORT": "5055",
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

---

## Alternative Execution Modes

### Option B: Native C# Server (.NET 10)

For environments where native compiled binary performance is preferred:

1. Download `AutoCAD.McpServer-Release.zip` from [Releases](https://github.com/LuiccianDev/autocad-mcp/releases/latest) or build the solution locally.
2. Configure your AI client:

```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "C:\\Path\\To\\AutoCAD.McpServer\\bin\\Release\\net10.0\\AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

### Option C: Local Python Development (`uv run`)

For contributors modifying Python tools directly from source:
```powershell
git clone https://github.com/LuiccianDev/autocad-mcp.git
cd autocad-mcp/AutoCAD.McpPython
uv run autocad-mcp
```

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

---

## Step 3: Verification & Test Run

Once your AI client is connected, ask it in chat:
> *"Inspect my active AutoCAD drawing status and draw a 20x10 meter rectangle on the layer ARQ-WALLS."*

The AI will sequentially execute:
1. `configurar_entorno_dibujo`: Sets metric units (`INSUNITS = 4`).
2. `obtener_info_dibujo`: Verifies active DWG name and read/write state.
3. `crear_capa` & `dibujar_rectangulo`: Creates the layer and draws the geometry atomically without dialog blocks.
4. `zoom_extension`: Frames the drafted elements on screen.

---

## References & Documentation

- [MCP Tools Reference Manual (45 Tools)](TOOLS_REFERENCE.md): Complete catalog with parameter schemas, types, and JSON responses.
- [Tools Quick Reference & Cheat Sheet](TOOLS_CHEAT_SHEET.md): Decision tree for selecting the right tool for each CAD design task.
- [System Architecture](ARCHITECTURE.md): Deep dive into the concurrency model and UI thread dispatcher.
- [Troubleshooting Guide](TROUBLESHOOTING.md): Resolving network, NETLOAD, and command execution issues.
