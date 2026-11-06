# Plan de Mitigación de Advertencia de Node.js 20 Deprecado en GitHub Actions

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Explicar el origen de la advertencia de Node.js 20 en GitHub Actions y actualizar el workflow `.github/workflows/ci.yml` con la variable oficial `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true` para ejecutar explícitamente las acciones en Node.js 24 y eliminar la anotación de advertencia del tablero de GitHub.

**Architecture:** Configurar la variable de entorno a nivel de workflow en `.github/workflows/ci.yml` según la directiva oficial de GitHub Actions ([GitHub Changelog 2025-09-19](https://github.blog/changelog/2025-09-19-deprecation-of-node-20-on-github-actions-runners/)), asegurando que `actions/checkout@v4`, `actions/setup-dotnet@v4` y `astral-sh/setup-uv@v5` corran en Node 24 sin generar alertas.

**Tech Stack:** GitHub Actions, YAML, Node.js 24, .NET 10, Python uv.

**Spec:** Consulta del usuario respecto a la anotación de advertencia:
`Node.js 20 is deprecated. The following actions target Node.js 20 but are being forced to run on Node.js 24: actions/checkout@v4, actions/setup-dotnet@v4, astral-sh/setup-uv@v5.`

---

## 1. Explicación Técnica del Warning

### ¿Qué significa esta advertencia?
- **No es un error:** El workflow se ejecutó exitosamente (**SUCCESS ✅**) y todas las compilaciones y pruebas pasaron al 100%.
- **Contexto de GitHub:** GitHub actualizó el motor interno de sus runners en la nube (`windows-latest`) de Node.js 20 (que llegó al fin de su ciclo de vida EOL) a **Node.js 24**.
- **Por qué aparece el aviso:** Las acciones oficiales (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `astral-sh/setup-uv@v5`) tienen configurado en su metadata interna `runs: using: node20`. Como el runner ya no tiene Node 20 por defecto, GitHub fuerza su ejecución sobre Node 24 para que tu pipeline no falle, y emite esta advertencia ("Annotation") a modo de aviso informativo.

### ¿Cómo se resuelve de forma limpia?
GitHub diseñó una variable de entorno oficial:
```yaml
env:
  FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true
```
Al definirla en el archivo de workflow, le indicamos al runner de GitHub Actions que autorizamos formalmente el uso de Node 24 para todas las acciones JavaScript, lo que **desactiva y silencia la advertencia en el tablero de ejecuciones**.

---

## User Review Required

> [!NOTE]
> Esta modificación es 100% segura y no altera el código fuente de C# ni de Python. Solo optimiza la configuración del pipeline CI en `.github/workflows/ci.yml` para tener un reporte visual completamente limpio (0 errores, 0 warnings).

---

## Proposed Changes

### [MODIFY] .github/workflows/ci.yml

Añadir la variable de entorno `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true` en el encabezado global del workflow:

```diff
 name: CI Build & Test (.NET 10 & Python)

 on:
   push:
     branches: [ main, master ]
   pull_request:
     branches: [ main, master ]

+env:
+  FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true

 jobs:
   build-and-test:
```

---

## Verification Plan

### Automated Checks
1. **Validación de sintaxis YAML:**
   Confirmar que `.github/workflows/ci.yml` preserve un formato YAML válido.
2. **Commit y Push a GitHub:**
   Subir el cambio a la rama `master` en GitHub:
   ```powershell
   git add .github/workflows/ci.yml
   git commit -m "ci: opt-in to node 24 for github actions to silence deprecation warning"
   git push origin master
   ```
3. **Monitoreo del Workflow:**
   Consultar la ejecución resultante en GitHub Actions con `gh run list` y verificar que el campo `ANNOTATIONS` quede en 0 warnings.
