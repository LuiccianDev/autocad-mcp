# Gestión y Generación Dinámica de Tokens en AutoCAD AI Gateway

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar comandos nativos interactivos en AutoCAD (`AI_SHOW_CONFIG`, `AI_SET_TOKEN`, `AI_GENERATE_TOKEN`), gestión en caliente de tokens en memoria y script de sincronización de variables de entorno para eliminar la necesidad de credenciales estáticas o hardcodeadas.

**Architecture:** El token activo del Gateway pasará a ser una propiedad mutable administrada por `HttpGatewayServer.ActiveToken`, inicializada desde la variable de entorno `AUTOCAD_GATEWAY_TOKEN` o el fallback `"autocad-gateway-token-2027"`. Comandos de AutoCAD permitirán inspeccionar, alterar o generar tokens criptográficos en caliente sin reiniciar `acad.exe`, y un script PowerShell facilitará la sincronización en variables de usuario de Windows.

**Tech Stack:** C# (.NET 10, AutoCAD Runtime API), PowerShell, Python (pytest).

**Spec:** Especificación de seguridad de 12-Factor App, `SECURITY.md` y directivas del plugin `autocad-gateway`.

## Global Constraints

- No romper la compatibilidad con clientes existentes que envían `autocad-gateway-token-2027`.
- Compilar en Release sobre .NET 10.
- Mantener silencio en `stdout` en el servidor MCP C#.
- Seguir directivas de Notion PM si se requiere registrar tareas.

---

### Task 1: Estado Mutable y Dinámico del Token en el Gateway

**Files:**
- Modify: `AutoCAD_AI_Gateway/src/Server/HttpGatewayServer.cs`
- Modify: `AutoCAD_AI_Gateway/src/Server/RequestRouter.cs`

- [ ] **Step 1:** Agregar la propiedad `public static string ActiveToken { get; set; }` en `HttpGatewayServer.cs`.
- [ ] **Step 2:** Inicializar `ActiveToken` en el constructor de `HttpGatewayServer` leyendo `Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN") ?? "autocad-gateway-token-2027"`.
- [ ] **Step 3:** En `RequestRouter.cs`, validar contra `HttpGatewayServer.ActiveToken`.

---

### Task 2: Comandos Interactivos en AutoCAD

**Files:**
- Modify: `AutoCAD_AI_Gateway/src/Commands/GatewayCommands.cs`
- Modify: `AutoCAD_AI_Gateway/src/Main.cs`

- [ ] **Step 1:** Implementar comando `AI_SHOW_CONFIG` que imprime en la consola de AutoCAD el estado del servidor, URL de escucha y token activo (enmascarado).
- [ ] **Step 2:** Implementar comando `AI_SET_TOKEN` que solicita un token por consola con `ed.GetString()` y actualiza `HttpGatewayServer.ActiveToken`.
- [ ] **Step 3:** Implementar comando `AI_GENERATE_TOKEN` que genera un token seguro aleatorio (`acad-` + GUID), lo aplica en memoria y lo persiste opcionalmente en el entorno del usuario de Windows.
- [ ] **Step 4:** Actualizar el banner de bienvenida en `Main.cs` para listar los nuevos comandos disponibles.

---

### Task 3: Script Utilitario PowerShell de Configuración

**Files:**
- Create: `scripts/set_gateway_token.ps1`

- [ ] **Step 1:** Crear `scripts/set_gateway_token.ps1` que acepte `-Token` o genere uno aleatorio seguro.
- [ ] **Step 2:** Persistir la variable de entorno a nivel de usuario en Windows (`[Environment]::SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", $Token, "User")`).
- [ ] **Step 3:** Mostrar el snippet JSON para actualizar `claude_desktop_config.json` o `mcp_config.json`.

---

### Task 4: Verificación, Pruebas y Compilación

**Files:**
- Test: `AutoCAD_AI_Gateway/AutoCAD_AI_Gateway.csproj`
- Test: `mcp-python/tests/`

- [ ] **Step 1:** Compilar `AutoCAD_AI_Gateway` en modo Release (`dotnet build AutoCAD_AI_Gateway/AutoCAD_AI_Gateway.csproj -c Release`).
- [ ] **Step 2:** Ejecutar `uv run pytest -v` en `mcp-python/` para confirmar que los tests siguen pasando.

---

### Task 5: Documentación y Registro

**Files:**
- Modify: `SECURITY.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1:** Documentar los nuevos comandos en `SECURITY.md` y `README.md`.
- [ ] **Step 2:** Registrar las novedades en `CHANGELOG.md`.
