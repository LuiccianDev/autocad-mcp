# Contributing to AutoCAD MCP (`autocad-mcp`)

Thank you for your interest in contributing to **AutoCAD MCP Server & Gateway** (`autocad-mcp`)! This project aims to establish a robust, reliable, and high-performance open-source standard connecting Artificial Intelligence Models (via Model Context Protocol v2.2) with Autodesk AutoCAD 2027 and Civil 3D.

By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

---

## 📋 Environment Prerequisites

1. **.NET 10 SDK** installed (`dotnet --version` >= 10.0.100).
2. **Autodesk AutoCAD 2027** or **Civil 3D 2027** installed at `C:\Program Files\Autodesk\AutoCAD 2027\`.
3. **Python 3.10+** and [`uv`](https://astral.sh/uv) (for the Python MCP server).
4. Recommended Editors: Visual Studio, Visual Studio Code (with C# Dev Kit), Cursor, or Antigravity.

---

## 🏗️ Repository Architecture

- `AutoCAD.Gateway/`: In-process C# plugin compiling to `net10.0-windows`. Loaded in AutoCAD via `NETLOAD`, exposing an HTTP REST server on port `5055`.
- `AutoCAD.McpServer/`: Standalone .NET 10 MCP server implementing the Model Context Protocol over STDIO transport with 43 strongly-typed tools.
- `AutoCAD.McpPython/`: Standalone Python FastMCP server packaging all 43 tools with instant `uvx` execution and pytest test suite.
- `docs/`: Technical documentation suite (`ARCHITECTURE.md`, `GETTING_STARTED.md`, `TOOLS_REFERENCE.md`, `TOOLS_CHEAT_SHEET.md`, `TROUBLESHOOTING.md`).
- `examples/`: Client configurations, test scripts, and sample AutoLISP routines.
- `.github/`: Issue templates, Pull Request template, and GitHub Actions CI/CD workflows (`ci.yml`, `codeql.yml`, `release.yml`).
- `scripts/`: PowerShell automation scripts (`build.ps1`, `start-mcp.ps1`, `set-gateway-token.ps1`).

---

## 🌿 Git Workflow & Branches

1. **Branch Naming:**
   Use semantic prefixes:
   - `feature/<feature-name>`: New MCP tools or Gateway capabilities.
   - `fix/<bug-description>`: Bug fixes or stability improvements.
   - `docs/<doc-improvement>`: Documentation, diagrams, or guide updates.
   - `refactor/<area>`: Internal refactoring preserving observable behavior.

2. **Commit Convention (Conventional Commits):**
   Write clear, present-tense commit messages in English:
   - `feat(mcp): add tool for surface cut and fill volumes`
   - `fix(gateway): resolve newline handling in command injection`
   - `docs(tools): update reference tables in TOOLS_REFERENCE.md`
   - `refactor(dispatcher): optimize concurrent queue draining on Idle`

---

## 🛠️ Local Build & Verification

Before submitting changes, verify that both the .NET solution and Python package build cleanly:

```powershell
# Build .NET solution
.\scripts\build.ps1

# Run Python test suite
uv run --directory AutoCAD.McpPython --all-extras pytest
```

---

## 📐 Coding & Architectural Standards

1. **Single Responsibility Principle (SRP):**
   - The entry point (`Main.cs`) only manages the extension lifecycle.
   - Drawing and database services belong in dedicated classes under `Services/`.
2. **Thread Safety:**
   - **Never** manipulate the AutoCAD database or UI document from background threads. All drawing operations must be enqueued and dispatched via `Application.Idle`.
3. **Strict `stdout` Silence:**
   - In `AutoCAD.McpServer`, standard output is strictly reserved for JSON-RPC messages. Never invoke `Console.WriteLine` or unredirected logging; use `Console.Error.WriteLine` (`stderr`) or `ILogger`.
4. **Non-Modal Text Mode Commands:**
   - Always prepend interactive commands with `_.-` (e.g. `_.-LAYER`) and end every input with `\n` to prevent blocking modal Windows dialogs.
5. **Nullable Reference Types & XML Docs:**
   - All C# code must compile cleanly with `#nullable enable` without warnings.
   - Public types and methods must be documented with XML doc comments (`/// <summary>`).

---

## 🚀 Submitting Pull Requests

1. Push your branch to GitHub and open a Pull Request targeting `master` (or `main`).
2. Fill out the PR template (`.github/PULL_REQUEST_TEMPLATE.md`), checking off items in the checklist.
3. Ensure all GitHub Actions checks (CI Build, CodeQL) pass cleanly.
4. Maintainers will review your contribution and provide constructive feedback.
