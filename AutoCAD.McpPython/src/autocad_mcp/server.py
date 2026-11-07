"""Servidor FastMCP para Autodesk AutoCAD 2027 y Civil 3D (45 herramientas tipadas)."""

from __future__ import annotations

import datetime
import json
import math
from typing import Any
from mcp.server.fastmcp import FastMCP

from autocad_mcp.cad_math import (
    all_finite,
    build_batch_geometry_lisp,
    calcular_area_poligono_analitico,
    is_valid_symbol_name,
    lisp_string,
    medir_distancia_analitica,
    num,
    parse_points,
)
from autocad_mcp.client import AutoCADGatewayClient

# Inicialización del servidor FastMCP
mcp = FastMCP("AutoCAD_AI_Context_Server")
gateway = AutoCADGatewayClient()


# ============================================================================
# MÓDULO 1: Inspección, Contexto y Visión (8 Tools)
# ============================================================================


@mcp.tool(name="obtener_info_dibujo")
async def obtener_info_dibujo() -> str:
    """Obtiene metadatos del dibujo activo en AutoCAD: nombre de archivo DWG, estado de solo lectura y unidades configuradas."""
    return await gateway.get_context("context/info")


@mcp.tool(name="leer_objetos_autocad")
async def leer_objetos_autocad() -> str:
    """Lee y devuelve un resumen (conteos por tipo) de todas las entidades y objetos que existen actualmente en el dibujo de AutoCAD (ModelSpace)."""
    return await gateway.get_context("context/entities")


@mcp.tool(name="leer_capas_autocad")
async def leer_capas_autocad() -> str:
    """Obtiene la lista completa de capas del dibujo de AutoCAD con sus estados (bloqueada, congelada, apagada) y código de color."""
    return await gateway.get_context("context/layers")


@mcp.tool(name="leer_textos_autocad")
async def leer_textos_autocad() -> str:
    """Lista todos los textos (DBText y MText) del dibujo con sus contenidos, coordenadas exactas (X, Y) y capa para ubicar ambientes."""
    return await gateway.get_context("context/texts")


@mcp.tool(name="obtener_limites_dibujo")
async def obtener_limites_dibujo() -> str:
    """Obtiene las coordenadas mínimas (X, Y) y máximas (X, Y) del dibujo actual (Extmin y Extmax) para calcular áreas de diseño libres."""
    return await gateway.get_context("context/extents")


@mcp.tool(name="leer_seleccion_autocad")
async def leer_seleccion_autocad() -> str:
    """Obtiene el número de entidades seleccionadas actualmente por el usuario con el ratón en AutoCAD."""
    return await gateway.get_context("context/selection")


@mcp.tool(name="listar_bloques_disponibles")
async def listar_bloques_disponibles() -> str:
    """Obtiene los nombres de todas las definiciones de bloques cargadas en el archivo actual."""
    return await gateway.get_context("context/blocks")


@mcp.tool(name="capturar_pantalla_autocad")
async def capturar_pantalla_autocad() -> str:
    """Captura una imagen PNG de la ventana activa de AutoCAD ('ojos para la IA') y guarda la captura en disco. Devuelve la ruta absoluta."""
    return await gateway.get_context("context/screenshot")


# ============================================================================
# MÓDULO 2: Geometría y Dibujo Paramétrico Tipado (8 Tools)
# ============================================================================


def _send_lisp(body: str) -> Any:
    return gateway.execute_command(f"(progn {body} (princ))\n")


def _layer_pair(capa: str) -> str | None:
    return f"(cons 8 {lisp_string(capa.strip())})" if is_valid_symbol_name(capa) else None


@mcp.tool(name="dibujar_geometria_lote")
async def dibujar_geometria_lote(
    elementos: list[dict[str, Any]] | str,
    capa_por_defecto: str = "0",
) -> str:
    """⚡ HERRAMIENTA PREFERIDA PARA DIBUJAR MÚLTIPLES ENTIDADES EN UN SOLO LLAMADO.

    Traza decenas o cientos de elementos geométricos (líneas, polilíneas, rectángulos, círculos, arcos, textos)
    en una única llamada ultrarrápida y atómica en AutoCAD, reduciendo el consumo de tokens y latencia en un 95%.

    'elementos' es una lista de diccionarios o un string JSON con formato:
    - tipo: "linea" | "rectangulo" | "circulo" | "arco" | "polilinea" | "texto"
    - capa: Nombre de capa destino (opcional, usa capa_por_defecto si se omite)
    - Parámetros específicos por tipo:
      * linea: x1, y1, x2, y2, z1?, z2?
      * rectangulo: x1, y1, x2, y2
      * circulo: x (o centro_x), y (o centro_y), radio, z?
      * arco: x, y, radio, angulo_inicio, angulo_fin (en grados), z?
      * polilinea: puntos ("x1,y1;x2,y2;..." o [[x,y],...]), cerrada? (bool)
      * texto: texto, x, y, altura?, rotacion? (grados), z?
    """
    try:
        lisp_code, summary = build_batch_geometry_lisp(elementos, default_layer=capa_por_defecto)
    except Exception as exc:
        return json.dumps({"status": "error", "message": f"Error validando geometría en lote: {exc}"})

    gateway_resp = await gateway.execute_command(lisp_code)
    try:
        parsed_resp = json.loads(gateway_resp)
        if isinstance(parsed_resp, dict):
            summary["gateway_response"] = parsed_resp
    except Exception:
        summary["gateway_response"] = gateway_resp

    return json.dumps(summary, ensure_ascii=False)


@mcp.tool(name="dibujar_linea")
async def dibujar_linea(
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    z1: float = 0.0,
    z2: float = 0.0,
    capa: str = "0",
) -> str:
    """Dibuja una línea entre (x1,y1,z1) y (x2,y2,z2) en metros. (Para múltiples elementos, prefiera 'dibujar_geometria_lote')."""
    if not all_finite(x1, y1, z1, x2, y2, z2):
        return json.dumps({"status": "error", "message": "Las coordenadas deben ser números finitos."})
    if x1 == x2 and y1 == y2 and z1 == z2:
        return json.dumps({"status": "error", "message": "Los puntos inicial y final son idénticos."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    body = f"(entmake (list '(0 . \"LINE\") {layer} (list 10 {num(x1)} {num(y1)} {num(z1)}) (list 11 {num(x2)} {num(y2)} {num(z2)})))"
    return await _send_lisp(body)


@mcp.tool(name="dibujar_polilinea")
async def dibujar_polilinea(puntos: str, cerrada: bool = False, capa: str = "0") -> str:
    """Dibuja una polilínea 2D a partir de vértices 'x1,y1;x2,y2;...' (mínimo 2, en metros). 'cerrada' une el último vértice con el primero."""
    pts = parse_points(puntos, 2)
    if not pts or len(pts) < 2:
        return json.dumps(
            {"status": "error", "message": "Formato de 'puntos' inválido. Use 'x1,y1;x2,y2;...' con al menos 2 vértices."}
        )
    if cerrada and len(pts) < 3:
        return json.dumps({"status": "error", "message": "Una polilínea cerrada requiere al menos 3 vértices."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    verts = " ".join(f"(list 10 {num(p[0])} {num(p[1])})" for p in pts)
    flag = "1" if cerrada else "0"
    body = f"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") {layer} '(100 . \"AcDbPolyline\") (cons 90 {len(pts)}) (cons 70 {flag}) {verts}))"
    return await _send_lisp(body)


@mcp.tool(name="dibujar_rectangulo")
async def dibujar_rectangulo(x1: float, y1: float, x2: float, y2: float, capa: str = "0") -> str:
    """Dibuja un rectángulo como polilínea cerrada a partir de dos esquinas opuestas (x1,y1) y (x2,y2) en metros."""
    if not all_finite(x1, y1, x2, y2):
        return json.dumps({"status": "error", "message": "Las coordenadas deben ser números finitos."})
    if x1 == x2 or y1 == y2:
        return json.dumps({"status": "error", "message": "Las esquinas deben diferir en X y en Y."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    pts = [[x1, y1], [x2, y1], [x2, y2], [x1, y2]]
    verts = " ".join(f"(list 10 {num(p[0])} {num(p[1])})" for p in pts)
    body = f"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") {layer} '(100 . \"AcDbPolyline\") (cons 90 4) (cons 70 1) {verts}))"
    return await _send_lisp(body)


@mcp.tool(name="dibujar_circulo")
async def dibujar_circulo(centroX: float, centroY: float, radio: float, capa: str = "0") -> str:
    """Dibuja un círculo con centro (centroX, centroY) y radio en metros."""
    if not all_finite(centroX, centroY, radio) or radio <= 0:
        return json.dumps({"status": "error", "message": "El centro debe ser numérico y el radio mayor que cero."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    body = f"(entmake (list '(0 . \"CIRCLE\") {layer} (list 10 {num(centroX)} {num(centroY)} 0.0) (cons 40 {num(radio)})))"
    return await _send_lisp(body)


@mcp.tool(name="dibujar_arco")
async def dibujar_arco(
    centroX: float,
    centroY: float,
    radio: float,
    anguloInicio: float,
    anguloFin: float,
    capa: str = "0",
) -> str:
    """Dibuja un arco circular de centro (centroX, centroY) y radio en metros, recorrido en sentido antihorario desde anguloInicio hasta anguloFin (grados, 0 = Este)."""
    if not all_finite(centroX, centroY, radio, anguloInicio, anguloFin) or radio <= 0:
        return json.dumps({"status": "error", "message": "Los valores deben ser numéricos y el radio mayor que cero."})
    if abs(anguloFin - anguloInicio) % 360.0 == 0:
        return json.dumps({"status": "error", "message": "Los ángulos coinciden; use 'dibujar_circulo'."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    a1_rad = math.radians(anguloInicio)
    a2_rad = math.radians(anguloFin)
    body = (
        f"(entmake (list '(0 . \"ARC\") {layer} (list 10 {num(centroX)} {num(centroY)} 0.0) "
        f"(cons 40 {num(radio)}) (cons 50 {num(a1_rad)}) (cons 51 {num(a2_rad)})))"
    )
    return await _send_lisp(body)


@mcp.tool(name="insertar_texto")
async def insertar_texto(
    texto: str,
    x: float,
    y: float,
    altura: float = 0.25,
    rotacion: float = 0.0,
    capa: str = "0",
) -> str:
    """Inserta un texto de una línea en (x, y) con altura en metros (por defecto 0.25) y rotación en grados, en la capa indicada."""
    if not texto or not texto.strip():
        return json.dumps({"status": "error", "message": "El texto no puede estar vacío."})
    if not all_finite(x, y, altura, rotacion) or altura <= 0:
        return json.dumps({"status": "error", "message": "Coordenadas deben ser numéricas y altura > 0."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    rot_rad = math.radians(rotacion)
    body = (
        f"(entmake (list '(0 . \"TEXT\") {layer} (list 10 {num(x)} {num(y)} 0.0) "
        f"(cons 40 {num(altura)}) (cons 1 {lisp_string(texto)}) (cons 50 {num(rot_rad)})))"
    )
    return await _send_lisp(body)


@mcp.tool(name="insertar_bloque")
async def insertar_bloque(
    nombreBloque: str,
    x: float,
    y: float,
    escala: float = 1.0,
    rotacion: float = 0.0,
    capa: str = "0",
) -> str:
    """Inserta una referencia de un bloque YA DEFINIDO en el dibujo en (x, y) con escala uniforme y rotación en grados."""
    if not is_valid_symbol_name(nombreBloque):
        return json.dumps({"status": "error", "message": f"Nombre de bloque inválido: '{nombreBloque}'."})
    if not all_finite(x, y, escala, rotacion) or escala <= 0:
        return json.dumps({"status": "error", "message": "Coordenadas deben ser numéricas y escala > 0."})

    layer = _layer_pair(capa)
    if not layer:
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{capa}'."})

    name = lisp_string(nombreBloque.strip())
    s = num(escala)
    rot_rad = math.radians(rotacion)
    body = (
        f'(if (tblsearch "BLOCK" {name}) '
        f"(entmake (list '(0 . \"INSERT\") (cons 2 {name}) {layer} (list 10 {num(x)} {num(y)} 0.0) "
        f"(cons 41 {s}) (cons 42 {s}) (cons 43 {s}) (cons 50 {num(rot_rad)}))) "
        f'(princ (strcat "\\n[AutoCAD MCP] Bloque no definido: " {name})))'
    )
    return await _send_lisp(body)


# ============================================================================
# MÓDULO 3: Gestión Avanzada de Capas (3 Tools)
# ============================================================================


@mcp.tool(name="crear_capa")
async def crear_capa(
    nombre: str,
    colorAci: int = 7,
    tipoLinea: str = "Continuous",
    descripcion: str = "",
) -> str:
    """Crea una capa o actualiza una existente con color ACI (1-255), tipo de línea y descripción."""
    if not is_valid_symbol_name(nombre):
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{nombre}'."})
    if not (1 <= colorAci <= 255):
        return json.dumps({"status": "error", "message": "El color ACI debe estar entre 1 y 255."})
    if not is_valid_symbol_name(tipoLinea):
        return json.dumps({"status": "error", "message": f"Nombre de tipo de línea inválido: '{tipoLinea}'."})

    lt = lisp_string(tipoLinea.strip())
    nm = lisp_string(nombre.strip())
    desc = lisp_string(descripcion or "")
    body = (
        "(vl-load-com) (setq *mcp-doc* (vla-get-ActiveDocument (vlax-get-acad-object))) "
        f'(if (not (tblsearch "LTYPE" {lt})) '
        f'(vl-catch-all-apply \'vla-load (list (vla-get-Linetypes *mcp-doc*) {lt} "acadiso.lin"))) '
        f"(setq *mcp-lay* (vla-add (vla-get-Layers *mcp-doc*) {nm})) "
        f"(vla-put-Color *mcp-lay* {colorAci}) "
        f'(if (tblsearch "LTYPE" {lt}) (vla-put-Linetype *mcp-lay* {lt})) '
        f"(vla-put-Description *mcp-lay* {desc})"
    )
    return await _send_lisp(body)


@mcp.tool(name="establecer_capa_actual")
async def establecer_capa_actual(nombre: str) -> str:
    """Establece la capa actual (CLAYER). La capa debe existir y no estar congelada."""
    if not is_valid_symbol_name(nombre):
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{nombre}'."})

    nm = lisp_string(nombre.strip())
    body = (
        f'(if (tblsearch "LAYER" {nm}) '
        f"(if (vl-catch-all-error-p (vl-catch-all-apply 'setvar (list \"CLAYER\" {nm}))) "
        f'(princ (strcat "\\n[AutoCAD MCP] No se pudo activar la capa (congelada?): " {nm}))) '
        f'(princ (strcat "\\n[AutoCAD MCP] La capa no existe: " {nm})))'
    )
    return await _send_lisp(body)


@mcp.tool(name="establecer_estado_capa")
async def establecer_estado_capa(nombre: str, accion: str) -> str:
    """Cambia el estado de una capa: activar, desactivar, congelar, descongelar, bloquear, desbloquear."""
    if not is_valid_symbol_name(nombre):
        return json.dumps({"status": "error", "message": f"Nombre de capa inválido: '{nombre}'."})

    ops = {
        "activar": ("LayerOn", ":vlax-true"),
        "desactivar": ("LayerOn", ":vlax-false"),
        "congelar": ("Freeze", ":vlax-true"),
        "descongelar": ("Freeze", ":vlax-false"),
        "bloquear": ("Lock", ":vlax-true"),
        "desbloquear": ("Lock", ":vlax-false"),
    }
    act = (accion or "").strip().lower()
    if act not in ops:
        return json.dumps(
            {
                "status": "error",
                "message": "Acción inválida. Use: activar, desactivar, congelar, descongelar, bloquear o desbloquear.",
            }
        )

    prop, val = ops[act]
    nm = lisp_string(nombre.strip())
    body = (
        "(vl-load-com) (setq *mcp-doc* (vla-get-ActiveDocument (vlax-get-acad-object))) "
        f'(if (tblsearch "LAYER" {nm}) '
        f"(if (vl-catch-all-error-p (vl-catch-all-apply 'vlax-put-property "
        f'(list (vla-item (vla-get-Layers *mcp-doc*) {nm}) "{prop}" {val}))) '
        f'(princ (strcat "\\n[AutoCAD MCP] No se pudo cambiar estado: " {nm})) '
        f"(vla-regen *mcp-doc* acActiveViewport)) "
        f'(princ (strcat "\\n[AutoCAD MCP] La capa no existe: " {nm})))'
    )
    return await _send_lisp(body)


# ============================================================================
# MÓDULO 4: Medición, Geometría Computacional y Mantenimiento (4 Tools)
# ============================================================================


@mcp.tool(name="medir_distancia")
def medir_distancia(
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    z1: float = 0.0,
    z2: float = 0.0,
) -> str:
    """Calcula (sin AutoCAD) la distancia horizontal 2D, distancia 3D, ΔX/ΔY/ΔZ, ángulo y pendiente en % entre dos puntos."""
    return json.dumps(medir_distancia_analitica(x1, y1, x2, y2, z1, z2))


@mcp.tool(name="calcular_area_poligono")
def calcular_area_poligono(puntos: str) -> str:
    """Calcula (sin AutoCAD) el área en m², hectáreas, perímetro, centroide y sentido de giro de un polígono cerrado (mínimo 3 vértices)."""
    return json.dumps(calcular_area_poligono_analitico(puntos))


@mcp.tool(name="limpiar_y_purgar_dibujo")
async def limpiar_y_purgar_dibujo(tipo: str = "todo", auditar: bool = True) -> str:
    """Elimina definiciones no utilizadas con -PURGE en dos pasadas y repara la base de datos con AUDIT."""
    codes = {
        "todo": "_A",
        "all": "_A",
        "bloques": "_B",
        "capas": "_LA",
        "tipos_linea": "_LT",
        "estilos_texto": "_ST",
        "estilos_cota": "_D",
    }
    tp = (tipo or "").strip().lower()
    if tp not in codes:
        return json.dumps(
            {"status": "error", "message": "Tipo inválido. Use: todo, bloques, capas, tipos_linea, estilos_texto o estilos_cota."}
        )

    code = codes[tp]
    cmd = f"_.-PURGE\n{code}\n*\n_N\n_.-PURGE\n{code}\n*\n_N\n"
    if auditar:
        cmd += "_.AUDIT\n_Y\n"
    return await gateway.execute_command(cmd)


@mcp.tool(name="regenerar_todo")
async def regenerar_todo() -> str:
    """Regenera todas las ventanas gráficas (_.REGENALL) para recalcular curvas y textos."""
    return await gateway.execute_command("_.REGENALL\n")


# ============================================================================
# MÓDULO 5: Ejecución, Control y AutoLISP (8 Tools)
# ============================================================================


@mcp.tool(name="ping_autocad_gateway")
async def ping_autocad_gateway() -> str:
    """Envía un ping al Gateway de AutoCAD para verificar la conectividad en el puerto 5055."""
    return await gateway.ping()


@mcp.tool(name="ejecutar_comandos_lote")
async def ejecutar_comandos_lote(comandos: list[str] | str) -> str:
    """⚡ Ejecuta una lista secuencial de comandos o macros de AutoCAD en un único envío por lotes.

    'comandos' puede ser una lista de cadenas de comando o un string JSON. Cada comando se ejecutará
    secuencialmente en la consola de AutoCAD.
    """
    if isinstance(comandos, str):
        try:
            parsed = json.loads(comandos)
            if isinstance(parsed, list):
                comandos = parsed
            else:
                comandos = [comandos]
        except Exception:
            comandos = [comandos]

    if not isinstance(comandos, list):
        return json.dumps({"status": "error", "message": "'comandos' debe ser una lista de comandos o string JSON."})

    full_cmd = ""
    for c in comandos:
        s = str(c).rstrip("\n")
        full_cmd += s + "\n"

    return await gateway.execute_command(full_cmd)


@mcp.tool(name="ejecutar_comando_autocad")
async def ejecutar_comando_autocad(command: str) -> str:
    """Envía comandos de dibujo, macros AutoLISP o comandos YQArch directamente a AutoCAD."""
    return await gateway.execute_command(command)


@mcp.tool(name="ejecutar_lisp")
async def ejecutar_lisp(codigoLisp: str) -> str:
    """Evalúa e inyecta expresiones o rutinas AutoLISP dinámicas directamente en AutoCAD."""
    if not codigoLisp or not codigoLisp.strip():
        return json.dumps({"status": "error", "message": "El código AutoLISP no puede estar vacío."})
    cmd = codigoLisp.strip()
    if not cmd.endswith("\n"):
        cmd += "\n"
    return await gateway.execute_command(cmd)


@mcp.tool(name="cargar_archivo_lisp")
async def cargar_archivo_lisp(rutaArchivoLsp: str) -> str:
    """Carga y evalúa un archivo de script AutoLISP (.lsp) desde el sistema de archivos dentro de AutoCAD mediante (load ...)."""
    if not rutaArchivoLsp or not rutaArchivoLsp.strip():
        return json.dumps({"status": "error", "message": "La ruta del archivo .lsp no puede estar vacía."})
    normalized = rutaArchivoLsp.replace("\\", "/")
    return await gateway.execute_command(f'(load "{normalized}")\n')


@mcp.tool(name="zoom_extension")
async def zoom_extension() -> str:
    """Ejecuta un Zoom Extensión ('_.ZOOM E') en AutoCAD para encuadrar y centrar automáticamente todos los elementos."""
    return await gateway.execute_command("_.ZOOM\nE\n")


@mcp.tool(name="guardar_dibujo")
async def guardar_dibujo() -> str:
    """Guarda el archivo de dibujo actual en AutoCAD ('_.QSAVE')."""
    return await gateway.execute_command("_.QSAVE\n")


@mcp.tool(name="configurar_entorno_dibujo")
async def configurar_entorno_dibujo() -> str:
    """Configura el entorno activo en AutoCAD a METROS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) y calibra cotas/textos."""
    script = (
        "_.-SETVAR\nINSUNITS\n4\n"
        "_.-SETVAR\nMEASUREMENT\n1\n"
        "_.-SETVAR\nLUNITS\n2\n"
        "_.-SETVAR\nLUPREC\n3\n"
        "_.-SETVAR\nAUNITS\n0\n"
        "_.-SETVAR\nANGBASE\n0.0\n"
        "_.-SETVAR\nANGDIR\n0\n"
        "_.-SETVAR\nDIMTXT\n0.22\n"
        "_.-SETVAR\nDIMASZ\n0.18\n"
        "_.-SETVAR\nDIMEXE\n0.11\n"
        "_.-SETVAR\nDIMEXO\n0.06\n"
        "_.-SETVAR\nDIMGAP\n0.08\n"
        "_.-SETVAR\nDIMDEC\n2\n"
        "_.-SETVAR\nDIMTAD\n1\n"
        "_.UCS\nW\n"
    )
    return await gateway.execute_command(script)


# ============================================================================
# MÓDULO 6: Presentaciones, Cajetines y Ploteo PDF (5 Tools)
# ============================================================================


@mcp.tool(name="crear_layout")
async def crear_layout(nombreLayout: str) -> str:
    """Crea una nueva pestaña de presentación (Layout) en espacio papel y cambia la vista activa a dicha presentación."""
    if not nombreLayout or not nombreLayout.strip():
        return json.dumps({"status": "error", "message": "El nombre del layout no puede estar vacío."})
    clean = nombreLayout.strip()
    cmd = f"_.-LAYOUT\nN\n{clean}\n_.-LAYOUT\nS\n{clean}\n_.TILEMODE\n0\n"
    return await gateway.execute_command(cmd)


@mcp.tool(name="configurar_hoja_layout")
async def configurar_hoja_layout(
    nombreLayout: str,
    tamanoHoja: str = "A1",
    estiloTrazado: str = "monochrome.ctb",
) -> str:
    """Configura el tamaño de página estandarizado (A0 a A4) de un Layout para trazado en PDF a escala 1:1 en mm."""
    if not nombreLayout or not nombreLayout.strip():
        return json.dumps({"status": "error", "message": "El nombre del layout no puede estar vacío."})

    media_map = {
        "A0": "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
        "A1": "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
        "A2": "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
        "A3": "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
        "A4": "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
    }
    media = media_map.get(tamanoHoja.upper().strip(), media_map["A1"])
    clean = nombreLayout.strip()
    cmd = (
        f"_.-LAYOUT\nS\n{clean}\n_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-PLOT\nN\n{clean}\nDWG To PDF.pc3\n{media}\nM\nL\nN\nL\n1:1\n0.00,0.00\nY\n{estiloTrazado}\nY\nN\nN\nY\nN\n"
    )
    return await gateway.execute_command(cmd)


@mcp.tool(name="crear_viewport")
async def crear_viewport(
    xInferior: float,
    yInferior: float,
    ancho: float,
    alto: float,
    escala: float,
    xCentroModelo: float,
    yCentroModelo: float,
) -> str:
    """Crea una ventana gráfica (Viewport / MVIEW) en el layout activo, calcula la escala métrica exacta (1000/Escala XP) y centra el modelo."""
    if escala <= 0:
        return json.dumps({"status": "error", "message": "La escala métrica debe ser mayor a cero (ej. 1000, 500, 200)."})

    x_sup = xInferior + ancho
    y_sup = yInferior + alto
    xp_factor = 1000.0 / escala

    cmd = (
        "_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-MVIEW\n{num(xInferior)},{num(yInferior)}\n{num(x_sup)},{num(y_sup)}\n"
        "_.MSPACE\n"
        f"_.ZOOM\nC\n{num(xCentroModelo)},{num(yCentroModelo)}\n{xp_factor:.6f}XP\n"
        "_.PSPACE\n"
    )
    return await gateway.execute_command(cmd)


@mcp.tool(name="insertar_cajetin_parametrico")
async def insertar_cajetin_parametrico(
    xEsquina: float,
    yEsquina: float,
    tituloProyecto: str = "PROYECTO DE INGENIERÍA",
    nombrePlano: str = "PLANO GENERAL",
    codigoLamina: str = "L-01",
    escalaTexto: str = "1:1000",
    autor: str = "INGENIERÍA",
    fecha: str | None = None,
) -> str:
    """Inserta y dibuja un cajetín/membrete técnico en espacio papel con campos paramétricos."""
    fecha_final = fecha.strip() if fecha and fecha.strip() else datetime.datetime.now().strftime("%Y-%m")
    ancho_cajetin = 180.0
    alto_cajetin = 45.0

    x0 = xEsquina - ancho_cajetin
    y0 = yEsquina
    x1 = xEsquina
    y1 = yEsquina + alto_cajetin

    cmd = (
        "_.TILEMODE\n0\n_.PSPACE\n"
        "_.-LAYER\nM\nFORMATO_CAJETIN\nC\n7\nFORMATO_CAJETIN\n\n"
        f"_.RECTANG\n{num(x0)},{num(y0)}\n{num(x1)},{num(y1)}\n"
        f"_.LINE\n{num(x0)},{num(y0 + 25.0)}\n{num(x1)},{num(y0 + 25.0)}\n\n"
        f"_.LINE\n{num(x0)},{num(y0 + 12.0)}\n{num(x1)},{num(y0 + 12.0)}\n\n"
        f"_.LINE\n{num(x0 + 120.0)},{num(y0)}\n{num(x0 + 120.0)},{num(y0 + 25.0)}\n\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 35.0)}\n3.5\n0\nPROYECTO: {tituloProyecto.upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 28.0)}\n3.0\n0\nPLANO: {nombrePlano.upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 16.0)}\n2.5\n0\nESPECIALIDAD / AUTOR: {autor.upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 4.0)}\n2.5\n0\nFECHA: {fecha_final}\n"
        f"_.-TEXT\n{num(x0 + 125.0)},{num(y0 + 16.0)}\n2.5\n0\nESCALA: {escalaTexto}\n"
        f"_.-TEXT\n{num(x0 + 125.0)},{num(y0 + 4.0)}\n4.5\n0\nLÁMINA: {codigoLamina.upper()}\n"
    )
    return await gateway.execute_command(cmd)


@mcp.tool(name="exportar_pdf")
async def exportar_pdf(nombreLayout: str, rutaPdf: str, estiloCtb: str = "monochrome.ctb") -> str:
    """Traza y genera un archivo PDF físico a partir de un Layout utilizando el motor virtual 'DWG To PDF.pc3'."""
    if not nombreLayout or not rutaPdf:
        return json.dumps({"status": "error", "message": "El nombre del layout y la ruta PDF son obligatorios."})

    norm_path = rutaPdf.replace("\\", "/")
    clean_ly = nombreLayout.strip()
    cmd = (
        f"_.-LAYOUT\nS\n{clean_ly}\n_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-PLOT\nY\n{clean_ly}\nDWG To PDF.pc3\n\n\n\n\n\n\n\nY\n{estiloCtb}\nY\nN\n{norm_path}\nN\nY\n"
    )
    return await gateway.execute_command(cmd)


# ============================================================================
# MÓDULO 7: Autodesk Civil 3D (9 Tools)
# ============================================================================


@mcp.tool(name="verificar_estado_civil3d")
async def verificar_estado_civil3d() -> str:
    """Verifica si el dibujo activo corre sobre Civil 3D 2027 y cuenta superficies y alineamientos."""
    return await gateway.get_context("civil/status")


@mcp.tool(name="listar_superficies_civil3d")
async def listar_superficies_civil3d() -> str:
    """Lista las superficies topográficas (TIN) del dibujo con nombre, tipo, cota mínima, cota máxima y número de triángulos."""
    return await gateway.get_context("civil/surfaces")


@mcp.tool(name="consultar_elevacion_terreno")
async def consultar_elevacion_terreno(x: float, y: float, superficie: str | None = None) -> str:
    """Devuelve la cota Z exacta del terreno (superficie TIN de Civil 3D) en el punto (x, y)."""
    q_sup = f"&surface={superficie}" if superficie else ""
    return await gateway.get_context(f"civil/surface/elevation?x={num(x)}&y={num(y)}{q_sup}")


@mcp.tool(name="listar_alineamientos_civil3d")
async def listar_alineamientos_civil3d() -> str:
    """Lista los alineamientos horizontales con nombre, progresiva inicial/final, estacas y longitud total."""
    return await gateway.get_context("civil/alignments")


@mcp.tool(name="consultar_estacion_alineamiento")
async def consultar_estacion_alineamiento(x: float, y: float, alineamiento: str | None = None) -> str:
    """Calcula la progresiva (estaca) y el desfase lateral de un punto (x, y) respecto a un alineamiento de Civil 3D."""
    q_al = f"&alignment={alineamiento}" if alineamiento else ""
    return await gateway.get_context(f"civil/alignment/station-offset?x={num(x)}&y={num(y)}{q_al}")


@mcp.tool(name="obtener_coordenada_en_estacion")
async def obtener_coordenada_en_estacion(
    progresiva: float,
    desfase: float = 0.0,
    alineamiento: str | None = None,
) -> str:
    """Devuelve la coordenada (x, y) correspondiente a una progresiva y desfase sobre un alineamiento de Civil 3D."""
    q_al = f"&alignment={alineamiento}" if alineamiento else ""
    return await gateway.get_context(
        f"civil/alignment/point-at-station?station={num(progresiva)}&offset={num(desfase)}{q_al}"
    )


@mcp.tool(name="consultar_perfil_longitudinal")
async def consultar_perfil_longitudinal(
    intervalo: float = 20.0,
    alineamiento: str | None = None,
    superficie: str | None = None,
) -> str:
    """Muestrea el perfil longitudinal del terreno a lo largo de un alineamiento de Civil 3D: cada 'intervalo' metros devuelve progresiva, X, Y y cota Z."""
    if not all_finite(intervalo) or intervalo <= 0:
        return json.dumps({"status": "error", "message": "El intervalo debe ser un número mayor que cero."})

    res_al = await gateway.get_context("civil/alignments")
    try:
        data_al = json.loads(res_al)
    except Exception:
        return res_al

    if data_al.get("status") != "success" or not data_al.get("data"):
        return json.dumps({"status": "error", "message": "No se encontraron alineamientos en Civil 3D."})

    elegido = None
    for a in data_al["data"]:
        if not alineamiento or a.get("nombre", "").lower() == alineamiento.lower():
            elegido = a
            break

    if not elegido:
        return json.dumps({"status": "error", "message": f"Alineamiento no encontrado: '{alineamiento}'."})

    nombre_al = elegido["nombre"]
    inicio = float(elegido["progresiva_inicial"])
    fin = float(elegido["progresiva_final"])

    progresivas: list[float] = []
    curr = inicio
    while curr < fin - 1e-6:
        progresivas.append(curr)
        curr += intervalo
    progresivas.append(fin)

    if len(progresivas) > 500:
        return json.dumps(
            {"status": "error", "message": f"El intervalo genera {len(progresivas)} muestras (máx. 500). Aumente el intervalo."}
        )

    muestras = []
    z_min: float | None = None
    z_max: float | None = None
    z_ini: float | None = None
    z_fin: float | None = None

    q_al = f"&alignment={nombre_al}"
    q_sup = f"&surface={superficie}" if superficie else ""

    for st in progresivas:
        res_pt = await gateway.get_context(f"civil/alignment/point-at-station?station={num(st)}&offset=0{q_al}")
        try:
            pt_data = json.loads(res_pt)
        except Exception:
            pt_data = {}

        if pt_data.get("status") != "success" or "x" not in pt_data or "y" not in pt_data:
            muestras.append({"progresiva": st, "x": None, "y": None, "cota": None})
            continue

        x_val = pt_data["x"]
        y_val = pt_data["y"]

        res_z = await gateway.get_context(f"civil/surface/elevation?x={num(x_val)}&y={num(y_val)}{q_sup}")
        try:
            z_data = json.loads(res_z)
            z_val = z_data.get("elevacion")
        except Exception:
            z_val = None

        if z_val is not None:
            z_min = z_val if z_min is None else min(z_min, z_val)
            z_max = z_val if z_max is None else max(z_max, z_val)
            if z_ini is None:
                z_ini = z_val
            z_fin = z_val

        muestras.append(
            {
                "progresiva": round(st, 3),
                "x": round(x_val, 4),
                "y": round(y_val, 4),
                "cota": round(z_val, 3) if z_val is not None else None,
            }
        )

    longitud = fin - inicio
    pendiente_media = None
    if z_ini is not None and z_fin is not None and longitud > 0:
        pendiente_media = round((z_fin - z_ini) / longitud * 100.0, 4)

    return json.dumps(
        {
            "status": "success",
            "alineamiento": nombre_al,
            "superficie": superficie or "(primera disponible)",
            "intervalo": intervalo,
            "progresiva_inicial": inicio,
            "progresiva_final": fin,
            "muestras_total": len(muestras),
            "cota_minima": z_min,
            "cota_maxima": z_max,
            "pendiente_media_porcentaje": pendiente_media,
            "muestras": muestras,
        }
    )


@mcp.tool(name="crear_alineamiento_civil3d")
async def crear_alineamiento_civil3d(
    nombre: str,
    puntos: str,
    radio: float = 0.0,
    capa: str | None = None,
) -> str:
    """Crea un alineamiento horizontal en Civil 3D a partir de PIs 'x1,y1;x2,y2;...' con curvas circulares tangentes de radio fijo (0 = sin curvas)."""
    pts = parse_points(puntos, 2)
    if not pts or len(pts) < 2:
        return json.dumps(
            {"status": "error", "message": "Formato de 'puntos' inválido. Use 'x1,y1;x2,y2;...' con al menos 2 PIs."}
        )

    payload = {
        "nombre": nombre,
        "puntos": [{"x": p[0], "y": p[1]} for p in pts],
        "radio": radio,
        "capa": capa,
    }
    return await gateway.post_json("civil/alignment/create", payload)


@mcp.tool(name="crear_superficie_civil3d")
async def crear_superficie_civil3d(nombre: str, puntos: str) -> str:
    """Crea una superficie TIN en Civil 3D a partir de puntos topográficos 'x1,y1,z1;x2,y2,z2;...' (mínimo 3)."""
    pts = parse_points(puntos, 3)
    if not pts or len(pts) < 3:
        return json.dumps(
            {"status": "error", "message": "Formato de 'puntos' inválido. Use 'x1,y1,z1;x2,y2,z2;...' con al menos 3 puntos."}
        )

    payload = {
        "nombre": nombre,
        "puntos": [{"x": p[0], "y": p[1], "z": p[2]} for p in pts],
    }
    return await gateway.post_json("civil/surface/create", payload)


# ============================================================================
# Punto de entrada principal para CLI / uvx
# ============================================================================


def main() -> None:
    """Ejecuta el servidor MCP sobre transporte STDIO."""
    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
