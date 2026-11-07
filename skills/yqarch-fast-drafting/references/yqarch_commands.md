# Catálogo de comandos YQArch para AutoCAD

**Versión objetivo:** YQArch 6.6.8  
**Uso:** referencia de trabajo para consulta y automatización con AutoLISP/Python.

> **Importante:** esta recopilación se basa en referencias publicadas de YQArch v6.6/v6.6.7 y documentación posterior. No todos los alias ni nombres internos han sido comprobados en una instalación de v6.6.8. Verifica los comandos localmente antes de utilizarlos en producción.

> **Nota Crítica para la IA (Gateway):** Al usar comandos interactivos de este catálogo (como `AD`, `WD`, `JJ`, `WC`, etc.) que abren ventanas modales (Dialog Boxes), la IA debe enviar el comando y detenerse, notificando al usuario para que seleccione la opción en pantalla antes de enviar más coordenadas. O bien, asegurarse de inyectar los comandos sin la secuencia de auto-cancelación (`\x03\x03`) al final para no abortar el diálogo prematuramente.

## Cómo interpretar los datos

- **Alias:** atajo que se escribe en la línea de comandos, si está configurado.
- **Nombre interno:** identificador reportado por las referencias consultadas.
- El nombre interno no garantiza que el comando pueda invocarse directamente con `(command ...)` en AutoLISP; puede requerir argumentos, selección o interacción.

## 1. Sistema y configuración

| Alias | Nombre interno | Función |
|---|---|---|
| `SYS` | `yq_systemsetup` | Menú de configuración |
| `YQSYS` | `yq_sys_setting` | Preferencias del usuario |
| `YQ` | `yqarch` | Catálogo de comandos |
| `TBA` | `yq_toolbarson` | Mostrar barras de herramientas |
| `TBAF` | `yq_toolbarsoff` | Ocultar barras de herramientas |
| `YQUSER` | `yq_setuser` | Seleccionar perfil |
| `YQLANG` | `yq_lang` | Cambiar idioma |
| `YQUNITS` | `yq_setunits` | Configurar unidades |
| `YQMENU` | `yq_menu` | Recargar menú |
| `SD` | `yq_setdwgscale` | Escala del dibujo |
| `TK` | `yq_dwgframe` | Dibujar marco de plano |
| `YQHELP` | `yq_help` | Ayuda |
| `YQVER` | `yq_ver` | Información de versión |
| `YQWEB` | `yq_website` | Sitio web |
| `YQABOUT` | `yq_about` | Información del complemento |
| `YQJZ` | `yq_donate` | Soporte |
| `YQUNINST` | `yq_uninstall` | Desinstalación |

## 2. Muros y columnas

| Alias | Nombre interno | Función |
|---|---|---|
| `WWW` | `yq_wallcolumn` | Menú de muros y columnas |
| `AX` | `yq_axisline` | Dibujar ejes |
| `ZXW` | `yq_gridaxis` | Crear cuadrícula de ejes |
| `WW` | `yq_wall` | Dibujar muros |
| `TW` | `yq_trim_fix_wall` | Recortar y ajustar muros |
| `WWT` | `yq_wall_chgthk` | Modificar espesor de muro |
| `XWW` | `yq_line2wall` | Convertir líneas en muros |
| `WWO` | `yq_wall_cdoffset` | Desplazar muros |
| `WWA` | `yq_wall2axis` | Obtener eje central del muro |
| `WWF` | `yq_fill_wall` | Rellenar muros |
| `ZZR` | `yq_r_column` | Columna rectangular |
| `ZZC` | `yq_o_column` | Columna circular |
| `ZZL` | `yq_l_column` | Columna en L |
| `ZZT` | `yq_t_column` | Columna en T |
| `ZZX` | `yq_c_column` | Columna en cruz |
| `ZZBZ` | `yq_axis_column` | Colocar columnas en cruces de ejes |
| `XXZZ` | `yq_pline2column` | Convertir geometría en columnas |
| `ZZF` | `yq_fill_column` | Rellenar columnas |
| `EW` | `yq_erase_wall` | Eliminar muros, columnas y aperturas |

## 3. Puertas y ventanas

| Alias | Nombre interno | Función |
|---|---|---|
| `MMM` | `yq_windowdoor` | Menú de puertas y ventanas |
| `AD` | `yq_hole_door` | Abrir hueco para puerta |
| `AD2` | `yq_door` | Dibujar puerta mediante dos puntos |
| `AW` | `yq_hole_win` | Abrir hueco para ventana |
| `AW2` | `yq_win` | Dibujar ventana mediante dos puntos |
| `TD` | `yq_windoor_replace` | Sustituir puerta o ventana |
| `HO` | `yq_hole` | Abrir hueco en muro |
| `ADT` | `yq_hole_pocketdoor` | Hueco para puerta con marco o embocadura |
| `ADT2` | `yq_pocketdoor` | Dibujar puerta con marco |
| `TDD` | `yq_pocketdoor_replace` | Sustituir puerta con marco |
| `WD` | `yq_hole_window` | Ventana paramétrica |
| `WD2` | `yq_window` | Dibujar ventana paramétrica |

## 4. Elementos constructivos y decoración

| Alias | Nombre interno | Función |
|---|---|---|
| `LTP` | `yq_staircase_section` | Sección de escalera |
| `LXTB` | `yq_step_section` | Sección de peldaños |
| `FSC` | `yq_waterproof` | Capa impermeabilizante |
| `FN` | `yq_stucco` | Capa de enlucido |
| `ZZZ` | `yq_decoration` | Menú de decoración |
| `ZS` | `yq_slddecoration` | Biblioteca de decoración |
| `JJ` | `yq_autofurniture` | Distribución de mobiliario |
| `WC` | `yq_ArrangeWC` | Distribución de baños |
| `YG` | `yq_chestplan` | Armarios en planta |
| `CLP` | `yq_curtainplan` | Cortinas |
| `SGX` | `yq_plasterstringplan` | Molduras de yeso |
| `GZ` | `yq_cupboardplan` | Armario sencillo en planta |
| `GZE` | `yq_cupboardelev` | Alzado de armario |
| `STB` | `yq_stonetile` | Placas de piedra |
| `MF` | `yq_woodenframe` | Estructura de madera |
| `CBZ` | `yq_array_boards` | Matriz de estantes |
| `MGB` | `yq_blockboard` | Tablero de carpintería |
| `JB` | `yq_plywood` | Contrachapado |
| `MDB` | `yq_woodflooring` | Pavimento de madera |
| `BL` | `yq_glass` | Vidrio |
| `SGB` | `yq_gypsumboard` | Placa de yeso |
| `THDC` | `yq_lighttrough` | Foseado para iluminación |
| `GQ` | `yq_partitionwall` | Tabique ligero |
| `XG` | `yq_formedsteel` | Perfil de acero |
| `JGJ` | `yq_ironframe` | Estructura de perfiles angulares |
| `BWM` | `yq_insulation` | Aislamiento |
| `HF` | `yq_weldingline` | Línea de soldadura |
| `RG` | `yq_hose` | Manguera |
| `GX` | `yq_pipe` | Tuberías o líneas de instalación |
| `KD` | `yq_boardhole` | Hueco cuadrado en placa |
| `YZX` | `yq_cylindershadow` | Sombreado de cilindros |
| `BSK` | `yq_backbolthole` | Agujero para perno posterior |
| `DJ` | `yq_autolamps` | Distribución de luminarias |
| `MTX` | `yq_randomspread` | Distribución aleatoria |
| `LM` | `yq_indoorelev` | Marcos de alzado interior |

## 5. Símbolos arquitectónicos, ejes y elevaciones

| Alias | Nombre interno | Función |
|---|---|---|
| `FFF` | `yq_archsymbols` | Menú de símbolos arquitectónicos |
| `FH` | `yq_sldsymbols` | Biblioteca de símbolos |
| `AZH` | `yq_auto_axis_dim` | Numerar ejes y acotar automáticamente |
| `ZH` | `yq_symbol_axis_c` | Numerar ejes consecutivos |
| `ZB` | `yq_geodesic_coordinates` | Coordenadas arquitectónicas/topográficas |
| `BG` | `yq_designed_elevation` | Cotas de elevación |
| `XBG` | `yq_Contour_Elevation` | Elevación de curvas de nivel |
| `PQF` | `yq_symbol_sectionPlane` | Símbolo de corte en planta |
| `DMF` | `yq_symbol_sectionCutter` | Símbolo de sección |
| `PD` | `yq_symbol_gradient` | Símbolo de pendiente |
| `ZX` | `yq_disconnection_line` | Línea de rotura |
| `GPX` | `yq_pipecutting` | Representación de sección de tubería |
| `DCZ` | `yq_symmetry_axis` | Eje de simetría |
| `ZXX` | `yq_central_line` | Línea de centro |
| `ACC` | `yq_wiresurround` | Cruce semicircular de cables |
| `ECC` | `yq_erasesurround` | Eliminar cruces de cables |
| `AAW` | `yq_bentarrow` | Flecha curva |
| `AAE` | `yq_entrancearrow` | Flecha de entrada o salida |
| `YYY` | `yq_indexers` | Menú de índices |
| `SY` | `yq_sldindexers` | Biblioteca de índices |
| `SY1` | `yq_identifier1` | Índice de corte 1 |
| `SY2` | `yq_identifier2` | Índice de corte 2 |
| `SY3` | `yq_identifier3` | Índice de corte 3 |
| `I0` | `yq_elevindexer4in1` | Índices de elevación combinados |

## 6. Estadísticas y cálculos

| Alias | Nombre interno | Función |
|---|---|---|
| `ZZB` | `yq_coordinate_list` | Recopilar coordenadas |
| `TTQ` | `yq_string_list` | Recopilar cadenas de texto |
| `CLB` | `yq_material_list` | Tabla de índices de materiales |
| `MJ` | `yq_calc_sumarea` | Calcular áreas y área total |
| `PMJ` | `yq_asso_area` | Asociar área y texto |
| `ZC` | `yq_calc_perimeter` | Calcular perímetros |
| `PZC` | `yq_asso_perimeter` | Asociar perímetro y texto |
| `ZL` | `yq_calweight` | Calcular peso por densidad |
| `FTB` | `yq_AreaStat` | Distribución de áreas |
| `+` | `yq_calc_addition` | Sumar valores |
| `-` | `yq_calc_subtration` | Restar valores |
| `*` | `yq_calc_multiplication` | Multiplicar valores |
| `/` | `yq_calc_division` | Dividir valores |
| `JS` | `yq_calc_expression` | Evaluar expresiones matemáticas |

## 7. Texto y edición

| Alias | Nombre interno | Función |
|---|---|---|
| `TTT` | `yq_textcontent` | Menú de texto |
| `DIC` | `yq_userdict` | Diccionario de usuario |
| `FY` | `yq_translate` | Traducción chino-inglés |
| `EQ` | `yq_updtextlist` | Actualizar lista de texto |
| `TT` | `yq_text` | Insertar texto de una línea |
| `TTE` | `yq_superedit` | Edición avanzada de texto y atributos |
| `TTG` | `yq_dict2text` | Sustituir mediante diccionario |
| `CTT` | `yq_hz_text` | Crear o introducir texto |
| `CED` | `yq_hz_edit` | Editar o generar texto |
| `TTR` | `yq_text_replace` | Reemplazar o calcular sustituciones |
| `TTRS` | `yq_replace_substrs` | Reemplazar varias subcadenas |
| `TTA` | `yq_text_append` | Unir textos |
| `TTJ` | `yq_jtext` | Combinar textos por líneas |
| `TTB` | `yq_text_break2` | Dividir texto en dos partes |
| `TTBB` | `yq_etext` | Separar texto en caracteres |
| `1234` | `yq_array_numbers` | Copiar números incrementalmente |
| `INC` | `yq_increase_numbers` | Incrementar valores numéricos |
| `TTZ` | `yq_increase_text` | Incrementar texto numérico |
| `TTF` | `yq_findtext` | Buscar texto específico |
| `TTTT` | `yq_textother` | Menú de modificación de texto |
| `TTN` | `yq_sldstyles` | Crear estilos de texto |
| `TTS` | `yq_text_cstyle` | Establecer estilo actual |
| `TTU` | `yq_text_update` | Actualizar texto por estilo |
| `TTRF` | `yq_replacefonts` | Sustituir fuentes faltantes |
| `TTC` | `yq_changetext` | Modificar texto por lotes |
| `TTW` | `yq_text_width` | Cambiar altura o proporción |
| `TTPX` | `yq_reordertexts` | Reordenar textos o tablas |
| `TTL` | `yq_text_alignmentleft` | Alinear texto abajo a la izquierda |
| `TTLL` | `yq_text_alignmentright` | Alinear texto abajo a la derecha |
| `TTRR` | `yq_text_restoremirr` | Restaurar orientación de texto o bloques |
| `TTSX` | `yq_text2attdef` | Convertir texto en definición de atributo |
| `TTX` | `yq_txtexp` | Convertir texto en líneas |

## 8. Acotación

| Alias | Nombre interno | Función |
|---|---|---|
| `DDD` | `yq_dimdraw` | Menú de acotación |
| `DD` | `yq_dim_linear` | Cotas horizontales y verticales |
| `DDX` | `yq_dim_aligned` | Cotas alineadas |
| `DDB` | `yq_dim_baseline` | Cotas desde una línea base |
| `DDR` | `yq_dim_radius` | Cotas de radio |
| `DDJ` | `yq_dim_diameter` | Cotas de diámetro |
| `DDAN` | `yq_dim_angular` | Cotas angulares |
| `DDAR` | `yq_dim_arc` | Cotas de longitud de arco |
| `DDQ` | `yq_dim_qdim` | Acotación rápida |
| `DDZ` | `yq_dim_closedspace` | Acotar espacios cerrados |
| `DDZZ` | `yq_dim_pline` | Acotar polilíneas cerradas |
| `DDSS` | `yq_dim_polygonsides` | Acotar lados por lotes |
| `DDW` | `yq_dim_axiswd` | Acotar ejes, puertas y ventanas |
| `DDWW` | `yq_dim_blocks` | Acotar bloques y muros interiores |
| `DDGZ` | `yq_dim_cupboard` | Acotar armarios en alzado |

## 9. Curvas y geometría

| Alias | Nombre interno | Función |
|---|---|---|
| `XXX` | `yq_curvetools` | Menú de curvas |
| `XXV` | `yq_pline_vertexes` | Añadir o eliminar vértices |
| `XXY` | `yq_pline_optimize` | Reducir vértices |
| `XXA` | `yq_line2arc` | Convertir línea y arco |
| `XXW` | `yq_plsegment_width` | Cambiar ancho de segmento |
| `XXS` | `yq_plsegment_offset` | Desplazar un segmento |
| `XXJ` | `yq_pline_join` | Unir líneas, arcos y polilíneas |
| `XXC` | `yq_pline_close` | Cerrar polilíneas |
| `XXFZ` | `yq_pline_reverse` | Invertir dirección de curva |
| `XXF` | `yq_wpline2wireframe` | Convertir polilínea gruesa en contorno |
| `LJX` | `yq_connectvertexes` | Conectar vértices |
| `LK` | `yq_outline` | Obtener contorno exterior |
| `NLK` | `yq_innercontours` | Obtener contornos interiores |
| `NLKS` | `yq_inneroffset` | Obtener contorno interior y desplazar |
| `HW` | `yq_tofreehand` | Convertir a trazo a mano |
| `DF` | `yq_break_equal` | Dividir curva en partes iguales |
| `DFX` | `yq_2ptsdivide` | División entre dos puntos |
| `E2A` | `yq_ellipse2arc` | Aproximar elipse con arcos |
| `CCA` | `yq_spline2apline` | Convertir spline a polilínea de arcos |
| `CCX` | `yq_spline2xpline` | Convertir spline a polilínea de segmentos |
| `CCXX` | `yq_curve2bowstring` | Dividir curva por cuerdas iguales |
| `CCY` | `yq_CurveProjection` | Obtener proyección de curva |

## 10. Herramientas especiales y modificación avanzada

| Alias | Nombre interno | Función |
|---|---|---|
| `EEE` | `yq_extratools` | Menú de herramientas especiales |
| `VV` | `yq_numberlines` | Gestión de líneas numeradas |
| `XD` | `yq_doubleline` | Dibujar líneas dobles |
| `TX` | `yq_trimdoubleline` | Recortar líneas dobles |
| `XDT` | `yq_dlineThickness` | Ajustar espesor de líneas dobles |
| `XXD` | `yq_2doubleline` | Convertir línea sencilla en doble |
| `XDO` | `yq_dline_offset` | Desplazar líneas dobles |
| `CE` | `yq_doublelineAxis` | Obtener eje de líneas dobles |
| `DQ` | `yq_alignment` | Alinear objetos |
| `AADQ` | `yq_leaderalign` | Alinear directrices |
| `TKTZ` | `yq_alignframe` | Ajustar marco |
| `BD` | `yq_BreakByKnife` | Cortar objetos con una línea |
| `BDD` | `yq_BreakByKnives` | Cortar varios objetos |
| `BX` | `yq_BreakAndErase` | Cortar y borrar objetos |
| `QG` | `yq_pastedetaildwg` | Extraer detalles de un dibujo |
| `TF` | `yq_transform` | Deformar geometría |
| `JX` | `yq_boundingbox` | Obtener caja envolvente o centro |
| `WK` | `yq_toboundingbox` | Convertir a marco rectangular |
| `XJX` | `yq_minBoundingbox` | Obtener rectángulo mínimo |
| `FZ` | `yq_overturn` | Voltear objetos |
| `JH` | `yq_exchangelocation` | Intercambiar posiciones |
| `DZBZ` | `yq_copy2points` | Distribuir objetos por puntos |
| `FZX` | `yq_assistantlines` | Líneas auxiliares |
| `EEEE` | `yq_modifyplus` | Menú de modificación avanzada |
| `ASC` | `yq_scale_each` | Escalar objetos individualmente |
| `ART` | `yq_rotate_each` | Girar objetos individualmente |
| `AMI` | `yq_mirror_each` | Simetría individual |
| `SCX` | `yq_scalexyz` | Escala diferente en X e Y |
| `CV` | `yq_dynamiccopy` | Copia dinámica |
| `STD` | `yq_stretch_d` | Estirar una distancia definida |
| `VD` | `yq_move_d` | Desplazar una distancia definida |
| `VR` | `yq_move_memorial` | Desplazamiento recordado |
| `CR` | `yq_copy_memorial` | Copia recordada |
| `SF` | `yq_superoffset` | Desplazamientos múltiples |
| `EXX` | `yq_ExtendToPoint` | Extender hasta punto o línea |
| `FF` | `yq_join_arcline` | Unir líneas y arcos compatibles |
| `FX` | `yq_connect2glines` | Extender y conectar dos grupos de líneas |

## 11. Bloques y atributos

| Alias | Nombre interno | Función |
|---|---|---|
| `BBD` | `yq_quickdetailblock` | Crear bloque de detalle |
| `BBS` | `yq_blockscale` | Configurar escala de bloque |
| `BBW` | `yq_block_writeout` | Exportar bloques por lotes |
| `BBTD` | `yq_replacebydwg` | Sustituir bloque por archivo DWG |
| `BBN` | `yq_quicknewblock` | Crear bloque rápidamente |
| `BBA` | `yq_block_add` | Añadir objetos a bloque |
| `BBE` | `yq_block_remove` | Eliminar objetos del bloque |
| `BBC` | `yq_block_copy` | Copiar parte de un bloque |
| `BBZZ` | `yq_blockwipeout` | Añadir o modificar máscara de bloque |
| `BBJM` | `yq_create_minsert` | Crear referencia múltiple anónima |
| `BBX` | `yq_explode_minsert` | Descomponer referencia múltiple |
| `BBXX` | `yq_explode_nblock` | Descomponer bloques anidados |
| `BBSX` | `yq_blocktext2att` | Convertir texto de bloque en atributos |
| `SXG` | `yq_modify_att` | Modificar atributos |
| `SXZ` | `yq_increase_att` | Incrementar atributos |
| `SXC` | `yq_changeatts` | Modificar atributos por lotes |
| `SXT` | `yq_blockatt2text` | Extraer atributos a texto |
| `SXE` | `yq_erase_blkatt` | Eliminar atributos |
| `BV` | `yq_blockviewer` | Administrar bibliotecas de bloques |
| `BV1` | `yq_blockviewer1` | Biblioteca de bloques 1 |
| `BV2` | `yq_blockviewer2` | Biblioteca de bloques 2 |
| `BV3` | `yq_blockviewer3` | Biblioteca de bloques 3 |

## 12. Sombreado

| Alias | Nombre interno | Función |
|---|---|---|
| `HHH` | `yq_hatchingtools` | Menú de sombreado |
| `HHSC` | `yq_hatchkeys` | Configurar atajos de sombreado |
| `XTC` | `yq_hatchingscale` | Modificar escala de sombreado |
| `TC` | `yq_hatch_quick` | Sombreado rápido |
| `TCT` | `yq_hatch_match` | Igualar patrón de sombreado |
| `TCC` | `yq_hatch_slide` | Sombreado paramétrico |
| `HHI` | `yq_hatch_newbase` | Mover origen del patrón |
| `HHA` | `yq_hatch_angle` | Cambiar ángulo |
| `HHB` | `yq_hatch_border` | Reconstruir contorno |
| `HHJ` | `yq_hatch_clipping` | Recortar sombreado |
| `HHX` | `yq_hatch_split` | Dividir sombreado |
| `HHU` | `yq_hatch_union` | Unir sombreados |
| `HHGL` | `yq_hatch_disasso` | Quitar asociación |

## 13. Categorías aún por completar

Las referencias consultadas también describen estas categorías, pero esta recopilación no incluye todos sus alias individuales:

- `CCC`: verificación y corrección del dibujo.
- `LLL`: herramientas de capas.
- `SSS`: pincel de propiedades o coincidencia de propiedades.
- `VVV`: ventanas gráficas.
- `QQQ`: utilidades diversas.

No inventes comandos para completar esas secciones. Usa la lista de comandos de la instalación o su archivo de atajos.

## 14. Validación recomendada para YQArch 6.6.8

1. Abre AutoCAD con YQArch 6.6.8 cargado.
2. Ejecuta `YQ` si ese alias está disponible.
3. Busca el archivo de atajos/configuración de YQArch; puede llamarse `yqshortcut.txt`, según la instalación.
4. Contrasta los alias y nombres internos con los archivos locales.
5. Prueba manualmente los comandos antes de automatizarlos.
6. Registra el resultado en una tabla de validación:

| Alias | Nombre interno | ¿Existe en 6.6.8? | ¿Se ejecuta desde AutoLISP? | Observaciones |
|---|---|---|---|---|
| `WW` | `yq_wall` | Pendiente | Pendiente | Probar en instalación |
| `AD2` | `yq_door` | Pendiente | Pendiente | Revisar interacción |
| `MJ` | `yq_calc_sumarea` | Pendiente | Pendiente | Revisar selección y salida |

## 15. Fuentes de consulta

- [Referencia de comandos YQArch traducida al español (Scribd)](https://es.scribd.com/document/633267205/YQARCH-TRADUCIDO-zh-CN-es)
- [Referencia YQArch Command AutoCAD (Scribd)](https://www.scribd.com/document/1007529896/yqarch-command-autocad)
- [Referencia de YQArch v6.6.7 (Studocu)](https://www.studocu.com/ph/document/university-of-pangasinan/bsce-bs-in-civil-engineering/%E6%BA%90%E6%B3%89%E8%AE%BE%E8%AE%A1%E5%BF%AB%E6%8D%B7%E9%94%AE%E8%AF%B4%E6%98%8E%E6%96%87%E4%BB%B6-v667/155939323?origin=course-new-6)
- [Sitio de YQArch](http://www.yqarch.cn/)

---

**Nota:** este documento es una recopilación de trabajo, no un catálogo oficial certificado de todos los comandos de YQArch 6.6.8. Verifica la versión local antes de generar rutinas automatizadas.
