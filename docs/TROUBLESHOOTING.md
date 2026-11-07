# Troubleshooting Guide — AutoCAD MCP (`autocad-mcp`)

This document provides direct solutions to the most frequent issues encountered when building, loading, or interacting with **AutoCAD MCP Server & Gateway** (`autocad-mcp`).

---

## 1. Issues with `NETLOAD` in AutoCAD

### Problem: "Cannot load assembly" or Windows Security Block
**Cause:** Windows automatically flags compiled or downloaded `.dll` binaries as potentially untrusted (*Zone.Identifier: 3*).
**Solution:**
Open PowerShell as Administrator and unblock the assembly:
```powershell
Unblock-File -Path ".\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll"
```
Then, inside AutoCAD, run `NETLOAD` again.

---

### Problem: "File locked by another process" during rebuild (`CS2012`)
**Cause:** Once AutoCAD loads a .NET DLL with `NETLOAD`, the assembly is held locked in memory by `acad.exe` until AutoCAD exits completely.
**Solution:**
1. Close all active AutoCAD 2027 instances before rebuilding via `.\scripts\build.ps1`.
2. Alternatively, compile to a temporary folder or terminate hung `acad.exe` processes from Task Manager.

---

## 2. Network Communication & Port 5055

### Problem: "Failed to start HTTP server: Port 5055 already in use"
**Cause:** Another AutoCAD session or background service is occupying TCP port 5055.
**Diagnostics:**
Run in PowerShell:
```powershell
Get-NetTCPConnection -LocalPort 5055 -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, OwningProcess
```
**Solution:**
If the process is an orphaned `acad.exe`, terminate it via:
```powershell
Stop-Process -Id <OwningProcessId> -Force
```

---

### Problem: HTTP `401 Unauthorized` when calling the Gateway
**Cause:** The `X-Gateway-Token` header sent by the MCP client does not match the token configured in the AutoCAD Gateway.
**Solution:**
1. Default security token is `luiccian-autocad-token-2027` (or `autocad-gateway-token-2027` fallback).
2. If `AUTOCAD_GATEWAY_TOKEN` is defined, verify the exact same value is configured in both AutoCAD and your AI client config (`claude_desktop_config.json` or `mcp_config.json`).
3. You can verify and update the token live in AutoCAD by running:
   ```text
   AI_SHOW_CONFIG
   AI_SET_TOKEN
   ```
4. Or synchronize your environment using:
   ```powershell
   .\scripts\set-gateway-token.ps1 -GenerateNew
   ```

---

## 3. Hanging Commands or UI Lockout in AutoCAD

### Problem: AutoCAD does not respond to injected commands or hangs in "waiting" prompt
**Cause:** A prior command triggered a Windows modal popup dialog (e.g. `LAYER` or `INSERT` instead of text-mode equivalents `_.-LAYER` or `_.-INSERT`), blocking text input.
**Solution:**
1. Press `ESC` twice in AutoCAD to cancel any hanging input states.
2. Ensure custom calls to `execute_command` strictly use non-modal prefix `_.-` and end each parameter with newline `\n`.
3. The Gateway automatically prepends `\x03\x03` (`ESC`/`Ctrl+C`), but cannot close Windows modal dialogs programmatically once opened.

---

## 4. MCP Server & STDIO Transport Integrity

### Problem: AI client (Claude / Antigravity) disconnects or reports "JSON-RPC parse error"
**Cause:** A component printed raw diagnostic text to `Console.Out` (`stdout`). Under the STDIO transport, standard output is strictly reserved for valid JSON-RPC framing.
**Solution:**
- Never call `Console.WriteLine` inside `AutoCAD.McpServer`.
- For logging and diagnostics, strictly use `Console.Error.WriteLine` (`stderr`) or `ILogger`. `stderr` is ignored by the MCP client parser and preserves communication integrity.

---

## 5. Autodesk Civil 3D Detection

### Problem: `check_civil3d_status` returns `civil_available: false` despite Civil 3D being installed
**Cause:** You launched AutoCAD using the *"Civil 3D 2027 as AutoCAD"* shortcut (vanilla AutoCAD profile without Civil 3D managed assemblies loaded) instead of the primary metric Civil 3D profile.
**Solution:**
Launch Civil 3D from the official shortcut: **Civil 3D 2027 Metric**. Open a drawing using a metric civil template (`_AutoCAD Civil 3D (Metric) NCS.dwt`) and run `NETLOAD` again.
