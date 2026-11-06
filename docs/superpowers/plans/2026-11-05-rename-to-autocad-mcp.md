# Plan de Implementación: Transición de Nombre e Identidad a `autocad-mcp`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Actualizar de manera coherente y profesional toda la identidad del proyecto de `mcp-autocad` a `autocad-mcp` en la documentación técnica (`README.md`, `AGENTS.md`, `CONTRIBUTING.md`), la página maestra del proyecto en Notion (`Project Manager Luiccian`), y verificar la integridad de la solución .NET 10.

**Architecture:** El núcleo del ecosistema se estandariza bajo el identificador oficial **`autocad-mcp`** (nombre canónico del producto: **AutoCAD MCP Server & Gateway**). Se actualizan las referencias en guías de contribución, instrucciones de clonado, comandos de configuración para clientes IA (Antigravity/Claude), y se sincroniza la página del proyecto en la colección `Projects` de Notion preservando los vínculos relacionales a sus tareas y el icono gris oficial.

**Tech Stack:** C# / .NET 10, Model Context Protocol (MCP v2.2), Markdown, Notion API / MCP (`notion_notion`).

**Spec:** Solicitud del usuario para renombrar el proyecto a `autocad-mcp` de forma adecuada, actualizar toda la documentación técnica, el README y la base de datos de proyectos en Notion.

## Global Constraints

- Identificador técnico estándar: `autocad-mcp`.
- Nombre de producto profesional: **AutoCAD MCP Server & Gateway** (o **AutoCAD MCP**).
- Conservar la integridad de compilación en .NET 10 Release (`AutoCAD_Integration.slnx`).
- En Notion, preservar las relaciones existentes hacia las tareas en `collection://d204cced-b56b-8382-8a48-078553efe16e`.
- Mantener estrictamente el icono oficial `icons/package_gray` en Notion y evitar emojis en el título de la tarjeta.
- No alterar rutas físicas de carpetas raíz en caliente que puedan invalidar la sesión activa del espacio de trabajo en Windows.

## Review Focus

1. **Rutas o comandos rotos:** Enlaces rotos de clonado (`git clone .../autocad-mcp.git`) o referencias a scripts en la documentación.
2. **Pérdida de relaciones en Notion:** Al actualizar la página del proyecto con `notion-update-page`, asegurarse de no sobreescribir ni vaciar el arreglo de IDs de la propiedad `Tasks`.
3. **Consistencia de nombres:** Evitar mezclas de `mcp-autocad` y `autocad-mcp` en títulos y encabezados de configuración.
4. **Validación de compilación:** Comprobar que ningún cambio documental o de metadatos afecte el archivo de solución `AutoCAD_Integration.slnx` ni los proyectos `.csproj`.
5. **Estética en Kanban de Notion:** Asegurar que el nuevo título del proyecto en Notion no sobrepase 4 palabras para mantener el diseño limpio de una sola línea.

---

### Task 1: Actualización de `AGENTS.md` y `CONTRIBUTING.md`

**Files:**
- Modify: `c:\Users\DAVID\Desktop\WorksSpacesCoding\mcp-autocad\AGENTS.md`
- Modify: `c:\Users\DAVID\Desktop\WorksSpacesCoding\mcp-autocad\CONTRIBUTING.md`

**Interfaces:**
- Consumes: Identidad `autocad-mcp`.
- Produces: Directivas de gobernanza y guías de contribución alineadas con el nuevo nombre.

- [ ] **Step 1: Actualizar encabezado e identificador en `AGENTS.md`**
  - Cambiar `Proyecto: **mcp-autocad**` por `Proyecto: **autocad-mcp** (AutoCAD MCP Server & Gateway)`.
- [ ] **Step 2: Actualizar `CONTRIBUTING.md`**
  - Cambiar título a `# Guía de Contribución a AutoCAD MCP (autocad-mcp)`.
  - Actualizar comandos de clonado: `git clone https://github.com/tu-usuario/autocad-mcp.git` y `cd autocad-mcp`.
- [ ] **Step 3: Verificar que no queden referencias obsoletas en ambos archivos**

---

### Task 2: Actualización Integral de `README.md`

**Files:**
- Modify: `c:\Users\DAVID\Desktop\WorksSpacesCoding\mcp-autocad\README.md`

**Interfaces:**
- Consumes: Estructura del README actual y nueva marca `autocad-mcp`.
- Produces: README exhaustivo, moderno y profesional con branding `AutoCAD MCP Server & Gateway` (`autocad-mcp`).

- [ ] **Step 1: Actualizar el título principal, insignias y descripción inicial**
  - Título: `# AutoCAD MCP — AI Gateway & MCP Server Integration`.
  - Badges y enlaces alineados a `autocad-mcp`.
- [ ] **Step 2: Actualizar la sección de inicio rápido y configuración de clientes IA**
  - Actualizar comandos `git clone` y rutas de configuración MCP para Antigravity (`agy mcp add autocad-mcp ...`) y Claude Desktop / Cursor.
- [ ] **Step 3: Verificar ortografía, enlaces relativos y diagramas de arquitectura**

---

### Task 3: Actualización del Proyecto en Notion (`Projects`)

**Files:**
- Modify via Notion MCP: Page ID `4104ccedb56b81d598f9ef4c8d3a7487` en la base de datos `Projects` (`collection://74d4cced-b56b-832c-8006-07f8b69995d6`).

**Interfaces:**
- Consumes: Propiedades actuales de la página del proyecto en Notion.
- Produces: Página de proyecto actualizada con nombre oficial `AutoCAD MCP Server`, manteniendo icono `icons/package_gray` y preservando todas las tareas vinculadas.

- [ ] **Step 1: Preparar payload de actualización con `notion-update-page`**
  - `page_id`: `4104ccedb56b81d598f9ef4c8d3a7487`
  - `properties`:
    - `Project Name`: `AutoCAD MCP Server` (título limpio de 3 palabras)
    - `Description`: `Integración bidireccional entre Modelos de Inteligencia Artificial y Autodesk AutoCAD 2027 / Civil 3D mediante MCP v2.2 y Gateway C# en .NET 10 (autocad-mcp).`
- [ ] **Step 2: Ejecutar `notion-update-page` y verificar la respuesta**
- [ ] **Step 3: Validar que el rollup de `Progress` y las 6 tareas vinculadas permanezcan intactos**

---

### Task 4: Verificación de Solución y Compilación

**Files:**
- Read/Verify: `AutoCAD_Integration.slnx`

**Interfaces:**
- Consumes: Código C# en `AutoCAD_AI_Gateway` y `AutoCAD_MCP_Server`.
- Produces: Binarios Release verificados con 0 errores.

- [ ] **Step 1: Ejecutar compilación en Release**
  - Comando: `dotnet build AutoCAD_Integration.slnx -c Release`
  - Expected: `0 Errores`, `0 Advertencias`.
- [ ] **Step 2: Documentar el resultado final de la transición**
