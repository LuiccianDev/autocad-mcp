---
name: Reporte de Error / Incidencia Técnica
about: Informar sobre un problema o fallo en AutoCAD AI Gateway o AutoCAD MCP Server
title: "[BUG] <descripción breve del problema>"
labels: ["bug", "triage"]
assignees: ""
---

## Descripción del Problema
Una descripción clara y concisa de lo que sucede de forma anómala.

## Componente Afectado
Selecciona el componente donde ocurre la falla:
- [ ] `AutoCAD.Gateway` (Plugin nativo C# en `acad.exe`)
- [ ] `AutoCAD.McpServer` (Servidor de consola STDIO MCP v2.2)
- [ ] Comunicación HTTP / Red (puerto `5055` o cabecera `X-Gateway-Token`)
- [ ] Integración con Civil 3D (`AeccDbMgd` / Superficies / Alineamientos)
- [ ] Cliente IA (Claude Desktop / Antigravity / Cursor / VS Code)

## Entorno de Ejecución
Por favor, completa los detalles técnicos de tu instalación:
- **Sistema Operativo:** Windows 10 / 11 (x64)
- **Versión de AutoCAD / Civil 3D:** (ej. AutoCAD 2027.0.1 / Civil 3D 2027)
- **Versión de .NET SDK:** (ejecuta `dotnet --version` en la terminal)
- **Cliente de IA utilizado:** (ej. Antigravity v2.0, Claude Desktop 0.8.x, Cursor)

## Pasos para Reproducir
1. Ejecutar comando en CAD / llamada a la herramienta MCP: `...`
2. Parámetros enviados en el payload: `...`
3. Observar el error en la consola: `...`

## Comportamiento Esperado
Descripción clara de lo que debería haber sucedido (ej. "La capa debía crearse sin abrir diálogos modales").

## Salida de Consola y Logs
```text
Pega aquí la salida de la consola de AutoCAD (F2) o los logs de stderr del servidor MCP.
```

## Información Adicional
Cualquier contexto adicional sobre el archivo DWG, unidades configuradas (`INSUNITS`), etc.
