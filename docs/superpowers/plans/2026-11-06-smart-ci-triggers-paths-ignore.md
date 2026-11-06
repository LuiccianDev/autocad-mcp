# Plan de Optimización de Disparadores de CI (Smart Triggers con paths-ignore y workflow_dispatch)

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Optimizar el workflow de GitHub Actions (`.github/workflows/ci.yml`) mediante filtros inteligentes `paths-ignore` (para evitar ejecuciones innecesarias cuando solo se modifique documentación o archivos estáticos) y habilitar la activación manual bajo demanda (`workflow_dispatch`).

**Architecture:** Modificar la sección `on` de `.github/workflows/ci.yml` para ignorar archivos `.md`, `docs/**`, licencias y plantillas en eventos `push` y `pull_request`, agregando además el evento `workflow_dispatch` para permitir ejecuciones manuales desde la interfaz web de GitHub.

**Tech Stack:** GitHub Actions, YAML, Git.

**Spec:** Requerimiento del usuario para ejecutar el CI únicamente cuando se alteren componentes de código reales (.NET, Python, scripts, builds), ignorando modificaciones puramente documentales.

---

## 1. Definición de Filtros Inteligentes

### ¿Cuándo SÍ debe ejecutarse el CI?
- Cambios en código C# (.NET 10): `AutoCAD.Gateway/**`, `AutoCAD.McpServer/**`.
- Cambios en código Python: `AutoCAD.McpPython/**`.
- Cambios en la solución o dependencias: `AutoCAD_Integration.slnx`, `pyproject.toml`, `uv.lock`.
- Cambios en scripts de compilación: `scripts/**`.
- Cambios en la configuración del CI: `.github/workflows/**`.
- Activación manual por el usuario desde GitHub web (`workflow_dispatch`).

### ¿Cuándo NO debe ejecutarse (Archivos Ignorados)?
- Cualquier archivo Markdown: `**.md` (`README.md`, `CHANGELOG.md`, `ROADMAP.md`, `CONTRIBUTING.md`, etc.).
- Carpeta de documentación técnica: `docs/**`.
- Archivos legales y de comunidad: `LICENSE`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`.
- Reglas de Git: `.gitignore`.
- Archivos estáticos o ejemplos sin compilación: `examples/lisp/**`.

---

## Global Constraints

- No alterar la lógica de build ni las pruebas que ya están funcionando en verde.
- Preservar la ejecución en ramas `master` y `main`.
- La sintaxis YAML debe ser validada rigurosamente.

---

## Review Focus

1. **Eficiencia en Pushes Documentales:** Si se edita solo un archivo `.md`, GitHub Actions debe ignorar el push sin iniciar el runner.
2. **Sensibilidad a Código:** Si se edita un archivo `.cs` o `.py` (incluso junto con un `.md`), el CI debe activarse y validar la compilación.
3. **Disparador Manual:** Confirmar la presencia de la opción "Run workflow" en la pestaña Actions de GitHub.

---

## Task Structure

### Task 1: Actualizar Disparadores en `.github/workflows/ci.yml`

**Files:**
- Modificar: `.github/workflows/ci.yml`

- [ ] **Paso 1.1:** Configurar `paths-ignore` para `push` y `pull_request`.
- [ ] **Paso 1.2:** Agregar `workflow_dispatch:` para habilitar el botón manual.

```yaml
on:
  push:
    branches: [ main, master ]
    paths-ignore:
      - '**.md'
      - 'docs/**'
      - 'LICENSE'
      - 'CODE_OF_CONDUCT.md'
      - 'SECURITY.md'
      - 'SUPPORT.md'
      - '.gitignore'
  pull_request:
    branches: [ main, master ]
    paths-ignore:
      - '**.md'
      - 'docs/**'
      - 'LICENSE'
      - 'CODE_OF_CONDUCT.md'
      - 'SECURITY.md'
      - 'SUPPORT.md'
      - '.gitignore'
  workflow_dispatch:
```

---

### Task 2: Commit y Despliegue a GitHub

**Files:**
- Modificar: `.github/workflows/ci.yml`

- [ ] **Paso 2.1:** Ejecutar `git add .github/workflows/ci.yml`.
- [ ] **Paso 2.2:** Realizar commit semántico:
  `ci: add smart paths-ignore filters and manual workflow dispatch trigger`
- [ ] **Paso 2.3:** Subir a GitHub con `git push origin master`.

---

### Task 3: Verificación de Comportamiento

- [ ] **Paso 3.1:** Verificar en `gh run list` la ejecución disparada por la actualización del workflow.
- [ ] **Paso 3.2:** Confirmar que `workflow_dispatch` esté disponible.
