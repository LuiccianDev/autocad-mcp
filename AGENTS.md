# AGENTS.md — Rules for AI Assistants

Project: **autocad-mcp** (AutoCAD 2027 & Civil 3D AI Gateway + MCP Server).

---

## 0. Language, Aesthetics & Documentation Policy

- **Repository Language:** All official repository documentation, code comments, pull requests, issue templates, workflows, and git commits **MUST** be written in **Technical English**.
- **User Interaction:** When interacting with the user, the assistant should reply in **Spanish** (or the user's preferred language) while preserving technical names in English and maintaining the formal engineering tone.
- **Strict Aesthetic Standard (Zero Informal Emojis):** Prohibit informal emojis (e.g., 📚, 🚀, ⚡, 📦, 📋, 🛠️, 🔌, 🤖, 🧪) in markdown documentation, section headers, release notes, issue templates, and commit messages. Maintain a sober engineering format utilizing markdown tables and GitHub alerts (`> [!NOTE]`, `> [!TIP]`, `> [!IMPORTANT]`).

---

## 1. Entorno y Compilación (.NET 10 & Python)

- Todo el código backend en C# está escrito sobre **.NET 10**.
- Compilación siempre en modo `Release`:
  ```powershell
  dotnet build AutoCAD_Integration.slnx -c Release
  # O mediante el script oficial:
  .\scripts\build.ps1
  ```
- **Prohibido** romper el transporte STDIO del servidor MCP: nunca agregar `Console.WriteLine` o logs a `stdout` dentro de `AutoCAD.McpServer`, ya que corrompe las tramas JSON-RPC. Usar exclusivamente `Console.Error.WriteLine` (stderr).
- Servidor MCP Python ubicado en `AutoCAD.McpPython`:
  ```powershell
  uv run --directory AutoCAD.McpPython pytest
  ```

---

## 2. Arquitectura de Dos Capas y Uso Conjunto Obligatorio

El proyecto funciona mediante piezas complementarias diseñadas para operar **en conjunto**:

1. **AutoCAD.Gateway (Plugin Nativo AutoCAD):**
   - Corre dentro de `acad.exe` tras ejecutarse `NETLOAD`.
   - DLL generada: `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`.
   - Expone endpoints REST en `http://localhost:5055/`.
   - Seguridad: encabezado `X-Gateway-Token` (token por defecto o vía variable `AUTOCAD_GATEWAY_TOKEN`).
   - Todos los comandos se inyectan en el hilo UI mediante `Application.Idle` y cola concurrente (`ConcurrentQueue<string>`).

2. **AutoCAD.McpServer (Servidor MCP STDIO .NET 10) / AutoCAD.McpPython (Python UVX):**
   - Servidor independiente que implementa el estándar Model Context Protocol (v2.2).
   - DLL generada: `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.dll`.
   - Consume el Gateway vía HTTP y expone herramientas (`ContextTools`, `ExecutionTools`, `Civil3DTools`).
   - Abstrae y estructura el JSON para que la IA disponga de herramientas tipadas directas, eliminando la necesidad de interactuar a ciegas con scripts manuales de PowerShell y parseos crudos (RAW).

### Directiva Obligatoria para Asistentes y Agentes de IA:
- **Uso Conjunto Indispensable:** Todo agente debe instruir o asegurar la configuración de **ambos componentes** (cargar `AutoCAD.Gateway.dll` vía `NETLOAD` en AutoCAD y registrar el MCP Server en la configuración del cliente de IA).
- **Vía Primaria Recomendada (Tier 1 - Zero-Install con `uvx`):** Al asistir a usuarios o configurar el entorno, priorizar siempre el método rápido de 2 pasos con `uvx luiccian-autocad-mcp`. Las alternativas de binarios compilados en C# (.NET 10) y desarrollo local con `uv run` deben presentarse como opciones secundarias avanzadas.
- **Prohibido trabajar a ciegas con PowerShell:** La IA no debe improvisar comandos de PowerShell contra el Gateway si el servidor MCP está disponible. El MCP garantiza tipado, validación y respuestas estructuradas sin saturar el contexto con respuestas HTTP en crudo.
- **Asistencia al Contribuyente:** Si el usuario o un contribuyente solicita configurar o conectar el proyecto, el asistente debe guiar de inmediato la puesta en marcha de los **dos pasos** (Gateway cargado en CAD + MCP configurado en el cliente).

---

## 3. Estándar de Dibujo y Comandos en CAD

- **Unidades Métricas:** Trabajar siempre en metros (`INSUNITS = 4`).
- **Modo Texto No Modal:** Utilizar siempre prefijo `_.-` para comandos interactivos (`_.-LAYER`, `_.-INSERT`, `_.-PLINE`) para evitar ventanas emergentes de Windows que bloqueen la sesión remota.
- **Terminadores de Línea:** Incluir `\n` al final de cada parámetro o comando.
- **Auto-Cancelación:** El Gateway inyecta `\x03\x03` (`ESC`/`Ctrl+C`) antes y después del comando.

---

## 4. Estándar Oficial de Nomenclatura y Jerarquía de Carpetas y Archivos

Para garantizar máxima limpieza, consistencia profesional y cero ambigüedad en todo el repositorio:

### 1. Estructura a Nivel Raíz (Clean Monorepo)
- **Componentes y Proyectos Principales:** Utilizan **`PascalCase` con puntos** y prefijo de identidad uniforme `AutoCAD.*`:
  - `AutoCAD.Gateway/` (C# .NET 10 - Plugin de AutoCAD)
  - `AutoCAD.McpServer/` (C# .NET 10 - Servidor MCP STDIO)
  - `AutoCAD.McpPython/` (Python - Servidor MCP alternativo)
- **Carpetas Estructurales y de Soporte:** Utilizan **`lowercase` (minúsculas puras)**:
  - `docs/` (documentación técnica y manuales)
  - `scripts/` (herramientas de automatización y shell)
  - `examples/` (archivos de configuración de ejemplo)
  - `.agents/` / `.github/` (configuraciones de agentes y CI/CD)

### 2. Proyectos y Código C# (.NET 10 - Microsoft Guidelines)
- **Proyectos y Namespaces:** `PascalCase` con puntos (`AutoCAD.Gateway`, `AutoCAD.McpServer`).
  - **Prohibición estricta:** No usar guiones bajos (`_`) ni guiones medios (`-`) en nombres de proyectos, archivos `.csproj` ni ensamblados.
- **Subcarpetas internas:** `PascalCase` (`Commands/`, `Core/`, `Server/`, `Services/`, `Models/`, `Tools/`).
  - Cada carpeta representa directamente su sub-namespace (`namespace AutoCAD.Gateway.Services;`).
  - **Sin subcarpetas intermedias:** No anidar carpetas `src/` redundantes dentro de proyectos individuales.
- **Archivos de código C#:** `PascalCase.cs` coincidiendo 1:1 con la clase principal (`HttpGatewayServer.cs`, `DrawingTools.cs`).
- **Interfaces C#:** Prefijo `I` obligatorio en `PascalCase` (`IAutoCADGatewayClient.cs`, `IAutoCADCommandDispatcher.cs`).

### 3. Código y Paquetes Python (PEP 8 & PyPI)
- **Archivos y Módulos Python:** `snake_case.py` (`cad_math.py`, `client.py`, `server.py`). Nunca usar PascalCase ni camelCase en nombres de archivos `.py`.
- **Carpetas de Paquete Importable:** `snake_case` (`src/autocad_mcp/`).
- **Nombre de Paquete en PyPI / CLI:** `kebab-case` (`name = "autocad-mcp"` en `pyproject.toml`).
- **Tests en Python:** `tests/test_*.py` en `snake_case` (`test_math.py`, `test_server_tools.py`).
- **Clases en Python:** `PascalCase` (`AutoCADClient`, `Point2D`).
- **Funciones y Variables:** `snake_case` (`get_drawing_info()`, `command_payload`).
- **Constantes:** `SCREAMING_SNAKE_CASE` (`DEFAULT_PORT`, `AUTOCAD_GATEWAY_TOKEN`).

### 4. Scripts y Archivos de Configuración
- **Scripts de Shell / PowerShell:** `kebab-case.ps1` (`build.ps1`, `start-mcp.ps1`, `set-gateway-token.ps1`). Prohibido mezclar guiones bajos en nombres de scripts.
- **Archivos de Configuración:** `snake_case.json` o `kebab-case.json` (`mcp_config.json`, `claude_desktop_config.json`).
- **Documentación Markdown:** `UPPERCASE.md` en raíz (`README.md`, `AGENTS.md`, `CHANGELOG.md`) y en `docs/` (`ARCHITECTURE.md`, `GETTING_STARTED.md`).

---

## 5. Optimización de Minutos y Cuota en GitHub Actions

Para proteger los minutos de cómputo y evitar ejecuciones innecesarias de runners en GitHub Actions:

1. **Exclusión de Archivos No Ejecutables (`paths-ignore`):**
   - Los workflows de CI (`ci.yml`) y CodeQL (`codeql.yml`) deben ignorar cualquier cambio que afecte exclusivamente a documentación (`**.md`, `docs/**`), ejemplos (`examples/**`), directivas de agentes (`.agents/**`), plantillas de issues/PR (`.github/ISSUE_TEMPLATE/**`, `.github/*.md`), y archivos de configuración git (`.gitignore`, `.gitattributes`, `.editorconfig`).

2. **Uso Obligatorio de `[skip ci]`:**
   - En commits puramente documentales, de notas de versión o de reglas que no requieran validación de compilación, incluir siempre la etiqueta `[skip ci]` o `[ci skip]` en el mensaje del commit para garantizar 0 consumo de minutos de GitHub Actions.

3. **Cancelación Automática de Trabajos Obsoletos (`concurrency`):**
   - Mantener siempre configurado `concurrency: cancel-in-progress: true` en todos los flujos de CI para que nuevos pushes a la misma rama cancelen de inmediato las ejecuciones en curso previas, evitando el consumo de minutos duplicados.

---

## 6. Política de Versionado y Mantenimiento del CHANGELOG.md

Para mantener estricta coherencia entre el repositorio, GitHub Releases y PyPI:

1. **Estándar SemVer y Keep a Changelog:**
   - Seguir rigurosamente [Keep a Changelog](https://keepachangelog.com/) y [Semantic Versioning 2.0.0](https://semver.org/).
   - Secciones permitidas: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.

2. **Convención de Versión en Desarrollo (`[Unreleased]`):**
   - Todos los cambios, optimizaciones y nuevas herramientas implementadas en la rama `master` que aún no hayan sido publicadas formalmente con un tag de git deben registrarse bajo el encabezado `## [X.Y.Z] - Unreleased`.
   - Queda prohibido marcar una fecha definitiva en el Changelog hasta que se cree el tag oficial de la versión correspondiente.

3. **Sincronización Atómica de Versiones:**
   - La versión activa del proyecto debe estar 100% sincronizada entre:
     - `AutoCAD.Gateway/AutoCAD.Gateway.csproj` (`<Version>`)
     - `AutoCAD.McpServer/AutoCAD.McpServer.csproj` (`<Version>`)
     - `AutoCAD.McpPython/pyproject.toml` (`version = "..."`)
     - `CHANGELOG.md` (`## [X.Y.Z] - YYYY-MM-DD`)
     - Tag de Git (`vX.Y.Z`).

4. **Redacción Concisa y Estructurada (Anti-Saturación):**
   - Las entradas de cambios deben ser concisas y técnicas: viñetas atómicas destacando el componente modificado y el beneficio o impacto funcional directo (máximo 2 a 3 líneas por viñeta).
   - Prohibido volcar logs en crudo, volcados JSON extensos, o párrafos narrativos informales en el changelog.

5. **Orden Cronológico Inverso Estricto y Fechas ISO:**
   - Las versiones se ordenan estrictamente de más reciente a más antigua (cronológico inverso: `[Unreleased]`, luego versión reciente con fecha, ej. `[2.4.0] - 2026-11-07`, `[2.3.0] - 2026-11-07`, etc.).
   - Encabezado formal obligatorio: `## [X.Y.Z] - YYYY-MM-DD` indicando año, mes y día de la publicación del tag.



