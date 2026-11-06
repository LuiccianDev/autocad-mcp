# Guía de Contribución a AutoCAD MCP (`autocad-mcp`)

¡Gracias por tu interés en contribuir a **AutoCAD MCP Server & Gateway** (`autocad-mcp`)! Este proyecto busca establecer un estándar de código abierto robusto, confiable y de alto rendimiento para la integración entre Modelos de Inteligencia Artificial (mediante Model Context Protocol v2.2) y Autodesk AutoCAD 2027 / Civil 3D.

Al participar en este proyecto, te comprometes a respetar y mantener nuestro [Código de Conducta](CODE_OF_CONDUCT.md).

---

## 📋 Requisitos de Entorno

1. **.NET 10 SDK** instalado (`dotnet --version` >= 10.0.100).
2. **Autodesk AutoCAD 2027** o **Civil 3D 2027** instalado en `C:\Program Files\Autodesk\AutoCAD 2027\`.
3. Editor recomendado: Visual Studio 2022/2026, Visual Studio Code (con C# Dev Kit), Cursor o Antigravity.

---

## 🏗️ Estructura del Repositorio

- `AutoCAD.Gateway/`: Plugin nativo C# (`Class Library` que compila a `net10.0-windows`). Se carga en AutoCAD con el comando `NETLOAD` y expone un servidor HTTP en el puerto `5055`.
- `AutoCAD.McpServer/`: Servidor Model Context Protocol en C# (.NET 10, transporte STDIO). Consume el Gateway vía HTTP y expone 27 herramientas estándar de MCP para clientes IA (Antigravity, Claude, Cursor, VS Code).
- `docs/`: Suite de documentación técnica (`ARCHITECTURE.md`, `GETTING_STARTED.md`, `TOOLS_REFERENCE.md`, `TROUBLESHOOTING.md`).
- `examples/`: Configuraciones de clientes IA, scripts de diagnóstico y rutinas AutoLISP de prueba.
- `.github/`: Plantillas de incidencias (issues), Pull Requests y workflows de integración continua (CI).
- `.agents/plugins/autocad-gateway/`: Especificación del plugin para agentes IA (skills, metadatos, configuraciones JSON y reglas).
- `scripts/`: Scripts de utilidad en PowerShell para compilación y verificación.

---

## 🌿 Flujo de Trabajo con Git y Ramas

1. **Crear un Fork o Rama de Trabajo:**
   Utiliza prefijos semánticos para nombrar tus ramas:
   - `feature/<nombre-de-la-caracteristica>`: Nuevas herramientas MCP o capacidades del Gateway.
   - `fix/<descripcion-del-bug>`: Correcciones de fallos o incidencias.
   - `docs/<mejora-documental>`: Ampliación de guías, diagramas o referencias.
   - `refactor/<area>`: Mejoras de código interno sin alterar el comportamiento observable.

2. **Convención de Commits (Conventional Commits):**
   Usa mensajes claros en tiempo presente siguiendo la especificación:
   - `feat: agregar herramienta MCP para consultar volúmenes de superficies`
   - `fix: corregir manejo de saltos de línea en inyección de comandos no modales`
   - `docs: actualizar diagrama C4 en ARCHITECTURE.md`
   - `refactor: optimizar encolado concurrente en AutoCADCommandDispatcher`

---

## 🛠️ Compilación y Verificación Local

Antes de enviar cualquier cambio, valida la compilación limpia de la solución completa en modo Release:

```powershell
.\scripts\build.ps1
# O manualmente:
dotnet build AutoCAD_Integration.slnx -c Release
```

El build debe concluir con:
```text
Compilación correcta: 0 Errores, 0 Advertencias
```

---

## 📐 Estándares de Código y Arquitectura

1. **Responsabilidad Única (SRP):**
   - El punto de entrada (`Main.cs`) solo gestiona el ciclo de vida de la extensión.
   - Los servicios de dibujo o lectura de bases de datos deben residir en `src/Services/`.
2. **Concurrencia y Seguridad de Hilos:**
   - **Prohibido** interactuar con la base de datos o consola de AutoCAD desde hilos secundarios. Toda orden debe despacharse mediante `ConcurrentQueue` y `Application.Idle`.
3. **Silenciamiento Estricto de `stdout`:**
   - En `AutoCAD.McpServer`, la salida estándar está reservada de forma exclusiva e inviolable para las tramas JSON-RPC. Nunca agregues `Console.WriteLine` o logs sin redirigir a `Console.Error` (`stderr`).
4. **Comandos en Modo Texto No Modal:**
   - Usa siempre el prefijo `_.-` (ej. `_.-LAYER`) y terminadores `\n` para evitar cuadros de diálogo bloqueantes de Windows.
5. **Contexto de Nulabilidad y Documentación:**
   - Todo el código debe compilar con `#nullable enable` activo sin advertencias.
   - Añade comentarios XML (`/// <summary>`) a todos los métodos y tipos públicos.

---

## 🚀 Envío de Pull Requests

1. Sube tu rama a GitHub y abre un Pull Request contra la rama principal (`main`).
2. Completa la plantilla de Pull Request (`.github/PULL_REQUEST_TEMPLATE.md`), marcando los puntos de la lista de verificación.
3. Asegúrate de que el workflow de GitHub Actions (`CI Build (.NET 10)`) pase en verde.
4. Un mantenedor del proyecto revisará tu contribución y te brindará retroalimentación constructiva.
