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
