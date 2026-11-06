# Guía de Inicio Rápido — AutoCAD MCP (`autocad-mcp`)

Esta guía te conducirá paso a paso para compilar la solución, desplegar el Gateway dentro de AutoCAD 2027 y conectar tu cliente de Inteligencia Artificial (Antigravity, Claude Desktop o Cursor) en menos de 5 minutos.

---

## 📋 Requisitos Previos

1. **Sistema Operativo:** Windows 10 o Windows 11 (64-bit).
2. **SDK de .NET 10:**
   - Comprueba tu versión abriendo una terminal PowerShell:
     ```powershell
     dotnet --version
     ```
   - Debe retornar `10.0.100` o superior. Si no lo tienes, descárgalo desde [.NET 10 Download](https://dotnet.microsoft.com/download/dotnet/10.0).
3. **Autodesk AutoCAD 2027 o Civil 3D 2027:**
   - Instalado en la ruta estándar: `C:\Program Files\Autodesk\AutoCAD 2027\`.

---

## 🛠️ Paso 1: Clonar y Compilar la Solución

1. Abre tu terminal favorita en el directorio donde quieras trabajar:
   ```powershell
   git clone https://github.com/tu-usuario/autocad-mcp.git
   cd autocad-mcp
   ```

2. Ejecuta el script de compilación incluido en el repositorio:
   ```powershell
   .\scripts\build.ps1
   ```
   *O alternativamente, mediante el CLI de .NET:*
   ```powershell
   dotnet build AutoCAD_Integration.slnx -c Release
   ```

3. Verifica que la salida confirme `0 Errores` y que se hayan generado los binarios:
   - `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`
   - `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.dll`

---

## 🔌 Paso 2: Cargar el Plugin Gateway en AutoCAD

1. Abre **AutoCAD 2027** o **Autodesk Civil 3D 2027**.
2. Abre un dibujo nuevo o existente (en unidades métricas).
3. En la línea de comandos de AutoCAD, escribe:
   ```text
   NETLOAD
   ```
4. Se abrirá una ventana de selección de archivos. Navega hasta la carpeta del proyecto y selecciona:
   `...\autocad-mcp\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`
5. En la barra de comandos verás el mensaje de confirmación:
   ```text
   >>> AutoCAD AI Gateway Plugin cargado exitosamente.
   >>> Comandos disponibles:
       - AI_START_SERVER   : Inicia el servidor HTTP Gateway.
       - AI_STOP_SERVER    : Detiene el servidor HTTP Gateway.
       - AI_SHOW_CONFIG    : Muestra el estado y token activo.
       - AI_SET_TOKEN      : Cambia el token en memoria en caliente.
       - AI_GENERATE_TOKEN : Genera un nuevo token criptografico seguro.
   ```

> [!TIP]
> **Desbloqueo de DLL (Solo si AutoCAD muestra error de carga):**
> Si Windows bloquea la DLL por haber sido descargada, abre PowerShell como administrador y ejecuta:
> ```powershell
> Unblock-File -Path ".\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll"
> ```

---

## 🤖 Paso 3: Conectar el Servidor MCP a tu Cliente de IA

Elige tu entorno preferido:

### Opción 1: Despliegue Instantáneo con Python (`uvx`) — Recomendado
Si utilizas [`uv`](https://astral.sh/uv), no requieres compilar nada:

**Para Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uvx",
      "args": ["autocad-mcp"],
      "env": {
        "AUTOCAD_GATEWAY_PORT": "5055",
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

**Para Antigravity / Cursor / VS Code (`mcp_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "uv",
      "args": [
        "--directory",
        "${workspaceFolder}/AutoCAD.McpPython",
        "run",
        "autocad-mcp"
      ],
      "env": {
        "AUTOCAD_GATEWAY_PORT": "5055",
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

### Opción 2: Servidor Nativo Compilado en C# (.NET 10)

**Para Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "C:\\Ruta\\Completa\\autocad-mcp\\AutoCAD.McpServer\\bin\\Release\\net10.0\\AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

**Para Antigravity / Cursor / VS Code (`mcp_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "${workspaceFolder}/AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

---

## 🎯 Paso 4: Primera Prueba y Verificación

Una vez que reinicies o conectes tu cliente de IA, pídele en el chat:
> *"Consulta el estado de mi dibujo activo en AutoCAD y dibuja un rectángulo de 20x10 metros en la capa ARQ-MUROS."*

La IA ejecutará automáticamente:
1. `configurar_entorno_dibujo` (asegurando `INSUNITS = 4` para metros).
2. `obtener_info_dibujo` (verificando nombre y estado del archivo).
3. `dibujar_rectangulo` (o creación de capa e inyección de geometría paramétrica).
4. `zoom_extension` (encuadrando la geometría en tu pantalla).

¡Felicidades! Ya tienes tu copiloto de ingeniería y dibujo conectado bidireccionalmente con AutoCAD.

---

## 📚 Siguientes Pasos y Referencias

- 🛠️ **[Manual de Referencia Técnica (43 Tools)](TOOLS_REFERENCE.md):** Consulta el catálogo completo de herramientas con todos sus parámetros, tipos y ejemplos de respuesta JSON.
- ⚡ **[Guía Rápida y Cheat Sheet de Prompts](TOOLS_CHEAT_SHEET.md):** Árbol de decisión para saber exactamente qué herramienta pedir a la IA en cada tarea de diseño.
- 🏗️ **[Arquitectura del Sistema](ARCHITECTURE.md):** Modelo de concurrencia e inyección segura en el hilo UI.
- 🔧 **[Solución de Problemas](TROUBLESHOOTING.md):** Respuestas a incidencias de red, NETLOAD y comandos.
