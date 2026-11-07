# Tool Decision Matrix — AutoCAD & Civil 3D MCP

This matrix maps high-level engineering and drafting objectives to the optimal tool call sequence in `luiccian-autocad-mcp`.

---

| User / Workflow Objective | Primary Tool Sequence | Key Parameters / Notes |
|---|---|---|
| **Start any session / Verify setup** | `ping_autocad_gateway` &rarr; `obtener_info_dibujo` &rarr; `configurar_entorno_dibujo` | Mandatory pre-flight. Sets `INSUNITS = 4` (meters). |
| **Draw architectural floor plan / room** | `crear_capa` &rarr; `dibujar_geometria_lote` &rarr; `zoom_extension` &rarr; `capturar_pantalla_autocad` | **Do not loop single tools.** Pass arrays of lines, rects, polylines, and text in one atomic call. |
| **Inspect existing drawing layers & objects** | `leer_capas_autocad` &rarr; `leer_objetos_autocad` &rarr; `obtener_limites_dibujo` | Non-destructive database reads via transactions. |
| **Read texts / annotations in drawing** | `leer_textos_autocad` | Returns text strings, styles, coordinates, and layer. |
| **Query ground elevation at coordinates** | `consultar_elevacion_terreno` | Parameters: `nombre_superficie`, `x`, `y`. Returns exact $Z$ elevation. |
| **Sample longitudinal profile along road/axis** | `listar_alineamientos_civil3d` &rarr; `consultar_perfil_longitudinal` | Parameters: `nombre_alineamiento`, `nombre_superficie`, `intervalo_estacion`. |
| **Calculate polygon area or boundary** | `calcular_area_poligono` | Returns area in $m^2$ and hectares, perimeter, and centroid (Gauss algorithm). |
| **Measure distance, slope and azimuth** | `medir_distancia` | Local analytical calculation: 2D/3D distance, $\Delta X, \Delta Y, \Delta Z$, azimuth, slope %. |
| **Purge drawing and audit database** | `limpiar_y_purgar_dibujo` | Runs two passes of `_.-PURGE` and `_.AUDIT` to fix errors and reduce file size. |
| **Evaluate custom AutoLISP code** | `ejecutar_lisp` (in-memory) or `cargar_archivo_lisp` (from disk) | Wrap strings carefully. Useful for complex `entmake` or legacy routines. |
| **Plot layout to physical PDF file** | `crear_layout` &rarr; `configurar_hoja_layout` &rarr; `crear_viewport` &rarr; `insertar_cajetin_parametrico` &rarr; `exportar_pdf` | Full automated sheet generation with calibrated metric scale (`1000/Scale XP`). |
