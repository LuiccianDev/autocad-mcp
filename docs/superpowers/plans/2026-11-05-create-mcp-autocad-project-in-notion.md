# Plan de Implementación: Registro del Proyecto `mcp-autocad` en Notion

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Registrar el proyecto real del repositorio (**AutoCAD 2027 & Civil 3D AI Gateway + MCP Server**) en la base de datos `Projects` de Notion y vincular su desglose de tareas técnicas en la base de datos `Tasks`, estableciendo prioridades, corrientes de trabajo (*workstreams*), criterios de aceptación (*Definition of Done*) y estados de avance reales.

**Architecture:** 
1. Creación del registro en `Projects` (`collection://74d4cced-b56b-832c-8006-07f8b69995d6`) vía `notion-create-pages`.
2. Obtención de la URL/ID del proyecto recién creado.
3. Creación iterativa de las tareas en `Tasks` (`collection://d204cced-b56b-8382-8a48-078553efe16e`), enlazando cada una al proyecto en la propiedad de relación `Project`.
4. Verificación del cálculo automático de progreso (`Progress` rollup) en Notion.

**Tech Stack:** Notion MCP Server (`notion_notion`), Model Context Protocol, JSON API.

**Spec:** Directivas de la skill `notion-pm` y esquema verificado de la página `Project Manager Luiccian` (`ada4ccedb56b82e8b3e881ccd979f114`).

## Global Constraints

- Cumplimiento estricto del esquema de Notion: respetar opciones de select (`Project Type: Software`, `Domain: Software / IT`, `Health: On track`, `Status: In progress`).
- Calidad de tareas: Cada tarea debe tener `Task Name` accionable, relación `Project`, `Workstream`, `Priority` y `Definition of Done`.
- Seguridad: No modificar ni borrar los proyectos de plantilla preexistentes (`Launching a New Product`, `Increasing Online Sales`, etc.).

## Review Focus

1. Verificar que el proyecto se cree con sus tipos y opciones exactos sin errores de validación.
2. Confirmar que las 6 tareas queden enlazadas al proyecto en la relación `Project`.
3. Validar que las tareas reflejen el estado real del trabajo realizado en el repositorio.

---

### Task 1: Crear el Registro Maestro del Proyecto en Notion

**Files:**
- Destino: Notion `Projects` (`collection://74d4cced-b56b-832c-8006-07f8b69995d6`)

- [ ] **Step 1: Crear la página de proyecto mediante `notion-create-pages`**
  - `Project Name`: "AutoCAD 2027 & Civil 3D AI Gateway + MCP Server"
  - `Project Type`: "Software"
  - `Domain`: "Software / IT"
  - `Status`: "In progress"
  - `Health`: "On track"
  - `Description`: "Integración bidireccional entre Modelos de Inteligencia Artificial y Autodesk AutoCAD 2027 / Civil 3D mediante MCP v2.2 y Gateway C# en .NET 10."
  - `Success Criteria`: "27 herramientas MCP operativas en Release, comunicación HTTP segura en puerto 5055, soporte para dibujo métrico, Civil 3D, AutoLISP y ploteo a PDF."

- [ ] **Step 2: Obtener la URL/ID del proyecto creado**
  Confirmar que la respuesta contiene la URL canónica (ej. `https://app.notion.com/p/...`).

---

### Task 2: Crear el Backlog de Tareas Técnicas Vinculadas

**Files:**
- Destino: Notion `Tasks` (`collection://d204cced-b56b-8382-8a48-078553efe16e`)

- [ ] **Step 1: Crear Tarea 1 - Compilación de binarios Release (.NET 10)**
  - `Task Name`: "Compilación y verificación de Gateway y MCP en Release (.NET 10)"
  - `Status`: "Done"
  - `Priority`: "High"
  - `Workstream`: "Technical"
  - `Definition of Done`: "Solución AutoCAD_Integration.slnx compilada con 0 errores en .NET 10."
  - `Project`: [URL del proyecto]

- [ ] **Step 2: Crear Tarea 2 - Herramientas de AutoLISP dinámico**
  - `Task Name`: "Implementación de herramientas de inyección AutoLISP (ejecutar_lisp y cargar_archivo_lisp)"
  - `Status`: "Done"
  - `Priority`: "High"
  - `Workstream`: "Technical"
  - `Definition of Done`: "Tools registradas en ExecutionTools.cs permitiendo evaluar expresiones en memoria y cargar archivos .lsp."
  - `Project`: [URL del proyecto]

- [ ] **Step 3: Crear Tarea 3 - Módulo de Presentaciones, Cajetín y Ploteo PDF**
  - `Task Name`: "Suite de Layouts, ventanas gráficas (MVIEW), cajetín paramétrico y exportación PDF"
  - `Status`: "Done"
  - `Priority`: "Medium"
  - `Workstream`: "Technical"
  - `Definition of Done`: "PlottingTools.cs implementado con soporte de hojas A0-A4, escalas métricas 1000/Escala XP y DWG To PDF.pc3."
  - `Project`: [URL del proyecto]

- [ ] **Step 4: Crear Tarea 4 - Visión por captura de pantalla**
  - `Task Name`: "Captura de pantalla del lienzo de AutoCAD para validación visual de la IA"
  - `Status`: "Done"
  - `Priority`: "Medium"
  - `Workstream`: "Technical"
  - `Definition of Done`: "Endpoint /context/screenshot y tool capturar_pantalla_autocad operativos generando imágenes PNG en disco."
  - `Project`: [URL del proyecto]

- [ ] **Step 5: Crear Tarea 5 - Pruebas de inyección en vivo con NETLOAD**
  - `Task Name`: "Pruebas de inyección y carga de AutoCAD_AI_Gateway.dll en AutoCAD 2027 mediante NETLOAD"
  - `Status`: "In progress"
  - `Priority`: "Urgent"
  - `Workstream`: "Review / QA"
  - `Definition of Done`: "Servidor iniciado en http://localhost:5055/ y comandos de prueba recibidos exitosamente en el hilo UI."
  - `Project`: [URL del proyecto]

- [ ] **Step 6: Crear Tarea 6 - Validación de diseño vial en Civil 3D**
  - `Task Name`: "Validación de consultas de alineamientos, superficies TIN y progresivas en Civil 3D"
  - `Status`: "To Review"
  - `Priority`: "High"
  - `Workstream`: "Technical"
  - `Definition of Done`: "Tools de Civil3DTools probadas con dibujo topográfico activo en Civil 3D 2027."
  - `Project`: [URL del proyecto]

---

### Task 3: Verificación y Resumen Ejecutivo en Notion

- [ ] **Step 1: Consultar el proyecto creado con sus tareas hijas**
  Ejecutar `notion-query-data-sources` para validar que el `Progress` rollup calcula el avance adecuado y las relaciones están intactas.
- [ ] **Step 2: Presentar el informe ejecutivo final al usuario**
  Mostrar la ficha técnica del proyecto en Notion con su enlace directo.
