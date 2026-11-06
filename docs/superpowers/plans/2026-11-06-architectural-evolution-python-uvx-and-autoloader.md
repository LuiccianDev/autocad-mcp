# Evolución Arquitectónica: Distribución Python con `uvx`, Autoloader y Mejoras Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transformar la experiencia de usuario y distribución de `autocad-mcp` para alcanzar paridad con el flujo *Zero-Config* de `vulcan-mcp` (`uvx vulcan-mcp`). Se diseñará un cliente MCP en Python distribuible mediante `uvx autocad-mcp`, un paquete Autoloader de Autodesk (`.bundle`) para eliminar la necesidad de ejecutar `NETLOAD` manualmente en AutoCAD, y una suite de nuevas sugerencias de alto impacto para el roadmap del proyecto.

**Architecture:** El ecosistema evoluciona hacia una **Arquitectura Híbrida de Dos Niveles**:
1. **Núcleo de Ejecución Nativo (C# / .NET 10):** Permanece como plugin en proceso dentro de `acad.exe` porque AutoCAD y Civil 3D requieren ObjectARX/.NET para acceder a la base de datos de dibujo sin latencia. Se empaqueta como Autodesk Autoloader (`.bundle`) para carga automática al iniciar AutoCAD.
2. **Capa MCP Dual:**
   - **Opción Python (`uvx autocad-mcp`):** Implementada con FastMCP/Python para que usuarios de Claude Desktop, Antigravity y Cursor lo configuren con una sola línea sin compilar .NET ni configurar rutas absolutas de DLL. Consume el Gateway HTTP en `http://localhost:5055/`.
   - **Opción C# (.NET 10):** Se mantiene para entornos empresariales o desarrolladores de .NET que prefieran binarios autocontenidos sin depender de Python.

**Tech Stack:** Python 3.10+, `uv` / `uvx`, FastMCP / MCP Python SDK, C# / .NET 10, Autodesk Application Plugins (`PackageContents.xml`), AutoCAD 2027 / Civil 3D 2027.

**Spec:** Consulta del usuario sobre la viabilidad de utilizar Python y `uvx` (al estilo de `LuiccianDev/vulcan-mcp`) frente a C#, eliminación de configuraciones complejas y nuevas sugerencias para robustecer el proyecto.

## Global Constraints

- El Gateway interno de AutoCAD **debe permanecer en C# / .NET 10**: AutoCAD 2027 solo ejecuta código nativo .NET/C++ en su proceso; Python no puede residir en el hilo de dibujo sin puentes externos.
- La capa Python MCP debe replicar las 43 herramientas existentes con exactamente los mismos nombres y tipos de retorno para no romper compatibilidad con clientes de IA.
- El paquete Autodesk Autoloader (`PackageContents.xml`) debe soportar tanto AutoCAD 2027 como Autodesk Civil 3D 2027 en versiones en español e inglés.
- El servidor Python debe ser ligero, con arranque instantáneo vía `uvx` y dependencia exclusiva de librerías estándar o cliente HTTP asíncrono (`httpx`).

## Review Focus

1. **Paridad de nombres y parámetros entre C# y Python:** Garantizar que las 43 herramientas expuestas por `uvx autocad-mcp` tengan idénticos identificadores que las de `AutoCAD_MCP_Server` en C#.
2. **Manejo de Gateway no disponible:** Si el usuario corre `uvx autocad-mcp` pero no ha abierto AutoCAD, el servidor debe responder con un mensaje amigable indicando que debe iniciar AutoCAD, sin generar excepciones de Python no controladas.
3. **Instalación limpia del Autoloader:** Asegurar que la carpeta `.bundle` se ubique en `%APPDATA%\Autodesk\ApplicationPlugins\` sin requerir permisos de Administrador de Windows.
4. **Tokens y variables de entorno:** Mantener la compatibilidad con `AUTOCAD_GATEWAY_TOKEN` en ambos servidores (Python y C#).
5. **Rendimiento de red:** Validar que las llamadas HTTP desde Python hacia `http://localhost:5055/` mantengan latencias inferiores a 5 ms en local.

---

### Task 1: Análisis Técnico Comparativo C# vs Python y Respuestas al Usuario

**Files:**
- Create: `docs/decisions/001-csharp-vs-python-mcp-architecture.md`

**Interfaces:**
- Consumes: Arquitectura actual de `autocad-mcp` y modelo de `vulcan-mcp`.
- Produces: Documento de decisión arquitectónica (ADR) con análisis de pros, contras y justificación de la arquitectura híbrida.

- [ ] **Step 1: Documentar por qué el Gateway interno debe ser C#**
  - Explicar que AutoCAD 2027 corre sobre .NET Core / .NET 10 en proceso (`acad.exe`), y que la API ObjectARX solo expone bindings para C++ y C#.
- [ ] **Step 2: Documentar las ventajas de ofrecer un servidor MCP Python con `uvx`**
  - Cero compilación para el usuario final, configuración de 1 línea en Claude Desktop/Cursor, auto-descarga de dependencias con `uv`.
- [ ] **Step 3: Sintetizar las recomendaciones estratégicas para el usuario**

---

### Task 2: Diseño e Implementación del Servidor MCP en Python (`clients/python/` o `autocad-mcp-py`)

**Files:**
- Create: `clients/python/pyproject.toml`
- Create: `clients/python/src/autocad_mcp/__init__.py`
- Create: `clients/python/src/autocad_mcp/server.py`
- Create: `clients/python/src/autocad_mcp/client.py`
- Create: `clients/python/README.md`

**Interfaces:**
- Consumes: Endpoints HTTP del Gateway en `http://localhost:5055/` (`/execute`, `/context/*`, `/civil/*`).
- Produces: Paquete Python compatible con `uvx autocad-mcp` que expone las 43 herramientas MCP.

- [ ] **Step 1: Crear `pyproject.toml` para distribución con `uv` / PyPI**
  - Definir entrypoint: `[project.scripts] autocad-mcp = "autocad_mcp.server:main"`.
  - Dependencias: `mcp>=1.0.0`, `httpx>=0.27.0`.
- [ ] **Step 2: Implementar el cliente HTTP Gateway (`client.py`)**
  - Métodos asíncronos para `get_context`, `execute_command`, `post_civil` con autenticación por token `X-Gateway-Token`.
- [ ] **Step 3: Implementar las herramientas en FastMCP (`server.py`)**
  - Registrar las 43 herramientas (Drawing, Layers, Measurements, Context, Civil 3D, Plotting, Control).
- [ ] **Step 4: Probar localmente la ejecución con `uv run` o `python -m autocad_mcp`**

---

### Task 3: Paquete Autodesk Autoloader (`AutoCAD_AI_Gateway.bundle`) para Carga Automática

**Files:**
- Create: `deploy/AutoCAD_AI_Gateway.bundle/PackageContents.xml`
- Create: `scripts/install-autoloader.ps1`
- Create: `scripts/uninstall-autoloader.ps1`

**Interfaces:**
- Consumes: Binario compilado `AutoCAD_AI_Gateway.dll`.
- Produces: Instalador de un solo clic que copia el bundle a `%APPDATA%\Autodesk\ApplicationPlugins\`.

- [ ] **Step 1: Crear la especificación `PackageContents.xml`**
  - Declarar componentes para `AutoCAD 2027` y `Civil 3D 2027` con `LoadOnAutoCADStartup="True"`.
- [ ] **Step 2: Crear el script de instalación en PowerShell (`install-autoloader.ps1`)**
  - Compila en Release y copia automáticamente la DLL y el manifiesto a la carpeta de plugins del usuario.
- [ ] **Step 3: Documentar el fin de la necesidad de teclear `NETLOAD` manualmente**

---

### Task 4: Catálogo de Nuevas Sugerencias Estratégicas para Robustecer el Proyecto

**Files:**
- Modify: `ROADMAP.md`
- Create: `docs/SUGGESTIONS.md`

**Interfaces:**
- Consumes: Visión de ingeniería civil, CAD y minería.
- Produces: Plan detallado de nuevas características de vanguardia.

- [ ] **Step 1: Detallar la propuesta de Generative UI / Visualización Web en Chat**
  - Renderizado inline de planos DWG mediante SVG interactivo o widgets HTML sin abrir AutoCAD.
- [ ] **Step 2: Detallar el módulo de Metrados y Cubicaciones Automáticas a Excel**
  - Extracción automática de tablas de corte/relleno de Civil 3D y recuentos de bloques a `.xlsx`.
- [ ] **Step 3: Detallar la integración interoperable con software minero/geológico (`vulcan-mcp` $\leftrightarrow$ `autocad-mcp`)**
  - Puente directo para transferir sólidos de mina, triangulaciones y sondajes desde Vulcan 12 hacia AutoCAD/Civil 3D.
- [ ] **Step 4: Actualizar `ROADMAP.md` con los nuevos hitos**

---

### Task 5: Verificación Integral y Registro en Notion

**Files:**
- Register via Notion MCP: Base de datos `Tasks` (`collection://d204cced-b56b-8382-8a48-078553efe16e`).

**Interfaces:**
- Consumes: Entregables del plan.
- Produces: Tarea registrada en Notion (`Evolución uvx y Autoloader`).

- [ ] **Step 1: Validar compilación C# y sintaxis de scripts y Python**
- [ ] **Step 2: Registrar la tarea en Notion**
- [ ] **Step 3: Generar artefacto walkthrough y responder al usuario**
