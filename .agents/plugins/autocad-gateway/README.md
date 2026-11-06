# AutoCAD & Civil 3D AI Gateway (`autocad-gateway`)

Bundle de integración interactiva en tiempo real con **AutoCAD 2027** y **Civil 3D 2027** mediante el plugin nativo en C# (.NET 10) y el servidor Model Context Protocol (`autocad-mcp`).

## Contenido del Paquete

### Subagentes Especializados (`agents/`)
- **`cad_civil3d` (`cad-civil3d-pa14`)**: Subagente especializado en generación de planos AutoLISP en metros (`entmake`), inyección interactiva al AutoCAD AI Gateway (`http://localhost:5055/execute`) y diseño geométrico vial DG-2018 para Civil 3D.

### Servidor MCP (`mcp_config.json`)
- **`autocad-mcp`**: Expone herramientas MCP v2.2 para inspección bidireccional del dibujo, ejecución de comandos seguros en el puerto `5055` y operaciones viales en Civil 3D.

### Reglas (`rules/AGENTS.md`)
- Estándares C#/.NET 10, seguridad de hilos (`Application.Idle`), transporte autenticado (`X-Gateway-Token`) y calibración métrica estricta (`INSUNITS = 4`).

### Skills (`skills/`)
- **`autolisp-drafting`**: Estándar de diseño y dibujo técnico en AutoLISP para túneles y campamentos en AutoCAD: entidades `entmake`, unidades métricas estrictas y capas `PA14-`.
- **`clean-code`**: Principios de diseño limpio y refactorización.
- **`csharp-docs`**: Documentación XML estándar para tipos y métodos C#.
- **`dotnet-backend-patterns`**: Patrones asíncronos, inyección de dependencias y arquitectura .NET.
- **`dotnet-best-practices`**: Buenas prácticas de compilación y nulabilidad en .NET 10.
- **`dotnet-mcp-builder`**: Construcción y mantenimiento de servidores MCP v2.x en C#.
- **`yqarch-fast-drafting`**: Flujo secuencial para planos arquitectónicos con YQArch e índice de referencia en `references/yqarch_commands.md`.
