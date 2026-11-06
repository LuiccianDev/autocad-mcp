# Sincronización de API y Configuración entre AutoCAD AI Gateway, MCP Server C# y Python

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unificar de forma estricta los valores por defecto de autenticación (token `autocad-gateway-token-2027`), soporte de variables de entorno de red (`AUTOCAD_GATEWAY_URL` y `AUTOCAD_GATEWAY_PORT`), encabezados CORS y validar la paridad total de endpoints entre el plugin nativo `AutoCAD_AI_Gateway` (.NET 10), el servidor MCP C# (`AutoCAD_MCP_Server`) y el servidor MCP Python (`mcp-python`).

**Architecture:** Se homogeniza la capa de red y configuración en las tres piezas del ecosistema. El Gateway en AutoCAD, el cliente C# y el cliente Python adoptarán la misma cadena de fallback para `AUTOCAD_GATEWAY_TOKEN` ("autocad-gateway-token-2027") y `AUTOCAD_GATEWAY_URL` ("http://localhost:5055/"). Además, se añaden los encabezados CORS faltantes (`X-Gateway-Token`) en el Gateway y pruebas unitarias de regresión.

**Tech Stack:** C# (.NET 10, HttpListener, HttpClient), Python 3.11 (FastMCP, httpx, pytest), UV.

**Spec:** Directivas de integración de `AGENTS.md` y `SECURITY.md`, especificación REST del Gateway (17 endpoints) y catálogo de 43 herramientas MCP.

## Global Constraints

- Compilar C# siempre en `Release` sobre .NET 10 (`dotnet build -c Release AutoCAD_Integration.slnx`).
- Mantener compatibilidad total con unidades métricas en metros (`INSUNITS = 4`).
- No romper las firmas de las 43 herramientas expuestas en C# ni en Python.
- El servidor MCP en C# debe mantener silencio estricto en `Console.Out` para proteger las tramas JSON-RPC.
- Seguir las directivas de `notion-pm` (títulos concisos de 2-4 palabras, icono gris oficial `icons/checkmark_gray`).

## Review Focus

1. **Token por Defecto Discordante:** Si el Gateway esperaba `"pa14-autocad-token-casmas-2026"` y Python enviaba `"autocad-gateway-token-2027"`, una sesión sin variables de entorno fallaba con HTTP 401. Debe resolverse al 100%.
2. **Ignorar `AUTOCAD_GATEWAY_URL`:** Si el usuario cambiaba el puerto en su archivo de configuración, los clientes C# y Python ignoraban la variable y seguían enviando a `5055`. Debe ser configurable dinámicamente.
3. **CORS Headers Preflight:** Si una petición OPTIONS del navegador no permitía `X-Gateway-Token`, fallaba la llamada. Debe incluirse en `Access-Control-Allow-Headers`.
4. **Paridad de Rutas:** Garantizar que los 17 endpoints invocados por C# y Python existan con el mismo método y path en `RequestRouter.cs`.
5. **Estabilidad de Compilación:** La solución `.slnx` y los tests de `pytest` deben ejecutarse con 0 errores y 0 fallos.

---

### Task 1: Unificación del Token de Seguridad por Defecto

**Files:**
- Modify: `AutoCAD_AI_Gateway/src/Server/RequestRouter.cs:42-48`
- Modify: `AutoCAD_MCP_Server/Program.cs:18-23`
- Modify: `mcp-python/src/autocad_mcp/client.py:20-22`
- Test: `mcp-python/tests/test_server_tools.py`

- [ ] **Step 1:** Modificar `RequestRouter.cs` para reemplazar el token de fallback heredado `"pa14-autocad-token-casmas-2026"` por `"autocad-gateway-token-2027"`.
- [ ] **Step 2:** Modificar `AutoCAD_MCP_Server/Program.cs` para reemplazar el token de fallback `"pa14-autocad-token-casmas-2026"` por `"autocad-gateway-token-2027"`.
- [ ] **Step 3:** Asegurar que `mcp-python/src/autocad_mcp/client.py` use `"autocad-gateway-token-2027"` como fallback tanto en `__init__` como en `_headers()`.
- [ ] **Step 4:** Verificar que no queden referencias residuales a `"pa14-autocad-token-casmas-2026"` en el código fuente.

---

### Task 2: Configuración Dinámica de URL y Puerto de Red

**Files:**
- Modify: `AutoCAD_AI_Gateway/src/Commands/GatewayCommands.cs:14-36`
- Modify: `AutoCAD_MCP_Server/Program.cs:16-22`
- Modify: `mcp-python/src/autocad_mcp/client.py:15-22`
- Test: `mcp-python/tests/test_server_tools.py`

- [ ] **Step 1:** Modificar `GatewayCommands.cs` para que `StartServerCommand` resuelva la URL de escucha mediante:
  `string url = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? (Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT") is string p ? $"http://localhost:{p}/" : DefaultServerUrl);`
- [ ] **Step 2:** Modificar `AutoCAD_MCP_Server/Program.cs` para que `client.BaseAddress` lea `AUTOCAD_GATEWAY_URL`:
  `string baseUrl = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? "http://localhost:5055/";`
  `if (!baseUrl.EndsWith("/")) baseUrl += "/";`
  `client.BaseAddress = new Uri(baseUrl);`
- [ ] **Step 3:** Modificar `mcp-python/src/autocad_mcp/client.py` para que si `base_url` no es suministrado, lea `os.getenv("AUTOCAD_GATEWAY_URL", "http://localhost:5055/")`.

---

### Task 3: Encabezados CORS y Paridad de Rutas en el Gateway

**Files:**
- Modify: `AutoCAD_AI_Gateway/src/Server/HttpGatewayServer.cs:128-132`
- Test: `mcp-python/tests/test_server_tools.py`

- [ ] **Step 1:** Actualizar `HttpGatewayServer.cs` en la sección de cabeceras OPTIONS/CORS:
  `response.AppendHeader("Access-Control-Allow-Headers", "Content-Type, X-Gateway-Token");`
- [ ] **Step 2:** Validar que los 17 endpoints registrados en `RequestRouter.cs` (`/execute`, `/context/*`, `/civil/*`) coincidan exactamente con las llamadas en `AutoCAD_MCP_Server/Tools/` y `mcp-python/src/autocad_mcp/server.py`.

---

### Task 4: Pruebas Automatizadas de Configuración y Compilación

**Files:**
- Modify: `mcp-python/tests/test_server_tools.py`
- Test: `mcp-python/tests/test_server_tools.py`
- Test: `AutoCAD_Integration.slnx`

- [ ] **Step 1:** Agregar pruebas unitarias en `test_server_tools.py` para verificar:
  - Lectura de `AUTOCAD_GATEWAY_URL` y fallback a `http://localhost:5055/`.
  - Lectura de `AUTOCAD_GATEWAY_TOKEN` y fallback a `autocad-gateway-token-2027`.
  - Header `X-Gateway-Token` generado correctamente.
- [ ] **Step 2:** Ejecutar `uv run pytest -v` en `mcp-python/` y verificar que el 100% de las pruebas pasen.
- [ ] **Step 3:** Compilar la solución .NET en Release (`dotnet build AutoCAD_Integration.slnx -c Release`) y confirmar 0 errores y 0 advertencias.

---

### Task 5: Documentación y Registro en Notion

**Files:**
- Modify: `CHANGELOG.md`
- Artifact: `walkthrough.md`

- [ ] **Step 1:** Registrar los cambios en `CHANGELOG.md` bajo `[Unreleased]`.
- [ ] **Step 2:** Actualizar el walkthrough con la verificación de alineación de API y configuración.
- [ ] **Step 3:** Con confirmación del usuario, registrar la tarea correspondiente en Notion (`Project Manager Luiccian`).
