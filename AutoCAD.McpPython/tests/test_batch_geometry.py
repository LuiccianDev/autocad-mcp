"""Pruebas unitarias para el motor de dibujo en lote (Batch Geometry) y ejecución por lotes."""

import json
import pytest
from autocad_mcp.cad_math import (
    build_batch_geometry_lisp,
)


def test_build_batch_geometry_lisp_valid():
    elements = [
        {"tipo": "linea", "x1": 0, "y1": 0, "x2": 10, "y2": 0, "capa": "ARQ-MUROS"},
        {"tipo": "rectangulo", "x1": 2, "y1": 2, "x2": 5, "y2": 4, "capa": "ARQ-VENTANAS"},
        {"tipo": "circulo", "x": 8, "y": 8, "radio": 2.5, "capa": "ARQ-COLUMNAS"},
        {"tipo": "arco", "x": 10, "y": 10, "radio": 3.0, "angulo_inicio": 0, "angulo_fin": 90, "capa": "ARQ-PUERTAS"},
        {"tipo": "polilinea", "puntos": "0,0;5,0;5,5;0,5", "cerrada": True, "capa": "ARQ-MUROS"},
        {"tipo": "texto", "texto": "SALA 20m²", "x": 5, "y": 5, "altura": 0.35, "capa": "ARQ-TEXTOS"},
    ]

    lisp_code, summary = build_batch_geometry_lisp(elements, default_layer="0")

    assert lisp_code.startswith("(progn ")
    assert lisp_code.endswith("(princ))\n")
    assert "LINE" in lisp_code
    assert "LWPOLYLINE" in lisp_code
    assert "CIRCLE" in lisp_code
    assert "ARC" in lisp_code
    assert "TEXT" in lisp_code
    assert "SALA 20m²" in lisp_code
    assert summary["total"] == 6
    assert summary["desglose"]["linea"] == 1
    assert summary["desglose"]["rectangulo"] == 1
    assert summary["desglose"]["circulo"] == 1
    assert summary["desglose"]["arco"] == 1
    assert summary["desglose"]["polilinea"] == 1
    assert summary["desglose"]["texto"] == 1


def test_build_batch_geometry_from_json_string():
    elements = [
        {"tipo": "linea", "x1": 0, "y1": 0, "x2": 5, "y2": 5},
        {"tipo": "circulo", "centro_x": 2, "centro_y": 2, "radio": 1.0},
    ]
    json_str = json.dumps(elements)

    lisp_code, summary = build_batch_geometry_lisp(json_str, default_layer="0")

    assert summary["total"] == 2
    assert "LINE" in lisp_code
    assert "CIRCLE" in lisp_code


def test_build_batch_geometry_invalid_entity_type():
    elements = [
        {"tipo": "extrusion_solida", "x": 1, "y": 2}
    ]
    with pytest.raises(ValueError, match="Tipo de entidad no soportado"):
        build_batch_geometry_lisp(elements)


def test_build_batch_geometry_invalid_coords():
    elements = [
        {"tipo": "linea", "x1": 0, "y1": 0, "x2": 0, "y2": 0}
    ]
    with pytest.raises(ValueError, match="longitud cero"):
        build_batch_geometry_lisp(elements)
