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
2. Alternatively, terminate hung `acad.exe` processes from Task Manager before rebuilding.

---

## 2. Session Discovery, Ports & Authentication

### Problem: MCP client cannot find the running AutoCAD Gateway
**Cause:** `AutoCAD.Gateway.dll` was loaded via `NETLOAD` (or Autoloader `.bundle`), but `AI_START_SERVER` has not been executed in the active AutoCAD session, or an orphaned `{pid}.json` discovery file exists.  
**Diagnostics:**
Inside AutoCAD, run:
```text
AI_SHOW_CONFIG
```
**Solution:**
1. Run `AI_START_SERVER` inside AutoCAD 2027 or Civil 3D 2027.
2. Leave `AUTOCAD_GATEWAY_TOKEN` and `AUTOCAD_GATEWAY_PORT` unset in your MCP client config so the server auto-discovers the active `{pid}.json` session file.
3. If you pinned a custom port or multiple AutoCAD instances are open, verify the active port in `AI_SHOW_CONFIG` or set `AUTOCAD_GATEWAY_PORT` explicitly.

---

### Problem: HTTP `401 Unauthorized` when calling the Gateway
**Cause:** The `X-Gateway-Token` header sent by the MCP client does not match the session token active in `AutoCAD.Gateway`.  
**Solution:**
1. For zero-config auto-discovery, leave `AUTOCAD_GATEWAY_TOKEN` unset so the MCP server reads the token directly from `{pid}.json`.
2. If you pinned a custom token via `AI_SET_TOKEN` inside AutoCAD, verify the exact same value is set in `AUTOCAD_GATEWAY_TOKEN` in your AI client config (`claude_desktop_config.json` or `mcp_config.json`).
3. You can inspect or rotate the token inside AutoCAD using:
   ```text
   AI_SHOW_CONFIG
   AI_GENERATE_TOKEN
   AI_SET_TOKEN
   ```
4. Or synchronize your local environment using:
   ```powershell
   .\scripts\set-gateway-token.ps1 -GenerateNew
   ```

---

## 3. Edit Guard & Optimistic Concurrency Errors

### Problem: Mutations fail with `edit_guard_blocked`
**Cause:** Edit Guard protects pre-existing user drawings from unintended modifications until explicitly authorized.  
**Solution:**
- Call `allow_edit_document` (or toggle via `AI_EDIT_GUARD` inside AutoCAD) to authorize edits on the open drawing, or create a new Gateway-managed drawing with `new_drawing`.

### Problem: Mutations or pagination fail with `revision_conflict`
**Cause:** The drawing database changed between reading entity handles/cursors and submitting a mutation.  
**Solution:**
- Re-run `query_entities` or `get_entity` to obtain the current handles, coordinates, and `revision` before retrying.

---

## 4. Hanging Commands or UI Lockout in AutoCAD

### Problem: AutoCAD does not respond to injected commands or hangs in "waiting" prompt
**Cause:** A prior command triggered a Windows modal popup dialog (e.g. `LAYER` or `INSERT` instead of text-mode equivalents `_.-LAYER` or `_.-INSERT`), blocking text input.  
**Solution:**
1. Press `ESC` twice in AutoCAD or call `cancel_command`.
2. Prefer native transactional tools (`create_layer`, `insert_block`, `execute_native_batch`) over raw `execute_command`.
3. When using `execute_command`, strictly use the non-modal prefix `_.-` and end each parameter with newline `\n`.

---

## 5. MCP Server & STDIO Transport Integrity

### Problem: AI client (Claude / Antigravity) disconnects or reports "JSON-RPC parse error"
**Cause:** A component printed raw diagnostic text to `stdout`, or an invalid `AUTOCAD_MCP_TOOL_MODE` / `AUTOCAD_MCP_OUTPUT_FORMAT` value was passed. Under STDIO transport, `stdout` is strictly reserved for JSON-RPC framing.  
**Solution:**
- Never call `Console.WriteLine` inside `AutoCAD.McpServer`; write diagnostics strictly to `Console.Error.WriteLine` (`stderr`).
- Verify `AUTOCAD_MCP_TOOL_MODE` is one of `granular`, `compound`, or `core`, and `AUTOCAD_MCP_OUTPUT_FORMAT` is `compact` or `json` (invalid values fail fast to `stderr` with exit code `1`).

---

## 6. Autodesk Civil 3D Detection

### Problem: `check_civil3d_status` returns `civil_available: false` despite Civil 3D being installed
**Cause:** You launched AutoCAD using the *"Civil 3D 2027 as AutoCAD"* shortcut (vanilla AutoCAD profile without `AeccDbMgd.dll` loaded) instead of the primary metric Civil 3D profile.  
**Solution:**
Launch Civil 3D from the official shortcut: **Civil 3D 2027 Metric**. Open a drawing using a metric civil template (`_AutoCAD Civil 3D (Metric) NCS.dwt`) and run `NETLOAD` + `AI_START_SERVER` again.
