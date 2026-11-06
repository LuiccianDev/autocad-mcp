# Plan de Estandarización a Inglés Técnico de Plantillas GitHub y Workflows Esenciales de CI/CD

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Estandarizar al inglés técnico internacional todas las plantillas comunitarias de GitHub (`.github/ISSUE_TEMPLATE/` y `.github/PULL_REQUEST_TEMPLATE.md`), optimizar el workflow de CI (`ci.yml`) con disparadores inteligentes y nombres en inglés, e incorporar exclusivamente las dos Actions esenciales para este proyecto: Análisis de Seguridad (`codeql.yml`) y Publicación Automática de Releases con binarios empaquetados (`release.yml`).

**Architecture:** Modificación de archivos YAML y Markdown en `.github/` para unificar el idioma estándar de proyectos Open Source en GitHub (Inglés). Implementación de un pipeline de 3 workflows complementarios sin sobrecargar el repositorio:
1. `ci.yml`: Integración continua (Compilación .NET 10 y Tests Python) con filtros `paths-ignore` y `workflow_dispatch`.
2. `codeql.yml`: Escaneo estático de seguridad y vulnerabilidades (C# y Python).
3. `release.yml`: Empaquetado automático de binarios descargables (`.zip` de DLLs de AutoCAD y Servidor MCP, y wheel de Python) al publicar un tag `v*`.

**Tech Stack:** GitHub Actions, CodeQL, .NET 10, Python uv/pytest, Markdown, YAML.

**Spec:** Requerimiento del usuario para internacionalizar a inglés las plantillas y agregar únicamente las GitHub Actions necesarias y específicas para proyectos de este tipo.

---

## Global Constraints

- Las plantillas y workflows deben usar terminología técnica precisa en inglés (idiomático para software de CAD/AI/MCP).
- Las reglas de compilación local y en la nube (`AutoCAD_Integration.slnx`, `uv run pytest`) no deben alterarse.
- Los disparadores `paths-ignore` deben aplicarse en `ci.yml` y `codeql.yml` para evitar disparar runners en cambios puramente documentales.
- El workflow de `release.yml` solo debe activarse ante tags de versión (`v*`) o ejecución manual (`workflow_dispatch`).

---

## Review Focus

1. **Precisión Idiomática:** Confirmar que las plantillas en `.github/` sigan el formato estándar de la comunidad de GitHub en inglés técnico.
2. **Filtrado Eficiente:** Comprobar que cambios en `.md` no activen `ci.yml`.
3. **Seguridad y Permisos:** Asegurar que `codeql.yml` y `release.yml` cuenten con los permisos mínimos requeridos (`permissions: contents: write` / `security-events: write`).
4. **Empaquetado de Release:** Verificar que `release.yml` genere zips limpios con las DLLs compiladas listas para que los usuarios las descarguen y carguen con `NETLOAD`.

---

## Task Structure

### Task 1: Internacionalización a Inglés de Plantillas Comunitarias

**Files:**
- Modificar: `.github/ISSUE_TEMPLATE/bug_report.md`
- Modificar: `.github/ISSUE_TEMPLATE/feature_request.md`
- Modificar: `.github/PULL_REQUEST_TEMPLATE.md`

- [ ] **Paso 1.1:** Reescribir `bug_report.md` en inglés formal:
  - Título, descripción de la incidencia, selector de componentes (`AutoCAD.Gateway`, `AutoCAD.McpServer`, `AutoCAD.McpPython`, HTTP Gateway), entorno técnico (OS, AutoCAD version, .NET version, AI client), pasos para reproducir, comportamiento esperado y logs.
- [ ] **Paso 1.2:** Reescribir `feature_request.md` en inglés formal:
  - Título, problema/necesidad a resolver, solución propuesta (nombre de herramienta MCP, parámetros, modo de comando CAD), beneficios y alternativas.
- [ ] **Paso 1.3:** Reescribir `PULL_REQUEST_TEMPLATE.md` en inglés formal:
  - Resumen de cambios, tipo de cambio (`feat`, `fix`, `docs`, `refactor`), checklist de calidad de código (.NET 10 conventions, strict `stdout` silence in MCP, non-modal AutoCAD commands, metric units), y detalles de pruebas.

---

### Task 2: Optimización en Inglés del Workflow Principal (`ci.yml`)

**Files:**
- Modificar: `.github/workflows/ci.yml`

- [ ] **Paso 2.1:** Renombrar y documentar todos los pasos en inglés:
  - `name: CI - Build & Test (.NET 10 & Python)`
  - `job: build-and-test`
- [ ] **Paso 2.2:** Integrar filtros inteligentes `paths-ignore`:
  ```yaml
  paths-ignore:
    - '**.md'
    - 'docs/**'
    - 'LICENSE'
    - 'CODE_OF_CONDUCT.md'
    - 'SECURITY.md'
    - 'SUPPORT.md'
    - '.gitignore'
  ```
- [ ] **Paso 2.3:** Habilitar `workflow_dispatch:` para permitir ejecuciones manuales.

---

### Task 3: Creación de Workflow de Seguridad Oficial (`codeql.yml`)

**Files:**
- Crear: `.github/workflows/codeql.yml`

- [ ] **Paso 3.1:** Crear workflow de CodeQL escaneando los lenguajes `csharp` y `python`.
- [ ] **Paso 3.2:** Configurar disparadores semanales (`cron: '0 6 * * 1'`), en `push` a `main`/`master` con `paths-ignore`, y `workflow_dispatch`.

---

### Task 4: Creación de Workflow de Publicación de Releases (`release.yml`)

**Files:**
- Crear: `.github/workflows/release.yml`

- [ ] **Paso 4.1:** Configurar activación ante tags `v*` (ej. `v2.4.0`) y `workflow_dispatch`.
- [ ] **Paso 4.2:** Definir pasos de construcción:
  1. Compilación de solución .NET en Release (`dotnet build -c Release`).
  2. Compilación del paquete Python con `uv build`.
  3. Empaquetado en `.zip` de los binarios de `AutoCAD.Gateway` y `AutoCAD.McpServer`.
  4. Publicación automática de la GitHub Release con notas automáticas y adjuntos descargables usando `softprops/action-gh-release@v2`.

---

### Task 5: Validación, Commit y Push

- [ ] **Paso 5.1:** Verificar sintaxis YAML de los 3 workflows.
- [ ] **Paso 5.2:** Realizar commit semántico:
  `ci: translate templates to english and add codeql and release workflows`
- [ ] **Paso 5.3:** Subir a GitHub (`git push origin master`) y verificar ejecución de CI.
