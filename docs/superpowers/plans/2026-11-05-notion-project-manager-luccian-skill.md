# Notion Project & Product Manager Suite (Estructura Actualizada y Simplificada)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar la suite de gestión de proyectos basada en la **estructura real, actual y simplificada** de Notion:
1. La **Skill Global** `notion-project-manager-luccian` en `C:\Users\DAVID\.gemini\config\skills\notion-project-manager-luccian\SKILL.md` reflejando con exactitud el modelo real de 2 bases de datos vinculadas (`Projects` $\leftrightarrow$ `Tasks`).
2. El **Subagente Especialista** `notion-project-manager` (Product & Project Manager) en `C:\Users\DAVID\.gemini\config\plugins\notion\agents\notion-project-manager.md` y registrado en el runtime (`define_subagent`), para listar, triajar, gestionar y documentar proyectos usando Notion MCP (`notion_notion`).
3. Verificación en vivo listando los proyectos reales y sus tareas.

**Architecture:** 
- **Estructura Real Verificada de Notion:** La página central "Project Manager Luiccian" (`ada4ccedb56b82e8b3e881ccd979f114`) opera con un modelo dual ágil y directo:
  - **`Projects` (`collection://74d4cced-b56b-832c-8006-07f8b69995d6`):** `Project Name`, `Status`, `Health`, `Project Type`, `Domain`, `Timeline`, `Manager`, `Budget`, `Client`, `Description`, `Success Criteria`, `Tasks` (relación), `Progress` (rollup % Done).
  - **`Tasks` (`collection://d204cced-b56b-8382-8a48-078553efe16e`):** `Task Name`, `Status`, `Priority`, `Workstream`, `Due Date`, `Assign`, `Project` (relación bidireccional), `Estimated Hours`, `Actual Hours`, `Definition of Done`, `Blocked` (checkbox).
- **Skill Global:** Guía de navegación, esquemas reales verificados, convenciones de calidad, reglas de solo lectura (*read-only by default*) y flujos de consulta.
- **Subagente:** Persona técnica de Product / Project Manager equipada con herramientas de `notion_notion`.

**Tech Stack:** Markdown / YAML frontmatter, Model Context Protocol (`notion_notion`), Antigravity Customizations.

## Global Constraints

- Ubicación de la skill global: `C:\Users\DAVID\.gemini\config\skills\notion-project-manager-luccian\SKILL.md`.
- Ubicación de la definición del agente: `C:\Users\DAVID\.gemini\config\plugins\notion\agents\notion-project-manager.md`.
- Registro interactivo: Ejecutar `define_subagent` para disponibilidad inmediata en la sesión.
- Seguridad: Lectura libre; creación/edición/archivado solo bajo petición explícita del usuario.
- Idioma: Respuestas en español técnico estructurado, conservando los nombres exactos en inglés de propiedades y estados de Notion.

## Review Focus

1. Verificar que la skill elimine bases de datos obsoletas y solo contemple la estructura real (`Projects` y `Tasks`).
2. Comprobar que los campos y opciones desplegables (`Domain`, `Project Type`, `Health`, `Workstream`, `Priority`) coincidan con los datos de Notion.
3. Asegurar que el subagente tenga permisos MCP y sistema de instrucciones como Product Manager.
4. Ejecutar una consulta de prueba que liste los proyectos sin alterar los datos.

---

### Task 1: Crear la Skill Global Simplificada y Verificada

**Files:**
- Create: `C:\Users\DAVID\.gemini\config\skills\notion-project-manager-luccian\SKILL.md`

- [ ] **Step 1: Redactar `SKILL.md` con la arquitectura real de 2 colecciones**
  - Metadata frontmatter (`name: notion-project-manager-luccian`).
  - Identidad: Página `ada4ccedb56b82e8b3e881ccd979f114`.
  - Esquema detallado de `Projects` y `Tasks` con sus tipos exactos, opciones y relaciones.
  - Flujos operativos: Consulta de proyectos, desglose de tareas, triaje de bloqueos y cálculo de progreso.
  - Protocolo de seguridad y reglas de solo lectura.

- [ ] **Step 2: Verificar la creación del archivo**
  Run PowerShell: `Test-Path "C:\Users\DAVID\.gemini\config\skills\notion-project-manager-luccian\SKILL.md"`
  Expected: `True`

---

### Task 2: Crear y Registrar el Subagente Product & Project Manager

**Files:**
- Create: `C:\Users\DAVID\.gemini\config\plugins\notion\agents\notion-project-manager.md`

- [ ] **Step 1: Redactar la definición del subagente**
  - YAML Frontmatter: `name: notion-project-manager`, `role: Technical Product & Project Manager`, `model: inherit`.
  - System Prompt: Principios de Product Management, gestión de backlog de tareas, control de salud de proyectos, uso de herramientas MCP de Notion (`notion-fetch`, `notion-query-data-sources`, `notion-create-pages`, `notion-update-page`).

- [ ] **Step 2: Registrar en tiempo de ejecución con `define_subagent`**
  - Ejecutar `define_subagent` con `EnableMCPTools: true`, `EnableWriteTools: true`.

---

### Task 3: Verificación Operativa y Reporte del Portafolio de Proyectos

**Files:**
- Interactuar vía: Notion MCP (`notion-query-data-sources`).

- [ ] **Step 1: Consultar los proyectos reales y sus tareas asociadas**
  Ejecutar consulta en `Projects` y listar proyectos con su estado, presupuesto, cliente y porcentaje de progreso.

- [ ] **Step 2: Presentar el resumen ejecutivo al usuario**
  Mostrar el reporte de estado de proyectos con formato Markdown estructurado y confirmar que el agente y la skill están 100% operativos.
