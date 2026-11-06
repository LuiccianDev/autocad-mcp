# Project Roadmap — AutoCAD MCP (`autocad-mcp`)

This document outlines the technical vision, strategic milestones, and future development priorities for the **AutoCAD MCP Server & Gateway** ecosystem.

---

## 🎯 Project Vision

Establish **AutoCAD MCP** as the definitive open-source standard for seamless, secure, and bidirectional integration between Artificial Intelligence Models (LLMs) and Autodesk engineering platforms (AutoCAD & Civil 3D) — enabling everything from AI-assisted drafting to fully automated civil infrastructure engineering.

---

## 🛣️ Planned Milestones

### Phase 1: Open-Source Consolidation & Robustness (Q4 2026) — *Completed / Current*
- [x] Standardized naming and folder architecture (`AutoCAD.Gateway`, `AutoCAD.McpServer`, `AutoCAD.McpPython`).
- [x] Full open-source governance suite (`.github/`, CI workflows, Code of Conduct, Security, Support).
- [x] Comprehensive documentation: architecture, 43-tool reference catalog, cheat sheet, and troubleshooting.
- [x] Dual MCP runtime: Python package with instant `uvx` setup and native .NET 10 server.
- [x] Fallback NuGet integration (`AutoCAD.NET` and `Speckle.Civil3D.API`) for headless CI compilation.
- [x] Apache 2.0 open-source licensing.

### Phase 2: Autodesk Civil 3D Expansion (Q1 2027)
- [ ] **Corridors & Assemblies:** MCP tools to inspect and parameterize assemblies, baseline regions, and frequency targets.
- [ ] **Pipe & Pressure Networks:** Creation and inspection of gravitational pipes, manholes, and pressure fittings.
- [ ] **Cut & Fill Earthwork Volumes:** Direct volume computation between TIN surfaces (Comparison Surface vs Existing Ground).
- [ ] **Sample Lines & Cross Sections:** Extraction and parametric generation of sample section lines.

### Phase 3: Performance & Developer Experience (Q2 2027)
- [ ] **Progress Streaming (MCP Notifications):** Real-time JSON-RPC progress events during long-running drawing generation or surface builds.
- [ ] **Entity Caching:** High-speed query optimization for `leer_objetos_autocad` using DWG database modification timestamps.
- [ ] **Automated Autodesk Bundle Installer:** One-click installer or PowerShell script generating `PackageContents.xml` (Autodesk Autoloader) so `AutoCAD.Gateway` loads automatically on AutoCAD startup without manual `NETLOAD`.

### Phase 4: Multimodal Vision & Advanced CAD (Q3 2027)
- [ ] **Augmented Computer Vision:** Automatic layout analysis on PNG captures detecting overlapping annotations, missing viewports, or unanchored text.
- [ ] **Dynamic Block Attributes:** Reading and modifying dynamic block parameter properties (visibility states, stretch distances, flip actions).
- [ ] **AutoCAD 2028 Compatibility:** Early validation against preview ObjectARX and next-generation .NET runtimes.

---

## 💡 Proposing a Roadmap Item

To propose a new feature or capability:
1. Verify it is not already tracked in the milestones above.
2. Open a [Feature Request](.github/ISSUE_TEMPLATE/feature_request.md) or start a conversation in **GitHub Discussions**.
3. Proposals backed by clear engineering workflows and strong community interest will be prioritized for minor version releases.
