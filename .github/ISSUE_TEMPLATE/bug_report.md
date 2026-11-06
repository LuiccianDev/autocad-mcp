---
name: Bug Report
about: Create a report to help us improve AutoCAD AI Gateway and MCP Server
title: "[BUG] <concise description of the issue>"
labels: ["bug", "triage"]
assignees: ""
---

## Description
A clear and concise description of the unexpected behavior.

## Affected Component
Select the component where the issue occurs:
- [ ] `AutoCAD.Gateway` (C# native plugin inside `acad.exe`)
- [ ] `AutoCAD.McpServer` (.NET 10 STDIO MCP console server)
- [ ] `AutoCAD.McpPython` (Python UV MCP server)
- [ ] HTTP Gateway / Network communication (port `5055` / `X-Gateway-Token`)
- [ ] Civil 3D integration (`AeccDbMgd` / Surfaces / Alignments)
- [ ] AI Client (Claude Desktop / Antigravity / Cursor / VS Code)

## Environment
- **Operating System:** Windows 10 / 11 (x64)
- **AutoCAD / Civil 3D Version:** (e.g. AutoCAD 2027.0.1 / Civil 3D 2027)
- **.NET SDK Version:** (run `dotnet --version` in terminal)
- **Python Version:** (if using McpPython, e.g. Python 3.11 / 3.12)
- **AI Client:** (e.g. Claude Desktop, Antigravity, Cursor)

## Steps to Reproduce
1. Execute command / call MCP tool: `...`
2. Arguments / payload sent: `...`
3. Observe error in console / logs: `...`

## Expected Behavior
A concise description of what you expected to happen.

## Console Output / Logs
```text
Paste AutoCAD text window (F2) or MCP stderr logs here.
```

## Additional Context
Any additional context about the DWG file, drawing units (`INSUNITS`), etc.
