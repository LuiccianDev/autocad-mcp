# AutoCAD AI Gateway — Documentación Técnica y Guía de Uso

**Versión:** 1.0.0  
**Target Framework:** .NET 10 (`net10.0-windows`)  
**Plataforma Host:** Autodesk AutoCAD 2027 (x64)  
**Tecnología:** C# / AutoCAD Managed .NET API (`acmgd`, `acdbmgd`, `accoremgd`)  
**Puerto de Escucha:** `http://localhost:5055/`

---

## 1. Propósito y Contexto del Proyecto

El **AutoCAD AI Gateway** es un plugin nativo para Autodesk AutoCAD 2027 que actúa como un puente de comunicación bidireccional entre Modelos de Inteligencia Artificial (agentes de desarrollo, servidores MCP, scripts externos) y la sesión activa de AutoCAD.

### ¿Por qué surgió esta solución?
1. **Limitaciones de AutoLISP puro:** Las rutinas tradicionales de AutoLISP (`.lsp`) no pueden interactuar de forma transparente con comandos definidos por plugins externos compilados o herramientas de terceros como **YQArch** (generando errores de *"Unknown command"* al invocarse desde `(command)`).
2. **Automatización en Tiempo Real:** Permite a un agente de IA interactuar en vivo con AutoCAD: consultar qué capas existen, cuántos objetos hay en pantalla, cuáles son las dimensiones del dibujo y ejecutar comandos o trazos paramétricos al instante.

### Naturaleza del Binario (.dll vs .exe)
Este proyecto es una **librería de clases administrada** (`Class Library`) que compila exclusivamente a **`AutoCAD.Gateway.dll`**. **No genera un archivo ejecutable `.exe` independiente**, ya que está diseñado para ejecutarse dentro del espacio de memoria del proceso `acad.exe` de AutoCAD 2027 tras ser cargado mediante el comando `NETLOAD`.

---

## 2. Arquitectura Modular del Gateway

El plugin implementa una arquitectura desacoplada basada en servicios, un enrutador de peticiones y sincronización defensiva con el hilo de interfaz de usuario de AutoCAD:

```
[ Agente IA / Servidor MCP ]
              │
              │  HTTP REST (Puerto 5055)
              ▼
    [ HttpGatewayServer ]  ───> Hilo en Segundo Plano (HttpListener + CORS)
              │
              ▼
       [ RequestRouter ]
        ├── POST /execute ──────────> [ AutoCADCommandDispatcher ]
        │                                      │ (Cola Concurrente)
        │                                      ▼ (Application.Idle)
        │                              [ doc.SendStringToExecute ] (\x03\x03)
        │
        └── GET /context/* ─────────> [ AutoCADContextReader ]
                                               │ (RunOnIdleAsync / TCS)
                                               ▼ (Database Transaction)
                                       [ Lectura DWG en Hilo UI ]
```

### Componentes Principales (`src/`):
* **`Main.cs`**: Implementa `IExtensionApplication`. Inicializa el plugin, emite mensajes de bienvenida en la consola de AutoCAD y garantiza el apagado ordenado del servidor HTTP al cerrar AutoCAD.
* **`Commands/GatewayCommands.cs`**: Registra los comandos de consola de AutoCAD `AI_START_SERVER` y `AI_STOP_SERVER`.
* **`Core/AutoCADCommandDispatcher.cs`**:
  * Administra la cola concurrente (`ConcurrentQueue<string>`).
  * Se suscribe al evento `Application.Idle` de AutoCAD para despachar comandos exclusivamente en el hilo principal (UI thread).
  * Se desvincula de inmediato (`-= _idleHandler`) para prevenir bucles de eventos.
  * Inyecta la secuencia defensiva `\x03\x03` (`Ctrl+C` / `ESC`) antes de cada comando para liberar comandos bloqueados previos.
* **`Core/Models/`**: Contiene los DTOs fuertemente tipados (`CommandRequest`, `DrawingInfo`, `LayerInfo`, `TextEntityInfo`, `DrawingExtentsInfo`).
* **`Services/AutoCADContextReader.cs`**:
  * Realiza transacciones seguras de solo lectura contra la base de datos de AutoCAD (`BlockTable`, `LayerTable`, etc.).
  * Ejecuta las lecturas en el hilo principal a través del helper `RunOnIdleAsync(Func<Document, string>)` desacoplado mediante `TaskCompletionSource<string>`.
* **`Server/HttpGatewayServer.cs` y `RequestRouter.cs`**:
  * Servidor HTTP basado en `System.Net.HttpListener` escuchando en `http://localhost:5055/`.
  * Soporte nativo de CORS (`Access-Control-Allow-Origin: *`, métodos `GET, POST, OPTIONS`).

---

## 3. Especificación de Endpoints REST y Seguridad

La API del Gateway expone 16 endpoints en `http://localhost:5055/` (8 de AutoCAD base y 8 de Civil 3D).

### Seguridad y Autenticación Obligatoria
* **Cabecera HTTP requerida:** `X-Gateway-Token: luiccian-autocad-token-2027` (configurable mediante la variable de entorno `AUTOCAD_GATEWAY_TOKEN`).
* **Respuesta (401 Unauthorized):** Si la cabecera no se envía o el token no coincide, el servidor rechaza la solicitud de inmediato:
  ```json
  { "status": "error", "message": "No autorizado: X-Gateway-Token invalido o ausente." }
  ```

### A. Ejecución de Comandos
* **`POST /execute`**  
  Inyecta comandos de consola, macros AutoLISP o comandos YQArch en AutoCAD.
  * **Payload:**
    ```json
    {
      "command": "_.RECTANG\n0,0\n25,15\n_.ZOOM\nE\n"
    }
    ```
  * **Respuesta (200 OK):**
    ```json
    {
      "status": "success",
      "message": "Comando encolado correctamente"
    }
    ```

### B. Inspección y Lectura de Contexto DWG
* **`GET /context/info`**: Metadatos del dibujo activo (nombre del archivo DWG, estado de solo lectura y código `INSUNITS`).
* **`GET /context/entities`**: Conteo resumen de entidades en el espacio modelo agrupadas por tipo (`Line`, `Polyline`, `Circle`, `BlockReference`, etc.).
* **`GET /context/layers`**: Lista de todas las capas del dibujo con sus propiedades:
  ```json
  {
    "status": "success",
    "data": [
      { "nombre": "PA14-EJE", "bloqueada": false, "congelada": false, "apagada": false, "color": 1 }
    ]
  }
  ```
* **`GET /context/texts`**: Lista de todos los textos (`DBText` y `MText`) en el dibujo con coordenadas espaciales `(x, y)` y capa asociada.
* **`GET /context/extents`**: Coordenadas de extensión mínima y máxima del dibujo (`minX`, `minY`, `minZ`, `maxX`, `maxY`, `maxZ`).
* **`GET /context/selection`**: Cantidad de objetos seleccionados actualmente por el usuario con el ratón.
* **`GET /context/blocks`**: Lista de definiciones de bloques disponibles (excluyendo anónimos y espacio papel).

### C. Integración Civil 3D 2027 (`/civil/*`)

El mismo `AutoCAD.Gateway.dll` funciona en **AutoCAD 2027** y en **Civil 3D 2027** (referencias `AeccDbMgd` y `AecBaseMgd` con `Private=False`). `Civil3DServiceFactory` solo instancia `Civil3DService` si `AeccDbMgd` ya está cargado en el proceso; en AutoCAD estándar usa `NullCivil3DService`. En ese caso, todos los endpoints `/civil/*` (salvo `/civil/status`) responden **409** con `civil_available: false`, sin excepciones.

**Lectura (GET):**
* **`GET /civil/status`**: Disponibilidad de Civil 3D, documento activo y conteo de superficies/alineamientos.
* **`GET /civil/surfaces`**: Superficies con cota mínima, cota máxima y número de triángulos.
* **`GET /civil/surface/elevation?x=..&y=..&surface=..`**: Cota Z del terreno en (x, y). `surface` es opcional (por defecto, la primera). Error si el punto está fuera de la superficie.
* **`GET /civil/alignments`**: Alineamientos con progresivas inicial/final, estacas (`1+250.50`) y longitud.
* **`GET /civil/alignment/station-offset?x=..&y=..&alignment=..`**: Progresiva y desfase de un punto respecto al eje.
* **`GET /civil/alignment/point-at-station?station=..&offset=..&alignment=..`**: Coordenada (x, y) de una progresiva y desfase (valida el rango del alineamiento).

**Escritura (POST, JSON):**
* **`POST /civil/alignment/create`**: Crea un alineamiento desde PIs con curvas circulares tangentes de radio fijo.
  ```json
  { "nombre": "EJE-TUNEL", "puntos": [{"x": 0, "y": 0}, {"x": 100, "y": 0}, {"x": 150, "y": 50}], "radio": 40, "capa": "PA14-EJE" }
  ```
  Validaciones: nombre obligatorio y único, mínimo 2 PIs, sin segmentos de longitud cero, valores finitos y radio ≥ 0. Si el radio no cabe (tangentes mayores que el segmento), se rechaza con un mensaje explícito. `radio` 0 o ausente genera un eje sin curvas. La capa se crea si no existe.
* **`POST /civil/surface/create`**: Crea una superficie TIN desde puntos topográficos.
  ```json
  { "nombre": "TERRENO", "puntos": [{"x": 0, "y": 0, "z": 3000}, {"x": 50, "y": 0, "z": 3005}, {"x": 0, "y": 50, "z": 3008}] }
  ```
  Validaciones: nombre obligatorio y único, mínimo 3 puntos, valores finitos.

Todas las escrituras se ejecutan en el hilo UI (`Application.Idle`) bajo `LockDocument` y transacción.

---

## 4. Estándares de Dibujo y Entorno

Al interactuar con AutoCAD a través del Gateway, rigen las siguientes directivas obligatorias:

1. **Unidades Estrictamente en Metros:**
   * `INSUNITS = 4` (Metros)
   * `MEASUREMENT = 1` (Métrico)
   * `LUNITS = 2` (Decimales), `LUPREC = 3`
2. **Calibración de Anotaciones (Cotas y Textos Proporcionales):**
   * Altura de texto: `DIMTXT = 0.22` m
   * Tamaño de flecha: `DIMASZ = 0.18` m
   * Extensión de línea de cota: `DIMEXE = 0.11` m, desfase de origen: `DIMEXO = 0.06` m
3. **Criterio de Modo:**
   * **Modo Nativo (Predeterminado para Túneles e Infraestructura):** Geometría de sección, excavación, emboquille, pernos y servicios mediante comandos nativos (`_.-PLINE`, `_.-ARC`, etc.) y capas `PA14-*`.
   * **Modo YQArch (Exclusivo para Campamento y Edificaciones):** Uso de comandos rápidos de arquitectura (`ZXW` ejes, `WW` muros, `AD`/`WD` aberturas) cuando se solicite expresamente.

---

## 5. Compilación y Puesta en Marcha

### Paso 1: Compilación (.NET 10)
```powershell
dotnet build -c Release plugins/AutoCAD.Gateway/AutoCAD.Gateway.csproj
# O compilar la solución completa:
.\scripts\build_plugins.ps1
```
El binario resultante se ubica en:  
`plugins/AutoCAD.Gateway/bin/Release/net10.0-windows/AutoCAD.Gateway.dll`

### Paso 2: Carga en AutoCAD 2027
1. En AutoCAD, abre un dibujo DWG en blanco.
2. Ejecuta el comando `NETLOAD`.
3. Selecciona la DLL compilada (`AutoCAD.Gateway.dll`).

### Paso 3: Iniciar el Servidor
1. En la consola de AutoCAD, ejecuta:
   ```text
   AI_START_SERVER
   ```
2. Verás en pantalla:
   ```text
   >>> [ON] AI Gateway Server escuchando exitosamente en http://localhost:5055/
   ```
3. Para apagar el servidor: `AI_STOP_SERVER`.

---

## 6. Ejemplos de Conexión

### Desde PowerShell:
```powershell
$payload = @{ command = "_.RECTANG`n0,0`n30,20`n_.ZOOM`nE`n" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5055/execute" -Method Post -ContentType "application/json" -Body $payload
```

### Desde Python:
```python
import requests

res = requests.post(
    "http://localhost:5055/execute",
    json={"command": "_.LINE\n0,0\n50,0\n50,20\n\n_.ZOOM\nE\n"},
)
print(res.json())
```
