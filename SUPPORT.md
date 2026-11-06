# Canales de Soporte y Ayuda

¡Bienvenido al centro de soporte y comunidad de **AutoCAD MCP** (`autocad-mcp`)! Aquí encontrarás las vías oficiales para resolver dudas, reportar anomalías técnicas o colaborar con otros desarrolladores e ingenieros de CAD.

---

## 🧭 ¿Por dónde empezar?

Antes de abrir una consulta o reporte, te recomendamos revisar los siguientes recursos:

1. **[Guía de Inicio Rápido (Getting Started)](docs/GETTING_STARTED.md):** Paso a paso ilustrado para compilar, cargar el Gateway en AutoCAD y conectar tu cliente de IA en menos de 5 minutos.
2. **[Guía de Solución de Problemas (Troubleshooting)](docs/TROUBLESHOOTING.md):** Respuestas y soluciones a las dudas más comunes (ensamblados bloqueados en Windows con `NETLOAD`, puertos ocupados, token mismatch y comandos modales).
3. **[Referencia de Herramientas MCP](docs/TOOLS_REFERENCE.md):** Lista exhaustiva de las 27 herramientas MCP con sus parámetros, tipos y ejemplos de llamadas.
4. **[Arquitectura del Sistema](docs/ARCHITECTURE.md):** Explicación del modelo de dos capas, concurrencia en hilo UI y protocolo MCP.

---

## 💬 Canales Oficiales

### 1. Preguntas de Uso, Ideas y Comunidad (GitHub Discussions)
Para dudas del tipo *"¿Cómo configuro el servidor MCP con Cursor?"* o *"¿Cómo puedo crear un plano con YQArch usando el Gateway?"*:
- Utiliza **GitHub Discussions** en las categorías:
  - **Q&A:** Dudas de configuración o uso cotidiano.
  - **Ideas:** Propuestas de mejoras o flujos de trabajo.
  - **Show and Tell:** Comparte planos, layouts o automatizaciones logradas con el proyecto.

### 2. Reporte de Errores Técnicos (GitHub Issues)
Si has identificado un fallo en el código, una excepción en el Gateway o un comportamiento no deseado:
- Abre un **[Bug Report](.github/ISSUE_TEMPLATE/bug_report.md)** en GitHub Issues.
- Asegúrate de incluir la versión de AutoCAD, versión del SDK de .NET y los logs de la consola.

### 3. Solicitud de Nuevas Funcionalidades
Si deseas proponer una nueva herramienta MCP, soporte para una nueva versión de Civil 3D o mejoras de API:
- Abre una **[Feature Request](.github/ISSUE_TEMPLATE/feature_request.md)**.

---

## 🧪 Pruebas de Diagnóstico Rápido

Si experimentas problemas de comunicación entre la IA y AutoCAD, ejecuta el script de diagnóstico incluido:
```powershell
.\examples\scripts\test_gateway.ps1
```
Este script validará si:
- El Gateway está activo en `http://localhost:5055/`.
- El token de seguridad `X-Gateway-Token` coincide.
- AutoCAD responde correctamente a solicitudes de información de dibujo.
