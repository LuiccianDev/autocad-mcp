# Implementación del Servidor MCP en Python (`mcp-python`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crear un proyecto independiente de servidor Model Context Protocol en Python bajo la carpeta `mcp-python/`, optimizado para distribución y ejecución instantánea con `uvx autocad-mcp` (al estilo de `LuiccianDev/vulcan-mcp`), manteniendo intacto el servidor C# existente (`AutoCAD_MCP_Server`).

**Architecture:** El nuevo proyecto `mcp-python/` implementará un servidor FastMCP modular que expone exactamente las mismas **43 herramientas tipadas**, conectándose vía HTTP asíncrono (`httpx`) al Gateway nativo de AutoCAD (`http://localhost:5055/`) con autenticación por cabecera `X-Gateway-Token`. El proyecto contará con un `pyproject.toml` estándar compatible con `uv`, `uvx` y publicación en PyPI.

**Tech Stack:** Python 3.10+, `uv` / `uvx`, `mcp` (Python SDK oficial / FastMCP), `httpx` (HTTP asíncrono), `pytest`.

**Spec:** Requerimiento del usuario para crear una nueva carpeta `mcp-python` con la estructura completa para soportar `uvx`, sin eliminar ni alterar el servidor MCP en C#.

## Global Constraints

- **No eliminar ni modificar el servidor C# (`AutoCAD_MCP_Server`):** Ambos servidores coexistirán como opciones complementarias.
- **Ubicación del nuevo proyecto:** Estrictamente dentro del subdirectorio `mcp-python/`.
- **Compatibilidad 100% con `uvx`:** El archivo `pyproject.toml` debe definir el script de consola `autocad-mcp = "autocad_mcp.server:main"` para que `uvx autocad-mcp` o `uvx --from ./mcp-python autocad-mcp` funcione de inmediato.
- **Paridad funcional estricta (43 herramientas):** Las 43 herramientas deben mantener exactamente los mismos identificadores en español (`dibujar_linea`, `medir_distancia`, `crear_viewport`, etc.) y las mismas firmas de parámetros que el servidor C#.
- **Cero dependencias pesadas:** Solo dependencias esenciales (`mcp>=1.0.0`, `httpx>=0.27.0`). Cálculos de áreas y distancias resueltos con módulos nativos (`math`).

## Review Focus

1. **Ausencia del Gateway activo:** Si AutoCAD no está abierto o el Gateway no responde, las herramientas dependientes de CAD deben devolver un JSON estructurado con `{"status": "error", "message": "No se pudo conectar con AutoCAD AI Gateway..."}` sin que el proceso Python se caiga.
2. **Herramientas Standalone:** Las herramientas de cálculo analítico (`medir_distancia`, `calcular_area_poligono`) deben ejecutarse localmente en Python con idénticos resultados matemáticos que la versión C#.
3. **Manejo de terminadores y saltos de línea:** Los comandos inyectados por `ejecutar_comando_autocad` y `ejecutar_lisp` deben preservar los saltos de línea `\n` requeridos por AutoCAD.
4. **Validación de tipos de datos:** Parámetros numéricos deben validarse contra `math.isnan()` e infinitos, y nombres de capas/bloques contra caracteres reservados de AutoCAD.
5. **Formato JSON-RPC sobre STDIO:** En Python, nunca utilizar `print()` directo a `stdout` que no forme parte del protocolo MCP; cualquier logging de diagnóstico debe emitirse a `sys.stderr` o vía `logging`.

---

### Task 1: Scaffolding y Configuración del Paquete Python (`mcp-python/`)

**Files:**
- Create: `mcp-python/pyproject.toml`
- Create: `mcp-python/README.md`
- Create: `mcp-python/.python-version`
- Create: `mcp-python/src/autocad_mcp/__init__.py`
- Create: `mcp-python/src/autocad_mcp/__main__.py`

**Interfaces:**
- Consumes: Estándar PEP 621 y empaquetado para `uv` / PyPI.
- Produces: Estructura de paquete instalable y ejecutable mediante `uvx`.

- [ ] **Step 1: Crear `mcp-python/pyproject.toml`**
  - Metadatos: nombre `autocad-mcp`, versión `2.3.0`, descripción, dependencias (`mcp>=1.0.0`, `httpx>=0.27.0`).
  - Entry points: `[project.scripts] autocad-mcp = "autocad_mcp.server:main"`.
- [ ] **Step 2: Crear `mcp-python/.python-version`** (fijando `3.11` o `3.12`).
- [ ] **Step 3: Crear `mcp-python/README.md`** con instrucciones de uso rápido con `uvx`.
- [ ] **Step 4: Crear `__init__.py` y `__main__.py`** para permitir ejecución con `python -m autocad_mcp`.

---

### Task 2: Cliente HTTP Asíncrono del Gateway y Matemáticas CAD (`client.py` y `cad_math.py`)

**Files:**
- Create: `mcp-python/src/autocad_mcp/client.py`
- Create: `mcp-python/src/autocad_mcp/cad_math.py`

**Interfaces:**
- Consumes: Endpoints del Gateway (`http://localhost:5055/`, cabecera `X-Gateway-Token`).
- Produces:
  - `AutoCADGatewayClient`: Métodos `get_context(endpoint)`, `execute_command(command)`, `ping()`, `post_json(endpoint, data)`.
  - `cad_math`: `medir_distancia_analitica(...)`, `calcular_area_poligono_analitico(...)`, validadores `is_valid_symbol_name(...)`.

- [ ] **Step 1: Implementar `client.py` con `httpx.AsyncClient`**
  - Timeout de 15 segundos, lectura de variable de entorno `AUTOCAD_GATEWAY_TOKEN`, manejo de excepciones de conexión y retorno de mensajes estructurados.
- [ ] **Step 2: Implementar `cad_math.py`**
  - Cálculo de distancias 2D/3D, azimuts sexagesimales y pendientes.
  - Algoritmo de Gauss para polígonos cerrados con traslación de origen (estabilidad en UTM).
  - Validadores de símbolos de AutoCAD (`<`, `>`, `/`, `\`, `"`, `:`, `;`, `?`, `*`, `|`, `,`, `=`, `` ` ``).

---

### Task 3: Implementación de las 43 Herramientas MCP en `server.py`

**Files:**
- Create: `mcp-python/src/autocad_mcp/server.py`

**Interfaces:**
- Consumes: `AutoCADGatewayClient` y `cad_math`.
- Produces: Servidor `FastMCP("AutoCAD_AI_Context_Server")` con las 43 herramientas registradas.

- [ ] **Step 1: Registrar herramientas de Inspección y Contexto (8 tools)**
  - `obtener_info_dibujo`, `leer_capas_autocad`, `leer_objetos_autocad`, `obtener_limites_dibujo`, `leer_textos_autocad`, `listar_bloques_disponibles`, `leer_seleccion_autocad`, `capturar_pantalla_autocad`.
- [ ] **Step 2: Registrar herramientas de Geometría Paramétrica Tipada (7 tools)**
  - `dibujar_linea`, `dibujar_polilinea`, `dibujar_rectangulo`, `dibujar_circulo`, `dibujar_arco`, `insertar_texto`, `insertar_bloque`.
- [ ] **Step 3: Registrar herramientas de Gestión de Capas (3 tools)**
  - `crear_capa`, `establecer_capa_actual`, `establecer_estado_capa`.
- [ ] **Step 4: Registrar herramientas de Medición y Mantenimiento (4 tools)**
  - `medir_distancia`, `calcular_area_poligono`, `limpiar_y_purgar_dibujo`, `regenerar_todo`.
- [ ] **Step 5: Registrar herramientas de Ejecución y Control (7 tools)**
  - `ping_autocad_gateway`, `ejecutar_comando_autocad`, `ejecutar_lisp`, `cargar_archivo_lisp`, `configurar_entorno_dibujo`, `zoom_extension`, `guardar_dibujo`.
- [ ] **Step 6: Registrar herramientas de Presentaciones y Ploteo PDF (5 tools)**
  - `crear_layout`, `configurar_hoja_layout`, `crear_viewport`, `insertar_cajetin_parametrico`, `exportar_pdf`.
- [ ] **Step 7: Registrar herramientas de Autodesk Civil 3D (9 tools)**
  - `verificar_estado_civil3d`, `listar_superficies_civil3d`, `consultar_elevacion_terreno`, `listar_alineamientos_civil3d`, `consultar_estacion_alineamiento`, `obtener_coordenada_en_estacion`, `consultar_perfil_longitudinal`, `crear_alineamiento_civil3d`, `crear_superficie_civil3d`.

---

### Task 4: Pruebas Unitarias y Smoke Test de `uvx` / Python

**Files:**
- Create: `mcp-python/tests/test_math.py`
- Create: `mcp-python/tests/test_server_tools.py`

**Interfaces:**
- Consumes: Módulos `cad_math` y `server`.
- Produces: Suite de pruebas ejecutables con `pytest` y validación de esquema MCP.

- [ ] **Step 1: Escribir pruebas unitarias para `cad_math.py`**
  - Validar cálculo analítico de distancias (triángulo 3-4-5 y 3D con Z=12) y polígono UTM de 10x5 m.
- [ ] **Step 2: Escribir prueba de introspección del servidor MCP**
  - Validar que el servidor FastMCP liste exactamente 43 herramientas con nombres conformes a la regex MCP.
- [ ] **Step 3: Ejecutar pruebas con `pytest` o script de verificación**

---

### Task 5: Documentación y Registro en Notion

**Files:**
- Modify: `README.md`
- Modify: `examples/configs/claude_desktop_config.json`
- Modify: `examples/configs/mcp_config.json`
- Register via Notion MCP: Base de datos `Tasks`.

**Interfaces:**
- Consumes: Proyecto `mcp-python/` completado.
- Produces: Guía de configuración con `uvx` en `README.md` y tarea en Notion.

- [ ] **Step 1: Actualizar `README.md` y archivos de configuración**
  - Documentar la opción preferida de inicio rápido: `uvx autocad-mcp` junto a la opción C#.
- [ ] **Step 2: Registrar la tarea en Notion**
  - `Task Name`: `Servidor MCP Python uvx` (4 palabras).
  - `Status`: `Done`.
  - `Workstream`: `Technical`.
  - `Project`: `AutoCAD MCP Server`.
- [ ] **Step 3: Generar walkthrough y presentar al usuario**
