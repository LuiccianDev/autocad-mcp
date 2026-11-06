# Política de Seguridad para AutoCAD MCP (`autocad-mcp`)

La seguridad y la integridad del entorno de diseño asistido por computadora (CAD) son prioritarias para este proyecto. Dado que **AutoCAD AI Gateway** interactúa de forma directa con la memoria y el proceso de `acad.exe`, y **AutoCAD.McpServer** se comunica mediante HTTP y STDIO, se aplican directivas rigurosas para proteger las estaciones de trabajo de los usuarios.

---

## Versiones Soportadas

Actualmente se proporciona soporte activo y parches de seguridad para las siguientes versiones:

| Componente | Versión | Estado de Soporte |
|---|---|---|
| `AutoCAD.Gateway` | v2.2.x | :white_check_mark: Soportado |
| `AutoCAD.McpServer` | v2.2.x | :white_check_mark: Soportado |
| .NET SDK | .NET 10 | :white_check_mark: Soportado |
| Autodesk Platform | AutoCAD / Civil 3D 2027 | :white_check_mark: Soportado |

---

## Modelo de Seguridad y Advertencias de Red

1. **Aislamiento en Red Local (`localhost`):**
   - El servidor HTTP de `AutoCAD.Gateway` escucha por defecto de manera exclusiva en `http://localhost:5055/`.
   - **ADVERTENCIA:** Nunca expongas el puerto `5055` a interfaces de red públicas ni lo vincules a `0.0.0.0` sin una capa de túnel cifrado (TLS/mTLS) y autenticación perimetral estricta. Cualquier cliente con acceso de red a este puerto podría inyectar comandos directamente en tu sesión activa de AutoCAD.

2. **Autenticación Basada en Tokens (`X-Gateway-Token`):**
   - Todas las solicitudes hacia el Gateway requieren el encabezado HTTP:
     ```http
     X-Gateway-Token: luiccian-autocad-token-2027
     ```
   - **Gestión Dinámica de Credenciales:** El token se evalúa en tiempo real (runtime) y no está congelado al compilar.
   - **Prioridad de Resolución:**
     1. Token en caliente asignado en la sesión de AutoCAD (`AI_SET_TOKEN` o `AI_GENERATE_TOKEN`).
     2. Variable de entorno de Windows `AUTOCAD_GATEWAY_TOKEN`.
     3. Bloque `"env"` en la configuración del cliente MCP (`claude_desktop_config.json` o `mcp_config.json`).
     4. Fallback por defecto (`luiccian-autocad-token-2027`) para desarrollo local sin fricción.
   - **Comandos de Gestión en AutoCAD:**
     - `AI_SHOW_CONFIG`: Muestra en la consola de AutoCAD el estado del servidor y el token enmascarado.
     - `AI_SET_TOKEN`: Permite ingresar un nuevo token en tiempo de ejecución sin reiniciar AutoCAD.
     - `AI_GENERATE_TOKEN`: Genera un token aleatorio criptográfico seguro (`luiccian-xxxxxxxx...`) y lo aplica de inmediato en memoria y en las variables de usuario de Windows.
   - **Script Utilitario de Sincronización:**
     Puedes generar y guardar automáticamente el token en tu máquina ejecutando:
     ```powershell
     .\scripts\set-gateway-token.ps1 -UpdateExampleConfigs
     ```

3. **Prevención de Ejecución de Comandos Maliciosos:**
   - La API inyecta automáticamente prefijos no modales `_.-` y secuencias de cancelación `\x03\x03` para evitar bloqueos del hilo principal.
   - La IA opera bajo las herramientas tipadas del servidor MCP, el cual valida tipos de datos y parámetros antes de enviar instrucciones al Gateway.

---

## Cómo Reportar una Vulnerabilidad

Si descubres una posible vulnerabilidad de seguridad en este proyecto:

1. **NO abras una incidencia pública (issue) en GitHub.**
2. Envía un reporte detallado a los mantenedores del repositorio mediante un **Security Advisory Privado** en GitHub (pestaña *Security* $\rightarrow$ *Advisories* $\rightarrow$ *New draft advisory*).
3. Incluye:
   - Descripción detallada de la vulnerabilidad.
   - Pasos para reproducirla o código de prueba de concepto (PoC).
   - Impacto potencial en la estación de trabajo o en los planos DWG.
4. Los mantenedores acusarán recibo del reporte en un plazo máximo de 48 horas laborables y coordinarán la mitigación antes de cualquier divulgación pública.
