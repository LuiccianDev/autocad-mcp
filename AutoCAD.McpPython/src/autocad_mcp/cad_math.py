"""Utilidades de cálculo analítico, geometría computacional y formateo seguro para AutoCAD."""

from __future__ import annotations

import math
from typing import Any

# Caracteres reservados prohibidos en nombres de capas, bloques o tablas de símbolos de AutoCAD
FORBIDDEN_SYMBOL_CHARS = set('<>/\\":;?*|,=`')


def is_valid_symbol_name(name: str | None) -> bool:
    """Valida si un nombre de capa, bloque o estilo es válido para la base de datos de AutoCAD."""
    if not name or not name.strip() or len(name) > 255:
        return False
    return not any(c in FORBIDDEN_SYMBOL_CHARS for c in name)


def num(value: float) -> str:
    """Formatea un número flotante en notación invariante con punto decimal."""
    if math.isnan(value) or math.isinf(value):
        raise ValueError("El valor no puede ser NaN o infinito.")
    # Si es entero exacto, formatear como entero o con decimal limpio
    if value == int(value):
        return f"{value:.1f}"
    return f"{value:.6f}".rstrip("0").rstrip(".")


def lisp_string(value: str) -> str:
    """Convierte una cadena de Python en un literal seguro para el intérprete de AutoLISP."""
    escaped = value.replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ").replace("\r", " ")
    return f'"{escaped}"'


def parse_points(points_str: str | None, columns: int) -> list[list[float]] | None:
    """Parsea una cadena en formato 'x1,y1;x2,y2;...' en una lista de listas de flotantes."""
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
    """Verifica que todos los valores numéricos sean finitos (no NaN ni infinito)."""
    return all(not (math.isnan(v) or math.isinf(v)) for v in values)


def medir_distancia_analitica(
    x1: float, y1: float, x2: float, y2: float, z1: float = 0.0, z2: float = 0.0
) -> dict[str, Any]:
    """Calcula distancias euclídeas 2D/3D, incrementos, azimut y pendiente sin requerir AutoCAD."""
    if not all_finite(x1, y1, z1, x2, y2, z2):
        return {"status": "error", "message": "Las coordenadas deben ser números finitos."}

    dx = x2 - x1
    dy = y2 - y1
    dz = z2 - z1
    d2 = math.sqrt(dx * dx + dy * dy)
    d3 = math.sqrt(d2 * d2 + dz * dz)

    angle = math.degrees(math.atan2(dy, dx))
    if angle < 0:
        angle += 360.0

    pendiente = round(dz / d2 * 100.0, 4) if d2 > 0 else None

    return {
        "status": "success",
        "distancia_2d": round(d2, 4),
        "distancia_3d": round(d3, 4),
        "delta_x": round(dx, 4),
        "delta_y": round(dy, 4),
        "delta_z": round(dz, 4),
        "angulo_grados": round(angle, 4),
        "pendiente_porcentaje": pendiente,
        "unidades": "metros",
    }


def calcular_area_poligono_analitico(points_str: str) -> dict[str, Any]:
    """Calcula área en m², hectáreas, perímetro, centroide y sentido de giro mediante el algoritmo de Gauss."""
    pts = parse_points(points_str, 2)
    if not pts or len(pts) < 3:
        return {
            "status": "error",
            "message": "Formato de 'puntos' inválido. Use 'x1,y1;x2,y2;...' con al menos 3 vértices numéricos.",
        }

    # Descartar duplicado final si el usuario cerró el polígono explícitamente
    n = len(pts)
    if n > 3 and pts[0][0] == pts[n - 1][0] and pts[0][1] == pts[n - 1][1]:
        n -= 1

    # Traslación al origen del primer punto para estabilidad numérica en coordenadas UTM grandes
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
        return {"status": "error", "message": "El polígono es degenerado (vértices colineales o área nula)."}

    area = twice_area / 2.0
    abs_area = abs(area)

    return {
        "status": "success",
        "area_m2": round(abs_area, 4),
        "area_hectareas": round(abs_area / 10000.0, 6),
        "perimetro_m": round(perimeter, 4),
        "centroide": {
            "x": round(cx / (3.0 * twice_area) + ox, 4),
            "y": round(cy / (3.0 * twice_area) + oy, 4),
        },
        "sentido": "antihorario" if area > 0 else "horario",
        "vertices": n,
    }


def build_batch_geometry_lisp(
    elements: list[dict[str, Any]] | str, default_layer: str = "0"
) -> tuple[str, dict[str, Any]]:
    """Compila una lista de definiciones geométricas en un único bloque AutoLISP atómico (progn ...)."""
    import json

    if isinstance(elements, str):
        try:
            elements = json.loads(elements)
        except Exception as exc:
            raise ValueError(f"Error parseando JSON de elementos: {exc}") from exc

    if not isinstance(elements, list):
        raise ValueError("El parámetro 'elementos' debe ser una lista de diccionarios o un string JSON válido.")

    if not elements:
        raise ValueError("La lista de elementos no puede estar vacía.")

    lisp_expressions: list[str] = []
    summary_counts: dict[str, int] = {}

    for idx, item in enumerate(elements):
        if not isinstance(item, dict):
            raise ValueError(f"El elemento en el índice {idx} debe ser un diccionario.")

        tipo = str(item.get("tipo", "")).lower().strip()
        if not tipo:
            raise ValueError(f"El elemento en el índice {idx} no especifica 'tipo'.")

        capa = str(item.get("capa") or default_layer).strip()
        if not is_valid_symbol_name(capa):
            raise ValueError(f"Nombre de capa inválido '{capa}' en el elemento {idx}.")
        layer_lisp = f"(cons 8 {lisp_string(capa)})"

        if tipo in ("linea", "line"):
            x1 = float(item.get("x1", 0.0))
            y1 = float(item.get("y1", 0.0))
            x2 = float(item.get("x2", 0.0))
            y2 = float(item.get("y2", 0.0))
            z1 = float(item.get("z1", 0.0))
            z2 = float(item.get("z2", 0.0))

            if not all_finite(x1, y1, z1, x2, y2, z2):
                raise ValueError(f"Coordenadas inválidas en elemento línea {idx}.")
            if x1 == x2 and y1 == y2 and z1 == z2:
                raise ValueError(f"La línea {idx} tiene longitud cero (inicio y fin idénticos).")

            expr = (
                f"(entmake (list '(0 . \"LINE\") {layer_lisp} "
                f"(list 10 {num(x1)} {num(y1)} {num(z1)}) "
                f"(list 11 {num(x2)} {num(y2)} {num(z2)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["linea"] = summary_counts.get("linea", 0) + 1

        elif tipo in ("rectangulo", "rectangle"):
            x1 = float(item.get("x1", 0.0))
            y1 = float(item.get("y1", 0.0))
            x2 = float(item.get("x2", 0.0))
            y2 = float(item.get("y2", 0.0))

            if not all_finite(x1, y1, x2, y2):
                raise ValueError(f"Coordenadas inválidas en elemento rectángulo {idx}.")
            if x1 == x2 or y1 == y2:
                raise ValueError(f"El rectángulo {idx} tiene área nula.")

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
            summary_counts["rectangulo"] = summary_counts.get("rectangulo", 0) + 1

        elif tipo in ("circulo", "circle"):
            cx = float(item.get("centro_x", item.get("centroX", item.get("x", 0.0))))
            cy = float(item.get("centro_y", item.get("centroY", item.get("y", 0.0))))
            cz = float(item.get("centro_z", item.get("centroZ", item.get("z", 0.0))))
            r = float(item.get("radio", item.get("r", 0.0)))

            if not all_finite(cx, cy, cz, r) or r <= 0:
                raise ValueError(f"Radio o coordenadas inválidas en círculo {idx}.")

            expr = (
                f"(entmake (list '(0 . \"CIRCLE\") {layer_lisp} "
                f"(list 10 {num(cx)} {num(cy)} {num(cz)}) (cons 40 {num(r)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["circulo"] = summary_counts.get("circulo", 0) + 1

        elif tipo in ("arco", "arc"):
            cx = float(item.get("centro_x", item.get("centroX", item.get("x", 0.0))))
            cy = float(item.get("centro_y", item.get("centroY", item.get("y", 0.0))))
            cz = float(item.get("centro_z", item.get("centroZ", item.get("z", 0.0))))
            r = float(item.get("radio", item.get("r", 0.0)))
            ang_ini = float(item.get("angulo_inicio", item.get("anguloInicio", 0.0)))
            ang_fin = float(item.get("angulo_fin", item.get("anguloFin", 0.0)))

            if not all_finite(cx, cy, cz, r, ang_ini, ang_fin) or r <= 0:
                raise ValueError(f"Valores inválidos en arco {idx}.")

            rad_ini = math.radians(ang_ini)
            rad_fin = math.radians(ang_fin)

            expr = (
                f"(entmake (list '(0 . \"ARC\") {layer_lisp} "
                f"(list 10 {num(cx)} {num(cy)} {num(cz)}) (cons 40 {num(r)}) "
                f"(cons 50 {num(rad_ini)}) (cons 51 {num(rad_fin)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["arco"] = summary_counts.get("arco", 0) + 1

        elif tipo in ("polilinea", "polyline"):
            pts_raw = item.get("puntos")
            cerrada = bool(item.get("cerrada", item.get("closed", False)))

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
                raise ValueError(f"Polilínea {idx} requiere al menos 2 vértices válidos.")

            flag = 1 if cerrada else 0
            v_str = " ".join(f"(list 10 {num(p[0])} {num(p[1])})" for p in pts)
            expr = (
                f"(entmake (append (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(100 . \"AcDbPolyline\") "
                f"{layer_lisp} (cons 90 {len(pts)}) (cons 70 {flag})) (list {v_str})))"
            )
            lisp_expressions.append(expr)
            summary_counts["polilinea"] = summary_counts.get("polilinea", 0) + 1

        elif tipo in ("texto", "text"):
            txt = str(item.get("texto", item.get("text", "")))
            tx = float(item.get("x", 0.0))
            ty = float(item.get("y", 0.0))
            tz = float(item.get("z", 0.0))
            h = float(item.get("altura", item.get("height", 0.25)))
            rot = float(item.get("rotacion", item.get("rotation", 0.0)))

            if not all_finite(tx, ty, tz, h, rot) or h <= 0:
                raise ValueError(f"Valores inválidos en texto {idx}.")

            rot_rad = math.radians(rot)
            expr = (
                f"(entmake (list '(0 . \"TEXT\") {layer_lisp} "
                f"(list 10 {num(tx)} {num(ty)} {num(tz)}) "
                f"(cons 40 {num(h)}) (cons 1 {lisp_string(txt)}) (cons 50 {num(rot_rad)})))"
            )
            lisp_expressions.append(expr)
            summary_counts["texto"] = summary_counts.get("texto", 0) + 1

        else:
            raise ValueError(f"Tipo de entidad no soportado: '{tipo}' en índice {idx}.")

    full_lisp = f"(progn {' '.join(lisp_expressions)} (princ))\n"
    summary = {
        "status": "success",
        "total": len(lisp_expressions),
        "desglose": summary_counts,
    }
    return full_lisp, summary
