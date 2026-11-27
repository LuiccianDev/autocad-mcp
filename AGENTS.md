# AGENTS.md — Rules for AI Assistants

Project: **autocad-mcp** (AutoCAD 2027 & Civil 3D AI Gateway + MCP Server).

---

## 0. Language, Aesthetics & Documentation Policy

- **Repository Language:** All official repository documentation, code comments, pull requests, issue templates, workflows, agent directives, and git commits **MUST** be written in **Technical English**.
- **User Interaction:** When interacting with the user, the assistant should reply in **Spanish** (or the user's preferred language) while preserving technical identifiers in English and maintaining a formal engineering tone.
- **Strict Aesthetic Standard (Zero Informal Emojis):** Prohibit informal emojis in markdown documentation, section headers, release notes, issue templates, and commit messages. Maintain a sober engineering format utilizing markdown tables and GitHub alerts (`> [!NOTE]`, `> [!TIP]`, `> [!IMPORTANT]`).

---

## 1. Environment and Build (.NET 10 & Python)

- All C# backend code targets **.NET 10**.
- Always build in `Release` configuration:
  ```powershell
  dotnet build AutoCAD_Integration.slnx -c Release
  # Or using the official helper script:
  .\scripts\build.ps1
  ```
- **Never** break the MCP server STDIO transport: never write `Console.WriteLine` or logs to `stdout` inside `AutoCAD.McpServer`, as doing so corrupts JSON-RPC framing. Write diagnostics strictly to `Console.Error` (`stderr`).
- Run .NET and Python test suites:
  ```powershell
  dotnet run --project AutoCAD.Gateway.Tests -c Release
  uv run --directory AutoCAD.McpPython --all-extras pytest
  ```

---

## 2. Two-Layer Architecture and Joint Operation

The project operates through complementary components designed to work **together**:

1. **AutoCAD.Gateway (Native AutoCAD Plugin):**
   - Runs inside `acad.exe` after loading via `NETLOAD` (or Autoloader `.bundle`) and starting with `AI_START_SERVER`.
   - Output assembly: `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`.
   - Exposes REST endpoints on loopback `http://127.0.0.1:<port>/` (`TcpListener`) and publishes `{pid}.json` for zero-config auto-discovery.
   - Protected by constant-time `X-Gateway-Token` authentication (`HttpRequestGuard`).
   - Dispatches UI-thread database work via `Acad.Read`, `Acad.Write`, `Acad.RunOnDocumentAsync`, and `Application.Idle` (`ConcurrentQueue<string>`).

2. **AutoCAD.McpPython (Python FastMCP) / AutoCAD.McpServer (.NET 10 STDIO MCP Server):**
   - Standalone STDIO servers implementing Model Context Protocol (MCP v2.2) with `AUTOCAD_MCP_TOOL_MODE` (`granular` = 90 tools, `compound` = 10 domain routers, `core` = 28 tools).
   - Output .NET assembly: `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.dll`.
   - Consumes the Gateway over loopback HTTP and exposes typed tools with strict parameter validation.

### Mandatory Directive for AI Assistants:
- **Joint Setup:** Always ensure or guide configuration of **both components** (Gateway loaded and started in AutoCAD + MCP server configured in the AI client).
- **Primary Option (`uvx`):** Recommend `uvx luiccian-autocad-mcp` first for zero-compile onboarding; present `.NET 10` binaries and local `uv run` as advanced alternatives.
- **Prefer MCP Tools Over Raw Shell Scripts:** Do not improvise manual PowerShell/curl scripts against the Gateway when the MCP server is available.

---

## 3. CAD Drafting & Command Execution Standards

- **Metric Units:** Always draft in meters (`INSUNITS = 4`).
- **Non-Modal Command Mode:** Always use `_.-` prefixes for interactive commands (`_.-LAYER`, `_.-INSERT`, `_.-PURGE`) so modal Windows dialogs never freeze the Gateway.
- **Line Terminators:** End every interactive prompt line with `\n`.
- **Auto-Cancellation:** `AutoCADCommandDispatcher` injects `\x03\x03` (`ESC`/`Ctrl+C`) before and after queued commands.

---

## 4. Official Naming and Repository Layout

### 1. Root Layout (Clean Monorepo)
- **Primary Components & Projects:** Use **`PascalCase` with dots** and uniform `AutoCAD.*` prefix:
  - `AutoCAD.Gateway/` (.NET 10 AutoCAD extension)
  - `AutoCAD.Gateway.Contracts/` (.NET 10 shared protocol, security, and `ToolMode` library)
  - `AutoCAD.Gateway.Tests/` (.NET 10 offline contract and MCP mode test suite)
  - `AutoCAD.McpServer/` (.NET 10 STDIO MCP server)
  - `AutoCAD.McpPython/` (Python FastMCP server)
- **Supporting Folders:** Use **`lowercase`**:
  - `docs/` (technical guides and references)
  - `scripts/` (build, bundle installer, and helper scripts)
  - `examples/` (AutoLISP routines and diagnostic shell scripts)
  - `skills/` (repository agent skills)
  - `.agents/` / `.github/` (agent plugin configs and CI/CD workflows)

### 2. C# Projects (.NET 10 Guidelines)
- **Projects & Namespaces:** `PascalCase` with dots (`AutoCAD.Gateway`, `AutoCAD.Gateway.Contracts`, `AutoCAD.McpServer`). No underscores or hyphens.
- **Internal Subfolders:** `PascalCase` directly under each project (`Commands/`, `Core/`, `Models/`, `Server/`, `Services/`, `Tools/`). Never nest redundant `src/` folders inside C# projects.
- **C# Files & Interfaces:** `PascalCase.cs` matching the primary type; interfaces prefixed with `I` (`IAutoCADGatewayClient.cs`).

### 3. Python Code & Packaging (PEP 8 & PyPI)
- **Modules:** `snake_case.py` (`cad_math.py`, `client.py`, `server.py`, `tool_mode.py`, `compound.py`).
- **Import Package:** `snake_case` (`src/autocad_mcp/`).
- **PyPI Package Name:** `kebab-case` (`luiccian-autocad-mcp` in `pyproject.toml`).
- **Tests:** `tests/test_*.py` in `snake_case`.
- **Classes / Functions / Constants:** `PascalCase`, `snake_case`, and `SCREAMING_SNAKE_CASE`.

### 4. Scripts, Configs & Markdown
- **PowerShell Scripts:** `kebab-case.ps1` (`build.ps1`, `install-bundle.ps1`, `start-mcp.ps1`, `set-gateway-token.ps1`).
- **Config Files:** `snake_case.json` or `kebab-case.json`.
- **Markdown Docs:** `UPPERCASE.md` in root (`README.md`, `AGENTS.md`, `CHANGELOG.md`) and `docs/` (`ARCHITECTURE.md`, `GETTING_STARTED.md`).

---

## 5. GitHub Actions Quota Optimization

1. **Path Exclusions (`paths-ignore`):** `ci.yml` and `codeql.yml` skip runs on docs-only commits (`**.md`, `docs/**`, `examples/**`, `.agents/**`, `.github/ISSUE_TEMPLATE/**`, `.github/*.md`, git config files).
2. **`[skip ci]` on Docs Commits:** Include `[skip ci]` or `[ci skip]` on purely documentary commits.
3. **Concurrency Cancellation:** Keep `concurrency: cancel-in-progress: true` enabled on CI workflows.

---

## 6. Versioning & `CHANGELOG.md` Policy

1. Follow [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning 2.0.0](https://semver.org/) (`Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`).
2. Keep unreleased changes on `master` under `## [X.Y.Z] - Unreleased` without a release date until the git tag is cut.
3. Synchronize version numbers atomically across `AutoCAD.Gateway/AutoCAD.Gateway.csproj`, `AutoCAD.McpServer/AutoCAD.McpServer.csproj`, `AutoCAD.McpPython/pyproject.toml`, `CHANGELOG.md`, and the git tag `vX.Y.Z`.
4. Keep changelog bullets concise (2–3 lines max per item), ordered in strict reverse chronological order with ISO dates (`## [X.Y.Z] - YYYY-MM-DD`).

---

## 7. Documentation Sync & `docs-craft` Workflow

Whenever substantial changes are made (new/renamed MCP tools, Gateway routes, architecture refactors, environment variables, or breaking changes), **synchronize documentation before finishing**:

1. **Audit Scope:** Verify and update [README.md](README.md), [AutoCAD.McpPython/README.md](AutoCAD.McpPython/README.md), [CHANGELOG.md](CHANGELOG.md) (`[Unreleased]` or target version), and affected guides under `docs/` (`ARCHITECTURE.md`, `GETTING_STARTED.md`, `TOOLS_REFERENCE.md`, `TOOLS_CHEAT_SHEET.md`, `TROUBLESHOOTING.md`).
2. **Source-Verified Accuracy:** Cross-check every documented tool name, signature, route, count, and environment variable directly against `GatewayToolCatalog.cs`, `AutoCADToolCatalog.cs`, and `tool_mode.py`—never copy names from memory.
3. **Use `docs-craft` Properly:** Follow the `docs-craft` skills (`docs-sync`, `docs-writer`, `readme-craft`) for formatting hygiene (PyPI-safe HTML in `AutoCAD.McpPython/README.md`, GFM/Mermaid in root/docs, zero decorative emojis) and always run:
   ```powershell
   python "C:\Users\DAVID\.gemini\config\plugins\docs-craft\scripts\check_docs.py" --repo-root . --json
   ```
