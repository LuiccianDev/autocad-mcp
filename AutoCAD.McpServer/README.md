# AutoCAD MCP Server — Servidor Model Context Protocol

**Versión:** 1.0.0  
**Target Framework:** .NET 10 (`net10.0`)  
**Protocolo:** Model Context Protocol (MCP v2.2)  
**Transporte:** STDIO (JSON-RPC)  
**Backend:** Cliente HTTP hacia AutoCAD AI Gateway (`http://localhost:5055/`)  
**Binarios generados:** `AutoCAD.McpServer.dll` (ensamblado administrado) y `AutoCAD.McpServer.exe` (ejecutable de Windows)

---

## 1. Propósito y Descripción

El **AutoCAD MCP Server** es un servidor que implementa la especificación estándar del **Model Context Protocol (MCP)** en C# / .NET 10. Su función principal es actuar como la interfaz de inteligencia artificial que permite a asistentes y agentes (como Antigravity, Claude Desktop, Cursor o cualquier cliente compatible con MCP) inspeccionar el dibujo activo en AutoCAD 2027 y ejecutar operaciones de diseño de manera segura y paramétrica.

---

## 2. Aclaración de Binarios (.dll vs .exe)

Al compilar este proyecto con `dotnet build -c Release`, el SDK de .NET genera en la carpeta `bin/Release/net10.0/` dos archivos principales:

1. **`AutoCAD.McpServer.dll` (Recomendado y Portátil):**  
   Es la biblioteca de ensamblado IL que contiene toda la lógica compilada del servidor MCP. Se ejecuta invocando el runtime de .NET:
   ```powershell
   dotnet "plugins/AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
   ```
   **Ventajas:** Es independiente de rutas absolutas duras, compatible con variables de entorno de IDEs como `${workspaceFolder}` y garantiza el uso controlado del SDK de .NET 10.

2. **`AutoCAD.McpServer.exe` (Ejecutable Nativo de Windows):**  
   Es el contenedor ejecutable generado por .NET para Windows (apphost). Permite invocar directamente el servidor sin anteponer `dotnet` en la línea de órdenes.

---

## 3. Arquitectura Interna

El servidor sigue los patrones modernos de desarrollo en .NET 10 recomendados en `dotnet-backend-patterns` y `dotnet-mcp-builder`:

```
[ Cliente MCP (Antigravity / VS Code / Claude) ]
                 │
                 │ JSON-RPC vía STDIO (Entrada/Salida Estándar)
                 ▼
        [ AutoCAD MCP Server ]
                 │
                 ├── Program.cs (DI Container + McpServer Host)
                 │
                 ├── [ IAutoCADGatewayClient ] (HttpClient tipado con Timeout)
                 │         │
                 │         ▼ HTTP REST (Puerto 5055)
                 │   [ AutoCAD AI Gateway ] ───> AutoCAD 2027
                 │
                 ├── [ ContextTools ]    ───> 7 Herramientas de Inspección DWG
                 ├── [ ExecutionTools ]  ───> 5 Herramientas de Acción y Control
                 └── [ Civil3DTools ]    ───> 8 Herramientas de Civil 3D (lectura y escritura)
```

### Características de Diseño:
1. **Silenciamiento Estricto de `stdout`:**  
   Se ejecuta `builder.Logging.ClearProviders()` para garantizar que ningún log de diagnóstico contamine la salida estándar (`Console.Out`), la cual queda reservada exclusivamente para el intercambio de tramas JSON-RPC del protocolo MCP.
2. **Inyección de Dependencias (.NET DI):**  
   El cliente HTTP (`IAutoCADGatewayClient`) se registra de forma desacoplada con `AddHttpClient`, inyectándose directamente en los constructores de las clases de herramientas.
3. **Segregación de Responsabilidades:**  
   Las 20 herramientas se dividen lógicamente en herramientas de solo lectura (`ContextTools`), de acción/control (`ExecutionTools`) y de ingeniería civil (`Civil3DTools`).

---

## 4. Catálogo de Herramientas MCP (20 Herramientas)

### A. Herramientas de Inspección y Contexto (`ContextTools`)

| Herramienta | Descripción | Parámetros |
|---|---|---|
| **`obtener_info_dibujo`** | Obtiene metadatos del dibujo: nombre de archivo DWG, estado de solo lectura y unidades configuradas (`INSUNITS`). | Ninguno |
| **`leer_objetos_autocad`** | Devuelve el resumen (conteo por tipo) de todas las entidades en el espacio modelo (Line, Polyline, Circle, BlockReference, etc.). | Ninguno |
| **`leer_capas_autocad`** | Lista todas las capas con sus estados (bloqueada, congelada, apagada) y código de color ACI. | Ninguno |
| **`leer_textos_autocad`** | Lista todos los textos (`DBText` y `MText`) con su contenido, capa y coordenadas exactas `(X, Y)`. | Ninguno |
| **`obtener_limites_dibujo`** | Obtiene las coordenadas extremas mínimas y máximas del dibujo (`Extmin` y `Extmax`). | Ninguno |
| **`leer_seleccion_autocad`** | Devuelve la cantidad de entidades que el usuario tiene seleccionadas con el ratón. | Ninguno |
| **`listar_bloques_disponibles`** | Obtiene los nombres de todas las definiciones de bloques cargadas en el archivo actual. | Ninguno |

### B. Herramientas de Ejecución y Configuración (`ExecutionTools`)

| Herramienta | Descripción | Parámetros |
|---|---|---|
| **`ping_autocad_gateway`** | Envía un ping al Gateway en el puerto 5055 para verificar la conectividad activa. | Ninguno |
| **`ejecutar_comando_autocad`** | Inyecta comandos de dibujo, macros AutoLISP o comandos YQArch directamente a AutoCAD. | `command` (cadena con saltos `\n`) |
| **`zoom_extension`** | Ejecuta un Zoom Extensión (`_.ZOOM E`) para centrar y encuadrar todo en pantalla. | Ninguno |
| **`guardar_dibujo`** | Guarda el archivo DWG activo en AutoCAD (`_.QSAVE`). | Ninguno |
| **`configurar_entorno_dibujo`** | Calibra automáticamente las variables métricas del dibujo (`INSUNITS=4`, `MEASUREMENT=1`, `DIMTXT=0.22`, `DIMASZ=0.18`, etc.). | Ninguno |

### C. Herramientas de Civil 3D 2027 (`Civil3DTools`)

Requieren Civil 3D 2027 con el Gateway activo. En AutoCAD estándar responden `civil_available: false` (HTTP 409 en el Gateway).

| Herramienta | Descripción | Parámetros |
|---|---|---|
| **`verificar_estado_civil3d`** | Indica si el host es Civil 3D y cuenta superficies y alineamientos. | Ninguno |
| **`listar_superficies_civil3d`** | Lista superficies TIN con cota mínima, máxima y triángulos. | Ninguno |
| **`consultar_elevacion_terreno`** | Cota Z del terreno en un punto. Falla si está fuera de la superficie. | `x`, `y`, `superficie` (opcional) |
| **`listar_alineamientos_civil3d`** | Lista alineamientos con progresivas, estacas y longitud. | Ninguno |
| **`consultar_estacion_alineamiento`** | Progresiva (estaca) y desfase de un punto respecto al eje. | `x`, `y`, `alineamiento` (opcional) |
| **`obtener_coordenada_en_estacion`** | Coordenada (x, y) de una progresiva y desfase. | `progresiva`, `desfase`, `alineamiento` (opcional) |
| **`crear_alineamiento_civil3d`** | Crea un alineamiento desde PIs con curvas circulares tangentes de radio fijo; valida nombre único y que el radio quepa. | `nombre`, `puntos` (`"x1,y1;x2,y2"`), `radio`, `capa` |
| **`crear_superficie_civil3d`** | Crea una superficie TIN desde puntos topográficos; valida nombre único y mínimo 3 puntos. | `nombre`, `puntos` (`"x1,y1,z1;x2,y2,z2"`) |

---

## 5. Configuración en Clientes MCP

### Método 1: Estándar y Recomendado (Portátil con `dotnet` y `${workspaceFolder}`)
Este es el formato estándar configurado en `.vscode/mcp.json` para el espacio de trabajo con token de seguridad:

```json
{
  "servers": {
    "autocad-mcp": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "${workspaceFolder}/plugins/AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

### Método 2: En Plugins de Antigravity (`.agents/plugins/autocad-gateway/mcp_config.json`)
Para plugins dentro del workspace con rutas relativas a la raíz y seguridad de token:

```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "plugins/AutoCAD.McpServer/bin/Release/net10.0/AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

### Método 3: Clientes Externos con Binario Directo (`.exe`)
Si se configura un cliente externo de Windows (como Claude Desktop fuera del proyecto) que requiera invocar directamente el ejecutable:

```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "C:\\Users\\DAVID\\Desktop\\WorksSpacesCoding\\Tunel-Casmas-Huaraz-PA14\\plugins\\AutoCAD.McpServer\\bin\\Release\\net10.0\\AutoCAD.McpServer.exe"
    }
  }
}
```

---

## 6. Compilación

Para compilar el proyecto de forma independiente:
```powershell
dotnet build -c Release plugins/AutoCAD.McpServer/AutoCAD.McpServer.csproj
```

O compilar toda la solución de integración CAD:
```powershell
.\scripts\build_plugins.ps1
```
