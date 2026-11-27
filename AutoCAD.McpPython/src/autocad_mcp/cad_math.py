"""Analytical calculation utilities, computational geometry, and safe formatting for AutoCAD."""

from __future__ import annotations

import math
from typing import Any

# Reserved characters forbidden in AutoCAD layer names, block names, or symbol tables
FORBIDDEN_SYMBOL_CHARS = set('<>/\\":;?*|,=`')


def is_valid_symbol_name(name: str | None) -> bool:
    """Validate whether a layer, block, or symbol name is valid for the AutoCAD database."""
    if not name or not name.strip():
        return False
    trimmed = name.strip()
    if len(trimmed) > 255:
        return False
    return not any(ord(c) < 0x20 or c in FORBIDDEN_SYMBOL_CHARS for c in trimmed)


def num(value: float) -> str:
    """Format a float in culture-invariant notation without truncating small non-zero values to '0'."""
    if math.isnan(value) or math.isinf(value):
        raise ValueError("Value cannot be NaN or infinity.")
    if value == int(value) and abs(value) < 1e15:
        return f"{value:.1f}"
    if 0 < abs(value) < 1e-4:
        return format(value, ".15g")
    return f"{value:.6f}".rstrip("0").rstrip(".")


def lisp_string(value: str) -> str:
    """Escape and format a Python string as a safe AutoLISP string literal."""
    escaped = value.replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ").replace("\r", " ")
    return f'"{escaped}"'


def parse_points(points_str: str | None, columns: int) -> list[list[float]] | None:
    """Parse a string formatted as 'x1,y1;x2,y2;...' into a list of float coordinate lists."""
    if not points_str or not points_str.strip():
        return None

    rows = [r.strip() for r in points_str.split(";") if r.strip()]
    if not rows:
        return None

    result: list[list[float]] = []
    for r in rows:
        parts = [p.strip() for p in r.split(",")]
        if len(parts) != columns:
            return None
        try:
            coords = [float(p) for p in parts]
            if any(math.isnan(c) or math.isinf(c) for c in coords):
                return None
            result.append(coords)
        except ValueError:
            return None

    return result


def all_finite(*values: float) -> bool:
    """Verify that all numeric values are finite (not NaN or infinite)."""
    return all(not (math.isnan(v) or math.isinf(v)) for v in values)


def _math_error(message: str) -> dict[str, Any]:
    return {
        "status": "error",
        "ok": False,
        "message": message,
        "error": {"code": "bad_argument", "message": message},
    }


def calculate_distance(
    x1: float, y1: float, x2: float, y2: float, z1: float = 0.0, z2: float = 0.0
) -> dict[str, Any]:
    """Calculate 2D/3D Euclidean distance, coordinate deltas, azimuth, and slope without requiring AutoCAD."""
    if not all_finite(x1, y1, z1, x2, y2, z2):
        return _math_error("Coordinates must be finite numbers.")

    dx = x2 - x1
    dy = y2 - y1
    dz = z2 - z1
    d2 = math.sqrt(dx * dx + dy * dy)
    d3 = math.sqrt(d2 * d2 + dz * dz)

    angle = math.degrees(math.atan2(dy, dx))
    if angle < 0:
        angle += 360.0

    slope = round(dz / d2 * 100.0, 4) if d2 > 0 else None

    return {
        "status": "success",
        "ok": True,
        "distance_2d": round(d2, 4),
        "distance_3d": round(d3, 4),
        "delta_x": round(dx, 4),
        "delta_y": round(dy, 4),
        "delta_z": round(dz, 4),
        "angle_degrees": round(angle, 4),
        "slope_percent": slope,
        "units": "meters",
        # Backward compatibility aliases
        "distancia_2d": round(d2, 4),
        "distancia_3d": round(d3, 4),
        "angulo_grados": round(angle, 4),
        "pendiente_porcentaje": slope,
    }


def calculate_polygon_area(points_str: str) -> dict[str, Any]:
    """Calculate polygon area in m2, hectares, perimeter, centroid, and winding orientation using Gauss algorithm."""
    pts = parse_points(points_str, 2)
    if not pts or len(pts) < 3:
        return _math_error(
            "Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 3 numeric vertices."
        )

    n = len(pts)
    if n > 3 and pts[0][0] == pts[n - 1][0] and pts[0][1] == pts[n - 1][1]:
        n -= 1

    ox, oy = pts[0][0], pts[0][1]
    twice_area = 0.0
    perimeter = 0.0
    cx = 0.0
    cy = 0.0

    for i in range(n):
        xi = pts[i][0] - ox
        yi = pts[i][1] - oy
        xj = pts[(i + 1) % n][0] - ox
        yj = pts[(i + 1) % n][1] - oy

        cross = xi * yj - xj * yi
        twice_area += cross
        cx += (xi + xj) * cross
        cy += (yi + yj) * cross
        perimeter += math.hypot(xj - xi, yj - yi)

    if abs(twice_area) < 1e-12:
        return _math_error("Degenerate polygon (collinear vertices or zero area).")

    area = twice_area / 2.0
    abs_area = abs(area)
    is_ccw = area > 0

    return {
        "status": "success",
        "ok": True,
        "area_m2": round(abs_area, 4),
        "area_hectares": round(abs_area / 10000.0, 6),
        "perimeter_m": round(perimeter, 4),
        "centroid": {
            "x": round(cx / (3.0 * twice_area) + ox, 4),
            "y": round(cy / (3.0 * twice_area) + oy, 4),
        },
        "is_clockwise": not is_ccw,
        "orientation": "counterclockwise" if is_ccw else "clockwise",
        "vertices": n,
        # Backward compatibility aliases
        "perimetro_m": round(perimeter, 4),
        "centroide": {
            "x": round(cx / (3.0 * twice_area) + ox, 4),
            "y": round(cy / (3.0 * twice_area) + oy, 4),
        },
        "sentido": "antihorario" if is_ccw else "horario",
    }


def _build_lower_map(item: dict[str, Any]) -> dict[str, Any]:
    lowered: dict[str, Any] = {}
    for k, v in item.items():
        lk = str(k).lower()
        if lk not in lowered or lowered[lk] is None:
            lowered[lk] = v
    return lowered


def _get_ci(lowered: dict[str, Any], *keys: str) -> Any:
    for k in keys:
        val = lowered.get(k.lower())
        if val is not None:
            return val
    return None


def _get_num(lowered: dict[str, Any], *keys: str, default: float | None = None) -> tuple[bool, float | None]:
    raw = _get_ci(lowered, *keys)
    if raw is None:
        return True, default
    if isinstance(raw, bool):
        return False, None
    try:
        val = float(raw)
    except (TypeError, ValueError):
        return False, None
    if not math.isfinite(val):
        return False, None
    return True, val


def _parse_bool(raw: Any, default: bool = False) -> tuple[bool, bool]:
    if raw is None:
        return True, default
    if isinstance(raw, bool):
        return True, raw
    if isinstance(raw, (int, float)) and not isinstance(raw, bool):
        return True, bool(raw)
    if isinstance(raw, str):
        norm = raw.strip().lower()
        if norm in ("true", "1", "yes", "on"):
            return True, True
        if norm in ("false", "0", "no", "off", ""):
            return True, False
    return False, default


def normalize_batch_element(
    item: dict[str, Any],
    default_layer: str,
    index: int,
) -> tuple[dict[str, Any] | None, str | None]:
    """Normalize a draw_batch_geometry dictionary (including legacy Spanish keys) into a canonical native /batch operation."""
    lowered = _build_lower_map(item)
    raw_type_val = _get_ci(lowered, "type", "tipo")
    raw_type = str(raw_type_val or "").strip().lower()
    if not raw_type:
        return None, f"Element #{index} is missing 'type'."

    raw_layer = _get_ci(lowered, "layer", "capa")
    layer = str(raw_layer if raw_layer is not None else default_layer).strip()
    if not is_valid_symbol_name(layer):
        return None, f"Element #{index} has invalid layer name '{layer}'."

    if raw_type in ("line", "linea"):
        ok1, x1 = _get_num(lowered, "x1", default=0.0)
        ok2, y1 = _get_num(lowered, "y1", default=0.0)
        ok3, z1 = _get_num(lowered, "z1", default=0.0)
        ok4, x2 = _get_num(lowered, "x2", default=0.0)
        ok5, y2 = _get_num(lowered, "y2", default=0.0)
        ok6, z2 = _get_num(lowered, "z2", default=0.0)
        if not (ok1 and ok2 and ok3 and ok4 and ok5 and ok6):
            return None, f"Invalid non-numeric coordinates in line element #{index}."
        if x1 == x2 and y1 == y2 and z1 == z2:
            return None, f"Line element #{index} has zero length (identical start and end points)."
        return {
            "op": "create",
            "type": "line",
            "start": [x1, y1, z1],
            "end": [x2, y2, z2],
            "layer": layer,
        }, None

    if raw_type in ("rectangle", "rectangulo", "rect"):
        ok1, x1 = _get_num(lowered, "x1", default=0.0)
        ok2, y1 = _get_num(lowered, "y1", default=0.0)
        ok3, x2 = _get_num(lowered, "x2", default=0.0)
        ok4, y2 = _get_num(lowered, "y2", default=0.0)
        if not (ok1 and ok2 and ok3 and ok4):
            return None, f"Invalid non-numeric coordinates in rectangle element #{index}."
        if x1 == x2 or y1 == y2:
            return None, f"Rectangle element #{index} has zero area."
        return {
            "op": "create",
            "type": "rectangle",
            "min": [x1, y1],
            "max": [x2, y2],
            "layer": layer,
        }, None

    if raw_type in ("circle", "circulo"):
        ok1, cx = _get_num(lowered, "center_x", "centerX", "centroX", "centro_x", "x", default=0.0)
        ok2, cy = _get_num(lowered, "center_y", "centerY", "centroY", "centro_y", "y", default=0.0)
        ok3, cz = _get_num(lowered, "center_z", "centerZ", "centroZ", "centro_z", "z", default=0.0)
        ok4, r = _get_num(lowered, "radius", "radio", "r", default=0.0)
        if not (ok1 and ok2 and ok3 and ok4) or r is None or r <= 0:
            return None, f"Invalid circle radius or center in element #{index}."
        return {"op": "create", "type": "circle", "center": [cx, cy, cz], "radius": r, "layer": layer}, None

    if raw_type in ("arc", "arco"):
        ok1, cx = _get_num(lowered, "center_x", "centerX", "centroX", "centro_x", "x", default=0.0)
        ok2, cy = _get_num(lowered, "center_y", "centerY", "centroY", "centro_y", "y", default=0.0)
        ok3, cz = _get_num(lowered, "center_z", "centerZ", "centroZ", "centro_z", "z", default=0.0)
        ok4, r = _get_num(lowered, "radius", "radio", "r", default=0.0)
        ok5, sa = _get_num(lowered, "start_angle", "startAngle", "anguloInicio", "angulo_inicio", default=0.0)
        ok6, ea = _get_num(lowered, "end_angle", "endAngle", "anguloFin", "angulo_fin", default=0.0)
        if not (ok1 and ok2 and ok3 and ok4 and ok5 and ok6) or r is None or r <= 0 or sa is None or ea is None:
            return None, f"Invalid arc parameters in element #{index}."
        if abs(ea - sa) < 1e-9 or (abs(ea - sa) % 360.0) < 1e-9:
            return None, f"Arc element #{index} start_angle and end_angle must differ."
        return {
            "op": "create",
            "type": "arc",
            "center": [cx, cy, cz],
            "radius": r,
            "start_angle": sa,
            "end_angle": ea,
            "layer": layer,
        }, None

    if raw_type in ("polyline", "polilinea", "lwpolyline"):
        raw_pts = _get_ci(lowered, "points", "puntos")
        pts: list[list[float]] | None = None
        if isinstance(raw_pts, str):
            parsed = parse_points(raw_pts, 2)
            pts = [[p[0], p[1]] for p in parsed] if parsed else None
        elif isinstance(raw_pts, list):
            extracted: list[list[float]] = []
            for p in raw_pts:
                if isinstance(p, (list, tuple)) and len(p) in (2, 3) and not any(isinstance(v, bool) for v in p):
                    try:
                        coords = [float(v) for v in p]
                    except (TypeError, ValueError):
                        extracted = []
                        break
                elif isinstance(p, dict) and "x" in p and "y" in p and not isinstance(p["x"], bool) and not isinstance(p["y"], bool):
                    try:
                        coords = [float(p["x"]), float(p["y"])]
                        if "bulge" in p and p["bulge"] is not None and not isinstance(p["bulge"], bool):
                            coords.append(float(p["bulge"]))
                    except (TypeError, ValueError):
                        extracted = []
                        break
                else:
                    extracted = []
                    break
                if not all_finite(*coords):
                    extracted = []
                    break
                extracted.append(coords)
            pts = extracted or None

        if not pts or len(pts) < 2:
            return None, f"Polyline element #{index} requires at least 2 valid vertices."
        ok_closed, closed = _parse_bool(_get_ci(lowered, "closed", "cerrada"), default=False)
        if not ok_closed:
            return None, f"Polyline element #{index} has invalid 'closed' boolean."
        return {"op": "create", "type": "polyline", "points": pts, "closed": closed, "layer": layer}, None

    if raw_type in ("text", "texto"):
        txt_val = _get_ci(lowered, "text", "texto")
        txt = str(txt_val) if txt_val is not None else ""
        if not txt.strip():
            return None, f"Text element #{index} requires non-empty 'text'."
        ok1, x = _get_num(lowered, "x", default=0.0)
        ok2, y = _get_num(lowered, "y", default=0.0)
        ok3, z = _get_num(lowered, "z", default=0.0)
        ok4, h = _get_num(lowered, "height", "altura", default=0.25)
        ok5, rot = _get_num(lowered, "rotation", "rotacion", default=0.0)
        if not (ok1 and ok2 and ok3 and ok4 and ok5) or h is None or h <= 0:
            return None, f"Invalid text geometry in element #{index}."
        return {
            "op": "create",
            "type": "text",
            "text": txt,
            "position": [x, y, z],
            "height": h,
            "rotation": rot,
            "layer": layer,
        }, None

    return None, f"Unsupported entity type: '{raw_type}' at element #{index}."


def build_batch_geometry_lisp(
    elements: list[dict[str, Any]] | str, default_layer: str = "0"
) -> tuple[str, dict[str, Any]]:
    """Compile a list of geometric entity definitions into a single atomic AutoLISP (progn ...) block."""
    import json

    if isinstance(elements, str):
        try:
            elements = json.loads(elements)
        except json.JSONDecodeError as exc:
            raise ValueError(f"Error parsing elements JSON: {exc}") from exc

    if not isinstance(elements, list):
        raise ValueError("The 'elements' parameter must be a list of dictionaries or valid JSON string.")

    if not elements:
        raise ValueError("The elements list cannot be empty.")

    lisp_expressions: list[str] = []
    summary_counts: dict[str, int] = {}

    for idx, item in enumerate(elements):
        if not isinstance(item, dict):
            raise ValueError(f"Element at index {idx} must be a dictionary.")

        spec, err = normalize_batch_element(item, default_layer, idx)
        if err or spec is None:
            raise ValueError(err or f"Invalid element at index {idx}.")

        entity_type = spec["type"]
        layer_lisp = f"(cons 8 {lisp_string(spec['layer'])})"

        if entity_type == "line":
            x1, y1, z1 = spec["start"]
            x2, y2, z2 = spec["end"]
            expr = (
                f"(entmake (list '(0 . \"LINE\") {layer_lisp} "
                f"(list 10 {num(x1)} {num(y1)} {num(z1)}) "
                f"(list 11 {num(x2)} {num(y2)} {num(z2)})))"
            )
        elif entity_type == "rectangle":
            x1, y1 = spec["min"]
            x2, y2 = spec["max"]
            min_x, max_x = min(x1, x2), max(x1, x2)
            min_y, max_y = min(y1, y2), max(y1, y2)
            expr = (
                f"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(100 . \"AcDbPolyline\") "
                f"{layer_lisp} '(90 . 4) '(70 . 1) "
                f"(list 10 {num(min_x)} {num(min_y)}) "
                f"(list 10 {num(max_x)} {num(min_y)}) "
                f"(list 10 {num(max_x)} {num(max_y)}) "
                f"(list 10 {num(min_x)} {num(max_y)})))"
            )
        elif entity_type == "circle":
            cx, cy, cz = spec["center"]
            expr = (
                f"(entmake (list '(0 . \"CIRCLE\") {layer_lisp} "
                f"(list 10 {num(cx)} {num(cy)} {num(cz)}) (cons 40 {num(spec['radius'])})))"
            )
        elif entity_type == "arc":
            cx, cy, cz = spec["center"]
            rad_start = math.radians(spec["start_angle"])
            rad_end = math.radians(spec["end_angle"])
            expr = (
                f"(entmake (list '(0 . \"ARC\") {layer_lisp} "
                f"(list 10 {num(cx)} {num(cy)} {num(cz)}) (cons 40 {num(spec['radius'])}) "
                f"(cons 50 {num(rad_start)}) (cons 51 {num(rad_end)})))"
            )
        elif entity_type == "polyline":
            pts = spec["points"]
            flag = 1 if spec["closed"] else 0
            v_parts: list[str] = []
            for p in pts:
                v_parts.append(f"(list 10 {num(p[0])} {num(p[1])})")
                if len(p) >= 3 and p[2] != 0:
                    v_parts.append(f"(cons 42 {num(p[2])})")
            v_str = " ".join(v_parts)
            expr = (
                f"(entmake (append (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(100 . \"AcDbPolyline\") "
                f"{layer_lisp} (cons 90 {len(pts)}) (cons 70 {flag})) (list {v_str})))"
            )
        else:
            tx, ty, tz = spec["position"]
            rot_rad = math.radians(spec["rotation"])
            expr = (
                f"(entmake (list '(0 . \"TEXT\") {layer_lisp} "
                f"(list 10 {num(tx)} {num(ty)} {num(tz)}) "
                f"(cons 40 {num(spec['height'])}) (cons 1 {lisp_string(spec['text'])}) (cons 50 {num(rot_rad)})))"
            )

        lisp_expressions.append(expr)
        summary_counts[entity_type] = summary_counts.get(entity_type, 0) + 1

    full_lisp = f"(progn {' '.join(lisp_expressions)} (princ))\n"
    desglose = {
        **summary_counts,
        "linea": summary_counts.get("line", 0),
        "rectangulo": summary_counts.get("rectangle", 0),
        "circulo": summary_counts.get("circle", 0),
        "arco": summary_counts.get("arc", 0),
        "polilinea": summary_counts.get("polyline", 0),
        "texto": summary_counts.get("text", 0),
    }
    summary = {
        "status": "success",
        "total": len(lisp_expressions),
        "breakdown": summary_counts,
        "desglose": desglose,
    }
    return full_lisp, summary


# Legacy aliases for backward compatibility during transition
medir_distancia_analitica = calculate_distance
calcular_area_poligono_analitico = calculate_polygon_area
