## Resumen del Cambio
Descripción concisa de qué resuelve o añade este Pull Request y por qué es necesario.

Closes # (incidencia relacionada, si aplica)

---

## Tipo de Cambio
Marca con una `x` las casillas que apliquen:
- [ ] `fix`: Corrección de un error o anomalía
- [ ] `feat`: Nueva funcionalidad o herramienta MCP
- [ ] `docs`: Actualización o ampliación de la documentación
- [ ] `refactor`: Refactorización de código sin cambios en el comportamiento
- [ ] `perf`: Mejora de rendimiento en Gateway o serialización
- [ ] `ci`: Cambios en flujos de trabajo de GitHub Actions o scripts

---

## Lista de Verificación (Checklist Obligatoria)

Por favor, confirma el cumplimiento de las siguientes directivas antes de solicitar revisión:

### Arquitectura y Código (.NET 10)
- [ ] El código compila limpiamente en modo Release: `dotnet build AutoCAD_Integration.slnx -c Release` (0 errores, 0 warnings).
- [ ] Se respeta el principio de Responsabilidad Única (SRP) y contexto de nulabilidad (`#nullable enable`).
- [ ] Todo método y clase pública cuenta con documentación XML (`/// <summary>`).

### Servidor MCP (`AutoCAD.McpServer`)
- [ ] **Silenciamiento estricto de `stdout`:** No se ha introducido ningún `Console.WriteLine` o escritura directa a la salida estándar (para no corromper las tramas JSON-RPC del protocolo MCP).
- [ ] Los logs de diagnóstico se dirigen exclusivamente a `Console.Error` o a `ILogger`.

### Plugin Nativo AutoCAD (`AutoCAD.Gateway`)
- [ ] No se accede al documento activo (`MdiActiveDocument`) ni a la base de datos desde hilos secundarios; toda interacción se canaliza a través de `ConcurrentQueue` y `Application.Idle`.
- [ ] Los comandos interactivos utilizan el prefijo no modal `_.-` (ej. `_.-LAYER`, `_.-INSERT`) para evitar cuadros de diálogo bloqueantes.
- [ ] Las unidades están estandarizadas en metros (`INSUNITS = 4`).

---

## Pruebas Realizadas
Describe las pruebas ejecutadas en un entorno con AutoCAD 2027 / Civil 3D y cliente MCP (Claude Desktop, Antigravity, etc.).
