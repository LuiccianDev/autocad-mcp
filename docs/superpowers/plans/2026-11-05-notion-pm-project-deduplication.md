# Plan de Implementación: Prevención de Duplicados y Vinculación Inteligente de Proyectos en `notion-pm`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar en el plugin `notion-pm` (reglas, skill, subagente y documentación) el protocolo mandatorio de detección previa de proyectos existentes en Notion para evitar duplicidades y garantizar la reutilización y vinculación relacional óptima de tareas.

**Architecture:** Se establece una compuerta previa de introspección (*Pre-flight Check*) en la colección `Projects` de Notion antes de cualquier acción de planificación o creación. Si el proyecto existe, se reutiliza su ID y se enlazan las nuevas tareas vía `Project` $\leftrightarrow$ `Tasks`. Solo si no existe tras una búsqueda exhaustiva se presenta al usuario la opción de crear uno nuevo.

**Tech Stack:** Markdown (Antigravity Plugins, Rules, Skills & Subagents), Notion MCP Server (`notion_notion`), SQLite / JSON schema.

**Spec:** Requerimiento del usuario para verificar la existencia previa de proyectos en la página de Notion "Project Manager Luiccian", evitando crear duplicados y estableciendo el modelo de relación idóneo para nuevas tareas.

## Global Constraints

- Espacio de trabajo único: `Project Manager Luiccian` (`ada4ccedb56b82e8b3e881ccd979f114`).
- Bases de datos maestras únicas: `Projects` (`74d4cced-b56b-832c-8006-07f8b69995d6`) y `Tasks` (`d204cced-b56b-8382-8a48-078553efe16e`).
- Estándar visual estricto: `icons/package_gray` (proyectos) y `icons/checkmark_gray` (tareas). Cero emojis.
- Regla de títulos limpios: 2 a 4 palabras por tarea (`[Acción] + [Objeto]`).
- Principio de solo lectura por defecto (*Read-Only Safety*): Toda creación o alteración requiere aprobación explícita.

## Review Focus

1. **Búsqueda difusa insuficiente:** Un proyecto denominado `AutoCAD AI Gateway & MCP` podría duplicarse si la búsqueda solo compara coincidencias literales exactas en lugar de palabras clave principales (`AutoCAD`, `Gateway`, `MCP`).
2. **Desvinculación relacional:** Tareas creadas sin poblar adecuadamente la propiedad `Project`, quedando huérfanas en el tablero sin sumar al rollup de `Progress`.
3. **Escritura no autorizada:** El agente creando automáticamente un proyecto nuevo al no encontrar coincidencia sin antes pedir confirmación al usuario.
4. **Sobrecarga de contexto:** El agente listando proyectos históricos archivados o completados sin filtrar adecuadamente el estado activo.
5. **Alteración del proyecto existente:** Modificar involuntariamente campos consolidados de un proyecto existente (`Status`, `Health`, `Timeline`) al vincularle nuevas tareas.

---

### Task 1: Directiva de Gobernanza Anti-Duplicados en `rules/AGENTS.md`

**Files:**
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\rules\AGENTS.md`

**Interfaces:**
- Consumes: Reglas de solo lectura y conformidad de esquema vigentes.
- Produces: Nueva sección obligatoria "Política Estricta Anti-Duplicados y Reutilización de Proyectos".

- [ ] **Step 1: Redactar la regla de verificación previa de existencia de proyecto**
  - Exigir que antes de proponer o crear cualquier iniciativa en Notion, la IA deba buscar en la base de datos `Projects` por palabras clave del proyecto o contexto del repositorio activo.
- [ ] **Step 2: Definir el flujo de reutilización preferente**
  - Si se detecta un proyecto coincidente, prohibir la creación de un nuevo registro y obligar a reutilizar el proyecto existente vinculando las nuevas tareas a su relación `Tasks`.
- [ ] **Step 3: Aplicar los cambios en `C:\Users\DAVID\.gemini\config\plugins\notion-pm\rules\AGENTS.md`**
- [ ] **Step 4: Verificar la coherencia del documento de reglas**

---

### Task 2: Protocolo Operativo y Relacional en `skills/notion-pm/SKILL.md`

**Files:**
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\skills\notion-pm\SKILL.md`

**Interfaces:**
- Consumes: Definición de esquemas de `Projects` y `Tasks`.
- Produces: Guía algorítmica de 3 pasos: (1) Consulta de existencia, (2) Decisión relacional (Reutilización vs. Creación), (3) Vinculación bidireccional.

- [ ] **Step 1: Documentar el algoritmo de resolución de proyectos en la Sección 5 (Fase 1: Inception)**
  - Detallar los comandos/herramientas de consulta (`notion-fetch`, `notion-search`, `notion-query-data-sources`) para auditar `Projects`.
  - Especificar cómo relacionar las tareas al proyecto (`Project` = `[URL del proyecto existente]`).
- [ ] **Step 2: Detallar el impacto en el rollup de `Progress`**
  - Explicar que al vincular nuevas tareas al proyecto existente, el rollup recalcula automáticamente el % completado sin crear duplicados ni dispersar métricas.
- [ ] **Step 3: Aplicar los cambios en `C:\Users\DAVID\.gemini\config\plugins\notion-pm\skills\notion-pm\SKILL.md`**
- [ ] **Step 4: Verificar la consistencia técnica de las instrucciones**

---

### Task 3: Protocolo Cognitivo en el Subagente `agents/notion-pm.md`

**Files:**
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\agents\notion-pm.md`

**Interfaces:**
- Consumes: Habilidades de `skills/notion-pm` y reglas de `rules/AGENTS.md`.
- Produces: Comportamiento proactivo del subagente: buscar proyectos antes de responder o planificar.

- [ ] **Step 1: Añadir el paso "Paso 0: Pre-flight Check de Existencia de Proyecto"**
  - El subagente analiza el repositorio o la solicitud del usuario, consulta `Projects` en Notion y presenta al usuario el resultado:
    - *"Detecté el proyecto existente '[Nombre]' en Notion. ¿Deseas vincular estas nuevas tareas a este proyecto?"*
- [ ] **Step 2: Blindar la regla de no crear proyectos duplicados**
  - Solo en caso de confirmación de que es una iniciativa totalmente nueva e inexistente se ofrece la creación de un nuevo proyecto.
- [ ] **Step 3: Aplicar los cambios en `C:\Users\DAVID\.gemini\config\plugins\notion-pm\agents\notion-pm.md`**
- [ ] **Step 4: Validar la claridad de las instrucciones del subagente**

---

### Task 4: Actualización de la Documentación en `README.md`

**Files:**
- Modify: `C:\Users\DAVID\.gemini\config\plugins\notion-pm\README.md`

**Interfaces:**
- Consumes: Cambios realizados en los componentes anteriores.
- Produces: Sección clara en el README explicando la prevención inteligente de duplicados.

- [ ] **Step 1: Documentar la funcionalidad de verificación previa y prevención de duplicados en el README**
- [ ] **Step 2: Aplicar los cambios en `C:\Users\DAVID\.gemini\config\plugins\notion-pm\README.md`**
- [ ] **Step 3: Verificar que el README esté completo y bien formateado**
