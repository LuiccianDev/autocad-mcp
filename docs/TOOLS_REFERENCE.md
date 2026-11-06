# Manual de Referencia Técnica de Herramientas MCP (43 Tools)

Este documento es la especificación técnica exhaustiva de las **43 herramientas** implementadas en **`AutoCAD.McpServer`** para su consumo por Modelos de Inteligencia Artificial (Claude, Antigravity, Cursor, ChatGPT) y desarrolladores bajo el estándar Model Context Protocol (v2.2).

Todas las coordenadas, dimensiones y cotas se interpretan de forma estricta en **metros** (`INSUNITS = 4`).

---

## 🧭 Mecanismos Internos de Ejecución

Cada herramienta MCP utiliza una de cuatro vías de ejecución diseñadas para garantizar máxima velocidad, predictibilidad y seguridad de hilos:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       MODALIDADES DE EJECUCIÓN                              │
├──────────────────────────┬──────────────────────────────────────────────────┤
│ 1. Cálculo Analítico     │ Se evalúa localmente en memoria del servidor     │
│    Local (Standalone)    │ MCP (.NET 10). Cero latencia, no requiere CAD.   │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ 2. Inyección AutoLISP    │ Genera expresiones (entmake ...) o llamadas VLA. │
│    Paramétrica (entmake) │ Atómico, independiente de OSNAP y capas activas. │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ 3. Comando No Modal      │ Inyecta instrucciones (_.-) en cola concurrente  │
│    (Application.Idle)    │ canalizadas hacia el hilo UI principal de CAD.   │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ 4. Endpoint REST         │ Consume APIs transaccionales del Gateway         │
│    Gateway (/civil, /ctx)│ (ObjectARX / AeccDbMgd en http://localhost:5055) │
└──────────────────────────┴──────────────────────────────────────────────────┘
```

---

## 📑 Índice General por Módulos

- [Módulo 1: Inspección, Contexto y Visión (8 herramientas)](#módulo-1-inspección-contexto-y-visión)
- [Módulo 2: Geometría y Dibujo Paramétrico Tipado (7 herramientas)](#módulo-2-geometría-y-dibujo-paramétrico-tipado)
- [Módulo 3: Gestión Avanzada de Capas (3 herramientas)](#módulo-3-gestión-avanzada-de-capas)
- [Módulo 4: Medición, Geometría Computacional y Mantenimiento (4 herramientas)](#módulo-4-medición-geometría-computacional-y-mantenimiento)
- [Módulo 5: Ejecución, Control y AutoLISP (7 herramientas)](#módulo-5-ejecución-control-y-autolisp)
- [Módulo 6: Presentaciones, Cajetines y Ploteo PDF (5 herramientas)](#módulo-6-presentaciones-cajetines-y-ploteo-pdf)
- [Módulo 7: Autodesk Civil 3D (9 herramientas)](#módulo-7-autodesk-civil-3d)

---

## Módulo 1: Inspección, Contexto y Visión

Clase C#: `AutoCAD.McpServer.Tools.ContextTools`  
Mecanismo: **Endpoint REST Gateway (`GET /context/*`)**

---

### 1. `obtener_info_dibujo`
- **Propósito:** Inspeccionar los metadatos globales del dibujo DWG abierto en AutoCAD.
- **Mecanismo:** `GET /context/info`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "obtener_info_dibujo", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "DrawingName": "TUNEL_METRO_CASMAS_TRAMO1.dwg",
    "Path": "C:\\Proyectos\\Metro\\TUNEL_METRO_CASMAS_TRAMO1.dwg",
    "IsReadOnly": false,
    "UnitsCode": 4,
    "UnitsDescription": "Metros (INSUNITS = 4)"
  }
  ```

---

### 2. `leer_capas_autocad`
- **Propósito:** Obtener el inventario de capas del dibujo con sus colores, visibilidad y bloqueos.
- **Mecanismo:** `GET /context/layers`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "leer_capas_autocad", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  [
    { "Name": "0", "Color": 7, "IsOn": true, "IsFrozen": false, "IsLocked": false },
    { "Name": "TUN-REVESTIMIENTO", "Color": 4, "IsOn": true, "IsFrozen": false, "IsLocked": false }
  ]
  ```

---

### 3. `leer_objetos_autocad`
- **Propósito:** Contar y resumir todas las entidades geométricas del espacio modelo (`ModelSpace`).
- **Mecanismo:** `GET /context/entities`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "leer_objetos_autocad", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "TotalEntities": 1420,
    "CountsByType": {
      "AcDbLine": 450,
      "AcDbPolyline": 320,
      "AcDbCircle": 180,
      "AcDbText": 120,
      "AcDbBlockReference": 350
    }
  }
  ```

---

### 4. `obtener_limites_dibujo`
- **Propósito:** Obtener la caja delimitadora (`ExtMin`, `ExtMax`) y el delta métrico del dibujo.
- **Mecanismo:** `GET /context/extents`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "obtener_limites_dibujo", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "MinX": 500000.0, "MinY": 8000000.0, "MinZ": 0.0,
    "MaxX": 501250.0, "MaxY": 8000850.0, "MaxZ": 45.2,
    "DeltaX": 1250.0, "DeltaY": 850.0
  }
  ```

---

### 5. `leer_textos_autocad`
- **Propósito:** Buscar y extraer cadenas de texto de objetos `DBText` y `MText` con sus coordenadas (X, Y).
- **Mecanismo:** `GET /context/texts`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "leer_textos_autocad", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  [
    { "Text": "PROG. 0+250", "Layer": "TUN-ANOT", "X": 500250.0, "Y": 8000120.0, "Z": 0.0, "Height": 0.5 }
  ]
  ```

---

### 6. `listar_bloques_disponibles`
- **Propósito:** Listar definiciones de bloques cargados en el archivo actual.
- **Mecanismo:** `GET /context/blocks`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "listar_bloques_disponibles", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  [ "DOVELA_TIPO_A", "DOVELA_TIPO_B", "PERNO_ANCLAJE", "SENAL_EMERGENCIA" ]
  ```

---

### 7. `leer_seleccion_autocad`
- **Propósito:** Leer cuántos objetos y de qué tipos están seleccionados por el usuario con el ratón.
- **Mecanismo:** `GET /context/selection`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "leer_seleccion_autocad", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  { "Count": 3, "Types": [ "AcDbPolyline", "AcDbPolyline", "AcDbCircle" ] }
  ```

---

### 8. `capturar_pantalla_autocad`
- **Propósito:** Captura PNG de la ventana gráfica activa para inspección multimodal por la IA.
- **Mecanismo:** `GET /context/screenshot`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "capturar_pantalla_autocad", "arguments": {} }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "status": "success",
    "image_path": "C:\\Users\\...\\AppData\\Local\\Temp\\acad_screen_20261106_113000.png",
    "width": 1920,
    "height": 1080
  }
  ```

---

## Módulo 2: Geometría y Dibujo Paramétrico Tipado

Clase C#: `AutoCAD.McpServer.Tools.DrawingTools`  
Mecanismo: **Inyección AutoLISP `(entmake ...)`**  
*Ventajas clave:* Cero dependencia de OSNAP, no cambia la capa actual y crea la capa destino automáticamente si no existe.

---

### 9. `dibujar_linea`
- **Propósito:** Traza un segmento de línea recta 3D entre dos puntos métricos.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `x1` | `double` | `number` | Sí | - | Coordenada X inicial (m). |
  | `y1` | `double` | `number` | Sí | - | Coordenada Y inicial (m). |
  | `x2` | `double` | `number` | Sí | - | Coordenada X final (m). |
  | `y2` | `double` | `number` | Sí | - | Coordenada Y final (m). |
  | `z1` | `double` | `number` | No | `0.0` | Cota Z inicial (m). |
  | `z2` | `double` | `number` | No | `0.0` | Cota Z final (m). |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "dibujar_linea",
    "arguments": { "x1": 0.0, "y1": 0.0, "x2": 20.0, "y2": 0.0, "capa": "TUN-EJE" }
  }
  ```
- **Respuesta:** `"Éxito: El comando se inyectó correctamente en AutoCAD."`

---

### 10. `dibujar_polilinea`
- **Propósito:** Traza una polilínea ligera 2D (`LWPOLYLINE`) abierta o cerrada a partir de vértices.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `puntos` | `string` | `string` | Sí | - | Vértices en formato `"x1,y1;x2,y2;..."` (mín. 2). |
  | `cerrada` | `bool` | `boolean` | No | `false` | Cierra uniendo último vértice con primero. |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "dibujar_polilinea",
    "arguments": {
      "puntos": "0,0;10,0;10,5;0,5",
      "cerrada": true,
      "capa": "TUN-REVESTIMIENTO"
    }
  }
  ```

---

### 11. `dibujar_rectangulo`
- **Propósito:** Traza un rectángulo cerrado definido por dos esquinas diagonales opuestas.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `x1`, `y1` | `double` | `number` | Sí | - | Primera esquina (m). |
  | `x2`, `y2` | `double` | `number` | Sí | - | Esquina opuesta (m). |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "dibujar_rectangulo",
    "arguments": { "x1": 5.0, "y1": 2.0, "x2": 15.0, "y2": 8.0, "capa": "ARQ-MUROS" }
  }
  ```

---

### 12. `dibujar_circulo`
- **Propósito:** Traza un círculo paramétrico dado su centro y radio.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `centroX`, `centroY` | `double` | `number` | Sí | - | Coordenadas del centro (m). |
  | `radio` | `double` | `number` | Sí | - | Radio en metros (> 0). |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "dibujar_circulo",
    "arguments": { "centroX": 0.0, "centroY": 0.0, "radio": 4.50, "capa": "TUN-EXCAVACION" }
  }
  ```

---

### 13. `dibujar_arco`
- **Propósito:** Traza un arco circular en sentido antihorario.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `centroX`, `centroY` | `double` | `number` | Sí | - | Coordenadas del centro (m). |
  | `radio` | `double` | `number` | Sí | - | Radio en metros (> 0). |
  | `anguloInicio` | `double` | `number` | Sí | - | Ángulo inicial en grados (0 = Este). |
  | `anguloFin` | `double` | `number` | Sí | - | Ángulo final en grados. |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "dibujar_arco",
    "arguments": {
      "centroX": 0.0, "centroY": 0.0, "radio": 4.5,
      "anguloInicio": 0.0, "anguloFin": 180.0, "capa": "TUN-BOVEDA"
    }
  }
  ```

---

### 14. `insertar_texto`
- **Propósito:** Inserta un texto de una sola línea (`DBText`) en el espacio modelo.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `texto` | `string` | `string` | Sí | - | Contenido del texto. |
  | `x`, `y` | `double` | `number` | Sí | - | Coordenadas de inserción (m). |
  | `altura` | `double` | `number` | No | `0.25` | Altura del texto en metros (> 0). |
  | `rotacion` | `double` | `number` | No | `0.0` | Ángulo en grados sexagesimales. |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "insertar_texto",
    "arguments": { "texto": "SECCIÓN TIPO PK 1+200", "x": 10.0, "y": 2.0, "altura": 0.35, "capa": "TUN-ROTULOS" }
  }
  ```

---

### 15. `insertar_bloque`
- **Propósito:** Inserta una referencia de un bloque ya definido en el dibujo.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombreBloque` | `string` | `string` | Sí | - | Nombre del bloque existente. |
  | `x`, `y` | `double` | `number` | Sí | - | Coordenadas de inserción (m). |
  | `escala` | `double` | `number` | No | `1.0` | Factor de escala uniforme (> 0). |
  | `rotacion` | `double` | `number` | No | `0.0` | Ángulo en grados sexagesimales. |
  | `capa` | `string` | `string` | No | `"0"` | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "insertar_bloque",
    "arguments": { "nombreBloque": "DOVELA_TIPO_A", "x": 0.0, "y": 4.5, "escala": 1.0, "rotacion": 45.0 }
  }
  ```

---

## Módulo 3: Gestión Avanzada de Capas

Clase C#: `AutoCAD.McpServer.Tools.LayerTools`  
Mecanismo: **ActiveX AutoLISP (`vla-*`) con manejo de errores seguro**

---

### 16. `crear_capa`
- **Propósito:** Crea o actualiza una capa con color ACI, tipo de línea y descripción sin cuadros de diálogo modales. Si el tipo de línea no está cargado, intenta cargarlo desde `acadiso.lin`.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Sí | - | Nombre de la capa (ej. `EST-COLUMNAS`). |
  | `colorAci` | `int` | `integer` | No | `7` | Código ACI entre 1 y 255. |
  | `tipoLinea` | `string` | `string` | No | `"Continuous"` | Nombre del tipo de línea. |
  | `descripcion`| `string` | `string` | No | `""` | Descripción o nota técnica. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "crear_capa",
    "arguments": { "nombre": "EST-VIGAS", "colorAci": 1, "tipoLinea": "Continuous", "descripcion": "Vigas principales de hormigón" }
  }
  ```

---

### 17. `establecer_capa_actual`
- **Propósito:** Fija la capa de trabajo activa (`CLAYER`).
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Sí | - | Nombre de capa existente y no congelada. |
- **Ejemplo de Invocación:**
  ```json
  { "name": "establecer_capa_actual", "arguments": { "nombre": "TUN-REVESTIMIENTO" } }
  ```

---

### 18. `establecer_estado_capa`
- **Propósito:** Cambia la visibilidad, congelación o bloqueo de una capa y regenera la vista.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Sí | - | Nombre de la capa. |
  | `accion` | `string` | `string` | Sí | - | `activar`, `desactivar`, `congelar`, `descongelar`, `bloquear`, `desbloquear`. |
- **Ejemplo de Invocación:**
  ```json
  { "name": "establecer_estado_capa", "arguments": { "nombre": "TUN-AUXILIAR", "accion": "congelar" } }
  ```

---

## Módulo 4: Medición, Geometría Computacional y Mantenimiento

Clase C#: `AutoCAD.McpServer.Tools.MeasurementTools`  
Mecanismo: **Cálculo analítico local en .NET 10** (para mediciones y áreas) / **Comandos no modales** (para purga y `REGENALL`).

---

### 19. `medir_distancia`
- **Propósito:** Calcula distancia 2D/3D, incrementos métricos, azimut y pendiente entre dos puntos. Se evalúa de inmediato sin requerir AutoCAD abierto.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `x1`, `y1` | `double` | `number` | Sí | - | Coordenadas punto inicial (m). |
  | `x2`, `y2` | `double` | `number` | Sí | - | Coordenadas punto final (m). |
  | `z1`, `z2` | `double` | `number` | No | `0.0` | Cotas Z de ambos puntos (m). |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "medir_distancia",
    "arguments": { "x1": 0.0, "y1": 0.0, "z1": 0.0, "x2": 3.0, "y2": 4.0, "z2": 12.0 }
  }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "status": "success",
    "distancia_2d": 5.0,
    "distancia_3d": 13.0,
    "delta_x": 3.0, "delta_y": 4.0, "delta_z": 12.0,
    "angulo_grados": 53.1301,
    "pendiente_porcentaje": 240.0,
    "unidades": "metros"
  }
  ```

---

### 20. `calcular_area_poligono`
- **Propósito:** Calcula área en $m^2$, hectáreas, perímetro, centroide y sentido de giro de un polígono cerrado dado por sus vértices (algoritmo de Gauss con traslación para coordenadas UTM).
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `puntos` | `string` | `string` | Sí | - | Vértices `"x1,y1;x2,y2;..."` (mín. 3). |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "calcular_area_poligono",
    "arguments": { "puntos": "500000,8000000;500010,8000000;500010,8000005;500000,8000005" }
  }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "status": "success",
    "area_m2": 50.0,
    "area_hectareas": 0.005,
    "perimetro_m": 30.0,
    "centroide": { "x": 500005.0, "y": 8000002.5 },
    "sentido": "antihorario",
    "vertices": 4
  }
  ```

---

### 21. `limpiar_y_purgar_dibujo`
- **Propósito:** Purga definiciones huérfanas en dos pasadas consecutivas con `_.-PURGE` y repara la base de datos DWG con `_.AUDIT`.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `tipo` | `string` | `string` | No | `"todo"` | `todo`, `bloques`, `capas`, `tipos_linea`, `estilos_texto`, `estilos_cota`. |
  | `auditar` | `bool` | `boolean` | No | `true` | Si es `true`, ejecuta `AUDIT` con corrección. |
- **Ejemplo de Invocación:**
  ```json
  { "name": "limpiar_y_purgar_dibujo", "arguments": { "tipo": "todo", "auditar": true } }
  ```

---

### 22. `regenerar_todo`
- **Propósito:** Fuerza la regeneración de todas las ventanas gráficas (`_.REGENALL`).
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "regenerar_todo", "arguments": {} }
  ```

---

## Módulo 5: Ejecución, Control y AutoLISP

Clase C#: `AutoCAD.McpServer.Tools.ExecutionTools`  
Mecanismo: **Inyección en cola concurrente (`Application.Idle`) con terminadores `\n` y auto-cancelación `\x03\x03`**

---

### 23. `ping_autocad_gateway`
- **Propósito:** Verifica que el Gateway de AutoCAD esté activo y responda en `http://localhost:5055/`.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "ping_autocad_gateway", "arguments": {} }
  ```

---

### 24. `ejecutar_comando_autocad`
- **Propósito:** Inyecta secuencias de comandos nativos o de plugins (ej. YQArch) en modo texto no modal (`_.-`).
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `command` | `string` | `string` | Sí | Cadena de comandos con saltos `\n` como Enter. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "ejecutar_comando_autocad",
    "arguments": { "command": "_.CIRCLE\n0,0\n5.0\n_.ZOOM\nE\n" }
  }
  ```

---

### 25. `ejecutar_lisp`
- **Propósito:** Evalúa código AutoLISP dinámico directamente en la memoria de AutoCAD.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `codigoLisp` | `string` | `string` | Sí | Expresión AutoLISP completa. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "ejecutar_lisp",
    "arguments": { "codigoLisp": "(setvar \"OSMODE\" 0)" }
  }
  ```

---

### 26. `cargar_archivo_lisp`
- **Propósito:** Carga y ejecuta un archivo `.lsp` externo desde disco mediante `(load ...)`.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `rutaArchivoLsp`| `string`| `string` | Sí | Ruta absoluta en Windows. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "cargar_archivo_lisp",
    "arguments": { "rutaArchivoLsp": "C:/scripts/sample_metro_tunnel.lsp" }
  }
  ```

---

### 27. `configurar_entorno_dibujo`
- **Propósito:** Estandariza el entorno métrico: `INSUNITS = 4` (metros), `MEASUREMENT = 1`, `LUNITS = 2`, `LUPREC = 3`, y calibra cotas/textos para evitar anotaciones sobredimensionadas.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "configurar_entorno_dibujo", "arguments": {} }
  ```

---

### 28. `zoom_extension`
- **Propósito:** Encuadre total (`_.ZOOM E`) en la ventana activa.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "zoom_extension", "arguments": {} }
  ```

---

### 29. `guardar_dibujo`
- **Propósito:** Guarda el archivo DWG en disco (`_.QSAVE`).
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "guardar_dibujo", "arguments": {} }
  ```

---

## Módulo 6: Presentaciones, Cajetines y Ploteo PDF

Clase C#: `AutoCAD.McpServer.Tools.PlottingTools`  
Mecanismo: **Espacio papel (`PSPACE`), ventanas gráficas escaladas en XP y motor virtual `DWG To PDF.pc3`**

---

### 30. `crear_layout`
- **Propósito:** Crea una nueva presentación (espacio papel) y activa la pestaña.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `nombreLayout` | `string` | `string` | Sí | Nombre único de la lámina (ej. `PLANO-TUNEL-01`). |
- **Ejemplo de Invocación:**
  ```json
  { "name": "crear_layout", "arguments": { "nombreLayout": "LAMINA-01" } }
  ```

---

### 31. `configurar_hoja_layout`
- **Propósito:** Configura tamaño normalizado (A0-A4), trazador `DWG To PDF.pc3` y escala 1:1 en mm con plumillas `monochrome.ctb`.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombreLayout` | `string` | `string` | Sí | - | Nombre de la presentación. |
  | `tamanoHoja` | `string` | `string` | No | `"A1"` | `A0`, `A1`, `A2`, `A3`, `A4`. |
  | `estiloTrazado`| `string`| `string` | No | `"monochrome.ctb"` | Tabla de estilos de ploteo. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "configurar_hoja_layout",
    "arguments": { "nombreLayout": "LAMINA-01", "tamanoHoja": "A1" }
  }
  ```

---

### 32. `crear_viewport`
- **Propósito:** Inserta una ventana gráfica (Viewport / `MVIEW`) con escala métrica calculada (`1000/Escala XP`) y centra el modelo en (X, Y).
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `xInferior`, `yInferior` | `double` | `number` | Sí | Esquina inferior izquierda en el papel (mm). |
  | `ancho`, `alto` | `double` | `number` | Sí | Dimensiones de la ventana en el papel (mm). |
  | `escala` | `double` | `number` | Sí | Escala numérica (ej. `1000` para 1:1000, `500` para 1:500). |
  | `xCentroModelo`, `yCentroModelo` | `double` | `number` | Sí | Coordenadas centrales del espacio modelo (m). |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "crear_viewport",
    "arguments": {
      "xInferior": 25.0, "yInferior": 25.0, "ancho": 500.0, "alto": 350.0,
      "escala": 500.0, "xCentroModelo": 500100.0, "yCentroModelo": 8000050.0
    }
  }
  ```

---

### 33. `insertar_cajetin_parametrico`
- **Propósito:** Dibuja un cajetín técnico formal normalizado (180x45 mm) con rótulos en espacio papel.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `xEsquina`, `yEsquina` | `double` | `number` | Sí | - | Coordenadas esquina inf. derecha de la lámina (mm). |
  | `tituloProyecto` | `string` | `string` | No | `"PROYECTO DE INGENIERÍA"` | Nombre de la obra. |
  | `nombrePlano` | `string` | `string` | No | `"PLANO GENERAL"` | Contenido del plano. |
  | `codigoLamina` | `string` | `string` | No | `"L-01"` | Código de lámina. |
  | `escalaTexto` | `string` | `string` | No | `"1:1000"` | Texto de escala indicado. |
  | `autor` | `string` | `string` | No | `"INGENIERÍA"` | Profesional proyectista. |
  | `fecha` | `string` | `string` | No | Fecha actual | Fecha indicada en el membrete. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "insertar_cajetin_parametrico",
    "arguments": {
      "xEsquina": 820.0, "yEsquina": 20.0,
      "tituloProyecto": "METRO LÍNEA 1",
      "nombrePlano": "SECCIÓN TIPO Y DETALLES",
      "codigoLamina": "TUN-01",
      "escalaTexto": "1:500",
      "autor": "ING. CIVIL"
    }
  }
  ```

---

### 34. `exportar_pdf`
- **Propósito:** Traza la presentación físicamente generando un archivo PDF en disco con `DWG To PDF.pc3`.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombreLayout` | `string` | `string` | Sí | - | Nombre de la presentación a trazar. |
  | `rutaPdf` | `string` | `string` | Sí | - | Ruta absoluta del archivo PDF de salida. |
  | `estiloCtb` | `string` | `string` | No | `"monochrome.ctb"` | Estilos de plumillas. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "exportar_pdf",
    "arguments": {
      "nombreLayout": "LAMINA-01",
      "rutaPdf": "C:/Proyectos/Metro/Plano_Tunel_01.pdf"
    }
  }
  ```

---

## Módulo 7: Autodesk Civil 3D

Clase C#: `AutoCAD.McpServer.Tools.Civil3DTools`  
Mecanismo: **Endpoints REST especializados (`/civil/*`) y reflexión dinámica sobre `AeccDbMgd.dll`**

---

### 35. `verificar_estado_civil3d`
- **Propósito:** Detecta si la sesión abierta corresponde a Civil 3D y reporta recuentos de superficies y alineamientos.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "verificar_estado_civil3d", "arguments": {} }
  ```
- **Respuesta:**
  ```json
  { "status": "success", "civil_available": true, "surfaces_count": 2, "alignments_count": 1 }
  ```

---

### 36. `listar_superficies_civil3d`
- **Propósito:** Inventario de superficies TIN con cotas mínimas/máximas y triángulos.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "listar_superficies_civil3d", "arguments": {} }
  ```
- **Respuesta:**
  ```json
  {
    "status": "success",
    "data": [
      {
        "nombre": "TERRENO_NATURAL",
        "cota_minima": 2450.2,
        "cota_maxima": 2680.8,
        "triangulos": 45200
      }
    ]
  }
  ```

---

### 37. `consultar_elevacion_terreno`
- **Propósito:** Cota Z exacta de una superficie TIN en una coordenada (X, Y).
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `x`, `y` | `double` | `number` | Sí | Coordenadas Este y Norte (m). |
  | `superficie` | `string?` | `string` | No | Nombre de la superficie TIN (por defecto primera). |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "consultar_elevacion_terreno",
    "arguments": { "x": 500250.0, "y": 8000100.0, "superficie": "TERRENO_NATURAL" }
  }
  ```
- **Respuesta:**
  ```json
  { "status": "success", "superficie": "TERRENO_NATURAL", "x": 500250.0, "y": 8000100.0, "elevacion": 2514.85 }
  ```

---

### 38. `listar_alineamientos_civil3d`
- **Propósito:** Lista todos los alineamientos horizontales con progresivas y longitudes.
- **Parámetros:** Ninguno.
- **Ejemplo de Invocación:**
  ```json
  { "name": "listar_alineamientos_civil3d", "arguments": {} }
  ```
- **Respuesta:**
  ```json
  {
    "status": "success",
    "data": [
      {
        "nombre": "EJE_VIAL_PRINCIPAL",
        "progresiva_inicial": 0.0,
        "progresiva_final": 2450.0,
        "estaca_inicial": "0+000.00",
        "estaca_final": "2+450.00",
        "longitud": 2450.0
      }
    ]
  }
  ```

---

### 39. `consultar_estacion_alineamiento`
- **Propósito:** Calcula progresiva métrica y desfasaje lateral (+ derecha / - izquierda) de un punto respecto al eje.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `x`, `y` | `double` | `number` | Sí | Coordenadas Este y Norte (m). |
  | `alineamiento` | `string?` | `string` | No | Nombre del alineamiento. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "consultar_estacion_alineamiento",
    "arguments": { "x": 500248.0, "y": 8000105.0, "alineamiento": "EJE_VIAL_PRINCIPAL" }
  }
  ```
- **Respuesta:**
  ```json
  {
    "status": "success",
    "alineamiento": "EJE_VIAL_PRINCIPAL",
    "progresiva": 250.0,
    "estaca": "0+250.00",
    "desfase": -3.5
  }
  ```

---

### 40. `obtener_coordenada_en_estacion`
- **Propósito:** Operación inversa: calcula coordenadas absolutas (X, Y) dadas una progresiva y un desfasaje.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `progresiva` | `double` | `number` | Sí | - | Progresiva métrica (ej. `1250.5`). |
  | `desfase` | `double` | `number` | No | `0.0` | Desfasaje lateral en metros. |
  | `alineamiento` | `string?`| `string` | No | null | Nombre del alineamiento. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "obtener_coordenada_en_estacion",
    "arguments": { "progresiva": 500.0, "desfase": 2.5, "alineamiento": "EJE_VIAL_PRINCIPAL" }
  }
  ```
- **Respuesta:**
  ```json
  {
    "status": "success",
    "alineamiento": "EJE_VIAL_PRINCIPAL",
    "progresiva": 500.0,
    "desfase": 2.5,
    "x": 500498.25,
    "y": 8000102.10
  }
  ```

---

### 41. `consultar_perfil_longitudinal`
- **Propósito:** Muestrea de forma regular el perfil longitudinal del terreno a lo largo de un eje cada `intervalo` metros. Devuelve cota mínima, máxima, pendiente media del tramo y listado de progresivas con cotas Z.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `intervalo` | `double` | `number` | No | `20.0` | Distancia métrica entre muestras (> 0). |
  | `alineamiento` | `string?`| `string` | No | null | Nombre del eje horizontal. |
  | `superficie` | `string?` | `string` | No | null | Nombre de la superficie TIN. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "consultar_perfil_longitudinal",
    "arguments": { "intervalo": 50.0, "alineamiento": "EJE_VIAL_PRINCIPAL" }
  }
  ```
- **Respuesta de Éxito:**
  ```json
  {
    "status": "success",
    "alineamiento": "EJE_VIAL_PRINCIPAL",
    "superficie": "TERRENO_NATURAL",
    "intervalo": 50.0,
    "progresiva_inicial": 0.0,
    "progresiva_final": 250.0,
    "muestras_total": 6,
    "cota_minima": 2450.0,
    "cota_maxima": 2465.5,
    "pendiente_media_porcentaje": 6.2,
    "muestras": [
      { "progresiva": 0.0, "x": 500000.0, "y": 8000000.0, "cota": 2450.0 },
      { "progresiva": 50.0, "x": 500050.0, "y": 8000000.0, "cota": 2453.2 },
      { "progresiva": 100.0, "x": 500100.0, "y": 8000000.0, "cota": 2456.8 },
      { "progresiva": 150.0, "x": 500150.0, "y": 8000000.0, "cota": 2460.1 },
      { "progresiva": 200.0, "x": 500200.0, "y": 8000000.0, "cota": 2463.0 },
      { "progresiva": 250.0, "x": 500250.0, "y": 8000000.0, "cota": 2465.5 }
    ]
  }
  ```

---

### 42. `crear_alineamiento_civil3d`
- **Propósito:** Crea un alineamiento horizontal paramétrico en Civil 3D a partir de una secuencia de PIs con curvas circulares tangentes de radio fijo.
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Default | Descripción |
  |---|---|---|---|---|---|
  | `nombre` | `string` | `string` | Sí | - | Nombre único del alineamiento. |
  | `puntos` | `string` | `string` | Sí | - | PIs `"x1,y1;x2,y2;..."` (mín. 2). |
  | `radio` | `double` | `number` | No | `0.0` | Radio en metros (0 = sin curvas). |
  | `capa` | `string?` | `string` | No | null | Capa destino. |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "crear_alineamiento_civil3d",
    "arguments": {
      "nombre": "VARIANTE_NORTE",
      "puntos": "500000,8000000;500500,8000200;501000,8000100",
      "radio": 150.0
    }
  }
  ```

---

### 43. `crear_superficie_civil3d`
- **Propósito:** Genera una nueva superficie TIN topográfica a partir de una nube de puntos (X, Y, Z).
- **Parámetros:**
  | Parámetro | Tipo C# | Tipo JSON | Requerido | Descripción |
  |---|---|---|---|---|
  | `nombre` | `string` | `string` | Sí | Nombre único de la superficie. |
  | `puntos` | `string` | `string` | Sí | Puntos `"x1,y1,z1;x2,y2,z2;..."` (mín. 3). |
- **Ejemplo de Invocación:**
  ```json
  {
    "name": "crear_superficie_civil3d",
    "arguments": {
      "nombre": "TERRENO_CANTERA",
      "puntos": "500000,8000000,2450;500100,8000000,2455;500050,8000100,2462"
    }
  }
  ```
