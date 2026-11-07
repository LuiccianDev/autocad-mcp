"""FastMCP server for Autodesk AutoCAD 2027 and Civil 3D (45 typed tools)."""

from __future__ import annotations

import datetime
import json
import math
from typing import Any
from mcp.server.fastmcp import FastMCP

from autocad_mcp.cad_math import (
    all_finite,
    build_batch_geometry_lisp,
    calculate_distance,
    calculate_polygon_area,
    is_valid_symbol_name,
    lisp_string,
    num,
    parse_points,
)
from autocad_mcp.client import AutoCADGatewayClient

# Initialize FastMCP Server
mcp = FastMCP("AutoCAD_AI_Context_Server")
gateway = AutoCADGatewayClient()


# ============================================================================
# MODULE 1: Inspection, Context & Vision (8 Tools)
# ============================================================================


@mcp.tool(name="get_drawing_info")
async def get_drawing_info() -> str:
    """Retrieve metadata of the active AutoCAD drawing: DWG filename, read-only state, and configured units."""
    return await gateway.get_context("context/info")


@mcp.tool(name="get_entities")
async def get_entities() -> str:
    """Read and return a summary (counts by entity type) of all entities existing in AutoCAD ModelSpace."""
    return await gateway.get_context("context/entities")


@mcp.tool(name="get_layers")
async def get_layers() -> str:
    """Retrieve the full list of AutoCAD drawing layers with their states (locked, frozen, off) and ACI color index."""
    return await gateway.get_context("context/layers")


@mcp.tool(name="get_texts")
async def get_texts() -> str:
    """List all text entities (DBText and MText) in the drawing with text contents, exact (X, Y) coordinates, and layers."""
    return await gateway.get_context("context/texts")


@mcp.tool(name="get_drawing_extents")
async def get_drawing_extents() -> str:
    """Retrieve minimum (X, Y) and maximum (X, Y) bounding coordinates of the active drawing (Extmin and Extmax)."""
    return await gateway.get_context("context/extents")


@mcp.tool(name="get_selection_count")
async def get_selection_count() -> str:
    """Get the number of entities currently selected by the user in AutoCAD."""
    return await gateway.get_context("context/selection")


@mcp.tool(name="list_blocks")
async def list_blocks() -> str:
    """Retrieve names of all block definitions loaded into the current drawing."""
    return await gateway.get_context("context/blocks")


@mcp.tool(name="capture_screenshot")
async def capture_screenshot() -> str:
    """Capture a PNG screenshot of the active AutoCAD viewport/window and save it to disk. Returns absolute file path."""
    return await gateway.get_context("context/screenshot")


@mcp.tool(name="read_layers")
async def read_layers() -> str:
    """Alias for get_layers: Retrieve the full list of AutoCAD drawing layers with their states."""
    return await get_layers()


@mcp.tool(name="read_objects")
async def read_objects() -> str:
    """Alias for get_entities: Read and return a summary of all entities existing in AutoCAD ModelSpace."""
    return await get_entities()


@mcp.tool(name="read_texts")
async def read_texts() -> str:
    """Alias for get_texts: List all text entities in the drawing with text contents and coordinates."""
    return await get_texts()


@mcp.tool(name="read_selection")
async def read_selection() -> str:
    """Alias for get_selection_count: Get the number of entities currently selected by the user."""
    return await get_selection_count()


@mcp.tool(name="list_available_blocks")
async def list_available_blocks() -> str:
    """Alias for list_blocks: Retrieve names of all block definitions loaded into current drawing."""
    return await list_blocks()


@mcp.tool(name="capture_screen")
async def capture_screen() -> str:
    """Alias for capture_screenshot: Capture a PNG screenshot of the active AutoCAD window."""
    return await capture_screenshot()


# ============================================================================
# MODULE 2: Parametric Geometry & Drawing (8 Tools)
# ============================================================================


def _send_lisp(body: str) -> Any:
    return gateway.execute_command(f"(progn {body} (princ))\n")


def _layer_pair(layer: str) -> str | None:
    return f"(cons 8 {lisp_string(layer.strip())})" if is_valid_symbol_name(layer) else None


@mcp.tool(name="draw_batch_geometry")
async def draw_batch_geometry(
    elements: list[dict[str, Any]] | str,
    default_layer: str = "0",
) -> str:
    """PREFERRED TOOL FOR DRAWING MULTIPLE GEOMETRIC ENTITIES IN A SINGLE CALL.

    Draw tens or hundreds of geometric entities (lines, polylines, rectangles, circles, arcs, texts)
    in a single atomic call into AutoCAD, reducing latency and token overhead by up to 95%.

    'elements' is a list of dictionaries or a JSON string with format:
    - type: "line" | "rectangle" | "circle" | "arc" | "polyline" | "text"
    - layer: Target layer name (optional, defaults to default_layer)
    - Specific parameters per type:
      * line: x1, y1, x2, y2, z1?, z2?
      * rectangle: x1, y1, x2, y2
      * circle: center_x (or x), center_y (or y), radius (or r), center_z?
      * arc: center_x, center_y, radius, start_angle, end_angle (degrees), center_z?
      * polyline: points ("x1,y1;x2,y2;..." or [[x,y],...]), closed? (bool)
      * text: text, x, y, height?, rotation? (degrees), z?
    """
    try:
        lisp_code, summary = build_batch_geometry_lisp(elements, default_layer=default_layer)
    except Exception as exc:
        return json.dumps({"status": "error", "message": f"Error validating batch geometry: {exc}"})

    gateway_resp = await gateway.execute_command(lisp_code)
    try:
        parsed_resp = json.loads(gateway_resp)
        if isinstance(parsed_resp, dict):
            summary["gateway_response"] = parsed_resp
    except Exception:
        summary["gateway_response"] = gateway_resp

    return json.dumps(summary, ensure_ascii=False)


@mcp.tool(name="draw_line")
async def draw_line(
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    z1: float = 0.0,
    z2: float = 0.0,
    layer: str = "0",
) -> str:
    """Draw a line between (x1, y1, z1) and (x2, y2, z2) in meters. (For multiple entities, prefer 'draw_batch_geometry')."""
    if not all_finite(x1, y1, z1, x2, y2, z2):
        return json.dumps({"status": "error", "message": "Coordinates must be finite numbers."})
    if x1 == x2 and y1 == y2 and z1 == z2:
        return json.dumps({"status": "error", "message": "Start and end points are identical."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    body = f"(entmake (list '(0 . \"LINE\") {layer_p} (list 10 {num(x1)} {num(y1)} {num(z1)}) (list 11 {num(x2)} {num(y2)} {num(z2)})))"
    return await _send_lisp(body)


@mcp.tool(name="draw_polyline")
async def draw_polyline(points: str, closed: bool = False, layer: str = "0") -> str:
    """Draw a 2D lightweight polyline from vertices 'x1,y1;x2,y2;...' (minimum 2 points in meters)."""
    pts = parse_points(points, 2)
    if not pts or len(pts) < 2:
        return json.dumps(
            {"status": "error", "message": "Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 2 vertices."}
        )
    if closed and len(pts) < 3:
        return json.dumps({"status": "error", "message": "A closed polyline requires at least 3 vertices."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    verts = " ".join(f"(list 10 {num(p[0])} {num(p[1])})" for p in pts)
    flag = "1" if closed else "0"
    body = f"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") {layer_p} '(100 . \"AcDbPolyline\") (cons 90 {len(pts)}) (cons 70 {flag}) {verts}))"
    return await _send_lisp(body)


@mcp.tool(name="draw_rectangle")
async def draw_rectangle(x1: float, y1: float, x2: float, y2: float, layer: str = "0") -> str:
    """Draw a rectangle as a closed polyline from two opposite corners (x1, y1) and (x2, y2) in meters."""
    if not all_finite(x1, y1, x2, y2):
        return json.dumps({"status": "error", "message": "Coordinates must be finite numbers."})
    if x1 == x2 or y1 == y2:
        return json.dumps({"status": "error", "message": "Corners must differ in both X and Y coordinates."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    pts = [[x1, y1], [x2, y1], [x2, y2], [x1, y2]]
    verts = " ".join(f"(list 10 {num(p[0])} {num(p[1])})" for p in pts)
    body = f"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") {layer_p} '(100 . \"AcDbPolyline\") (cons 90 4) (cons 70 1) {verts}))"
    return await _send_lisp(body)


@mcp.tool(name="draw_circle")
async def draw_circle(center_x: float, center_y: float, radius: float, layer: str = "0") -> str:
    """Draw a circle centered at (center_x, center_y) with given radius in meters."""
    if not all_finite(center_x, center_y, radius) or radius <= 0:
        return json.dumps({"status": "error", "message": "Center must be numeric and radius must be greater than zero."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    body = f"(entmake (list '(0 . \"CIRCLE\") {layer_p} (list 10 {num(center_x)} {num(center_y)} 0.0) (cons 40 {num(radius)})))"
    return await _send_lisp(body)


@mcp.tool(name="draw_arc")
async def draw_arc(
    center_x: float,
    center_y: float,
    radius: float,
    start_angle: float,
    end_angle: float,
    layer: str = "0",
) -> str:
    """Draw a circular arc centered at (center_x, center_y) with radius in meters from start_angle to end_angle (degrees, CCW)."""
    if not all_finite(center_x, center_y, radius, start_angle, end_angle) or radius <= 0:
        return json.dumps({"status": "error", "message": "Parameters must be numeric and radius must be greater than zero."})
    if abs(end_angle - start_angle) % 360.0 == 0:
        return json.dumps({"status": "error", "message": "Start and end angles are identical; use 'draw_circle'."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    a1_rad = math.radians(start_angle)
    a2_rad = math.radians(end_angle)
    body = (
        f"(entmake (list '(0 . \"ARC\") {layer_p} (list 10 {num(center_x)} {num(center_y)} 0.0) "
        f"(cons 40 {num(radius)}) (cons 50 {num(a1_rad)}) (cons 51 {num(a2_rad)})))"
    )
    return await _send_lisp(body)


@mcp.tool(name="insert_text")
async def insert_text(
    text: str,
    x: float,
    y: float,
    height: float = 0.25,
    rotation: float = 0.0,
    layer: str = "0",
) -> str:
    """Insert a single-line text entity at (x, y) with height in meters (default 0.25) and rotation in degrees."""
    if not text or not text.strip():
        return json.dumps({"status": "error", "message": "Text content cannot be empty."})
    if not all_finite(x, y, height, rotation) or height <= 0:
        return json.dumps({"status": "error", "message": "Coordinates must be numeric and height must be > 0."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    rot_rad = math.radians(rotation)
    body = (
        f"(entmake (list '(0 . \"TEXT\") {layer_p} (list 10 {num(x)} {num(y)} 0.0) "
        f"(cons 40 {num(height)}) (cons 1 {lisp_string(text)}) (cons 50 {num(rot_rad)})))"
    )
    return await _send_lisp(body)


@mcp.tool(name="insert_block")
async def insert_block(
    block_name: str,
    x: float,
    y: float,
    scale: float = 1.0,
    rotation: float = 0.0,
    layer: str = "0",
) -> str:
    """Insert a reference of an existing block definition at (x, y) with uniform scale and rotation in degrees."""
    if not is_valid_symbol_name(block_name):
        return json.dumps({"status": "error", "message": f"Invalid block name: '{block_name}'."})
    if not all_finite(x, y, scale, rotation) or scale <= 0:
        return json.dumps({"status": "error", "message": "Coordinates must be numeric and scale must be > 0."})

    layer_p = _layer_pair(layer)
    if not layer_p:
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{layer}'."})

    name = lisp_string(block_name.strip())
    s = num(scale)
    rot_rad = math.radians(rotation)
    body = (
        f'(if (tblsearch "BLOCK" {name}) '
        f"(entmake (list '(0 . \"INSERT\") (cons 2 {name}) {layer_p} (list 10 {num(x)} {num(y)} 0.0) "
        f"(cons 41 {s}) (cons 42 {s}) (cons 43 {s}) (cons 50 {num(rot_rad)}))) "
        f'(princ (strcat "\\n[AutoCAD MCP] Block not found: " {name})))'
    )
    return await _send_lisp(body)


# ============================================================================
# MODULE 3: Advanced Layer Management (3 Tools)
# ============================================================================


@mcp.tool(name="create_layer")
async def create_layer(
    name: str,
    color_aci: int = 7,
    linetype: str = "Continuous",
    description: str = "",
) -> str:
    """Create a layer or update an existing one with ACI color (1-255), linetype, and description."""
    if not is_valid_symbol_name(name):
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{name}'."})
    if not (1 <= color_aci <= 255):
        return json.dumps({"status": "error", "message": "ACI color must be between 1 and 255."})
    if not is_valid_symbol_name(linetype):
        return json.dumps({"status": "error", "message": f"Invalid linetype name: '{linetype}'."})

    lt = lisp_string(linetype.strip())
    nm = lisp_string(name.strip())
    desc = lisp_string(description or "")
    body = (
        "(vl-load-com) (setq *mcp-doc* (vla-get-ActiveDocument (vlax-get-acad-object))) "
        f'(if (not (tblsearch "LTYPE" {lt})) '
        f'(vl-catch-all-apply \'vla-load (list (vla-get-Linetypes *mcp-doc*) {lt} "acadiso.lin"))) '
        f"(setq *mcp-lay* (vla-add (vla-get-Layers *mcp-doc*) {nm})) "
        f"(vla-put-Color *mcp-lay* {color_aci}) "
        f'(if (tblsearch "LTYPE" {lt}) (vla-put-Linetype *mcp-lay* {lt})) '
        f"(vla-put-Description *mcp-lay* {desc})"
    )
    return await _send_lisp(body)


@mcp.tool(name="set_current_layer")
async def set_current_layer(name: str) -> str:
    """Set the active drawing layer (CLAYER). The layer must exist and not be frozen."""
    if not is_valid_symbol_name(name):
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{name}'."})

    nm = lisp_string(name.strip())
    body = (
        f'(if (tblsearch "LAYER" {nm}) '
        f"(if (vl-catch-all-error-p (vl-catch-all-apply 'setvar (list \"CLAYER\" {nm}))) "
        f'(princ (strcat "\\n[AutoCAD MCP] Could not set layer (frozen?): " {nm}))) '
        f'(princ (strcat "\\n[AutoCAD MCP] Layer does not exist: " {nm})))'
    )
    return await _send_lisp(body)


@mcp.tool(name="set_layer_state")
async def set_layer_state(name: str, action: str) -> str:
    """Modify the state of a layer: on, off, freeze, thaw, lock, or unlock."""
    if not is_valid_symbol_name(name):
        return json.dumps({"status": "error", "message": f"Invalid layer name: '{name}'."})

    ops = {
        "on": ("LayerOn", ":vlax-true"),
        "off": ("LayerOn", ":vlax-false"),
        "freeze": ("Freeze", ":vlax-true"),
        "thaw": ("Freeze", ":vlax-false"),
        "lock": ("Lock", ":vlax-true"),
        "unlock": ("Lock", ":vlax-false"),
        # Legacy Spanish aliases
        "activar": ("LayerOn", ":vlax-true"),
        "desactivar": ("LayerOn", ":vlax-false"),
        "congelar": ("Freeze", ":vlax-true"),
        "descongelar": ("Freeze", ":vlax-false"),
        "bloquear": ("Lock", ":vlax-true"),
        "desbloquear": ("Lock", ":vlax-false"),
    }
    act = (action or "").strip().lower()
    if act not in ops:
        return json.dumps(
            {
                "status": "error",
                "message": "Invalid action. Use: on, off, freeze, thaw, lock, or unlock.",
            }
        )

    prop, val = ops[act]
    nm = lisp_string(name.strip())
    body = (
        "(vl-load-com) (setq *mcp-doc* (vla-get-ActiveDocument (vlax-get-acad-object))) "
        f'(if (tblsearch "LAYER" {nm}) '
        f"(if (vl-catch-all-error-p (vl-catch-all-apply 'vlax-put-property "
        f'(list (vla-item (vla-get-Layers *mcp-doc*) {nm}) "{prop}" {val}))) '
        f'(princ (strcat "\\n[AutoCAD MCP] Could not update layer state: " {nm})) '
        f"(vla-regen *mcp-doc* acActiveViewport)) "
        f'(princ (strcat "\\n[AutoCAD MCP] Layer does not exist: " {nm})))'
    )
    return await _send_lisp(body)


# ============================================================================
# MODULE 4: Measurement, Computational Geometry & Audit (4 Tools)
# ============================================================================


@mcp.tool(name="measure_distance")
def measure_distance(
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    z1: float = 0.0,
    z2: float = 0.0,
) -> str:
    """Calculate 2D horizontal distance, 3D distance, coordinate deltas, azimuth, and slope % between two points (without CAD)."""
    return json.dumps(calculate_distance(x1, y1, x2, y2, z1, z2))


@mcp.tool(name="calculate_polygon_area")
def calculate_polygon_area(points: str) -> str:
    """Calculate area in m2, hectares, perimeter, centroid, and orientation of a closed polygon (minimum 3 vertices, without CAD)."""
    return json.dumps(calculate_polygon_area(points))


@mcp.tool(name="purge_and_audit_drawing")
async def purge_and_audit_drawing(target: str = "all", audit: bool = True) -> str:
    """Purge unused symbol table definitions safely and audit database errors."""
    tp = (target or "").strip().lower()
    if tp in ("all", "todo"):
        cmd = "(progn (vl-load-com) (vla-PurgeAll (vla-get-ActiveDocument (vlax-get-acad-object))) (princ))\n"
    else:
        codes = {
            "blocks": "_B",
            "layers": "_LA",
            "linetypes": "_LT",
            "text_styles": "_ST",
            "dim_styles": "_D",
            # Legacy aliases
            "bloques": "_B",
            "capas": "_LA",
            "tipos_linea": "_LT",
            "estilos_texto": "_ST",
            "estilos_cota": "_D",
        }
        if tp not in codes:
            return json.dumps(
                {"status": "error", "message": "Invalid target. Use: all, blocks, layers, linetypes, text_styles, or dim_styles."}
            )
        code = codes[tp]
        cmd = f"_.-PURGE\n{code}\n*\n_N\n"

    if audit:
        cmd += "_.AUDIT\n_Y\n"
    return await gateway.execute_command(cmd)


@mcp.tool(name="clean_and_purge_drawing")
async def clean_and_purge_drawing(target: str = "all", audit: bool = True) -> str:
    """Alias for purge_and_audit_drawing: Purge unused definitions safely and audit errors."""
    return await purge_and_audit_drawing(target=target, audit=audit)


@mcp.tool(name="regen_all")
async def regen_all() -> str:
    """Regenerate all open viewports and recalculate drawing display curves and text metrics (_.REGENALL)."""
    return await gateway.execute_command("_.REGENALL\n")


# ============================================================================
# MODULE 5: Execution, Control & AutoLISP (8 Tools)
# ============================================================================


@mcp.tool(name="ping_gateway")
async def ping_gateway() -> str:
    """Ping the native AutoCAD Gateway server to verify HTTP connectivity on port 5055."""
    return await gateway.ping()


@mcp.tool(name="ping_autocad_gateway")
async def ping_autocad_gateway() -> str:
    """Alias for ping_gateway: Ping the native AutoCAD Gateway server to verify HTTP connectivity."""
    return await ping_gateway()


@mcp.tool(name="execute_batch_commands")
async def execute_batch_commands(commands: list[str] | str) -> str:
    """Execute a sequential list of AutoCAD command strings or macros in a single transactional batch payload."""
    if isinstance(commands, str):
        try:
            parsed = json.loads(commands)
            if isinstance(parsed, list):
                commands = parsed
            else:
                commands = [commands]
        except Exception:
            commands = [commands]

    if not isinstance(commands, list):
        return json.dumps({"status": "error", "message": "'commands' must be a list of command strings or a JSON array."})

    full_cmd = ""
    for c in commands:
        s = str(c).rstrip("\n")
        full_cmd += s + "\n"

    return await gateway.execute_command(full_cmd)


@mcp.tool(name="execute_command")
async def execute_command(command: str) -> str:
    """Enqueue native non-modal AutoCAD commands, AutoLISP macros, or YQArch commands directly to the main UI thread."""
    return await gateway.execute_command(command)


@mcp.tool(name="evaluate_lisp")
async def evaluate_lisp(code: str) -> str:
    """Evaluate and inject dynamic AutoLISP expressions directly into the active AutoCAD document."""
    if not code or not code.strip():
        return json.dumps({"status": "error", "message": "AutoLISP code cannot be empty."})
    cmd = code.strip()
    if not cmd.endswith("\n"):
        cmd += "\n"
    return await gateway.execute_command(cmd)


@mcp.tool(name="execute_lisp")
async def execute_lisp(code: str) -> str:
    """Alias for evaluate_lisp: Evaluate and inject dynamic AutoLISP expressions into AutoCAD."""
    return await evaluate_lisp(code=code)


@mcp.tool(name="load_lisp_file")
async def load_lisp_file(file_path: str) -> str:
    """Load and execute an external AutoLISP script file (.lsp) from disk into AutoCAD via (load ...)."""
    if not file_path or not file_path.strip():
        return json.dumps({"status": "error", "message": "File path cannot be empty."})
    normalized = file_path.replace("\\", "/")
    return await gateway.execute_command(f'(load "{normalized}")\n')


@mcp.tool(name="zoom_extents")
async def zoom_extents() -> str:
    """Execute Zoom Extents ('_.ZOOM E') in AutoCAD to center and fit all drawing entities in the current viewport."""
    return await gateway.execute_command("_.ZOOM\nE\n")


@mcp.tool(name="save_drawing")
async def save_drawing() -> str:
    """Save the active AutoCAD drawing file ('_.QSAVE')."""
    return await gateway.execute_command("_.QSAVE\n")


@mcp.tool(name="configure_drawing_units")
async def configure_drawing_units() -> str:
    """Configure AutoCAD drawing environment to METRIC METERS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) and calibrate dimensions."""
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


@mcp.tool(name="configure_drawing_environment")
async def configure_drawing_environment() -> str:
    """Alias for configure_drawing_units: Configure AutoCAD drawing environment to metric meters."""
    return await configure_drawing_units()


# ============================================================================
# MODULE 6: Sheet Layouts, Title Blocks & PDF Plotting (5 Tools)
# ============================================================================


@mcp.tool(name="create_layout")
async def create_layout(layout_name: str) -> str:
    """Create a new PaperSpace Layout tab and switch the active view to that layout."""
    if not layout_name or not layout_name.strip():
        return json.dumps({"status": "error", "message": "Layout name cannot be empty."})
    clean = layout_name.strip()
    cmd = f"_.-LAYOUT\nN\n{clean}\n_.-LAYOUT\nS\n{clean}\n_.TILEMODE\n0\n"
    return await gateway.execute_command(cmd)


@mcp.tool(name="configure_layout_page")
async def configure_layout_page(
    layout_name: str,
    paper_size: str = "A1",
    plot_style: str = "monochrome.ctb",
) -> str:
    """Configure standardized sheet size (A0 to A4) of a Layout for 1:1 metric PDF plotting."""
    if not layout_name or not layout_name.strip():
        return json.dumps({"status": "error", "message": "Layout name cannot be empty."})

    media_map = {
        "A0": "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
        "A1": "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
        "A2": "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
        "A3": "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
        "A4": "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
    }
    media = media_map.get(paper_size.upper().strip(), media_map["A1"])
    clean = layout_name.strip()
    cmd = (
        f"_.-LAYOUT\nS\n{clean}\n_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-PLOT\nN\n{clean}\nDWG To PDF.pc3\n{media}\nM\nL\nN\nL\n1:1\n0.00,0.00\nY\n{plot_style}\nY\nN\nN\nY\nN\n"
    )
    return await gateway.execute_command(cmd)


@mcp.tool(name="configure_layout_sheet")
async def configure_layout_sheet(
    layout_name: str,
    paper_size: str = "A1",
    plot_style: str = "monochrome.ctb",
) -> str:
    """Alias for configure_layout_page: Configure standardized sheet size (A0 to A4) of a Layout."""
    return await configure_layout_page(layout_name=layout_name, paper_size=paper_size, plot_style=plot_style)


@mcp.tool(name="create_viewport")
async def create_viewport(
    lower_x: float,
    lower_y: float,
    width: float,
    height: float,
    scale: float,
    center_model_x: float,
    center_model_y: float,
) -> str:
    """Create a layout viewport (MVIEW) in the active PaperSpace, configure metric scale (1000/Scale XP), and center model geometry."""
    if scale <= 0:
        return json.dumps({"status": "error", "message": "Metric scale denominator must be greater than zero (e.g. 1000, 500, 200)."})

    x_sup = lower_x + width
    y_sup = lower_y + height
    xp_factor = 1000.0 / scale

    cmd = (
        "_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-MVIEW\n{num(lower_x)},{num(lower_y)}\n{num(x_sup)},{num(y_sup)}\n"
        "_.MSPACE\n"
        f"_.ZOOM\nC\n{num(center_model_x)},{num(center_model_y)}\n{xp_factor:.6f}XP\n"
        "_.PSPACE\n"
    )
    return await gateway.execute_command(cmd)


@mcp.tool(name="insert_title_block")
async def insert_title_block(
    corner_x: float,
    corner_y: float,
    project_title: str = "ENGINEERING PROJECT",
    drawing_name: str = "GENERAL PLAN",
    sheet_code: str = "L-01",
    scale_text: str = "1:1000",
    author: str = "ENGINEERING",
    date: str | None = None,
) -> str:
    """Insert and draw a parametric engineering title block in PaperSpace with customizable metadata."""
    final_date = date.strip() if date and date.strip() else datetime.datetime.now().strftime("%Y-%m")
    title_width = 180.0
    title_height = 45.0

    x0 = corner_x - title_width
    y0 = corner_y
    x1 = corner_x
    y1 = corner_y + title_height

    cmd = (
        "_.TILEMODE\n0\n_.PSPACE\n"
        "_.-LAYER\nM\nFORMAT_TITLEBLOCK\nC\n7\nFORMAT_TITLEBLOCK\n\n"
        f"_.RECTANG\n{num(x0)},{num(y0)}\n{num(x1)},{num(y1)}\n"
        f"_.LINE\n{num(x0)},{num(y0 + 25.0)}\n{num(x1)},{num(y0 + 25.0)}\n\n"
        f"_.LINE\n{num(x0)},{num(y0 + 12.0)}\n{num(x1)},{num(y0 + 12.0)}\n\n"
        f"_.LINE\n{num(x0 + 120.0)},{num(y0)}\n{num(x0 + 120.0)},{num(y0 + 25.0)}\n\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 35.0)}\n3.5\n0\nPROJECT: {project_title.upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 28.0)}\n3.0\n0\nDRAWING: {drawing_name.upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 16.0)}\n2.5\n0\nAUTHOR / DISCIPLINE: {author.upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 4.0)}\n2.5\n0\nDATE: {final_date}\n"
        f"_.-TEXT\n{num(x0 + 125.0)},{num(y0 + 16.0)}\n2.5\n0\nSCALE: {scale_text}\n"
        f"_.-TEXT\n{num(x0 + 125.0)},{num(y0 + 4.0)}\n4.5\n0\nSHEET: {sheet_code.upper()}\n"
    )
    return await gateway.execute_command(cmd)


@mcp.tool(name="insert_parametric_titleblock")
async def insert_parametric_titleblock(
    corner_x: float,
    corner_y: float,
    project_title: str = "ENGINEERING PROJECT",
    drawing_name: str = "GENERAL PLAN",
    sheet_code: str = "L-01",
    scale_text: str = "1:1000",
    author: str = "ENGINEERING",
    date: str | None = None,
) -> str:
    """Alias for insert_title_block: Insert and draw a parametric engineering title block in PaperSpace."""
    return await insert_title_block(
        corner_x=corner_x,
        corner_y=corner_y,
        project_title=project_title,
        drawing_name=drawing_name,
        sheet_code=sheet_code,
        scale_text=scale_text,
        author=author,
        date=date,
    )


@mcp.tool(name="export_pdf")
async def export_pdf(layout_name: str, pdf_path: str, plot_style: str = "monochrome.ctb") -> str:
    """Plot and export a layout directly to a physical PDF document using 'DWG To PDF.pc3'."""
    if not layout_name or not pdf_path:
        return json.dumps({"status": "error", "message": "Layout name and PDF path are required."})

    norm_path = pdf_path.replace("\\", "/")
    clean_ly = layout_name.strip()
    cmd = (
        f"_.-LAYOUT\nS\n{clean_ly}\n_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-PLOT\nY\n{clean_ly}\nDWG To PDF.pc3\n\n\n\n\n\n\n\nY\n{plot_style}\nY\nN\n{norm_path}\nN\nY\n"
    )
    return await gateway.execute_command(cmd)


# ============================================================================
# MODULE 7: Autodesk Civil 3D (9 Tools)
# ============================================================================


@mcp.tool(name="check_civil3d_status")
async def check_civil3d_status() -> str:
    """Check whether the active drawing runs under Civil 3D 2027 and count available surfaces and alignments."""
    return await gateway.get_context("civil/status")


@mcp.tool(name="list_civil3d_surfaces")
async def list_civil3d_surfaces() -> str:
    """List TIN surfaces in Civil 3D with name, type, minimum elevation, maximum elevation, and triangle count."""
    return await gateway.get_context("civil/surfaces")


@mcp.tool(name="get_surface_elevation")
async def get_surface_elevation(x: float, y: float, surface_name: str | None = None) -> str:
    """Query exact ground elevation (Z coordinate) at point (x, y) on a Civil 3D TIN surface."""
    q_sup = f"&surface={surface_name}" if surface_name else ""
    return await gateway.get_context(f"civil/surface/elevation?x={num(x)}&y={num(y)}{q_sup}")


@mcp.tool(name="list_civil3d_alignments")
async def list_civil3d_alignments() -> str:
    """List horizontal alignments in Civil 3D with name, start station, end station, and length."""
    return await gateway.get_context("civil/alignments")


@mcp.tool(name="get_alignment_station_offset")
async def get_alignment_station_offset(x: float, y: float, alignment_name: str | None = None) -> str:
    """Calculate station and lateral offset of point (x, y) relative to a Civil 3D horizontal alignment."""
    q_al = f"&alignment={alignment_name}" if alignment_name else ""
    return await gateway.get_context(f"civil/alignment/station-offset?x={num(x)}&y={num(y)}{q_al}")


@mcp.tool(name="get_point_at_station")
async def get_point_at_station(
    station: float,
    offset: float = 0.0,
    alignment_name: str | None = None,
) -> str:
    """Calculate (X, Y) coordinate given a station and lateral offset along a Civil 3D alignment."""
    q_al = f"&alignment={alignment_name}" if alignment_name else ""
    return await gateway.get_context(
        f"civil/alignment/point-at-station?station={num(station)}&offset={num(offset)}{q_al}"
    )


@mcp.tool(name="get_station_coordinate")
async def get_station_coordinate(
    station: float,
    offset: float = 0.0,
    alignment_name: str | None = None,
) -> str:
    """Alias for get_point_at_station: Calculate (X, Y) coordinate given a station and lateral offset."""
    return await get_point_at_station(station=station, offset=offset, alignment_name=alignment_name)


@mcp.tool(name="sample_longitudinal_profile")
async def sample_longitudinal_profile(
    interval: float = 20.0,
    alignment_name: str | None = None,
    surface_name: str | None = None,
) -> str:
    """Sample terrain longitudinal profile along an alignment every 'interval' meters, returning station, X, Y, and elevation."""
    if not all_finite(interval) or interval <= 0:
        return json.dumps({"status": "error", "message": "Interval must be greater than zero."})

    # Fast path: Attempt atomic profile query directly from Gateway (1 roundtrip)
    q_al_fast = f"&alignment={alignment_name}" if alignment_name else ""
    q_sf_fast = f"&surface={surface_name}" if surface_name else ""
    try:
        atomic_res = await gateway.get_context(f"civil/profile?interval={num(interval)}{q_al_fast}{q_sf_fast}")
        parsed_atomic = json.loads(atomic_res)
        if parsed_atomic.get("status") == "success":
            return atomic_res
    except Exception:
        pass

    res_al = await gateway.get_context("civil/alignments")
    try:
        data_al = json.loads(res_al)
    except Exception:
        return res_al

    if data_al.get("status") != "success" or not data_al.get("data"):
        return json.dumps({"status": "error", "message": "No alignments found in Civil 3D."})

    chosen = None
    for a in data_al["data"]:
        a_name = a.get("name", a.get("nombre", ""))
        if not alignment_name or a_name.lower() == alignment_name.lower():
            chosen = a
            break

    if not chosen:
        return json.dumps({"status": "error", "message": f"Alignment not found: '{alignment_name}'."})

    chosen_name = chosen.get("name", chosen.get("nombre", ""))
    start_st = float(chosen.get("start_station", chosen.get("progresiva_inicial", 0.0)))
    end_st = float(chosen.get("end_station", chosen.get("progresiva_final", 0.0)))

    stations: list[float] = []
    curr = start_st
    while curr < end_st - 1e-6:
        stations.append(curr)
        curr += interval
    stations.append(end_st)

    if len(stations) > 500:
        return json.dumps(
            {"status": "error", "message": f"Interval generates {len(stations)} samples (max 500). Increase interval."}
        )

    samples = []
    z_min: float | None = None
    z_max: float | None = None
    z_ini: float | None = None
    z_fin: float | None = None

    q_al = f"&alignment={chosen_name}"
    q_sup = f"&surface={surface_name}" if surface_name else ""

    for st in stations:
        res_pt = await gateway.get_context(f"civil/alignment/point-at-station?station={num(st)}&offset=0{q_al}")
        try:
            pt_data = json.loads(res_pt)
        except Exception:
            pt_data = {}

        if pt_data.get("status") != "success" or "x" not in pt_data or "y" not in pt_data:
            samples.append({"station": st, "x": None, "y": None, "elevation": None})
            continue

        x_val = pt_data["x"]
        y_val = pt_data["y"]

        res_z = await gateway.get_context(f"civil/surface/elevation?x={num(x_val)}&y={num(y_val)}{q_sup}")
        try:
            z_data = json.loads(res_z)
            z_val = z_data.get("elevation", z_data.get("elevacion"))
        except Exception:
            z_val = None

        if z_val is not None:
            z_min = z_val if z_min is None else min(z_min, z_val)
            z_max = z_val if z_max is None else max(z_max, z_val)
            if z_ini is None:
                z_ini = z_val
            z_fin = z_val

        samples.append(
            {
                "station": round(st, 3),
                "x": round(x_val, 4),
                "y": round(y_val, 4),
                "elevation": round(z_val, 3) if z_val is not None else None,
            }
        )

    length = end_st - start_st
    avg_slope = None
    if z_ini is not None and z_fin is not None and length > 0:
        avg_slope = round((z_fin - z_ini) / length * 100.0, 4)

    return json.dumps(
        {
            "status": "success",
            "alignment": chosen_name,
            "surface": surface_name or "(first available)",
            "interval": interval,
            "start_station": start_st,
            "end_station": end_st,
            "total_samples": len(samples),
            "min_elevation": z_min,
            "max_elevation": z_max,
            "average_slope_percent": avg_slope,
            "samples": samples,
        }
    )


@mcp.tool(name="get_longitudinal_profile")
async def get_longitudinal_profile(
    interval: float = 20.0,
    alignment_name: str | None = None,
    surface_name: str | None = None,
) -> str:
    """Alias for sample_longitudinal_profile: Sample terrain longitudinal profile along an alignment."""
    return await sample_longitudinal_profile(
        interval=interval,
        alignment_name=alignment_name,
        surface_name=surface_name,
    )


@mcp.tool(name="create_civil3d_alignment")
async def create_civil3d_alignment(
    name: str,
    points: str,
    radius: float = 0.0,
    layer: str | None = None,
) -> str:
    """Create a horizontal alignment in Civil 3D from PI vertices 'x1,y1;x2,y2;...' with fixed radius tangent circular curves."""
    pts = parse_points(points, 2)
    if not pts or len(pts) < 2:
        return json.dumps(
            {"status": "error", "message": "Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 2 PIs."}
        )

    payload = {
        "name": name,
        "points": [{"x": p[0], "y": p[1]} for p in pts],
        "radius": radius,
        "layer": layer,
    }
    return await gateway.post_json("civil/alignment/create", payload)


@mcp.tool(name="create_civil3d_surface")
async def create_civil3d_surface(name: str, points: str) -> str:
    """Create a TIN surface in Civil 3D from survey points 'x1,y1,z1;x2,y2,z2;...' (minimum 3 points)."""
    pts = parse_points(points, 3)
    if not pts or len(pts) < 3:
        return json.dumps(
            {"status": "error", "message": "Invalid 'points' format. Use 'x1,y1,z1;x2,y2,z2;...' with at least 3 points."}
        )

    payload = {
        "name": name,
        "points": [{"x": p[0], "y": p[1], "z": p[2]} for p in pts],
    }
    return await gateway.post_json("civil/surface/create", payload)


# ============================================================================
# Main Entry Point for CLI / uvx
# ============================================================================


def main() -> None:
    """Run MCP server over standard input/output (STDIO) transport."""
    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
