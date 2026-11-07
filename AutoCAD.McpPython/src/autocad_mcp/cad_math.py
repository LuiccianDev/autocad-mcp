"""Analytical calculation utilities, computational geometry, and safe formatting for AutoCAD."""

from __future__ import annotations

import math
from typing import Any

# Reserved characters forbidden in AutoCAD layer names, block names, or symbol tables
FORBIDDEN_SYMBOL_CHARS = set('<>/\\":;?*|,=`')


def is_valid_symbol_name(name: str | None) -> bool:
    """Validate whether a layer, block, or symbol name is valid for the AutoCAD database."""
    if not name or not name.strip() or len(name) > 255:
        return False
    return not any(c in FORBIDDEN_SYMBOL_CHARS for c in name)


def num(value: float) -> str:
    """Format a float in culture-invariant notation with a decimal point."""
    if math.isnan(value) or math.isinf(value):
        raise ValueError("Value cannot be NaN or infinity.")
    if value == int(value):
        return f"{value:.1f}"
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


def calculate_distance(
    x1: float, y1: float, x2: float, y2: float, z1: float = 0.0, z2: float = 0.0
) -> dict[str, Any]:
    """Calculate 2D/3D Euclidean distance, coordinate deltas, azimuth, and slope without requiring AutoCAD."""
    if not all_finite(x1, y1, z1, x2, y2, z2):
        return {"status": "error", "message": "Coordinates must be finite numbers."}

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
        return {
            "status": "error",
            "message": "Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 3 numeric vertices.",
        }

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
        return {"status": "error", "message": "Degenerate polygon (collinear vertices or zero area)."}

    area = twice_area / 2.0
    abs_area = abs(area)
    is_ccw = area > 0

    return {
        "status": "success",
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


def build_batch_geometry_lisp(
    elements: list[dict[str, Any]] | str, default_layer: str = "0"
) -> tuple[str, dict[str, Any]]:
    """Compile a list of geometric entity definitions into a single atomic AutoLISP (progn ...) block."""
    import json

    if isinstance(elements, str):
        try:
            elements = json.loads(elements)
        except Exception as exc:
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

        entity_type = str(item.get("type", item.get("tipo", ""))).lower().strip()
        if not entity_type:
            raise ValueError(f"Element at index {idx} does not specify 'type'.")

        layer = str(item.get("layer", item.get("capa", default_layer))).strip()
        if not is_valid_symbol_name(layer):
            raise ValueError(f"Invalid layer name '{layer}' in element {idx}.")
        layer_lisp = f"(cons 8 {lisp_string(layer)})"

        if entity_type in ("line", "linea"):
            x1 = float(item.get("x1", 0.0))
            y1 = float(item.get("y1", 0.0))
            x2 = float(item.get("x2", 0.0))
            y2 = float(item.get("y2", 0.0))
            z1 = float(item.get("z1", 0.0))
            z2 = float(item.get("z2", 0.0))

            if not all_finite(x1, y1, z1, x2, y2, z2):
                raise ValueError(f"Invalid coordinates in line element {idx}.")
            if x1 == x2 and y1 == y2 and z1 == z2:
                raise ValueError(f"Line element {idx} has zero length (identical start and end points).")

            expr = (
                f"(entmake (list '(0 . \"LINE\") {layer_lisp} "
                f"(list 10 {num(x1)} {num(y1)} {num(z1)}) "
                f"(list 11 {num(x2)} {num(y2)} {num(z2)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["line"] = summary_counts.get("line", 0) + 1

        elif entity_type in ("rectangle", "rectangulo"):
            x1 = float(item.get("x1", 0.0))
            y1 = float(item.get("y1", 0.0))
            x2 = float(item.get("x2", 0.0))
            y2 = float(item.get("y2", 0.0))

            if not all_finite(x1, y1, x2, y2):
                raise ValueError(f"Invalid coordinates in rectangle element {idx}.")
            if x1 == x2 or y1 == y2:
                raise ValueError(f"Rectangle element {idx} has zero area.")

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
            lisp_expressions.append(expr)
            summary_counts["rectangle"] = summary_counts.get("rectangle", 0) + 1

        elif entity_type in ("circle", "circulo"):
            cx = float(item.get("center_x", item.get("centerX", item.get("centro_x", item.get("centroX", item.get("x", 0.0))))))
            cy = float(item.get("center_y", item.get("centerY", item.get("centro_y", item.get("centroY", item.get("y", 0.0))))))
            cz = float(item.get("center_z", item.get("centerZ", item.get("centro_z", item.get("centroZ", item.get("z", 0.0))))))
            r = float(item.get("radius", item.get("radio", item.get("r", 0.0))))

            if not all_finite(cx, cy, cz, r) or r <= 0:
                raise ValueError(f"Invalid radius or coordinates in circle element {idx}.")

            expr = (
                f"(entmake (list '(0 . \"CIRCLE\") {layer_lisp} "
                f"(list 10 {num(cx)} {num(cy)} {num(cz)}) (cons 40 {num(r)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["circle"] = summary_counts.get("circle", 0) + 1

        elif entity_type in ("arc", "arco"):
            cx = float(item.get("center_x", item.get("centerX", item.get("centro_x", item.get("centroX", item.get("x", 0.0))))))
            cy = float(item.get("center_y", item.get("centerY", item.get("centro_y", item.get("centroY", item.get("y", 0.0))))))
            cz = float(item.get("center_z", item.get("centerZ", item.get("centro_z", item.get("centroZ", item.get("z", 0.0))))))
            r = float(item.get("radius", item.get("radio", item.get("r", 0.0))))
            ang_start = float(item.get("start_angle", item.get("startAngle", item.get("angulo_inicio", item.get("anguloInicio", 0.0)))))
            ang_end = float(item.get("end_angle", item.get("endAngle", item.get("angulo_fin", item.get("anguloFin", 0.0)))))

            if not all_finite(cx, cy, cz, r, ang_start, ang_end) or r <= 0:
                raise ValueError(f"Invalid parameters in arc element {idx}.")

            rad_start = math.radians(ang_start)
            rad_end = math.radians(ang_end)

            expr = (
                f"(entmake (list '(0 . \"ARC\") {layer_lisp} "
                f"(list 10 {num(cx)} {num(cy)} {num(cz)}) (cons 40 {num(r)}) "
                f"(cons 50 {num(rad_start)}) (cons 51 {num(rad_end)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["arc"] = summary_counts.get("arc", 0) + 1

        elif entity_type in ("polyline", "polilinea"):
            pts_raw = item.get("points", item.get("puntos"))
            closed = bool(item.get("closed", item.get("cerrada", False)))

            if isinstance(pts_raw, str):
                pts = parse_points(pts_raw, 2)
            elif isinstance(pts_raw, list):
                pts = []
                for p in pts_raw:
                    if isinstance(p, (list, tuple)) and len(p) >= 2:
                        pts.append([float(p[0]), float(p[1])])
                    elif isinstance(p, dict) and "x" in p and "y" in p:
                        pts.append([float(p["x"]), float(p["y"])])
            else:
                pts = None

            if not pts or len(pts) < 2:
                raise ValueError(f"Polyline element {idx} requires at least 2 valid vertices.")

            flag = 1 if closed else 0
            v_str = " ".join(f"(list 10 {num(p[0])} {num(p[1])})" for p in pts)
            expr = (
                f"(entmake (append (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(100 . \"AcDbPolyline\") "
                f"{layer_lisp} (cons 90 {len(pts)}) (cons 70 {flag})) (list {v_str})))"
            )
            lisp_expressions.append(expr)
            summary_counts["polyline"] = summary_counts.get("polyline", 0) + 1

        elif entity_type in ("text", "texto"):
            txt = str(item.get("text", item.get("texto", "")))
            tx = float(item.get("x", 0.0))
            ty = float(item.get("y", 0.0))
            tz = float(item.get("z", 0.0))
            h = float(item.get("height", item.get("altura", 0.25)))
            rot = float(item.get("rotation", item.get("rotacion", 0.0)))

            if not all_finite(tx, ty, tz, h, rot) or h <= 0:
                raise ValueError(f"Invalid values in text element {idx}.")

            rot_rad = math.radians(rot)
            expr = (
                f"(entmake (list '(0 . \"TEXT\") {layer_lisp} "
                f"(list 10 {num(tx)} {num(ty)} {num(tz)}) "
                f"(cons 40 {num(h)}) (cons 1 {lisp_string(txt)}) (cons 50 {num(rot_rad)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["text"] = summary_counts.get("text", 0) + 1

        else:
            raise ValueError(f"Unsupported entity type: '{entity_type}' at index {idx}.")

    full_lisp = f"(progn {' '.join(lisp_expressions)} (princ))\n"
    summary = {
        "status": "success",
        "total": len(lisp_expressions),
        "breakdown": summary_counts,
        "desglose": summary_counts,
    }
    return full_lisp, summary


# Legacy aliases for backward compatibility during transition
medir_distancia_analitica = calculate_distance
calcular_area_poligono_analitico = calculate_polygon_area
