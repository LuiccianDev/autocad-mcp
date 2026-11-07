"""Unit tests for the Batch Geometry engine and batch execution."""

import json
import pytest
from autocad_mcp.cad_math import (
    build_batch_geometry_lisp,
)


def test_build_batch_geometry_lisp_valid():
    elements = [
        {"type": "line", "x1": 0, "y1": 0, "x2": 10, "y2": 0, "layer": "ARCH-WALLS"},
        {"type": "rectangle", "x1": 2, "y1": 2, "x2": 5, "y2": 4, "layer": "ARCH-WINDOWS"},
        {"type": "circle", "center_x": 8, "center_y": 8, "radius": 2.5, "layer": "ARCH-COLUMNS"},
        {"type": "arc", "center_x": 10, "center_y": 10, "radius": 3.0, "start_angle": 0, "end_angle": 90, "layer": "ARCH-DOORS"},
        {"type": "polyline", "points": "0,0;5,0;5,5;0,5", "closed": True, "layer": "ARCH-WALLS"},
        {"type": "text", "text": "ROOM 20m2", "x": 5, "y": 5, "height": 0.35, "layer": "ARCH-TEXTS"},
    ]

    lisp_code, summary = build_batch_geometry_lisp(elements, default_layer="0")

    assert lisp_code.startswith("(progn ")
    assert lisp_code.endswith("(princ))\n")
    assert "LINE" in lisp_code
    assert "LWPOLYLINE" in lisp_code
    assert "CIRCLE" in lisp_code
    assert "ARC" in lisp_code
    assert "TEXT" in lisp_code
    assert "ROOM 20m2" in lisp_code
    assert summary["total"] == 6
    assert summary["breakdown"]["line"] == 1
    assert summary["breakdown"]["rectangle"] == 1
    assert summary["breakdown"]["circle"] == 1
    assert summary["breakdown"]["arc"] == 1
    assert summary["breakdown"]["polyline"] == 1
    assert summary["breakdown"]["text"] == 1
    # Check backward compatibility aliases
    assert summary["desglose"]["linea"] == 1


def test_build_batch_geometry_from_json_string():
    elements = [
        {"type": "line", "x1": 0, "y1": 0, "x2": 5, "y2": 5},
        {"type": "circle", "center_x": 2, "center_y": 2, "radius": 1.0},
    ]
    json_str = json.dumps(elements)

    lisp_code, summary = build_batch_geometry_lisp(json_str, default_layer="0")

    assert summary["total"] == 2
    assert "LINE" in lisp_code
    assert "CIRCLE" in lisp_code


def test_build_batch_geometry_invalid_entity_type():
    elements = [
        {"type": "solid_extrusion", "x": 1, "y": 2}
    ]
    with pytest.raises(ValueError, match="Unsupported entity type"):
        build_batch_geometry_lisp(elements)


def test_build_batch_geometry_invalid_coords():
    elements = [
        {"type": "line", "x1": 0, "y1": 0, "x2": 0, "y2": 0}
    ]
    with pytest.raises(ValueError, match="zero length"):
        build_batch_geometry_lisp(elements)
