# Plan de Implementación: Renombrar Plugin a `notion-pm`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renombrar y migrar de forma atómica el plugin `notion-project-manager` hacia su nuevo nombre ultra corto `notion-pm`, actualizando manifiesto, subagente, skill, reglas y rutas de archivo, eliminando la versión larga previa para evitar colisiones.

**Architecture:** El plugin residirá en `C:\Users\DAVID\.gemini\config\plugins\notion-pm\`. Todos sus identificadores internos (`name` en `plugin.json`, `name` en el agente `agents/notion-pm.md`, y `name` en la skill `skills/notion-pm/SKILL.md`) pasarán a ser `notion-pm`, permitiendo al usuario e IA invocar la suite escribiendo simplemente `notion-pm`.

**Tech Stack:** Antigravity Customization Plugin System, Markdown, YAML Frontmatter, JSON.

**Spec:** Elección del usuario mediante `ask_question`: `notion-pm`.

## Global Constraints

- Directorio destino: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\`.
- Directorio a eliminar: `C:\Users\DAVID\.gemini\config\plugins\notion-project-manager\`.
- Nombre de skill unificado: `notion-pm`.
- Nombre de agente unificado: `notion-pm`.
- Preservar íntegramente las conexiones MCP a Notion (`Projects` `collection://74d4cced...` y `Tasks` `collection://d204cced...`) y las reglas de seguridad de datos.

## Review Focus

1. Verificar que `plugin.json` tenga `"name": "notion-pm"`.
2. Verificar que `agents/notion-pm.md` tenga `name: notion-pm`.
3. Verificar que `skills/notion-pm/SKILL.md` tenga `name: notion-pm`.
4. Asegurar que el directorio antiguo sea eliminado completamente.

---

### Task 1: Crear la Nueva Estructura del Plugin `notion-pm`

**Files:**
- Create: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\plugin.json`
- Create: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\agents\notion-pm.md`
- Create: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\skills\notion-pm\SKILL.md`
- Create: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\rules\AGENTS.md`
- Create: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\README.md`

- [ ] **Step 1: Escribir `plugin.json` con `name: "notion-pm"`**
- [ ] **Step 2: Escribir `agents/notion-pm.md` con `name: notion-pm`**
- [ ] **Step 3: Escribir `skills/notion-pm/SKILL.md` con `name: notion-pm`**
- [ ] **Step 4: Escribir `rules/AGENTS.md` y `README.md`**
- [ ] **Step 5: Validar que todos los archivos nuevos existen**
  Run: `Test-Path "C:\Users\DAVID\.gemini\config\plugins\notion-pm\plugin.json"`
  Expected: `True`

---

### Task 2: Limpieza y Eliminación del Plugin Anterior

**Files:**
- Delete: `C:\Users\DAVID\.gemini\config\plugins\notion-project-manager\`

- [ ] **Step 1: Eliminar directorio anterior de forma recursiva**
  Run PowerShell: `Remove-Item -Recurse -Force "C:\Users\DAVID\.gemini\config\plugins\notion-project-manager"`
- [ ] **Step 2: Verificar que el directorio antiguo ya no existe**
  Run PowerShell: `Test-Path "C:\Users\DAVID\.gemini\config\plugins\notion-project-manager"`
  Expected: `False`

---

### Task 3: Verificación de Registro y Pruebas

- [ ] **Step 1: Comprobar el árbol completo del nuevo plugin**
  Listar archivos en `C:\Users\DAVID\.gemini\config\plugins\notion-pm\`
- [ ] **Step 2: Confirmar que la IA reconoce `notion-pm` como agente y skill activa**
