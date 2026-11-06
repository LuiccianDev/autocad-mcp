# Simplificación de Configuración: Número de Puerto (`AUTOCAD_GATEWAY_PORT`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplificar la configuración del cliente MCP y del Gateway para que el usuario solo tenga que especificar el número de puerto (ej. `"AUTOCAD_GATEWAY_PORT": "5055"`) en lugar de toda la URL con protocolo (`http://localhost:5055`), manteniendo la construcción automática de la URL local en C# y Python.

**Architecture:** Se unifica la prioridad de resolución de red dando preferencia al parámetro `AUTOCAD_GATEWAY_PORT` (por defecto `"5055"`). Tanto el Gateway de AutoCAD como los clientes C# y Python construirán automáticamente la URL canónica `http://localhost:<puerto>/`. Se actualizarán todos los archivos de configuración JSON de ejemplo, scripts y documentación para reflejar exclusivamente la variable limpia de puerto.

**Tech Stack:** C# (.NET 10), Python 3.11 (FastMCP, pytest), PowerShell, JSON.

**Spec:** Simplificación de experiencia de usuario (DX), directivas de configuración sin protocolos innecesarios.

## Global Constraints

- El puerto por defecto seguirá siendo `5055` si la variable `AUTOCAD_GATEWAY_PORT` se omite.
- Mantener compatibilidad residual con `AUTOCAD_GATEWAY_URL` en caso de que algún entorno avanzado la defina.
- Los clientes deben enviar `http://localhost:<puerto>/` automáticamente.
- Compilar en Release sobre .NET 10.
- Mantener 100% de los tests pasando en `pytest`.

## Review Focus

1. **Paridad de Fallback:** Si no se define ninguna variable, ambos clientes (C# y Python) y el Gateway deben conectarse al puerto `5055`.
2. **Puerto Numérico o String:** Python y C# deben aceptar puertos tanto numéricos como strings con espacios limpios (ej. `5055`, `"5055"` o `5060`).
3. **Limpieza en Configuraciones:** Ningún archivo de configuración (`claude_desktop_config.json`, `mcp_config.json`, snippets de PowerShell) debe requerir `http://localhost:5055`, usando únicamente `AUTOCAD_GATEWAY_PORT`.
4. **Pruebas de Regresión:** Actualizar `test_gateway_client_environment_overrides` en `test_server_tools.py` para asegurar que `AUTOCAD_GATEWAY_PORT` gobierna la URL base.

---

### Task 1: Adaptación del Cliente Python (`mcp-python/`)

**Files:**
- Modify: `mcp-python/src/autocad_mcp/client.py`
- Modify: `mcp-python/tests/test_server_tools.py`
- Test: `mcp-python/tests/test_server_tools.py`

- [ ] **Step 1:** Modificar el constructor de `AutoCADGatewayClient` en `client.py` para aceptar `port: int | str | None = None` como primer parámetro, resolviendo `raw_url = f"http://localhost:{resolved_port}/"`.
- [ ] **Step 2:** Actualizar `test_server_tools.py` para validar la inicialización con `port`, con la variable `AUTOCAD_GATEWAY_PORT` y con el valor por defecto `5055`.
- [ ] **Step 3:** Ejecutar `uv run pytest -v` en `mcp-python/` y verificar 9 de 9 tests pasando.

---

### Task 2: Adaptación del Servidor C# y Gateway (.NET 10)

**Files:**
- Modify: `AutoCAD_MCP_Server/Program.cs`
- Modify: `AutoCAD_AI_Gateway/src/Commands/GatewayCommands.cs`

- [ ] **Step 1:** Modificar `Program.cs` en `AutoCAD_MCP_Server` para resolver la dirección base a partir de:
  `string port = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT") ?? "5055";`
  `string baseUrl = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? $"http://localhost:{port.Trim()}/";`
- [ ] **Step 2:** Asegurar en `GatewayCommands.cs` (`StartServerCommand` y `ShowConfigCommand`) que se informe claramente el puerto activo `5055` o el definido en `AUTOCAD_GATEWAY_PORT`.
- [ ] **Step 3:** Compilar `AutoCAD_AI_Gateway` en modo Release con `dotnet build -c Release`.

---

### Task 3: Actualización de Ejemplos de Configuración y Scripts

**Files:**
- Modify: `examples/configs/claude_desktop_config.json`
- Modify: `examples/configs/mcp_config.json`
- Modify: `scripts/set_gateway_token.ps1`

- [ ] **Step 1:** Reemplazar `"AUTOCAD_GATEWAY_URL": "http://localhost:5055"` por `"AUTOCAD_GATEWAY_PORT": "5055"` en `claude_desktop_config.json`.
- [ ] **Step 2:** Reemplazar `"AUTOCAD_GATEWAY_URL": "http://localhost:5055"` por `"AUTOCAD_GATEWAY_PORT": "5055"` en `mcp_config.json`.
- [ ] **Step 3:** Modificar `scripts/set_gateway_token.ps1` para que emita en su plantilla y actualice `AUTOCAD_GATEWAY_PORT`.

---

### Task 4: Sincronización de Documentación Técnica

**Files:**
- Modify: `README.md`
- Modify: `mcp-python/README.md`
- Modify: `docs/GETTING_STARTED.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1:** Actualizar bloques de código en `README.md` y `docs/GETTING_STARTED.md` mostrando `"AUTOCAD_GATEWAY_PORT": "5055"`.
- [ ] **Step 2:** Actualizar `mcp-python/README.md` destacando la simplicidad del parámetro de puerto.
- [ ] **Step 3:** Registrar el cambio en `CHANGELOG.md` bajo `[2.4.0]`.

---

### Task 5: Verificación Integral y Walkthrough

**Files:**
- Test: `mcp-python/tests/`
- Test: `AutoCAD_AI_Gateway/AutoCAD_AI_Gateway.csproj`
- Artifact: `walkthrough.md`

- [ ] **Step 1:** Ejecutar `uv run pytest -v` en `mcp-python/`.
- [ ] **Step 2:** Probar ejecución de `.\scripts\set_gateway_token.ps1 -UpdateExampleConfigs`.
- [ ] **Step 3:** Actualizar `walkthrough.md` con la nueva configuración simplificada.
