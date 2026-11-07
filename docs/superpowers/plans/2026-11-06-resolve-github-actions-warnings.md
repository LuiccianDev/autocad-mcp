# Resolve GitHub Actions Warnings & Upgrade CodeQL to v4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminar las advertencias en los workflows de GitHub Actions actualizando CodeQL Action a v4, configurando explícitamente el `build-mode` por lenguaje (manual para C#, none para Python) y documentando la naturaleza informativa del aviso de Node 24 para las acciones oficiales.

**Architecture:** Actualización quirúrgica de `.github/workflows/codeql.yml` para adoptar `github/codeql-action/init@v4` y `analyze@v4`, especificando en la matriz el `build-mode` correspondiente a cada lenguaje (`csharp: manual`, `python: none`). Mantenimiento de `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true` en todos los workflows (`ci.yml`, `codeql.yml`, `release.yml`) para garantizar ejecución estable en Node 24.

**Tech Stack:** GitHub Actions, CodeQL v4, .NET 10, Python 3.11, YAML.

**Spec:** Requerimiento del usuario para diagnosticar las advertencias en los logs de GitHub Actions y actualizar los workflows para mantener la integración continua completamente limpia y conforme a los estándares de GitHub.

---

## Global Constraints

- **No romper el CI existente:** Mantener los fallbacks condicionales de NuGet (`AutoCAD.NET` y `Speckle.Civil3D.API`) y las pruebas de `pytest`.
- **Preservar `paths-ignore`:** No alterar las reglas de omisión de documentación en los disparadores `push` y `pull_request`.
- **Compatibilidad con Node 24:** Conservar la variable de entorno `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true`.

---

## Review Focus

1. **Disponibilidad de CodeQL Action v4:** Verificar que `github/codeql-action/init@v4` y `analyze@v4` sean resolubles por los runners de GitHub.
2. **Configuración de `build-mode`:** Comprobar que C# use `manual` y Python use `none` para suprimir la advertencia *"Cannot build overlay-base database"*.
3. **Persistencia en verde:** Verificar que tanto el job de C# como el de Python completen su análisis con 0 alertas de seguridad.
4. **Actualización en Notion:** Completar la tarea pendiente *"Verificación Logs GitHub Actions"* en el Project Manager.

---

## Task Structure

### Task 1: Diagnóstico de las 3 Advertencias de GitHub Actions

**Files:**
- Inspeccionar: `.github/workflows/ci.yml`
- Inspeccionar: `.github/workflows/codeql.yml`

- [ ] **Paso 1.1:** Analizar la advertencia 1: *Node.js 20 is deprecated*.
  - **Diagnóstico:** Acciones oficiales (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `astral-sh/setup-uv@v5`) tienen `node20` en su metadata. GitHub fuerza la ejecución en Node 24 gracias a nuestra variable `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true`. **Es un aviso puramente informativo, no un error ni falla.**
- [ ] **Paso 1.2:** Analizar la advertencia 2: *CodeQL Action v3 will be deprecated in December 2026*.
  - **Diagnóstico:** GitHub lanzó CodeQL Action v4. La v3 seguirá funcionando hasta fines de 2026, pero actualizar a v4 ahora elimina la advertencia y utiliza Node 24 de forma nativa.
- [ ] **Paso 1.3:** Analizar la advertencia 3: *Cannot build an overlay-base database because build-mode is set to "undefined"*.
  - **Diagnóstico:** Ocurre porque la matriz no especifica `build-mode`. C# requiere compilación explícita (`manual`), mientras que Python es interpretado (`none`).

---

### Task 2: Actualización de `codeql.yml` a v4 y Configuración de `build-mode`

**Files:**
- Modificar: `.github/workflows/codeql.yml`

- [ ] **Paso 2.1:** Configurar la estrategia de matriz con `language` y `build-mode`:
  ```yaml
      strategy:
        fail-fast: false
        matrix:
          include:
            - language: csharp
              build-mode: manual
            - language: python
              build-mode: none
  ```
- [ ] **Paso 2.2:** Actualizar las referencias de CodeQL Action a `@v4`:
  - `uses: github/codeql-action/init@v4` con `build-mode: ${{ matrix.build-mode }}`
  - `uses: github/codeql-action/analyze@v4`
- [ ] **Paso 2.3:** Validar sintaxis YAML.

---

### Task 3: Verificación, Commit y Monitoreo en GitHub Actions

**Files:**
- Modificar: `.github/workflows/codeql.yml`

- [ ] **Paso 3.1:** Ejecutar commit semántico:
  `ci: upgrade codeql action to v4 and configure explicit build-mode per language`
- [ ] **Paso 3.2:** Subir cambios a `origin/master`.
- [ ] **Paso 3.3:** Monitorear con `gh run list` y `gh run view` la ejecución del workflow de CodeQL para confirmar que las advertencias de CodeQL v3 y build-mode hayan desaparecido.

---

### Task 4: Actualización en Notion Project Manager

- [ ] **Paso 4.1:** Actualizar la tarea *"Verificación Logs GitHub Actions"* en Notion a estado `Done` con los hallazgos documentados.
