# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.4.0] - Unreleased

### Added
- **Atomic Batch Drafting Engine (45 Tools Parity):**
  - Added `draw_batch_geometry`: Atomic batch drafting tool consolidating lines, polylines, rectangles, circles, arcs, and text into a single AutoLISP evaluation `(progn (entmake ...) ...)`, reducing execution latency by ~99% and token usage by ~95%.
  - Added `execute_batch_commands`: Atomic command sequencing without intermediate client roundtrips.
- **PyPI Trusted Publishing (OIDC):** Automated provenance and cryptographic attestations via GitHub Actions workflow `publish-pypi.yml`.
- **Zero-Install Deployment via `uvx luiccian-autocad-mcp`:** Formalized as the primary Tier 1 setup for Claude Desktop, Antigravity, and Cursor.

### Fixed & Security
- **Security Remediation (CWE-209):** Sanitized HTTP 500 error responses in `HttpGatewayServer.cs` to eliminate exception information exposure, verified and closed as `Fixed` in GitHub CodeQL scanning.
- **GitHub Actions Quota Optimization:** Implemented comprehensive `paths-ignore` exclusions (`.agents/**`, `examples/**`, templates, git configs) and `concurrency: cancel-in-progress` in `ci.yml` and `codeql.yml` to prevent billable runner waste.
- **YAML Workflow Validation:** Resolved multiline indentation syntax error in release notes generation by decoupling logic into `scripts/generate-release-notes.ps1`.
- **Release Asset Sanitation:** Fixed asset globbing to exclude unintended files (`.gitignore`) from GitHub Releases.

### Changed
- **English-First Standardization Across MCP Tools, Gateway and Documentation:**
  - Standardized all 45 MCP tools in both C# .NET 10 (`AutoCAD.McpServer`) and Python FastMCP (`AutoCAD.McpPython`) to canonical Technical English names (`get_drawing_info`, `draw_batch_geometry`, `draw_line`, `create_layer`, `measure_distance`, `execute_command`, `create_layout`, `check_civil3d_status`, `list_civil3d_alignments`, `create_civil3d_surface`, etc.).
  - Standardized all tool input parameters, XML docstrings, and analytical math utilities (`calculate_distance`, `calculate_polygon_area`, `build_batch_geometry_lisp`) to English signatures.
  - Standardized Gateway REST models, endpoints, and JSON serialization keys to English while maintaining backward-compatibility aliases for existing clients.
  - Synchronized documentation across `README.md`, `docs/TOOLS_REFERENCE.md`, `docs/TOOLS_CHEAT_SHEET.md`, `docs/GETTING_STARTED.md`, `docs/TROUBLESHOOTING.md`, and agent skills.
- Standardized tool count to 45 across `README.md`, `docs/GETTING_STARTED.md`, `AutoCAD.McpPython/README.md`, and example configs.
- Modernized root `README.md` with centered HTML hero section, badges, and quick navigation breadcrumbs.

---

## [2.3.0] - 2026-11-07

### Added
- **Python MCP Server (`AutoCAD.McpPython/`) with Instant `uvx` Support:**
  - Complete implementation based on FastMCP providing 100% functional parity across all 43 tools.
  - Packaged with Hatchling and `uv` for zero-install execution via `uvx luiccian-autocad-mcp`.
  - Modules `cad_math.py` (pure analytical geometry) and `client.py` (asynchronous HTTP client with `httpx`).
  - Automated test suite with `pytest` and `pytest-asyncio` (9 passing unit tests).
- **Dynamic Network Support & Simplified Port Configuration:**
  - Simplified port configuration using `AUTOCAD_GATEWAY_PORT: "5055"` instead of full URLs, automatically constructing local endpoints in C# and Python.
  - Dynamic resolution of `AUTOCAD_GATEWAY_PORT` in native AutoCAD command `AI_START_SERVER` and both MCP clients.
- **Dynamic Token Management in AutoCAD:**
  - New native AutoCAD commands: `AI_SHOW_CONFIG` (status & token inspection), `AI_SET_TOKEN` (hot-reloading token in memory), and `AI_GENERATE_TOKEN` (cryptographic random token generator with Windows environment variable persistence).
  - PowerShell utility script `scripts/set-gateway-token.ps1` for automatic synchronization between Windows user variables and AI client configs.
- **MCP Catalog Expansion to 43 Specialized Tools:**
  - **`DrawingTools` Module (7 tools):** Strongly-typed parametric geometry (`dibujar_linea`, `dibujar_polilinea`, `dibujar_rectangulo`, `dibujar_circulo`, `dibujar_arco`, `insertar_texto`, `insertar_bloque`) implemented with metric `entmake`, independent of OSNAP and preserving active layers.
  - **`LayerTools` Module (3 tools):** Layer management and state control (`crear_capa`, `establecer_capa_actual`, `establecer_estado_capa` for visibility, freeze/thaw, and lock/unlock).
  - **`MeasurementTools` Module (4 tools):** Local analytical computational geometry (`medir_distancia` 2D/3D with azimuth and slope, `calcular_area_poligono` using Gauss algorithm and centroid, `limpiar_y_purgar_dibujo` with `AUDIT`, and `regenerar_todo` with `REGENALL`).
  - **`Civil3DTools` Module:** Added `consultar_perfil_longitudinal` for parametric ground profile sampling along alignments with slope and extreme elevation analysis.
- **CI/CD Hardening & GitHub Governance:**
  - Headless CI compilation via fallback NuGet packages (`AutoCAD.NET 26.0.0` and `Speckle.Civil3D.API 2027.0.1`).
  - Node 24 runner opt-in (`FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true`).
  - Automated release pipeline (`release.yml`) and PyPI publication chain.

### Changed
- **Folder and Project Standard Re-architecture:**
  - Standardized all projects to Microsoft guidelines: `AutoCAD.Gateway`, `AutoCAD.McpServer`, and `AutoCAD.McpPython`.
  - Flattened redundant `src/` hierarchy within `AutoCAD.Gateway`.
  - Renamed scripts to kebab-case: `build.ps1`, `start-mcp.ps1`, `set-gateway-token.ps1`.
- **License Switch to Apache License 2.0:**
  - Transitioned repository licensing from MIT to Apache License Version 2.0 (Copyright 2026 LuiccianDev).
- **Official Branding & Attribution (Luiccian):**
  - Attribution and creator credits to **Luiccian** (`LuiccianDev`) across assemblies, packages, and AutoCAD welcome banners.
  - Standardized default security token to `luiccian-autocad-token-2027`.

---

## [2.2.0] - 2026-11-06

### Added
- **Full Open-Source Documentation Suite:**
  - `docs/ARCHITECTURE.md`: Architecture document with Mermaid C4 diagrams and UI thread concurrency lifecycle.
  - `docs/GETTING_STARTED.md`: 5-minute quickstart guide for developers and end users.
  - `docs/TOOLS_REFERENCE.md`: Technical reference manual for MCP tools.
  - `docs/TROUBLESHOOTING.md`: Common issue resolution guide (NETLOAD, file locking, ports, and tokens).
  - `ROADMAP.md`: Strategic roadmap and future project milestones.
  - `SECURITY.md`, `SUPPORT.md`, `CODE_OF_CONDUCT.md`: Open-source community governance standards.
- **Community Templates (.github/):**
  - Issue templates for bug reports (`bug_report.md`) and feature requests (`feature_request.md`).
  - Standardized pull request template (`PULL_REQUEST_TEMPLATE.md`).
  - Continuous Integration (CI) GitHub Actions workflow (`ci.yml`) for .NET 10 on Windows.
- **Practical Examples Directory (`examples/`):**
  - Ready-to-use configurations for Claude Desktop and Antigravity / Cursor / VS Code.
  - Diagnostic scripts in PowerShell and Bash.
  - Sample metric parametric AutoLISP routine (`sample_metro_tunnel.lsp`).

---

## [2.1.0] - 2026-11-05

### Added
- **Sheet Layouts, Title Blocks & PDF Plotting Tools (5 tools):**
  - `crear_layout`: Programmatic paper space layout creation.
  - `configurar_hoja_layout`: Metric paper sizing (A0 to A4), virtual plotter `DWG To PDF.pc3`, and plot style `monochrome.ctb`.
  - `crear_viewport`: Viewport creation with metric scale (`1000/Scale XP`) centered on model coordinates.
  - `insertar_cajetin_parametrico`: Standardized technical title blocks with dynamic fields.
  - `exportar_pdf`: Physical PDF plotting from layout.
- **Vision Tool `capturar_pantalla_autocad`:** Active viewport screenshot export for multimodal AI feedback.
- **Dynamic AutoLISP Support:** Tools `ejecutar_lisp` and `cargar_archivo_lisp` for evaluating routines and manipulating `entmake` lists.

---

## [2.0.0] - 2026-11-04

### Added
- **Native Autodesk Civil 3D 2027 Integration:**
  - `Civil3DService` and `NullCivil3DService` loaded conditionally via reflection to maintain AutoCAD compatibility.
  - Specialized MCP tools: TIN surface inspection (`listar_superficies_civil3d`), elevation queries (`consultar_elevacion_terreno`), horizontal alignments (`listar_alineamientos_civil3d`), station/offset calculations, and alignment/surface generation.
- **.NET 10 & C# 13 Migration:** Complete migration to .NET 10 SDK with strict nullable reference types (`#nullable enable`).

---

## [1.0.0] - 2026-11-01

### Added
- **Initial Ecosystem Release:**
  - Native `AutoCAD.Gateway` plugin with thread-safe UI injection via `Application.Idle` and `ConcurrentQueue`.
  - `AutoCAD.McpServer` implementing MCP v2.2 over STDIO transport.
