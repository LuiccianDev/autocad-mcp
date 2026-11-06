# Guía de Solución de Problemas (Troubleshooting)

Este documento recopila las soluciones directas a las incidencias más frecuentes al compilar, cargar o utilizar **AutoCAD MCP Server & Gateway** (`autocad-mcp`).

---

## 1. Incidencias con `NETLOAD` en AutoCAD

### Problema: "No se puede cargar el ensamblado" o error de seguridad de Windows
**Causa:** Windows marca automáticamente los archivos `.dll` compilados o descargados de repositorios como potencialmente peligrosos (*Zone.Identifier: 3*).
**Solución:**
Abre una terminal PowerShell como Administrador y desbloquea el archivo:
```powershell
Unblock-File -Path ".\AutoCAD.Gateway\bin\Release\net10.0-windows\AutoCAD.Gateway.dll"
```
Luego, en AutoCAD, vuelve a ejecutar `NETLOAD`.

---

### Problema: "Archivo bloqueado por otro proceso" al recompilar (`CS2012`)
**Causa:** Una vez que AutoCAD carga una DLL con `NETLOAD`, el archivo queda bloqueado en memoria por el proceso `acad.exe` hasta que AutoCAD se cierra por completo.
**Solución:**
1. Cierra la instancia de AutoCAD 2027 antes de recompilar con `build.ps1`.
2. O bien, compila en una ruta temporal de desarrollo y copia el binario con un nuevo nombre de revisión.

---

## 2. Comunicación de Red y Puerto 5055

### Problema: "No se pudo iniciar el servidor HTTP: El puerto 5055 ya está en uso"
**Causa:** Hay otra instancia previa de AutoCAD en ejecución o un servicio local ocupando el puerto TCP 5055.
**Diagnóstico:**
Ejecuta en PowerShell:
```powershell
Get-NetTCPConnection -LocalPort 5055 -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, OwningProcess
```
**Solución:**
Si el proceso es un `acad.exe` huérfano, ciérralo desde el Administrador de Tareas o finalízalo con:
```powershell
Stop-Process -Id <OwningProcessId> -Force
```

---

### Problema: Error `401 Unauthorized` al invocar el Gateway
**Causa:** El encabezado HTTP `X-Gateway-Token` enviado por el cliente MCP no coincide con el configurado en AutoCAD.
**Solución:**
1. El token por defecto es `autocad-gateway-token-2027`.
2. Si has configurado la variable de entorno `AUTOCAD_GATEWAY_TOKEN`, asegúrate de que esté definida con el mismo valor tanto en la sesión de AutoCAD como en el entorno del cliente de IA (`claude_desktop_config.json` o `mcp_config.json`).
3. Prueba la conexión ejecutando:
   ```powershell
   .\examples\scripts\test_gateway.ps1
   ```

---

## 3. Comandos Colgados o Bloqueos en AutoCAD

### Problema: AutoCAD no responde a comandos inyectados o se queda "esperando"
**Causa habitual:** Un comando anterior abrió una ventana emergente modal de Windows (por ejemplo, `LAYER` o `INSERT` en lugar de sus equivalentes de línea de comandos `_.-LAYER` o `_.-INSERT`), deteniendo la recepción de texto.
**Solución:**
1. Presiona `ESC` dos veces en la ventana de AutoCAD para cancelar cualquier comando pendiente.
2. Asegúrate de que las llamadas a `ejecutar_comando_autocad` utilicen siempre el prefijo no modal `_.-` y terminen cada instrucción con un salto de línea `\n`.
3. El Gateway ya antepone la secuencia `\x03\x03` (`ESC`/`Ctrl+C`), pero no puede cerrar ventanas modales del sistema operativo una vez desplegadas.

---

## 4. Servidor MCP y Corrupción del Protocolo STDIO

### Problema: El cliente de IA (Claude/Antigravity) se desconecta o reporta "JSON-RPC parse error"
**Causa:** Algún componente o dependencia imprimió texto en `Console.Out` (`stdout`). En el protocolo MCP sobre transporte STDIO, la salida estándar está reservada de forma exclusiva y estricta para mensajes JSON-RPC válidos.
**Solución:**
- Nunca uses `Console.WriteLine` dentro de `AutoCAD.McpServer`.
- Si necesitas depurar o emitir mensajes de log, utiliza `Console.Error.WriteLine` (`stderr`) o el sistema de inyección `ILogger`. `stderr` es ignorado por el deserializador JSON-RPC y preserva la estabilidad del cliente.

---

## 5. Detección de Autodesk Civil 3D

### Problema: `verificar_estado_civil3d` retorna `isCivil3DAvailable: false` a pesar de tener Civil 3D instalado
**Causa:** Has iniciado el icono de *"Civil 3D 2027 as AutoCAD"* (perfil de AutoCAD básico sin las extensiones civiles cargadas) en lugar del acceso directo principal de Civil 3D (perfil métrico o imperial).
**Solución:**
Inicia Civil 3D desde el acceso directo oficial: **Civil 3D 2027 Metric**. Abre un dibujo con plantilla civil (`_AutoCAD Civil 3D (Metric) NCS.dwt`) y vuelve a ejecutar `NETLOAD`.
