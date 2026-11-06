# AutoCAD MCP Server — Model Context Protocol Server (.NET 10)

**Version:** 2.4.0  
**Target Framework:** .NET 10 (`net10.0`)  
**Protocol:** Model Context Protocol (MCP v2.2)  
**Transport:** STDIO (JSON-RPC)  
**Backend:** Typed HTTP Client connecting to AutoCAD AI Gateway (`http://localhost:5055/`)  
**Generated Binaries:** `AutoCAD.McpServer.dll` (portable managed assembly) and `AutoCAD.McpServer.exe` (Windows executable)

---

## 1. Overview & Purpose

The **AutoCAD MCP Server** is an implementation of the **Model Context Protocol (MCP)** specification in C# on .NET 10. It serves as the primary artificial intelligence interface enabling autonomous agents and LLM clients (such as Antigravity, Claude Desktop, Cursor, and VS Code) to inspect active AutoCAD 2027 / Civil 3D drawings and execute design operations cleanly and parametrically.

---

## 2. Binaries (.dll vs .exe)

When building this project with `dotnet build -c Release`, the .NET SDK outputs two primary files to `bin/Release/net10.0/`:

1. **`AutoCAD.McpServer.dll` (Recommended & Portable):**  
   The managed IL assembly containing all MCP server logic. Executed via the .NET runtime:
   ```powershell
   dotnet "AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
   ```
   **Advantages:** Portable across workspaces, compatible with IDE variables like `${workspaceFolder}`, and ensures execution on the .NET 10 runtime.

2. **`AutoCAD.McpServer.exe` (Native Windows Executable):**  
   The Windows apphost executable. Allows direct execution without prefixing `dotnet`.

---

## 3. Internal Architecture

The server adheres to modern .NET 10 backend and MCP design patterns:

```
[ MCP Client (Antigravity / Cursor / Claude) ]
                 │
                 │ JSON-RPC over STDIO
                 ▼
        [ AutoCAD.McpServer ]
                 │
                 ├── Program.cs (DI Container + McpServer Host)
                 │
                 ├── [ IAutoCADGatewayClient ] (Typed HttpClient with Timeout)
                 │         │
                 │         ▼ HTTP REST (Port 5055)
                 │   [ AutoCAD AI Gateway ] ───> AutoCAD 2027 / Civil 3D
                 │
                 ├── [ ContextTools ]     ───> 8 Inspection & Vision Tools
                 ├── [ DrawingTools ]     ───> 7 Parametric Geometry Tools
                 ├── [ LayerTools ]       ───> 3 Layer Management Tools
                 ├── [ MeasurementTools ] ───> 4 Local Geometry & Audit Tools
                 ├── [ ExecutionTools ]   ───> 7 Command & AutoLISP Tools
                 ├── [ PlottingTools ]    ───> 5 Layout & PDF Plotting Tools
                 └── [ Civil3DTools ]     ───> 9 Autodesk Civil 3D Tools
```

### Architectural Highlights:
1. **Strict `stdout` Silence:**  
   `builder.Logging.ClearProviders()` is called during startup to prevent diagnostic text from corrupting the standard output stream (`Console.Out`), which is strictly reserved for JSON-RPC messages.
2. **Dependency Injection (.NET DI):**  
   The HTTP gateway client (`IAutoCADGatewayClient`) is registered via `AddHttpClient` with resilience handlers and injected directly into tool class constructors.
3. **Modular Tool Segregation:**  
   The 43 tools are organized into 7 cohesive classes across `Tools/`, each with dedicated responsibilities.

---

## 4. MCP Tools Summary (43 Tools)

For full parameter schemas, type definitions, and JSON-RPC examples, refer to [docs/TOOLS_REFERENCE.md](../docs/TOOLS_REFERENCE.md).

- **ContextTools (8 tools):** `obtener_info_dibujo`, `leer_capas_autocad`, `leer_objetos_autocad`, `obtener_limites_dibujo`, `leer_textos_autocad`, `listar_bloques_disponibles`, `leer_seleccion_autocad`, `capturar_pantalla_autocad`.
- **DrawingTools (7 tools):** `dibujar_linea`, `dibujar_polilinea`, `dibujar_rectangulo`, `dibujar_circulo`, `dibujar_arco`, `insertar_texto`, `insertar_bloque`.
- **LayerTools (3 tools):** `crear_capa`, `establecer_capa_actual`, `establecer_estado_capa`.
- **MeasurementTools (4 tools):** `medir_distancia`, `calcular_area_poligono`, `limpiar_y_purgar_dibujo`, `regenerar_todo`.
- **ExecutionTools (7 tools):** `ping_autocad_gateway`, `ejecutar_comando_autocad`, `ejecutar_lisp`, `cargar_archivo_lisp`, `configurar_entorno_dibujo`, `zoom_extension`, `guardar_dibujo`.
- **PlottingTools (5 tools):** `crear_layout`, `configurar_hoja_layout`, `crear_viewport`, `insertar_cajetin_parametrico`, `exportar_pdf`.
- **Civil3DTools (9 tools):** `verificar_estado_civil3d`, `listar_superficies_civil3d`, `consultar_elevacion_terreno`, `listar_alineamientos_civil3d`, `consultar_estacion_alineamiento`, `obtener_coordenada_en_estacion`, `consultar_perfil_longitudinal`, `crear_alineamiento_civil3d`, `crear_superficie_civil3d`.

---

## 5. Client Configuration Examples

### Claude Desktop (`claude_desktop_config.json`)
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "C:\\Path\\To\\autocad-mcp\\AutoCAD.McpServer\\bin\\Release\\net10.0\\AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

### Antigravity / Cursor / VS Code (`mcp_config.json`)
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

## 6. Build Instructions

Build the MCP Server individually:
```powershell
dotnet build AutoCAD.McpServer/AutoCAD.McpServer.csproj -c Release
```

Or build the entire solution:
```powershell
.\scripts\build.ps1
```
