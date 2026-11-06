# Documentación Exhaustiva de las 43 Herramientas MCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crear una suite documental técnica completa, exhaustiva y pedagógica de las 43 herramientas del servidor MCP (`autocad-mcp`), detallando para cada herramienta su contrato de parámetros, validaciones, respuestas JSON de ejemplo, mecanismo interno de ejecución (memoria local, AutoLISP `entmake`, comandos no modales o REST Gateway) y una guía rápida (Cheat Sheet) de selección para asistentes de IA y desarrolladores.

**Architecture:** Se creará una documentación de referencia técnica profunda en `docs/TOOLS_REFERENCE.md` con tablas completas de esquemas JSON y ejemplos de llamada y retorno para cada una de las 43 herramientas organizadas en sus 7 módulos. Se complementará con una guía rápida orientada a prompts (`docs/TOOLS_CHEAT_SHEET.md`) que permita a agentes de IA y usuarios humanos identificar de inmediato qué herramienta invocar según la intención de diseño.

**Tech Stack:** Model Context Protocol (MCP v2.2), Markdown (GFM), C# / .NET 10, AutoCAD 2027 ObjectARX / AutoLISP, Autodesk Civil 3D 2027 API (`AeccDbMgd`).

**Spec:** Solicitud del usuario para documentar de forma integral y profunda todas las herramientas creadas en el servidor MCP (`autocad-mcp`).

## Global Constraints

- Documentar exactamente las 43 herramientas activas y verificadas en `AutoCAD_MCP_Server`.
- Estandarizar todas las coordenadas, dimensiones y parámetros espaciales en **metros** (`INSUNITS = 4`).
- Reflejar con total fidelidad los nombres exactos en código C# (ej. `insertar_cajetin_parametrico` sin tildes, conforme a la regex MCP `^[A-Za-z0-9_.-]{1,128}\z`).
- Explicitar el mecanismo de ejecución de cada herramienta:
  - *Cálculo analítico local:* en el proceso del servidor MCP (sin requerir CAD abierto).
  - *Inyección AutoLISP `entmake`:* atómica, segura, independiente de OSNAP y sin alterar capas activas.
  - *Comandos no modales `_.-`:* canalizados por la cola concurrente y ejecutados en `Application.Idle`.
  - *Lectura/Servicio REST:* endpoints transaccionales en `http://localhost:5055/`.
- Mantener los nombres de propiedades y campos en español neutro profesional, coherente con el código fuente.

## Review Focus

1. **Discrepancia entre firmas de C# y documentación:** Comprobar que cada parámetro documentado coincida exactamente en nombre, tipo de dato (`string`, `double`, `int`, `bool`), valor por defecto y obligatoriedad con el código en `AutoCAD_MCP_Server/Tools/`.
2. **Ejemplos JSON-RPC inválidos:** Asegurar que cada ejemplo de llamada (`tools/call`) y retorno (`content[0].text`) sea JSON válido y contenga valores realistas de ingeniería métrica.
3. **Casos de error y límites no documentados:** Indicar explícitamente qué sucede cuando se pasa un radio negativo, una capa inexistente, coordenadas colineales o cuando Civil 3D no está activo.
4. **Mapeo de intenciones ambiguas:** Proporcionar en el Cheat Sheet soluciones para preguntas frecuentes de usuarios e IA (ej. ¿cuándo usar `dibujar_linea` vs `ejecutar_comando_autocad` vs `ejecutar_lisp`?).
5. **Enlaces cruzados rotos:** Validar que los enlaces en `README.md`, `docs/GETTING_STARTED.md` y `docs/ARCHITECTURE.md` apunten de forma consistente a la nueva documentación.

---

### Task 1: Documentación Exhaustiva y Contratos de las 43 Herramientas (`docs/TOOLS_REFERENCE.md`)

**Files:**
- Modify: `docs/TOOLS_REFERENCE.md`

**Interfaces:**
- Consumes: Código fuente y firmas de `AutoCAD_MCP_Server/Tools/*.cs` (`ContextTools`, `DrawingTools`, `LayerTools`, `MeasurementTools`, `ExecutionTools`, `PlottingTools`, `Civil3DTools`).
- Produces: Manual de referencia técnica definitivo con esquemas JSON completos y ejemplos de entrada/salida.

- [ ] **Step 1: Auditar y mapear las 43 herramientas contra el código fuente**
  - Listar los 7 módulos y verificar nombres de métodos, parámetros, tipos y atributos `[McpServerTool]`.
- [ ] **Step 2: Redactar la especificación técnica profunda por cada herramienta**
  - Cada herramienta incluirá:
    1. Propósito y caso de uso técnico.
    2. Mecanismo de ejecución interna (Local / AutoLISP `entmake` / `_.-` / REST Gateway).
    3. Tabla de Parámetros de Entrada (Nombre, Tipo C#, Tipo JSON, Requerido/Opcional, Valor por Defecto, Descripción y Reglas de Validación).
    4. Esquema y Ejemplo de Retorno JSON (Campos de éxito y estructura de error).
    5. Ejemplo real de Invocación MCP (`tools/call`).
    6. Consideraciones y advertencias operativas (efectos colaterales, regeneración, dependencias de Civil 3D).
- [ ] **Step 3: Validar que no falte ninguna de las 43 herramientas y guardar el archivo**

---

### Task 2: Guía Rápida y Árbol de Selección para Prompts de IA (`docs/TOOLS_CHEAT_SHEET.md`)

**Files:**
- Create: `docs/TOOLS_CHEAT_SHEET.md`

**Interfaces:**
- Consumes: Catálogo de herramientas documentado en Task 1.
- Produces: Guía de decisión rápida para desarrolladores, prompts de agentes y flujos de trabajo.

- [ ] **Step 1: Diseñar la matriz rápida de decisión por intención de usuario**
  - "¿Qué deseas hacer en AutoCAD?" $\rightarrow$ "Herramienta recomendada" $\rightarrow$ "Argumentos mínimos".
  - Casos: Dibujar geometría, Gestionar capas, Medir o calcular, Consultar el plano, Trazar planos / layouts / PDF, Operaciones de Civil 3D, Mantenimiento y control.
- [ ] **Step 2: Definir patrones de encadenamiento de herramientas (Workflows estándar)**
  - Flujo 1: Inicio de proyecto nuevo (`configurar_entorno_dibujo` $\rightarrow$ `crear_capa` $\rightarrow$ `dibujar_*` $\rightarrow$ `zoom_extension`).
  - Flujo 2: Generación de lámina técnica (`crear_layout` $\rightarrow$ `configurar_hoja_layout` $\rightarrow$ `crear_viewport` $\rightarrow$ `insertar_cajetin_parametrico` $\rightarrow$ `exportar_pdf`).
  - Flujo 3: Topografía e ingeniería vial (`listar_superficies_civil3d` $\rightarrow$ `consultar_perfil_longitudinal` $\rightarrow$ `obtener_coordenada_en_estacion`).
- [ ] **Step 3: Documentar mejores prácticas para formular prompts a la IA con estas tools**

---

### Task 3: Sincronización de Enlaces en Documentos Centrales (`README.md` y `docs/GETTING_STARTED.md`)

**Files:**
- Modify: `README.md`
- Modify: `docs/GETTING_STARTED.md`

**Interfaces:**
- Consumes: Rutas `docs/TOOLS_REFERENCE.md` y `docs/TOOLS_CHEAT_SHEET.md`.
- Produces: Repositorio cohesionado con navegación fluida y enlaces directos.

- [ ] **Step 1: Actualizar tabla de documentación en `README.md`**
  - Añadir enlace directo a `docs/TOOLS_CHEAT_SHEET.md` (Cheat Sheet y Guía Rápida).
- [ ] **Step 2: Actualizar sección de herramientas en `docs/GETTING_STARTED.md`**
  - Incorporar enlace hacia la referencia exhaustiva y la guía rápida.
- [ ] **Step 3: Verificar que todos los enlaces relativos funcionen y no existan referencias rotas**

---

### Task 4: Verificación Integral y Registro en Notion

**Files:**
- Verify: `AutoCAD_Integration.slnx`
- Register via Notion MCP: Base de datos `Tasks` (`collection://d204cced-b56b-8382-8a48-078553efe16e`).

**Interfaces:**
- Consumes: Artefactos documentales completados.
- Produces: Solución validada y tarea registrada en Notion (`Documentación 43 Tools MCP`).

- [ ] **Step 1: Ejecutar verificación de compilación Release de la solución**
  - `dotnet build AutoCAD_Integration.slnx -c Release` $\rightarrow$ Validar `0 Errores`.
- [ ] **Step 2: Registrar la tarea en Notion**
  - `Task Name`: `Documentación 43 Tools MCP` (4 palabras).
  - `Status`: `Done`.
  - `Workstream`: `Documentation`.
  - `Project`: `AutoCAD MCP Server` (ID: `4104ccedb56b81d598f9ef4c8d3a7487`).
  - `Icon`: `icons/checkmark_gray`.
- [ ] **Step 3: Crear el artefacto walkthrough y presentar el resultado al usuario**
