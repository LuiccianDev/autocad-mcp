# Registro de Cambios (Changelog)

Todos los cambios notables en este proyecto se documentarán en este archivo.

El formato está basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y este proyecto se adhiere a [Semantic Versioning](https://semver.org/lang/es/).

---

## [2.4.0] - 2026-11-06

### Añadido
- **Servidor MCP en Python (`mcp-python/`) con Soporte `uvx`:**
  - Implementación completa basada en FastMCP con paridad funcional total del catálogo de 43 herramientas.
  - Empaquetado moderno con Hatchling y `uv` para ejecución instantánea con `uvx autocad-mcp`.
  - Módulos `cad_math.py` (geometría analítica pura) y `client.py` (cliente HTTP asíncrono con `httpx`).
  - Suite de pruebas automatizadas con `pytest` y `pytest-asyncio` (9 tests pasando al 100%).
- **Soporte Dinámico de Red y Simplificación de Puerto:**
  - Configuración limpia mediante el número de puerto `AUTOCAD_GATEWAY_PORT: "5055"` en lugar de URLs completas con protocolo, construyendo automáticamente la URL local en C# y Python.
  - Resolución dinámica de `AUTOCAD_GATEWAY_PORT` tanto en el comando nativo `AI_START_SERVER` de AutoCAD como en los clientes C# y Python.
- **Gestión Dinámica de Tokens en AutoCAD y Utilidades:**
  - Nuevos comandos nativos de AutoCAD: `AI_SHOW_CONFIG` (inspección de estado y token), `AI_SET_TOKEN` (cambio de token en caliente en memoria) y `AI_GENERATE_TOKEN` (generador criptográfico aleatorio y persistencia en Windows).
  - Script utilitario en PowerShell `scripts/set_gateway_token.ps1` para sincronización automática de credenciales entre Windows y las configuraciones de IA.

### Corregido
- **Identidad de Marca y Autoría Oficial (Luiccian):**
  - Atribución de autoría y créditos de creación a **Luiccian** (`LuiccianDev`) en licencia MIT, paquetes NuGet/PyPI y banner de bienvenida de AutoCAD al cargar vía `NETLOAD`.
  - Estandarización del token de seguridad por defecto a `luiccian-autocad-token-2027` y generación de tokens aleatorios con prefijo `luiccian-`.
- **CORS para Diagnósticos Locales:**
  - Inclusión de `X-Gateway-Token` en las cabeceras permitidas (`Access-Control-Allow-Headers`) de `HttpGatewayServer.cs`.

---

## [2.3.0] - 2026-11-06

### Añadido
- **Expansión del Catálogo MCP a 43 Herramientas Especializadas:**
  - **Módulo `DrawingTools` (7 herramientas):** Geometría paramétrica tipada (`dibujar_linea`, `dibujar_polilinea`, `dibujar_rectangulo`, `dibujar_circulo`, `dibujar_arco`, `insertar_texto`, `insertar_bloque`) implementadas con `entmake` en metros, independientes de OSNAP y sin alterar la capa actual.
  - **Módulo `LayerTools` (3 herramientas):** Gestión y control de capas (`crear_capa`, `establecer_capa_actual`, `establecer_estado_capa` para activar/desactivar, congelar/descongelar y bloquear/desbloquear).
  - **Módulo `MeasurementTools` (4 herramientas):** Geometría computacional analítica local (`medir_distancia` 2D/3D con azimut y pendiente, `calcular_area_poligono` con fórmula de Gauss y centroide, `limpiar_y_purgar_dibujo` con auditoría `AUDIT`, y `regenerar_todo` con `REGENALL`).
  - **Módulo `Civil3DTools`:** Nueva herramienta `consultar_perfil_longitudinal` para muestreo paramétrico de cotas de terreno a lo largo de alineamientos con análisis de pendientes y cotas extremas.
  - **Utilidades de Formato `CadFormat`:** Normalización numérica invariante, filtrado de nombres de símbolos AutoCAD y parseo seguro de puntos.
- **Corrección de Compatibilidad MCP Regex:** Eliminación de caracteres acentuados en nombres de herramientas (`insertar_cajetin_parametrico`) para estricta conformidad con la especificación `^[A-Za-z0-9_.-]{1,128}\z`.

---

## [2.2.0] - 2026-11-06

### Añadido
- **Suite Documental Open Source Completa:**
  - `docs/ARCHITECTURE.md`: Documento de arquitectura integral con diagramas Mermaid C4 y ciclo de vida de concurrencia en hilo UI.
  - `docs/GETTING_STARTED.md`: Guía de inicio rápido paso a paso para desarrolladores y usuarios finales.
  - `docs/TOOLS_REFERENCE.md`: Catálogo y referencia técnica de las 27 herramientas MCP (parámetros, tipos, requeridos y ejemplos JSON).
  - `docs/TROUBLESHOOTING.md`: Guía de resolución de problemas comunes (NETLOAD, bloqueo de DLLs, puertos y token).
  - `ROADMAP.md`: Planificación estratégica e hitos futuros del proyecto.
  - `SECURITY.md`, `SUPPORT.md`, `CODE_OF_CONDUCT.md`: Estándares de gobernanza y comunidad de código abierto.
- **Plantillas de Comunidad (.github/):**
  - Plantillas de Issue para reporte de errores (`bug_report.md`) y solicitud de características (`feature_request.md`).
  - Plantilla estandarizada de Pull Request (`PULL_REQUEST_TEMPLATE.md`).
  - Workflow de Integración Continua (CI) en GitHub Actions (`ci.yml`) para .NET 10 en Windows.
- **Directorio de Ejemplos (`examples/`):**
  - Configuraciones listas para Claude Desktop (`claude_desktop_config.json`) y Antigravity / Cursor / VS Code (`mcp_config.json`).
  - Scripts de diagnóstico y prueba local en PowerShell (`test_gateway.ps1`) y Bash (`test_gateway.sh`).
  - Rutina AutoLISP de muestra paramétrica métrica (`sample_metro_tunnel.lsp`).

### Cambiado
- **Estandarización de Identidad de Marca:** Transición y unificación definitiva bajo el nombre canónico **AutoCAD MCP Server & Gateway** (`autocad-mcp`).
- Actualización de `README.md` y `CONTRIBUTING.md` con índices estructurados, enlaces directos a guías y directivas de Conventional Commits.

---

## [2.1.0] - 2026-11-05

### Añadido
- **Herramientas de Presentación, Cajetines y Ploteo PDF (5 nuevas herramientas):**
  - `crear_layout`: Creación programática de presentaciones (espacio papel).
  - `configurar_hoja_layout`: Configuración de tamaños métricos (A0 a A4), trazador `DWG To PDF.pc3` y plumillas `monochrome.ctb`.
  - `crear_viewport`: Ventana gráfica con escala métrica calculada (`1000/Escala XP`) centrada en coordenadas del modelo.
  - `insertar_cajetin_paramétrico`: Generación de cajetines técnicos normalizados con atributos dinámicos.
  - `exportar_pdf`: Trazado físico directo a formato PDF.
- **Herramienta de Visión `capturar_pantalla_autocad`:** Exportación visual del viewport activo para retroalimentación multimodal de la IA.
- **Soporte AutoLISP Dinámico:** Herramientas `ejecutar_lisp` y `cargar_archivo_lisp` para evaluar rutinas complejas y manipulación de listas `entmake`.

---

## [2.0.0] - 2026-11-04

### Añadido
- **Integración Nativa con Autodesk Civil 3D 2027:**
  - `Civil3DService` y `NullCivil3DService` mediante carga condicional con reflexión para no romper sesiones de AutoCAD estándar.
  - Herramientas MCP especializadas: inspección de superficies TIN (`listar_superficies_civil3d`), cotas de elevación Z (`consultar_elevacion_terreno`), alineamientos (`listar_alineamientos_civil3d`), cálculo de progresivas y desfasajes, y creación de alineamientos y superficies TIN.
- **Compilación en .NET 10 y C# 13:** Migración integral al SDK de .NET 10 con soporte estricto de nulabilidad (`#nullable enable`).

---

## [1.0.0] - 2026-11-01

### Añadido
- **Lanzamiento Inicial del Ecosistema:**
  - Plugin nativo `AutoCAD_AI_Gateway` con inyección segura en hilo UI vía `Application.Idle` y `ConcurrentQueue`.
  - Servidor `AutoCAD_MCP_Server` bajo protocolo MCP v2.2 y transporte STDIO.
  - Primer lote de herramientas MCP de contexto (capas, entidades, límites, bloques, textos) y ejecución de comandos no modales.
