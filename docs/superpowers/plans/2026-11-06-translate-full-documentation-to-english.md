# Plan de Internacionalización y Traducción Completa de la Documentación a Inglés

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transformar y estandarizar toda la documentación pública y técnica del repositorio (`README.md`, `docs/*`, guías de subproyectos y políticas de contribución) al inglés técnico internacional (*English-first*), elevando el estándar de adopción global, visibilidad y credibilidad del proyecto en GitHub.

**Architecture:** Traducción técnica rigurosa preservando diagramas Mermaid, esquemas C4, bloques de código, tablas de referencia de las 43 herramientas MCP y enlaces relativos válidos. División modular en 4 paquetes de documentación:
1. Documentación Raíz Principal (`README.md`, `ROADMAP.md`, `CHANGELOG.md`).
2. Guías Técnicas de Arquitectura y Referencia (`docs/ARCHITECTURE.md`, `GETTING_STARTED.md`, `TOOLS_REFERENCE.md`, `TOOLS_CHEAT_SHEET.md`, `TROUBLESHOOTING.md`).
3. Guías de Componentes de Código (`AutoCAD.Gateway/README.md`, `AutoCAD.McpServer/README.md`, `AutoCAD.McpPython/README.md`).
4. Políticas Comunitarias y Directivas de Agentes (`CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`, `AGENTS.md`).

**Tech Stack:** Markdown (GFM), Mermaid Diagrams, GitHub Flavored Markdown, Technical English.

**Spec:** Requerimiento del usuario para convertir toda la documentación y presentación del proyecto a inglés técnico profesional.

---

## Global Constraints

- **Preservación Técnica:** Mantener intactos nombres de funciones, tipos de C# / Python, comandos de AutoCAD (`_.-LAYER`, `_.-PLINE`), nombres de variables de entorno (`AUTOCAD_GATEWAY_TOKEN`, `AUTOCAD_GATEWAY_PORT`) y snippets de código.
- **English-First:** Todo el texto explicativo, tablas, encabezados y advertencias se traducen al inglés técnico estándar de la industria (AEC / CAD / AI Agents).
- **Consistencia de Enlaces:** Todos los enlaces relativos (`docs/GETTING_STARTED.md`, `examples/`, `AutoCAD.Gateway/`) deben mantenerse funcionales y exactos.

---

## Review Focus

1. **Legibilidad y Tono:** Tono profesional, conciso y de ingeniería de software open-source.
2. **Tablas de Herramientas MCP:** Traducir las descripciones de las 43 tools en `docs/TOOLS_REFERENCE.md` y `docs/TOOLS_CHEAT_SHEET.md` sin alterar los nombres de las herramientas ni de sus parámetros.
3. **Diagramas Mermaid:** Conservar la sintaxis de diagramas Mermaid traduciendo etiquetas explicativas.
4. **Directiva en AGENTS.md:** Actualizar la directiva de idioma: documentación y commits en inglés, mientras los agentes pueden dialogar con el usuario en español si él lo solicita.

---

## Task Structure

### Task 1: Traducción de la Documentación Raíz (`README.md`, `ROADMAP.md`, `CHANGELOG.md`)

**Files:**
- Modificar: `README.md`
- Modificar: `ROADMAP.md`
- Modificar: `CHANGELOG.md`

- [ ] **Paso 1.1:** Reescribir `README.md` en inglés técnico:
  - Header, badges, high-performance integration overview.
  - Documentation index table.
  - Architecture overview and ASCII/Mermaid flowcharts.
  - Quickstart with Python (`uvx`) and .NET 10 (`dotnet`).
  - Feature highlights (43 MCP tools, Civil 3D support, thread-safety).
- [ ] **Paso 1.2:** Traducir `ROADMAP.md`:
  - Strategic vision, current release milestones, upcoming features (Civil 3D surfaces, streaming, YQArch integrations).
- [ ] **Paso 1.3:** Traducir `CHANGELOG.md`:
  - Keep a Changelog standard format, release entries, added/changed/fixed sections.

---

### Task 2: Traducción de las Guías Técnicas en `docs/`

**Files:**
- Modificar: `docs/GETTING_STARTED.md`
- Modificar: `docs/ARCHITECTURE.md`
- Modificar: `docs/TOOLS_REFERENCE.md`
- Modificar: `docs/TOOLS_CHEAT_SHEET.md`
- Modificar: `docs/TROUBLESHOOTING.md`

- [ ] **Paso 2.1:** Traducir `docs/GETTING_STARTED.md`:
  - Prerequisites, 5-minute setup, building .NET solution, loading Gateway via NETLOAD, configuring Claude Desktop / Cursor / Antigravity.
- [ ] **Paso 2.2:** Traducir `docs/ARCHITECTURE.md`:
  - Dual-layer decoupled architecture, concurrency model, thread-safe UI thread dispatcher, security tokens, HTTP gateway endpoints.
- [ ] **Paso 2.3:** Traducir `docs/TOOLS_REFERENCE.md`:
  - Complete technical specification for all 43 MCP tools (Parameters, Types, Returns, and Example JSON-RPC).
- [ ] **Paso 2.4:** Traducir `docs/TOOLS_CHEAT_SHEET.md`:
  - Quick decision matrix ("What I want to do -> Which tool to call"), chained prompt recipes for AI models.
- [ ] **Paso 2.5:** Traducir `docs/TROUBLESHOOTING.md`:
  - Troubleshooting NETLOAD errors, port collisions, token auth failures, modal dialog blocks, and Civil 3D API loading.

---

### Task 3: Traducción de las Guías de Componentes

**Files:**
- Modificar: `AutoCAD.Gateway/README.md`
- Modificar: `AutoCAD.McpServer/README.md`
- Modificar: `AutoCAD.McpPython/README.md`

- [ ] **Paso 3.1:** Traducir `AutoCAD.Gateway/README.md`:
  - Native in-process extension architecture, compilation instructions, AutoCAD 2027 dependencies, security tokens.
- [ ] **Paso 3.2:** Traducir `AutoCAD.McpServer/README.md`:
  - .NET 10 console STDIO server, dependency injection, tool registration, client connection guides.
- [ ] **Paso 3.3:** Traducir `AutoCAD.McpPython/README.md`:
  - FastMCP Python architecture, instant `uvx` setup, local development with `uv`, package configuration.

---

### Task 4: Traducción de Políticas y Directivas (`CONTRIBUTING.md`, `AGENTS.md`, etc.)

**Files:**
- Modificar: `CONTRIBUTING.md`
- Modificar: `CODE_OF_CONDUCT.md`
- Modificar: `SECURITY.md`
- Modificar: `SUPPORT.md`
- Modificar: `AGENTS.md`

- [ ] **Paso 4.1:** Traducir `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md` a inglés estándar de GitHub/Open Source.
- [ ] **Paso 4.2:** Actualizar `AGENTS.md`:
  - Especificar inglés como idioma oficial del repositorio y documentación, manteniendo compatibilidad de diálogo en español para el usuario.

---

### Task 5: Validación, Commit y Despliegue

- [ ] **Paso 5.1:** Verificar que todos los enlaces relativos entre documentos funcionen.
- [ ] **Paso 5.2:** Realizar commit semántico:
  `docs: standardize entire repository documentation to professional technical english`
- [ ] **Paso 5.3:** Subir a GitHub (`git push origin master`).
  *(Nota: Gracias al filtro `paths-ignore` implementado, este commit de documentación no disparará builds innecesarios en GitHub Actions).*
