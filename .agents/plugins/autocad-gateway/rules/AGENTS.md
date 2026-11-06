# Directivas del Plugin AutoCAD Gateway

Este plugin gobierna el desarrollo, mantenimiento y comunicación segura con el plugin nativo **AutoCAD.Gateway** (.NET 10 / C#) y su integración con AutoCAD 2027.

---

## 1. Estándares de Código C# y .NET (Clean Code & Best Practices)

1. **Responsabilidad Única (SRP):**
   - El punto de entrada (`Main.cs`) solo registra comandos de AutoCAD e inicializa el ciclo de vida de la extensión (`IExtensionApplication`).
   - La capa de red y despacho (`HttpGatewayServer.cs`) gestiona el servidor HTTP y el encolado seguro de comandos.
   - Si se añade lógica geométrica o de entidades AutoCAD en C#, debe ir en clases de servicio dedicadas bajo `Services/`.

2. **Concurrencia y Seguridad de Hilos (Thread Safety):**
   - **Prohibido** modificar directamente el documento activo de AutoCAD (`MdiActiveDocument`) desde los hilos de `HttpListener`.
   - Toda interacción con la base de datos o consola de AutoCAD **debe** canalizarse a través de la cola concurrente (`ConcurrentQueue<string>`) despachada en el evento `Application.Idle` o a través del `Dispatcher` del hilo principal.
   - Los manejadores de eventos (como `_idleHandler`) deben desacoplarse inmediatamente tras la ejecución para evitar bucles infinitos en el hilo UI.

3. **Buenas Prácticas de .NET (.NET 10):**
   - Respetar el contexto de nulabilidad (`#nullable enable`). No ignorar advertencias de tipo nulo (`CS8600`, `CS8602`, `CS8625`).
   - Usar `CancellationToken` y `async/await` estructurado para tareas de red.
   - En caso de compilaciones, usar siempre la configuración `Release`:
     ```powershell
     dotnet build -c Release AutoCAD.Gateway/AutoCAD.Gateway.csproj
     ```

4. **Documentación de Código (C# Docs):**
   - Todo método o clase pública debe contar con comentarios XML (`/// <summary>`) describiendo parámetros y excepciones posibles.

---

## 2. Protocolo de Comunicación IA <-> AutoCAD (Uso Conjunto Gateway + MCP)

El ecosistema está diseñado para que el **Gateway** (dentro de AutoCAD) y el **MCP Server** (conectado al cliente de IA) operen en conjunto de manera obligatoria:

1. **Jerarquía de Comunicación de la IA:**
   - **Canal Principal (MCP Server):** La IA debe comunicarse prioritariamente a través de las herramientas tipadas del **AutoCAD.McpServer** (`ContextTools`, `ExecutionTools`, `Civil3DTools`). Esto garantiza respuestas estructuradas, validación de parámetros y evita saturar el contexto con volcados JSON en crudo (RAW).
   - **Canal Secundario / Pruebas (PowerShell directo):** El uso de scripts PowerShell con `Invoke-RestMethod` contra el Gateway queda reservado para diagnósticos locales o pruebas manuales del usuario, no como el mecanismo ordinario de la IA.

2. **Endpoint, Transporte y Seguridad:**
   - URL: `http://localhost:5055/execute`
   - Método: `POST`
   - Content-Type: `application/json`
   - **Autenticación:** Encabezado HTTP `X-Gateway-Token: autocad-gateway-token-2027` (configurable mediante la variable de entorno `AUTOCAD_GATEWAY_TOKEN`).
   - Formato Payload:
     ```json
     {
       "command": "_.LINE\n0,0\n20,0\n\n"
     }
     ```

3. **Construcción de Comandos Seguros:**
   - **Comandos en Modo Texto:** Siempre utilizar el prefijo `_.-` para comandos que normalmente abran cuadros de diálogo modales (ej. `_.-LAYER` en lugar de `LAYER`, `_.-INSERT` en lugar de `INSERT`). Si AutoCAD muestra una ventana modal de Windows, la conexión remota se bloqueará hasta que un humano haga clic.
   - **Terminadores de Entrada:** Asegurarse de enviar los saltos de línea `\n` requeridos por cada solicitud de AutoCAD.
   - **Auto-Cancelación:** El Gateway inyecta automáticamente doble código de cancelación `\x03\x03` (`Ctrl+C` / `ESC`) antes y después del comando para garantizar que AutoCAD no quede en estado de espera ("pegado").
   - **Internacionalización:** Usar el prefijo `_.` en los comandos nativos (ej. `_.RECTANG`, `_.LINE`, `_.ZOOM`, `_.PLINE`) para asegurar compatibilidad en versiones de AutoCAD en español o inglés.

4. **Envío de Comandos de Prueba vía PowerShell:**
   - Patrón estándar de diagnóstico incluyendo el token de seguridad:
     ```powershell
     $headers = @{ "X-Gateway-Token" = "autocad-gateway-token-2027" }
     $payload = @{ command = "_.RECTANG`n0,0`n10,10`n_.ZOOM`nE`n" } | ConvertTo-Json
     Invoke-RestMethod -Uri "http://localhost:5055/execute" -Method Post -Headers $headers -ContentType "application/json" -Body $payload
     ```

---

## 3. Uso y Conocimiento de YQArch

- **Base de Conocimiento Obligatoria:** Cuando el usuario solicite crear elementos arquitectónicos usando YQArch (muros, ventanas, grillas, ejes, acotaciones), la IA **DEBE** consultar primero el archivo `yqarch_commands.md` ubicado en `.agents/plugins/autocad-gateway/skills/yqarch-fast-drafting/references/yqarch_commands.md`.
- **Prohibido Adivinar:** La IA no debe inventar comandos de YQArch. Siempre debe basarse en el diccionario oficial documentado.
- **Flujo Híbrido:** Si el comando documentado abre una ventana emergente en AutoCAD (como `AD`, `WD`, `JJ`), la IA debe inyectar el comando sin escapes (`\x03\x03`) al final, pedir al usuario que interactúe con la ventana, y esperar confirmación antes de seguir inyectando coordenadas.
- **Flujo de Trabajo (Fast Drafting):** Al iniciar el diseño de un proyecto o planta, la IA **DEBE** aplicar automáticamente el skill `yqarch-fast-drafting`. Esto obliga a seguir la secuencia estructural correcta: Ejes (`ZXW`/`AX`), Muros paramétricos (`WW`), Aberturas (`AD`/`WD`) y finalmente Anotaciones (`AZH`/`DD`), asegurando siempre la estricta separación por capas.

---

## 4. Estándar de Nomenclatura y Jerarquía de Carpetas

- **Proyectos .NET:** `PascalCase` con puntos (`AutoCAD.Gateway`, `AutoCAD.McpServer`). Sin guiones bajos ni guiones medios.
- **Subcarpetas internas:** `PascalCase` directo bajo el proyecto (`Commands/`, `Core/`, `Models/`, `Server/`, `Services/`, `Tools/`). No anidar carpetas `src/` dentro de proyectos C#.
- **Archivos C#:** `PascalCase.cs` e interfaces con prefijo `I` (`IAutoCADGatewayClient.cs`).
- **Scripts:** `kebab-case.ps1` (`build.ps1`, `start-mcp.ps1`, `set-gateway-token.ps1`).
