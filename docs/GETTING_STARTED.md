# Getting Started Guide — AutoCAD MCP (`autocad-mcp`)

This step-by-step guide walks you through building the solution, deploying the native Gateway plugin inside AutoCAD 2027, and connecting your Artificial Intelligence client (Antigravity, Claude Desktop, Cursor, or VS Code) in under 5 minutes.

---

## 📋 Prerequisites

1. **Operating System:** Windows 10 or Windows 11 (64-bit).
2. **.NET 10 SDK:**
   - Verify your version by opening a PowerShell console:
     ```powershell
     dotnet --version
     ```
   - Must output `10.0.100` or higher. If not installed, download from [.NET 10 Download](https://dotnet.microsoft.com/download/dotnet/10.0).
3. **Autodesk AutoCAD 2027 or Autodesk Civil 3D 2027:**
   - Installed at the default location: `C:\Program Files\Autodesk\AutoCAD 2027\`.
4. **Python (Optional for Python MCP Server):**
   - Python 3.10+ or [`uv`](https://astral.sh/uv) installed.

---

## 🛠️ Step 1: Clone and Build the Solution

1. Open PowerShell in your working workspace:
   ```powershell
   git clone https://github.com/LuiccianDev/autocad-mcp.git
   cd autocad-mcp
   ```

2. Run the automated build script:
   ```powershell
   .\scripts\build.ps1
   ```
   *Or build manually via .NET CLI:*
   ```powershell
   dotnet build AutoCAD_Integration.slnx -c Release
   ```

3. Confirm the output reports `0 Errors` and produced the following artifacts:
   - `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll` (Native AutoCAD Plugin).
   - `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.dll` (Portable .NET MCP Server).
   - `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.exe` (Windows Executable).

---

## 🔌 Step 2: Load the Gateway Plugin in AutoCAD

1. Launch **AutoCAD 2027** or **Autodesk Civil 3D 2027**.
2. Open a new or existing drawing (using metric units).
3. In the AutoCAD command prompt, type:
   ```text
   NETLOAD
   ```
4. In the file dialog, navigate to and select:
   `...\autocad-mcp\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`
5. The command line will display the welcome confirmation:
   ```text
   >>> AutoCAD AI Gateway Plugin loaded successfully.
   >>> Available commands:
       - AI_START_SERVER   : Starts the HTTP Gateway server.
       - AI_STOP_SERVER    : Stops the HTTP Gateway server.
       - AI_SHOW_CONFIG    : Displays server status and active token.
       - AI_SET_TOKEN      : Updates the active token in memory.
       - AI_GENERATE_TOKEN : Generates a cryptographically secure token.
   ```

> [!TIP]
> **DLL Unblock (If AutoCAD fails to load the file):**
> If Windows blocks downloaded DLLs, run this in PowerShell:
> ```powershell
> Unblock-File -Path ".\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll"
> ```

---

## 🤖 Step 3: Connect the MCP Server to Your AI Client

Choose your preferred MCP runtime:

### Option 1: Instant Python Deployment with `uvx` (Recommended)
If using [`uv`](https://astral.sh/uv), no manual compilation is required:

**Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):**
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

### Option 2: Native C# Server (.NET 10)

**Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):**
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

## 🎯 Step 4: First Test & Verification

Once your AI client is connected, ask it in the chat:
> *"Inspect my active AutoCAD drawing status and draw a 20x10 meter rectangle on the layer ARQ-WALLS."*

The AI will automatically:
1. `configurar_entorno_dibujo` (ensuring `INSUNITS = 4` for metric meters).
2. `obtener_info_dibujo` (verifying drawing name and status).
3. `crear_capa` & `dibujar_rectangulo` (injecting parametric geometry without dialogs).
4. `zoom_extension` (framing the new geometry on your screen).

Congratulations! Your engineering AI copilot is now fully connected with AutoCAD.

---

## 📚 Next Steps & References

- 🛠️ **[MCP Tools Reference Manual (43 Tools)](TOOLS_REFERENCE.md):** Complete catalog with parameter schemas, types, and JSON responses.
- ⚡ **[Tools Quick Reference & Cheat Sheet](TOOLS_CHEAT_SHEET.md):** Decision tree for selecting the right tool for each CAD design task.
- 🏗️ **[System Architecture](ARCHITECTURE.md):** Deep dive into the concurrency model and UI thread dispatcher.
- 🔧 **[Troubleshooting Guide](TROUBLESHOOTING.md):** Resolving network, NETLOAD, and command execution issues.
