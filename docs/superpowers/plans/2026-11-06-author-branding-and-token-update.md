# Estandarización de Autoría (Luiccian), Token Personalizado y Conexión de Clientes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Asignar la autoría y créditos oficiales del proyecto a **Luiccian** (`LuiccianDev`), actualizar el token por defecto a `luiccian-autocad-token-2027` (y el generador a prefijo `luiccian-`), y enriquecer la documentación y esquemas de conexión para clientes IA (Claude Desktop, Cursor, Antigravity, VS Code).

**Architecture:** Se actualizarán los metadatos de autoría en ensamblados C# (.NET 10), paquete Python (`pyproject.toml`), licencia MIT y banners de AutoCAD. Se reemplazará la cadena de fallback del token en Gateway, C#, Python y scripts por la identidad `luiccian-*`. Se mejorarán exhaustivamente los esquemas y guías de conexión para todos los clientes MCP compatibles.

**Tech Stack:** C# (.NET 10), Python 3.11 (FastMCP, Hatchling), PowerShell, Markdown.

**Spec:** Requerimientos de marca de autoría de Luiccian, especificación MCP v2.2 y directivas de seguridad.

## Global Constraints

- No alterar las firmas ni lógica de las 43 herramientas de dibujo, capas, mediciones y Civil 3D.
- Compilar la solución en Release sobre .NET 10 (`AutoCAD_AI_Gateway`).
- Mantener silencio en `stdout` en el servidor MCP C#.
- Mantener el 100% de los tests en pytest pasando con el nuevo token por defecto `luiccian-autocad-token-2027`.

## Review Focus

1. **Tokens Desincronizados:** Asegurar que tanto el Gateway, el cliente C#, el cliente Python, los scripts y las configuraciones de ejemplo utilicen exactamente `luiccian-autocad-token-2027`.
2. **Tokens Generados Aleatorios:** `AI_GENERATE_TOKEN` y `set_gateway_token.ps1` deben generar tokens con el prefijo `luiccian-` (ej. `luiccian-a1b2c3...`).
3. **Consistencia de Autoría:** Todo el proyecto (`LICENSE`, `pyproject.toml`, `.csproj`, `README.md`, `Main.cs`) debe atribuir el crédito a **Luiccian / LuiccianDev**.
4. **Claridad en Conexión:** Las guías deben proveer configuración de copiar-y-pegar para Claude Desktop, Cursor y Antigravity en ambas opciones (`uvx` y `.NET 10`).

---

### Task 1: Actualización de Identidad de Tokens (`luiccian-*`)

**Files:**
- Modify: `AutoCAD_AI_Gateway/src/Server/HttpGatewayServer.cs`
- Modify: `AutoCAD_AI_Gateway/src/Commands/GatewayCommands.cs`
- Modify: `AutoCAD_MCP_Server/Program.cs`
- Modify: `mcp-python/src/autocad_mcp/client.py`
- Modify: `scripts/set_gateway_token.ps1`
- Modify: `examples/configs/claude_desktop_config.json`
- Modify: `examples/configs/mcp_config.json`
- Test: `mcp-python/tests/test_server_tools.py`

- [ ] **Step 1:** Actualizar fallback en `HttpGatewayServer.cs` a `"luiccian-autocad-token-2027"`.
- [ ] **Step 2:** Actualizar prefijo del generador en `GatewayCommands.cs` (`AI_GENERATE_TOKEN`) a `"luiccian-" + Guid.NewGuid().ToString("N")`.
- [ ] **Step 3:** Actualizar fallback en `AutoCAD_MCP_Server/Program.cs` a `"luiccian-autocad-token-2027"`.
- [ ] **Step 4:** Actualizar fallback en `mcp-python/src/autocad_mcp/client.py` a `"luiccian-autocad-token-2027"`.
- [ ] **Step 5:** Actualizar generador en `scripts/set_gateway_token.ps1` con prefijo `"luiccian-"`.
- [ ] **Step 6:** Actualizar `test_server_tools.py` para validar `luiccian-autocad-token-2027` y ejecutar `uv run pytest -v`.

---

### Task 2: Atribución de Autoría y Créditos Oficiales (Luiccian / LuiccianDev)

**Files:**
- Modify: `LICENSE`
- Modify: `mcp-python/pyproject.toml`
- Modify: `AutoCAD_AI_Gateway/AutoCAD_AI_Gateway.csproj`
- Modify: `AutoCAD_MCP_Server/AutoCAD_MCP_Server.csproj`
- Modify: `AutoCAD_AI_Gateway/src/Main.cs`

- [ ] **Step 1:** Modificar `LICENSE` con `Copyright (c) 2026 Luiccian (LuiccianDev)`.
- [ ] **Step 2:** Actualizar `pyproject.toml` con URL oficial del repositorio (`https://github.com/LuiccianDev/autocad-mcp`), autor `Luiccian` y correo de contacto.
- [ ] **Step 3:** Agregar `<Authors>Luiccian</Authors>` y `<Company>LuiccianDev</Company>` en ambos `.csproj`.
- [ ] **Step 4:** Actualizar banner de bienvenida en `Main.cs` con la atribución a Luiccian.

---

### Task 3: Enriquecimiento de Documentación y Guías de Conexión

**Files:**
- Modify: `README.md`
- Modify: `docs/GETTING_STARTED.md`
- Modify: `SECURITY.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1:** Actualizar `README.md` con enlaces oficiales de GitHub de `LuiccianDev/autocad-mcp`, sección de autoría de Luiccian y matrices de conexión visuales.
- [ ] **Step 2:** Enriquecer `docs/GETTING_STARTED.md` con guías paso a paso para Claude Desktop, Cursor, VS Code y Antigravity.
- [ ] **Step 3:** Actualizar `SECURITY.md` con el nuevo token y directivas.
- [ ] **Step 4:** Registrar novedades en `CHANGELOG.md`.

---

### Task 4: Verificación Integral de Compilación y Suites de Pruebas

**Files:**
- Test: `AutoCAD_AI_Gateway/AutoCAD_AI_Gateway.csproj`
- Test: `mcp-python/tests/`

- [ ] **Step 1:** Compilar `AutoCAD_AI_Gateway` en modo Release (`dotnet build -c Release`).
- [ ] **Step 2:** Ejecutar `uv run pytest -v` en `mcp-python/` asegurando 9 de 9 tests aprobados.
- [ ] **Step 3:** Actualizar el artefacto `walkthrough.md` y presentar resumen al usuario.
