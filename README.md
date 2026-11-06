# AutoCAD MCP — AI Gateway & MCP Server Integration

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Python 3.10+](https://img.shields.io/badge/Python-3.10+-3776AB?style=flat&logo=python&logoColor=white)](https://www.python.org/)
[![uv](https://img.shields.io/badge/managed%20by-uv-261230?style=flat&logo=uv&logoColor=white)](https://astral.sh/uv)
[![AutoCAD 2027](https://img.shields.io/badge/AutoCAD-2027-E51937?style=flat&logo=autodesk&logoColor=white)](https://www.autodesk.com/autocad)
[![MCP Protocol](https://img.shields.io/badge/MCP-v2.2-0052CC?style=flat)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI Build](https://img.shields.io/badge/CI-GitHub_Actions-2ea44f?style=flat&logo=githubactions&logoColor=white)](.github/workflows/ci.yml)
[![Changelog](https://img.shields.io/badge/Changelog-Keep_a_Changelog-orange?style=flat)](CHANGELOG.md)

Integración bidireccional de alto rendimiento entre **Modelos de Inteligencia Artificial** (Claude, Antigravity, Cursor, ChatGPT, etc.) y **Autodesk AutoCAD 2027 / Civil 3D**. Cuenta con arquitectura dual de cliente MCP (**Python con soporte instantáneo `uvx`** o **C# sobre .NET 10**) y la especificación estándar del **Model Context Protocol (MCP v2.2)** con 43 herramientas tipadas.

---

## 📚 Documentación y Recursos Técnicos

Para facilitar la adopción, comprensión y soporte de este proyecto como estándar de código abierto, consulta las guías dedicadas:

| Documento | Descripción |
|---|---|
| 🚀 **[Guía de Inicio Rápido (Getting Started)](docs/GETTING_STARTED.md)** | Instalación, compilación y conexión con clientes IA en 5 minutos. |
| 🐍 **[Servidor MCP en Python (`AutoCAD.McpPython/`)](AutoCAD.McpPython/README.md)** | Ejecución instantánea con `uvx autocad-mcp` sin compilación manual. |
| 🏗️ **[Arquitectura del Sistema](docs/ARCHITECTURE.md)** | Concurrencia, inyección en hilo UI, cola segura y diagramas C4/Mermaid. |
| 🛠️ **[Manual de Referencia de Herramientas MCP](docs/TOOLS_REFERENCE.md)** | Especificación técnica exhaustiva de las 43 tools (parámetros, tipos, JSON-RPC y retorno). |
| ⚡ **[Guía Rápida y Cheat Sheet de Tools](docs/TOOLS_CHEAT_SHEET.md)** | Matriz de decisión ("Qué quiero hacer -> Qué tool usar") y flujos encadenados para la IA. |
| 🔧 **[Solución de Problemas (Troubleshooting)](docs/TROUBLESHOOTING.md)** | Resolución de incidencias con NETLOAD, puertos, tokens y bloqueos. |
| 🛣️ **[Hoja de Ruta (Roadmap)](ROADMAP.md)** | Visión estratégica e hitos planificados (Civil 3D, streaming y utilidades). |
| 📁 **[Ejemplos Prácticos (`examples/`)](examples/)** | Configuraciones JSON, scripts de prueba PowerShell/Bash y rutinas AutoLISP. |

---

## 🏗️ Arquitectura del Sistema

El ecosistema opera mediante una arquitectura desacoplada de dos capas que garantiza seguridad de hilos, concurrencia y tolerancia a fallos:

```
┌─────────────────────────────────────────────────────────────┐
│          Cliente IA (Antigravity / Claude / Cursor)         │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  STDIO (JSON-RPC Protocol)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│             Servidores MCP (43 Tools Disponibles)           │
│  Opción A: Python FastMCP (`uvx autocad-mcp`)               │
│  Opción B: AutoCAD.McpServer (.NET 10 Nativo)              │
│  - DI Container & Tool Registry                             │
│  - Typed HttpClient con Timeout & Token Authentication      │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  HTTP REST (http://localhost:5055/)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                AutoCAD.Gateway (.NET 10)                 │
│  (Librería C# cargada en acad.exe vía NETLOAD)              │
│  - HttpGatewayServer (Hilo segundo plano)                   │
│  - RequestRouter (/execute, /context/*, /civil3d/*)         │
│  - AutoCADCommandDispatcher (ConcurrentQueue + Idle Event)  │
│  - AutoCADContextReader (Read-only Database Transactions)   │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               │  Inyección en Hilo UI Principal
                               ▼
┌─────────────────────────────────────────────────────────────┐
│         Sesión Activa de AutoCAD 2027 / Civil 3D            │
└─────────────────────────────────────────────────────────────┘
```

---

## 💡 ¿Por qué es fundamental usar Gateway y MCP en Conjunto?

Para que un modelo de Inteligencia Artificial (o cualquier asistente) opere de forma robusta con AutoCAD, **ambos componentes deben desplegarse y configurarse juntos**:

| Componente | Rol en el Ecosistema | Si se usa solo... |
|---|---|---|
| **AutoCAD.Gateway** (.NET DLL en `acad.exe`) | **El ejecutor y sensor:** Accede a la base de datos de AutoCAD, inspecciona capas/entidades e inyecta comandos en el hilo principal (`Application.Idle`) de forma segura. | Funciona como API REST local. Puedes enviarle comandos por PowerShell/cURL, pero la IA tendría que programar scripts de terminal a ciegas y parsear respuestas JSON crudas (RAW), consumiendo más contexto y con riesgo de errores de sintaxis. |
| **Servidores MCP** (`uvx autocad-mcp` o `.NET 10`) | **El traductor de la IA:** Implementa el estándar Model Context Protocol (MCP v2.2), exponiendo 43 herramientas tipadas (`dibujar_linea`, `crear_capa`, `medir_distancia`, `consultar_perfil_longitudinal`, etc.) con esquemas y validación. | **No puede funcionar solo.** Depende obligatoriamente del Gateway para interactuar con la sesión abierta de AutoCAD. |

> [!IMPORTANT]
> **Directiva de Integración:** La experiencia completa de copiloto IA requiere compilar ambas soluciones, cargar la DLL del Gateway en AutoCAD (`NETLOAD`) y registrar el MCP Server en el cliente de IA (`mcp_config.json` o `claude_desktop_config.json`). Esto permite que la IA consulte el contexto y dibuje de manera bidireccional, limpia y tipada, sin depender de scripts manuales de PowerShell.

---

## 🚀 Características Principales

1. **Despacho Seguro en el Hilo UI:** Nunca bloquea ni corrompe el documento de AutoCAD; las operaciones se sincronizan con el evento `Application.Idle` de AutoCAD.
2. **Defensa contra Bloqueos de Comandos:** Inyección automática de secuencias `\x03\x03` (`ESC`/`Ctrl+C`) antes de cada instrucción para liberar comandos previos en estado de espera.
3. **43 Herramientas MCP Especializadas:**
   - **Inspección y Visión:** Capas, bloques, entidades, textos, límites y captura de pantalla PNG para la IA.
   - **Geometría Paramétrica Tipada:** Líneas, polilíneas, rectángulos, círculos, arcos, textos y bloques con `entmake`.
   - **Gestión de Capas:** Creación con color/tipo de línea, activación y control de estados (on/off, freeze/thaw, lock/unlock).
   - **Medición y Mantenimiento:** Distancias 2D/3D, áreas y perímetros por fórmula de Gauss, purga con auditoría y regeneración.
   - **Ejecución y Control:** Inyección de comandos nativos, evaluación LISP en memoria y scripts `.lsp` externos.
   - **Presentaciones y PDF:** Layouts, configuración de hoja (A0-A4), viewports calibrados en XP, cajetines y trazado PDF.
   - **Civil 3D Nativo:** Superficies TIN, cotas Z, alineamientos horizontales, perfiles longitudinales y creación paramétrica.
4. **Seguridad y Token Zero-Secrets:** Cabecera HTTP `X-Gateway-Token` configurable mediante variables de entorno.
5. **Silenciamiento Estricto de `stdout`:** Garantiza la total integridad de las tramas JSON-RPC requeridas por el estándar MCP.

---

## 📋 Requisitos Previos

- **Sistema Operativo:** Windows 10/11 (x64).
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) instalado (`dotnet --version` >= 10.0.100).
- **CAD:** Autodesk AutoCAD 2027 o Autodesk Civil 3D 2027 instalado en la ruta predeterminada:
  `C:\Program Files\Autodesk\AutoCAD 2027\`.

---

## ⚙️ Compilación e Instalación

### 1. Clonar el repositorio
```powershell
git clone https://github.com/LuiccianDev/autocad-mcp.git
cd autocad-mcp
```

### 2. Compilar la solución en Release
Puedes utilizar el script incluido o el comando oficial de .NET:
```powershell
.\scripts\build.ps1
# O manualmente:
dotnet build AutoCAD_Integration.slnx -c Release
```

Esto generará:
- `AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll` (Plugin nativo de AutoCAD).
- `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.dll` (Servidor MCP portable).
- `AutoCAD.McpServer\bin\Release\net10.0\AutoCAD.McpServer.exe` (Ejecutable de Windows).

---

## 🔌 Puesta en Marcha

### Paso 1: Cargar el Gateway en AutoCAD 2027
1. Abre AutoCAD 2027 o Civil 3D 2027.
2. En la línea de comandos, escribe:
   ```text
   NETLOAD
   ```
3. Navega y selecciona el archivo compilado:
   `...\autocad-mcp\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll`
4. En la consola verás la confirmación:
   ```text
   >>> AutoCAD AI Gateway Plugin cargado exitosamente.
   >>> Comandos disponibles: AI_START_SERVER, AI_STOP_SERVER, AI_SHOW_CONFIG, AI_SET_TOKEN, AI_GENERATE_TOKEN
   ```

### Paso 2: Conectar el Servidor MCP a tu Cliente de IA

Puedes utilizar indistintamente cualquiera de los dos servidores MCP con idéntica paridad de 43 herramientas:

#### Opción A: Despliegue Instantáneo con Python (`uvx`) — Recomendado
Si utilizas [`uv`](https://astral.sh/uv), no necesitas compilar nada en tu máquina:

**Claude Desktop (`claude_desktop_config.json`):**
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

**Antigravity / Cursor / VS Code (`mcp_config.json`):**
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

#### Opción B: Servidor Nativo en C# (.NET 10)
Para entornos donde se prefiera la ejecución binaria nativa compilada:

**Claude Desktop (`claude_desktop_config.json`):**
```json
{
  "mcpServers": {
    "autocad-mcp": {
      "command": "dotnet",
      "args": [
        "C:\\Ruta\\A\\autocad-mcp\\AutoCAD.McpServer\\bin\\Release\\net10.0\\AutoCAD.McpServer.dll"
      ],
      "env": {
        "AUTOCAD_GATEWAY_TOKEN": "luiccian-autocad-token-2027"
      }
    }
  }
}
```

**Antigravity / Cursor / VS Code (`mcp_config.json`):**
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

## 🛠️ Catálogo de Herramientas MCP (43 Tools)

### 1. Herramientas de Inspección, Contexto y Visión (8)
| Herramienta | Descripción |
|---|---|
| `obtener_info_dibujo` | Consulta nombre del archivo, ruta DWG, modo solo lectura y unidades configuradas. |
| `leer_capas_autocad` | Devuelve el catálogo completo de capas con color, visibilidad y estado de bloqueo. |
| `leer_objetos_autocad` | Lista el resumen y recuento de entidades existentes agrupadas por tipo. |
| `obtener_limites_dibujo` | Retorna las coordenadas de la caja delimitadora (`ExtMin`, `ExtMax`) y delta métrico. |
| `leer_textos_autocad` | Búsqueda y listado de cadenas de texto en objetos `DBText` y `MText` con sus coordenadas (X, Y). |
| `listar_bloques_disponibles` | Lista los nombres de definiciones de bloques disponibles en el dibujo. |
| `leer_seleccion_autocad` | Retorna la cantidad de objetos seleccionados actualmente con el cursor por el usuario. |
| `capturar_pantalla_autocad` | **Ojos para la IA:** Captura una imagen PNG de la ventana gráfica activa de AutoCAD para validación visual. |

### 2. Geometría y Dibujo Paramétrico Tipado (7)
| Herramienta | Descripción |
|---|---|
| `dibujar_linea` | Dibuja un segmento de línea recta 3D entre dos puntos en metros (sin alterar capa actual ni depender de OSNAP). |
| `dibujar_polilinea` | Dibuja una polilínea 2D (`LWPOLYLINE`) abierta o cerrada a partir de vértices métricos. |
| `dibujar_rectangulo` | Dibuja un rectángulo como polilínea cerrada dado por dos esquinas opuestas. |
| `dibujar_circulo` | Dibuja un círculo paramétrico dado su centro (X, Y) y radio en metros. |
| `dibujar_arco` | Dibuja un arco circular antihorario dado su centro, radio y ángulos inicial/final en grados. |
| `insertar_texto` | Inserta un texto de una línea (`DBText`) en el espacio modelo con altura métrica y rotación. |
| `insertar_bloque` | Inserta una referencia de bloque existente con escala uniforme y ángulo de rotación. |

### 3. Gestión Avanzada de Capas (3)
| Herramienta | Descripción |
|---|---|
| `crear_capa` | Crea o actualiza una capa con color ACI (1-255), tipo de línea y descripción (sin diálogos modales). |
| `establecer_capa_actual` | Establece la capa de trabajo activa (`CLAYER`). |
| `establecer_estado_capa` | Controla visibilidad (`activar`/`desactivar`), congelación (`congelar`/`descongelar`) o bloqueo (`bloquear`/`desbloquear`). |

### 4. Medición, Geometría Computacional y Mantenimiento (4)
| Herramienta | Descripción |
|---|---|
| `medir_distancia` | Calcula distancias 2D/3D, incrementos $\Delta X$, $\Delta Y$, $\Delta Z$, acimut y pendiente en % (cálculo analítico local). |
| `calcular_area_poligono` | Calcula área en $m^2$, hectáreas, perímetro, centroide y sentido de giro de polígonos cerrados (fórmula de Gauss). |
| `limpiar_y_purgar_dibujo` | Purga definiciones no utilizadas en dos pasadas (`_.-PURGE`) y audita/repara la base de datos DWG (`_.AUDIT`). |
| `regenerar_todo` | Fuerza una regeneración total de todas las ventanas gráficas (`_.REGENALL`). |

### 5. Herramientas de Ejecución, Control y AutoLISP (7)
| Herramienta | Descripción |
|---|---|
| `ping_autocad_gateway` | Diagnóstico de conectividad HTTP en el puerto 5055. |
| `ejecutar_comando_autocad` | Inyecta comandos en modo texto no modal (`_.-`) asegurando terminadores `\n`. |
| `ejecutar_lisp` | Evalúa expresiones y rutinas AutoLISP dinámicas en memoria (ej. `entmake`, variables, funciones). |
| `cargar_archivo_lisp` | Carga y ejecuta un archivo `.lsp` externo desde el sistema de archivos mediante `(load ...)`. |
| `configurar_entorno_dibujo` | Ajusta el estándar métrico obligatorio: `INSUNITS = 4` (metros), `MEASUREMENT = 1`. |
| `zoom_extension` | Ejecuta un encuadre total (`_.ZOOM E`) en la ventana gráfica activa. |
| `guardar_dibujo` | Guarda el archivo DWG activo mediante `_.QSAVE`. |

### 6. Presentaciones, Cajetines y Ploteo PDF (5)
| Herramienta | Descripción |
|---|---|
| `crear_layout` | Crea una nueva pestaña de presentación (espacio papel) y activa su vista. |
| `configurar_hoja_layout` | Configura el tamaño de papel (A0 a A4), trazador `DWG To PDF.pc3` y plumillas `monochrome.ctb`. |
| `crear_viewport` | Crea una ventana gráfica (Viewport) en la lámina con escala métrica exacta (`1000/Escala XP`) y centra el modelo en (X, Y). |
| `insertar_cajetin_parametrico` | Inserta y dibuja un cajetín técnico en espacio papel con campos paramétricos (Título, Plano, Lámina, Escala, Diseñador, Fecha). |
| `exportar_pdf` | Traza y genera un archivo PDF físico a partir de un Layout utilizando el motor virtual `DWG To PDF.pc3`. |

### 7. Herramientas de Autodesk Civil 3D (9)
| Herramienta | Descripción |
|---|---|
| `verificar_estado_civil3d` | Detecta si Civil 3D está cargado y expone la API administrada (`AeccDbMgd`). |
| `listar_superficies_civil3d` | Retorna las superficies TIN presentes con cota mínima, cota máxima y triángulos. |
| `consultar_elevacion_terreno` | Devuelve la cota Z exacta del terreno en cualquier coordenada (X, Y). |
| `listar_alineamientos_civil3d` | Devuelve los alineamientos horizontales (`Alignment`), longitud y estacas. |
| `consultar_estacion_alineamiento` | Calcula progresiva (estaca) y desfase lateral (+ derecha / - izquierda) de un punto (X, Y). |
| `obtener_coordenada_en_estacion` | Dada una progresiva y desfase lateral, devuelve las coordenadas (X, Y) correspondientes. |
| `consultar_perfil_longitudinal` | Muestrea progresivas, coordenadas y cotas Z a intervalos regulares a lo largo de un eje civil. |
| `crear_alineamiento_civil3d` | Crea un alineamiento horizontal paramétrico a partir de PIs con curvas tangentes de radio fijo. |
| `crear_superficie_civil3d` | Crea una superficie TIN topográfica a partir de una nube de puntos (X, Y, Z). |

---

## 🔒 Reglas para Comandos Seguros

1. **Evitar cuadros de diálogo modales:** Usa siempre el prefijo `_.-` (ejemplo: `_.-LAYER` en lugar de `LAYER`, `_.-INSERT` en lugar de `INSERT`). Si AutoCAD despliega una ventana modal de Windows, el Gateway quedará en espera hasta la intervención humana.
2. **Internacionalización:** Usa el prefijo `_.` para garantizar compatibilidad con versiones de AutoCAD en español o inglés (ejemplo: `_.LINE`, `_.RECTANG`).
3. **Unidades:** Trabajar siempre en **metros**.

---

## 🤝 Comunidad, Gobernanza y Soporte Open Source

Este proyecto está diseñado para ser un estándar de código abierto mantenido por y para la comunidad de desarrolladores CAD, ingenieros civiles y especialistas en IA.

- 📖 **[Código de Conducta](CODE_OF_CONDUCT.md):** Promovemos un espacio respetuoso, inclusivo y libre de acoso (Contributor Covenant v2.1).
- 💡 **[Guía de Contribución](CONTRIBUTING.md):** Estándares de desarrollo, directivas de Git y Conventional Commits.
- 🛡️ **[Política de Seguridad](SECURITY.md):** Divulgación responsable de vulnerabilidades y buenas prácticas del Gateway.
- 💬 **[Canales de Soporte y Ayuda](SUPPORT.md):** Dónde plantear preguntas, proponer ideas y reportar incidencias.
- 📝 **[Registro de Versiones (Changelog)](CHANGELOG.md):** Historial cronológico de cambios y versiones publicadas.

---

## 👨‍💻 Autor y Desarrollador

Este proyecto ha sido concebido, diseñado y desarrollado por:

- **Luiccian** ([@LuiccianDev](https://github.com/LuiccianDev))
  - Creador y Arquitecto Principal de **AutoCAD MCP Server & Gateway** (`autocad-mcp`) y **Vulcan MCP Server** (`vulcan-mcp`).
  - Especialista en integración de Inteligencia Artificial para ingeniería, minería, topografía y automatización CAD.

---

## 📄 Licencia

Este proyecto se distribuye bajo la licencia **MIT** (Copyright (c) 2026 Luiccian). Consulta el archivo [LICENSE](LICENSE) para más detalles.
