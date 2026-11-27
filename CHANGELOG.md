# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.7.0] - Unreleased

### Added
- **Modernized MCP Output Protocol (`AUTOCAD_MCP_OUTPUT_FORMAT`):**
  - Added `AUTOCAD_MCP_OUTPUT_FORMAT` across `AutoCAD.McpServer` (.NET 10) and `AutoCAD.McpPython` supporting `compact` (default) and `json`, with fail-fast startup validation writing strictly to `stderr`.
  - Enabled native `CallToolResult` envelopes: `isError: true` (with `structuredContent: null`) on validation/Gateway/partial-batch errors, native `ImageContent` (`image/png`) in `capture_screenshot(inline=True)` without raw base64 in text or `structuredContent`, and token-saving compact tabular formatters alongside full JSON in `structuredContent`.
  - Exposed deterministic MCP `ToolAnnotations` (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`) across all 90 tools and dynamically aggregated compound domain routers.
- **Native CAD Drafting, Plotting, Layer, Document & AutoLISP Endpoints (62 Gateway Routes):**
  - Added native `create_layout` (`POST /plotting/layout` with `clear_default_viewport`), `configure_layout_page` (`POST /plotting/page-setup` ISO A0-A4 landscape/portrait `PlotRotation`), `insert_title_block` (`POST /plotting/title-block` with `iso_standard`, `compact`, `detailed` and optional raster logo), `insert_raster_image` (`POST /drafting/raster-image`), `load_lisp_file` (`POST /lisp/load`), `save_drawing` (`POST /documents/save` safe `Database.SaveAs` with overwrite protection and `DwgVersion` mapping), and `regen_all` (`POST /session/regen`).
  - Expanded `create_viewport` (`POST /plotting/viewport`) with 3D DCS camera controls (`center_model_z`, `view_direction`, `twist_angle`, `shade_plot`, `visual_style`, `lock_viewport`) and `create_layer` / `set_layer_state` with ACI color, on-demand `.lin` linetype loading, ISO `lineweight`, `is_plottable`, and `description`.
  - Standardized Gateway JSON responses with `ok`, `status`, and `duration_ms` (`RequestRouter`) and added structured `error.hint` diagnostics.

### Changed
- **Decomposed Gateway & MCP Architecture:**
  - Split `PlottingService.cs` into `LayoutService.cs`, `ViewportService.cs`, `TitleBlockBuilder.cs`, and `PlottingService.cs`; extracted `RasterImageService.cs` from `DraftingService.cs`; extracted `BatchElementNormalizer.cs` from `DrawingTools.cs`; and split contract policies into `LineWeightPolicy.cs`, `DwgVersionPolicy.cs`, `PlottingPolicy.cs`, `OutputFormat.cs`, and `ToolAnnotationPolicy.cs`.
  - Fixed `create_dimension` metric scaling via `SetDimstyleData` across both `INSUNITS = 4` (mm) and `INSUNITS = 6` (m), stabilized silent synchronous `PlotEngine` PDF plotting (`BACKGROUNDPLOT=0`), and **moved `regen_all` in `compound` mode from `autocad_measurement` to `autocad_execution`** (breaking change for `autocad_measurement(action="regen_all")` callers; `granular` and `core` modes are unchanged).

### Fixed
- **Live Command-Context Rejection on `regen_all`:**
  - Removed the redundant `Acad.EnsureQuiescent(doc)` call inside `DocumentControlService.RegenAllAsync`'s `ExecuteInCommandContextAsync` callback, keeping the `preflight` quiescence guard before entering AutoCAD's `EXECUTEFUNCTION` command context.

### Security
- **Audit Hardening (`security-audit` + Thermos):**
  - Enforced `LispBridge.LoadFileAsync` file size cap (`16 MiB`), deferred directory creation until after document/revision/Edit-Guard/layout validations in `PlotPdfAsync` and `SaveDrawingAsync`, upgraded `RasterImageDef` to `ForWrite` on reused logos, blocked `ByLayer`/`ByBlock` layer linetypes, and removed `AcceptScalarAsString` parameter-name hardcoding from `CompoundDispatcher`.

---

## [2.5.0] - 2026-11-24

### Added
- **Selective MCP Tool Mode Loading (`AUTOCAD_MCP_TOOL_MODE`):**
  - Added `AUTOCAD_MCP_TOOL_MODE` across both `AutoCAD.McpPython` and `AutoCAD.McpServer` (.NET 10) supporting `granular` (default, 89 tools), `compound` (10 domain router tools `autocad_context` through `autocad_execution` with two-tier `action="help"` introspection, cross-domain hints, alias resolution, and structured JSON `anyOf` parameter schemas), and `core` (28 high-leverage standalone tools without aliases).
  - Enforced fail-fast startup validation writing configuration errors strictly to `stderr` with exit code `1` and zero `stdout` output so STDIO JSON-RPC framing stays intact.
- **Hardened Loopback Socket Transport (`AutoCAD.Gateway` & `AutoCAD.Gateway.Contracts`):**
  - Replaced `HttpListener` with a dedicated `TcpListener` loopback server paired with `HttpRequestGuard` and `HttpWireCodec`, enforcing constant-time `X-Gateway-Token` authentication, strict `Host` validation, Fetch Metadata / `Origin` / `Referer` CSRF rejection, fixed `Content-Length` framing, and per-session `{pid}.json` auto-discovery.
- **Native ObjectARX Entities, Layers, Drafting, Blocks, Xrefs & Plotting (55 Gateway Endpoints, 89 MCP Tools):**
  - Added native single-transaction entity inspection, filtered/spatial cursor pagination (`query_entities` with HMAC `QueryCursor`), property modification, transforms (`move`, `rotate`, `scale`, `copy`, `mirror`), deletion, explode (`explode_entities`), and atomic batches (`execute_native_batch`, up to 500 ops with rollback).
  - Added native layer creation/state/deletion with purge validation and multi-document lifecycle controls (`list_documents`, `new_drawing`, `open_drawing`, `activate_document`, `close_document`, `allow_edit_document`, `cancel_command`, `get_system_variable`, `set_system_variable`) protected by Edit Guard and optimistic document revisions.
  - Added native `create_dimension`, `create_mleader`, `create_hatch`, `create_table`, `define_block`, `get_block_definition`, `get_block_attributes`, `set_block_attributes`, `list_xrefs`, native PaperSpace `create_viewport`, synchronous `export_pdf` (`BACKGROUNDPLOT=0`), and `capture_screenshot(inline=True)` base64 vision support.
  - Added Civil 3D `get_civil3d_surface_stats` and `sample_civil3d_cross_section` endpoints, plus dynamic agent introspection endpoints (`GET /_tools` and `GET /_guide`) and `scripts/install-bundle.ps1` Autoloader `.bundle` staging.

### Changed
- **Main-Thread Dispatcher & Modular Architecture:**
  - Replaced `UiThreadInvoker` with `[DllImport("user32.dll")] PostMessageW` idle waking, `StartOpenCloseTransaction` for reads via `Acad.Read`, single-transaction `Acad.Write` locks, synchronous `LispBridge` with delimited `LispSyntax` pre-validation, and clean C#/Python parity modules (`EntityBuilder`/`Modifier`/`Transformer`, modular Python `tools/` packages, unified `normalize_batch_element`).
- **Civil 3D & Plotting Reliability:**
  - Routed `sample_longitudinal_profile` directly to `GET /civil/profile`, unified all Gateway responses on `GatewayProtocol.WireJsonOptions` + `AcadGatewayException`, fixed PaperSpace `_.-PLOT` prompt alignment with temporary atomic PDF staging, and bounded profile/cross-section loops upfront.
- **Metric Dimension Scaling, HTTP Client Reuse & Token Compaction:**
  - Added automatic metric calibration (`text_height`, `arrow_size`, `dim_scale`) on `create_dimension` for meter drawings (`INSUNITS = 4`) when using uncalibrated default styles.
  - Added persistent `httpx.AsyncClient` pooling in Python, 2s endpoint discovery caching, tiered timeouts (`10s` reads, `30s` writes, `60s` heavy ops), and idempotent `GET`-only retry across Python and .NET clients.
  - Removed duplicate Spanish JSON keys (`nombre`, `soloLectura`, `unidades`, `bloqueada`, `congelada`, `apagada`, `tipo`, `texto`, `capa`) from context payloads, rounded extents/text coordinates to 4 decimals, and added SHA-256 manifest verification plus `-Uninstall` to `scripts/install-bundle.ps1`.

### Security
- **Defense-in-Depth Hardening (`security-audit` Pass):**
  - Enforced loopback-only gateway URLs, disabled HTTP proxies and auto-redirects in C#/Python clients, blocked `Sec-Fetch-*` cross-site browser requests, and rejected symbolic-link/oversized discovery files.
  - Hardened local path validation against Windows DOS reserved device names, trailing dots, UNC shares, alternate data streams, NTFS junctions/symlinks (`ReparsePoint` ancestor checks), and traversal; expanded `BlockedSystemVariables` with `_`/`.` prefix normalization; added `AUTOCAD_GATEWAY_RESTRICTED_MODE=1` to optionally lock down `/execute` and `/lisp/eval`; bound `/execute` idle dispatch to verified documents; and hardened `plot_style`, `layout_name`, strict JSON parsing (`NaN`/recursion guards), geometry/batch vertex budgets, and CI/PyPI release tag alignment.

---

## [2.4.0] - 2026-11-07

### Added
- **Atomic Longitudinal Profile Endpoint (`GET /civil/profile`):**
  - Added native Civil 3D profile extraction endpoint in `AutoCAD.Gateway` that samples station coordinates and terrain elevations across an entire alignment in a single atomic transaction, reducing profile sampling latency from 25 seconds down to ~5ms.
  - Added transparent fast-path optimization in `sample_longitudinal_profile` (and alias `get_longitudinal_profile`) in both Python and C# MCP servers with graceful fallback.
- **Bidirectional Tool Aliases (14 Aliases for Complete Parity):**
  - Implemented 14 bidirectional tool aliases across Python and C# MCP servers (`clean_and_purge_drawing`, `get_longitudinal_profile`, `read_layers`, `read_objects`, `read_texts`, `read_selection`, `list_available_blocks`, `capture_screen`, `ping_autocad_gateway`, `execute_lisp`, `configure_drawing_environment`, `configure_layout_sheet`, `insert_parametric_titleblock`, `get_station_coordinate`), bringing total available tool entries to 59 and ensuring prompt synonyms resolve without friction.
- **Atomic Batch Drafting Engine (45 Tools Parity):**
  - Added `draw_batch_geometry`: Atomic batch drafting tool consolidating lines, polylines, rectangles, circles, arcs, and text into a single AutoLISP evaluation `(progn (entmake ...) ...)`, reducing execution latency by ~99% and token usage by ~95%.
  - Added `execute_batch_commands`: Atomic command sequencing without intermediate client roundtrips.
- **PyPI Trusted Publishing (OIDC):** Automated provenance and cryptographic attestations via GitHub Actions workflow `publish-pypi.yml`.
- **Zero-Install Deployment via `uvx luiccian-autocad-mcp`:** Formalized as the primary Tier 1 setup for Claude Desktop, Antigravity, and Cursor.

### Fixed & Security
- **Civil 3D & AutoCAD Crash Prevention on Purge/Audit:**
  - Replaced fragile multi-pass console command sequences (`_.-PURGE\n_A\n*\n_N...`) with safe, non-modal ActiveX AutoLISP symbol table purge `(progn (vl-load-com) (vla-PurgeAll (vla-get-ActiveDocument (vlax-get-acad-object))) (princ))\n`.
  - Guarded drawing database audit against `AccessViolationException` in Civil 3D `AeccDbMgd.dll`, eliminating `acad.exe` fatal errors and subsequent `WinError 1450` transport exhaustion crashes in `uvx`.
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
