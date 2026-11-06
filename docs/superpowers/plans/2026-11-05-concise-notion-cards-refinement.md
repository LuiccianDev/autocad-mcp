# Plan de Implementación: Estandarización Visual e Iconos Oficiales en Notion (`notion-pm`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Estandarizar la identidad visual del workspace **Project Manager Luiccian** en Notion:
1. Reemplazar los emojis genéricos y coloridos por los **iconos nativos monocromáticos del sistema**:
   - Proyectos: `icons/package_gray` (icono oficial de proyectos en Notion).
   - Tareas: `icons/checkmark_gray` (icono oficial de tareas en Notion).
2. Simplificar y acortar los títulos de tareas a **2 a 4 palabras** (1 sola línea visual en el tablero Kanban) y el título del proyecto a `AutoCAD AI Gateway & MCP`.
3. Actualizar las reglas del plugin `notion-pm` para que cualquier creación futura use siempre los iconos oficiales y títulos ultra concisos.

**Architecture:** 
- **Iconografía Oficial Verificada:**
  - `Projects`: `icons/package_gray`
  - `Tasks`: `icons/checkmark_gray`
- **Títulos Concisos:** Formato `[Acción] + [Objeto]` (máximo 4 palabras). Nombres de archivo, comandos o párrafos técnicos se mantienen exclusivamente en `Definition of Done` o `Description`.
- **Ejecución MCP:** Actualizaciones en vivo con `notion-update-page` (`command: "update_properties"` con `icon` y `properties`).

**Tech Stack:** Notion MCP (`notion_notion`), Markdown, YAML Frontmatter.

**Spec:** Capturas del usuario indicando que los emojis genéricos rompían la coherencia visual con las tarjetas estándar de Notion.

## Global Constraints

- Icono de Proyectos: estrictamente `icons/package_gray`.
- Icono de Tareas: estrictamente `icons/checkmark_gray`.
- Longitud de títulos de tareas: 2 a 4 palabras máximo.
- Prohibido usar emojis aleatorios (`⚡`, `✅`, `📐`, `👁️`, `🚀`, `🗺️`).

## Review Focus

1. Verificar que el proyecto y las 6 tareas queden con sus iconos nativos oficiales grises.
2. Comprobar que los títulos de tareas no superen una sola línea.
3. Asegurar que las directivas del plugin prohíban emojis informales en el workspace.

---

### Task 1: Actualizar Directivas del Plugin `notion-pm` (Iconos Nativos y Títulos Cortos)

**Files:**
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\rules\AGENTS.md`
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\agents\notion-pm.md`
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\skills\notion-pm\SKILL.md`

- [ ] **Step 1: Añadir la Directiva de Iconos Nativos en `rules/AGENTS.md`**
  - Todo nuevo proyecto debe usar `icons/package_gray`.
  - Toda nueva tarea debe usar `icons/checkmark_gray`.
  - Prohibido el uso de emojis genéricos coloridos.
  - Títulos de tareas estrictamente de 2 a 4 palabras.

- [ ] **Step 2: Actualizar `agents/notion-pm.md` y `skills/notion-pm/SKILL.md`**
  - Registrar la especificación de iconos y títulos minimalistas.

---

### Task 2: Actualizar en Vivo en Notion (Iconos Grises Oficiales + Títulos Concisos)

**Files:**
- Notion MCP (`notion-update-page`)

- [ ] **Step 1: Actualizar Proyecto**
  - Page ID: `4104cced-b56b-81d5-98f9-ef4c8d3a7487`
  - Icon: `icons/package_gray`
  - `Project Name`: "AutoCAD AI Gateway & MCP"

- [ ] **Step 2: Actualizar Tarea 1**
  - Page ID: `4104cced-b56b-8120-91ce-e37375a6760a`
  - Icon: `icons/checkmark_gray`
  - `Task Name`: "Compilación Release .NET 10"

- [ ] **Step 3: Actualizar Tarea 2**
  - Page ID: `4104cced-b56b-8105-8988-eaa7c1d6832a`
  - Icon: `icons/checkmark_gray`
  - `Task Name`: "Herramientas AutoLISP"

- [ ] **Step 4: Actualizar Tarea 3**
  - Page ID: `4104cced-b56b-81d8-8a33-fec7dba8f945`
  - Icon: `icons/checkmark_gray`
  - `Task Name`: "Módulo Layouts y PDF"

- [ ] **Step 5: Actualizar Tarea 4**
  - Page ID: `4104cced-b56b-8169-91a9-f8b5405180cf`
  - Icon: `icons/checkmark_gray`
  - `Task Name`: "Captura Pantalla CAD"

- [ ] **Step 6: Actualizar Tarea 5**
  - Page ID: `4104cced-b56b-81a6-9809-e18d3347870b`
  - Icon: `icons/checkmark_gray`
  - `Task Name`: "Pruebas NETLOAD Gateway"

- [ ] **Step 7: Actualizar Tarea 6**
  - Page ID: `4104cced-b56b-8177-a81b-d007dab7dc03`
  - Icon: `icons/checkmark_gray`
  - `Task Name`: "Validación Civil 3D"

---

### Task 3: Verificación Visual

- [ ] **Step 1: Consultar los registros actualizados en Notion**
  Verificar que el proyecto tiene `icons/package_gray` y las 6 tareas tienen `icons/checkmark_gray`.
- [ ] **Step 2: Presentar la confirmación al usuario**
