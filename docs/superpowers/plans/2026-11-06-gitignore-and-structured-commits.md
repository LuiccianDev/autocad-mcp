# Plan de Blindaje de `.gitignore` y Estrategia de Commits Estructurados

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Actualizar y blindar el archivo `.gitignore` para la solución políglota (.NET 10, Python, AutoCAD/Civil 3D, IDEs y SO), purgar archivos de caché compilados del disco, y ejecutar una secuencia de commits semánticos y atómicos según el estándar *Conventional Commits*.

**Architecture:** Purga de artefactos compilados (`__pycache__`, `.pytest_cache`, `bin/`, `obj/`), configuración exhaustiva de exclusiones en `.gitignore` multicapa, y organización del historial de Git en commits atómicos separados por subsistemas funcionales (Core Gateway, Servidores MCP, Scripts de automatización, Documentación y Plugins de Agente).

**Tech Stack:** Git, .NET 10, Python 3.10+, PowerShell 7+, MSBuild, Visual Studio Code / Visual Studio.

**Spec:** Requerimiento del usuario para commits adecuados y blindaje de `.gitignore` contra archivos no deseados.

---

## Global Constraints

- Nunca commitear binarios compilados (`.dll`, `.exe`, `.pdb`, `.pyc`), carpetas de caché (`bin/`, `obj/`, `__pycache__/`, `.pytest_cache/`) ni entornos virtuales (`.venv/`).
- No commitear secretos ni tokens reales en texto plano (mantener `.env.example` como plantilla no sensible).
- Seguir la especificación [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) (`feat:`, `chore:`, `docs:`, `test:`).
- Preservar la integridad de los archivos de prueba y configuración requeridos (`pyproject.toml`, `uv.lock`, `.slnx`, `.csproj`).

---

## Review Focus

1. **Bytecode de Python:** Asegurar que ningún archivo `.pyc` ni carpeta `__pycache__` sea rastreado por Git tras actualizar `.gitignore`.
2. **Archivos temporales de AutoCAD:** Asegurar que archivos temporales como `*.ac$`, `*.sv$`, `*.dwl`, `*.dwl2`, `*.bak` queden bloqueados.
3. **Binarios de compilación .NET:** Garantizar que `AutoCAD.Gateway/bin/` y `AutoCAD.McpServer/bin/` permanezcan 100% excluidos.
4. **Archivos de usuario de IDE:** Bloquear `*.user`, `.vs/`, `.idea/` preservando los archivos de equipo en `.vscode/` (`settings.json`, `tasks.json`, etc.).
5. **Historial de Git ordenado:** Verificar que `git log --oneline` refleje una progresión lógica y profesional del proyecto.

---

## Task Structure

### Task 1: Limpieza de Cachés y Artefactos Temporales en Disco

**Files:**
- Eliminar de disco:
  - `AutoCAD.McpPython/src/autocad_mcp/__pycache__/`
  - `AutoCAD.McpPython/tests/__pycache__/`
  - `AutoCAD.McpPython/.pytest_cache/`

- [ ] **Paso 1.1:** Ejecutar comando de PowerShell para purgar carpetas `__pycache__` y `.pytest_cache`.
- [ ] **Paso 1.2:** Verificar que `AutoCAD.Gateway/bin`, `AutoCAD.Gateway/obj`, `AutoCAD.McpServer/bin`, `AutoCAD.McpServer/obj` estén limpios o excluidos.

---

### Task 2: Actualización y Blindaje Exhaustivo de `.gitignore`

**Files:**
- Modificar: `.gitignore`

- [ ] **Paso 2.1:** Sobrescribir `.gitignore` con las secciones completas:
  - `.NET 10 / C# / MSBuild` (`bin/`, `obj/`, `*.nupkg`, `*.suo`, `.vs/`, `.idea/`, etc.).
  - `Python (PEP 8 / uv / pytest)` (`__pycache__/`, `*.py[cod]`, `.pytest_cache/`, `.venv/`, `.ruff_cache/`, etc.).
  - `AutoCAD / DWG` (`*.bak`, `*.dwl`, `*.dwl2`, `*.ac$`, `*.sv$`, `*.err`).
  - `Secretos y Entorno` (`.env`, `.env.*`, excepto `!.env.example`).
  - `Sistemas Operativos` (`Thumbs.db`, `.DS_Store`, `desktop.ini`).
- [ ] **Paso 2.2:** Ejecutar `git status` para confirmar que los archivos `.pyc` ya no aparecen como untracked.

---

### Task 3: Ejecución de Commits Semánticos Atómicos

Organizar el proyecto en commits modulares y descriptivos en lugar de un único commit masivo:

- [ ] **Commit 1 (Repositorio y Configuración):**
  - **Archivos:** `.gitignore`, `.github/`, `AutoCAD_Integration.slnx`, `LICENSE`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`.
  - **Mensaje:** `chore(repo): initialize repository configuration, CI workflows, and gitignore`
- [ ] **Commit 2 (AutoCAD Gateway - Plugin Nativo C#):**
  - **Archivos:** `AutoCAD.Gateway/`
  - **Mensaje:** `feat(gateway): implement AutoCAD.Gateway native extension for AutoCAD 2027 (.NET 10)`
- [ ] **Commit 3 (AutoCAD McpServer - C# .NET 10):**
  - **Archivos:** `AutoCAD.McpServer/`
  - **Mensaje:** `feat(mcp-dotnet): implement AutoCAD.McpServer Model Context Protocol server in C#`
- [ ] **Commit 4 (AutoCAD McpPython - Servidor Python):**
  - **Archivos:** `AutoCAD.McpPython/`
  - **Mensaje:** `feat(mcp-python): implement AutoCAD.McpPython server with UV and PyPI support`
- [ ] **Commit 5 (Scripts y Ejemplos de Configuración):**
  - **Archivos:** `scripts/`, `examples/`
  - **Mensaje:** `chore(scripts): add PowerShell automation scripts and client configurations`
- [ ] **Commit 6 (Documentación y Directivas):**
  - **Archivos:** `README.md`, `CHANGELOG.md`, `ROADMAP.md`, `CONTRIBUTING.md`, `AGENTS.md`, `docs/`
  - **Mensaje:** `docs: add comprehensive architecture, tools reference, and agent guidelines`
- [ ] **Commit 7 (Plugins de Agentes e Integraciones):**
  - **Archivos:** `.agents/`
  - **Mensaje:** `feat(agents): add autocad-gateway plugin, skills, and agent definitions`

---

### Task 4: Verificación Final del Repositorio

- [ ] **Paso 4.1:** Ejecutar `git status` y verificar que el working tree esté completamente limpio (`nothing to commit, working tree clean`).
- [ ] **Paso 4.2:** Ejecutar `git log --oneline` para verificar la secuencia ordenada de commits.
- [ ] **Paso 4.3:** Ejecutar `.\scripts\build.ps1` y `uv run --directory AutoCAD.McpPython pytest` para confirmar que nada se rompió tras los commits.
