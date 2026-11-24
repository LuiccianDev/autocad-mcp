"""Parametric 2D/3D drawing MCP tools routed through native ObjectARX Gateway transactions."""

from __future__ import annotations

from typing import Any

from autocad_mcp import runtime
from autocad_mcp.cad_math import all_finite, is_valid_symbol_name, parse_points
from autocad_mcp.policy import (
    MAX_BATCH_OPERATIONS,
    error_json,
    normalize_batch_element,
    parse_json_value,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("drawing")


@domain.tool(name="draw_batch_geometry", core=True)
async def draw_batch_geometry(elements: list[dict[str, Any]] | str, default_layer: str = "0") -> str:
    """PREFERRED TOOL FOR DRAWING MULTIPLE ENTITIES IN A SINGLE CALL inside one atomic ObjectARX transaction."""
    if not is_valid_symbol_name(default_layer):
        return error_json(f"Invalid default layer name: '{default_layer}'.")

    parsed_elements, elements_err = parse_json_value(elements, list, "elements")
    if elements_err or not parsed_elements:
        return error_json(elements_err or "'elements' must be a non-empty list of entity definitions.")

    if len(parsed_elements) > MAX_BATCH_OPERATIONS:
        return error_json(
            f"Batch size {len(parsed_elements)} exceeds maximum {MAX_BATCH_OPERATIONS} operations per atomic transaction."
        )

    operations: list[dict[str, Any]] = []
    for idx, item in enumerate(parsed_elements):
        if not isinstance(item, dict):
            return error_json(f"Element #{idx} must be a JSON object.")
        op, err = normalize_batch_element(item, default_layer.strip(), idx)
        if err or op is None:
            return error_json(err or f"Invalid element at index {idx}.")
        operations.append(op)

    payload = {
        "operations": operations,
        "default_layer": default_layer.strip(),
        "rollback_on_error": True,
    }
    return await runtime.gateway.post_json("batch", payload, timeout=60.0)


@domain.tool(name="draw_line")
async def draw_line(
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    z1: float = 0.0,
    z2: float = 0.0,
    layer: str = "0",
) -> str:
    """Draw a 2D/3D line segment between (x1, y1, z1) and (x2, y2, z2) in meters."""
    if not all_finite(x1, y1, z1, x2, y2, z2):
        return error_json("Line coordinates must be finite numbers.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {"type": "line", "start": [x1, y1, z1], "end": [x2, y2, z2], "layer": layer.strip()},
    )


@domain.tool(name="draw_polyline")
async def draw_polyline(points: str, closed: bool = False, layer: str = "0") -> str:
    """Draw a 2D polyline from 'x1,y1;x2,y2;...' coordinates in meters."""
    pts = parse_points(points, 2)
    if not pts or len(pts) < 2:
        return error_json("At least 2 vertices in 'x1,y1;x2,y2;...' format are required.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {
            "type": "polyline",
            "points": [[p[0], p[1]] for p in pts],
            "closed": bool(closed),
            "layer": layer.strip(),
        },
    )


@domain.tool(name="draw_rectangle")
async def draw_rectangle(x1: float, y1: float, x2: float, y2: float, layer: str = "0") -> str:
    """Draw an axis-aligned rectangle as a closed polyline in meters."""
    if not all_finite(x1, y1, x2, y2):
        return error_json("Rectangle coordinates must be finite numbers.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {"type": "rectangle", "min": [x1, y1], "max": [x2, y2], "layer": layer.strip()},
    )


@domain.tool(name="draw_circle")
async def draw_circle(center_x: float, center_y: float, radius: float, layer: str = "0") -> str:
    """Draw a circle centered at (center_x, center_y) with radius in meters."""
    if not all_finite(center_x, center_y, radius) or radius <= 0:
        return error_json("Center must be finite and radius must be > 0.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {"type": "circle", "center": [center_x, center_y, 0.0], "radius": radius, "layer": layer.strip()},
    )


@domain.tool(name="draw_arc")
async def draw_arc(
    center_x: float,
    center_y: float,
    radius: float,
    start_angle: float,
    end_angle: float,
    layer: str = "0",
) -> str:
    """Draw a circular arc in meters using start and end angles in degrees."""
    if not all_finite(center_x, center_y, radius, start_angle, end_angle) or radius <= 0:
        return error_json("Parameters must be finite and radius must be > 0.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {
            "type": "arc",
            "center": [center_x, center_y, 0.0],
            "radius": radius,
            "start_angle": start_angle,
            "end_angle": end_angle,
            "layer": layer.strip(),
        },
    )


@domain.tool(name="insert_text")
async def insert_text(
    text: str,
    x: float,
    y: float,
    height: float = 0.25,
    rotation: float = 0.0,
    layer: str = "0",
) -> str:
    """Insert a single-line text entity at (x, y) in meters."""
    if not text or not text.strip():
        return error_json("Text content cannot be empty.")
    if not all_finite(x, y, height, rotation) or height <= 0:
        return error_json("Coordinates/rotation must be finite and height > 0.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {
            "type": "text",
            "text": text,
            "position": [x, y, 0.0],
            "height": height,
            "rotation": rotation,
            "layer": layer.strip(),
        },
    )


@domain.tool(name="insert_block")
async def insert_block(
    block_name: str,
    x: float,
    y: float,
    scale: float = 1.0,
    rotation: float = 0.0,
    layer: str = "0",
) -> str:
    """Insert a reference of an existing block definition at (x, y) in meters."""
    if not is_valid_symbol_name(block_name):
        return error_json(f"Invalid block name: '{block_name}'.")
    if not all_finite(x, y, scale, rotation) or scale <= 0:
        return error_json("Coordinates/rotation must be finite and scale > 0.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    return await runtime.gateway.post_json(
        "entities/create",
        {
            "type": "block_ref",
            "block_name": block_name.strip(),
            "position": [x, y, 0.0],
            "scale": scale,
            "rotation": rotation,
            "layer": layer.strip(),
        },
    )
