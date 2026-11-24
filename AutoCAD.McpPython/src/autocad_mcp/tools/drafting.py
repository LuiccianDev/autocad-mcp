"""Native ObjectARX drafting, dimensioning, hatching, tables, blocks, and Xref MCP tools."""

from __future__ import annotations

from typing import Any
from urllib.parse import quote

from autocad_mcp import runtime
from autocad_mcp.cad_math import all_finite, is_valid_symbol_name, parse_points
from autocad_mcp.policy import (
    MAX_TABLE_COLS,
    MAX_TABLE_ROWS,
    all_optional_finite,
    error_json,
    is_valid_entity_handle,
    parse_json_value,
    parse_valid_handles,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("drafting")

_VALID_DIM_TYPES = frozenset({"aligned", "rotated", "radial", "diametric", "angular"})


@domain.tool(name="create_dimension")
async def create_dimension(
    type: str,
    x1: float = 0.0,
    y1: float = 0.0,
    x2: float = 0.0,
    y2: float = 0.0,
    dim_line_x: float | None = None,
    dim_line_y: float | None = None,
    center_x: float | None = None,
    center_y: float | None = None,
    angle: float = 0.0,
    leader_length: float = 0.5,
    text_override: str | None = None,
    dim_style: str | None = None,
    text_height: float | None = None,
    arrow_size: float | None = None,
    dim_scale: float | None = None,
    layer: str = "0",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a native dimension (aligned, rotated, radial, diametric, angular) inside one atomic transaction."""
    if not type or type.strip().lower() not in _VALID_DIM_TYPES:
        return error_json("'type' must be one of: aligned, rotated, radial, diametric, angular.")
    norm_type = type.strip().lower()
    if dim_style is not None and dim_style.strip() and not is_valid_symbol_name(dim_style):
        return error_json(f"Invalid dimension style name '{dim_style}'.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name '{layer}'.")
    if (dim_line_x is None) != (dim_line_y is None):
        return error_json("Provide both 'dim_line_x' and 'dim_line_y', or neither.")
    if (center_x is None) != (center_y is None):
        return error_json("Provide both 'center_x' and 'center_y', or neither.")
    if norm_type == "angular" and center_x is None:
        return error_json("Angular dimensions require explicit vertex coordinates ('center_x' and 'center_y').")
    if (
        not all_finite(x1, y1, x2, y2, angle, leader_length)
        or leader_length < 0
        or not all_optional_finite(dim_line_x, dim_line_y, center_x, center_y, text_height, arrow_size, dim_scale)
    ):
        return error_json("Dimension coordinates, angle, and non-negative leader_length must be finite numbers.")
    if any(v is not None and v <= 0 for v in (text_height, arrow_size, dim_scale)):
        return error_json("'text_height', 'arrow_size', and 'dim_scale' must be finite numbers greater than zero.")

    dim_line_pt = [dim_line_x, dim_line_y, 0.0] if dim_line_x is not None and dim_line_y is not None else None
    center_pt = [center_x, center_y, 0.0] if center_x is not None and center_y is not None else None

    payload = {
        "type": norm_type,
        "p1": [x1, y1, 0.0],
        "p2": [x2, y2, 0.0],
        "dim_line_point": dim_line_pt,
        "center": center_pt if center_pt is not None else [x1, y1, 0.0],
        "chord": [x2, y2, 0.0],
        "vertex": center_pt if center_pt is not None else [x1, y1, 0.0],
        "angle": angle,
        "leader_length": leader_length,
        "text_override": text_override,
        "dim_style": dim_style.strip() if dim_style and dim_style.strip() else None,
        "text_height": text_height,
        "arrow_size": arrow_size,
        "dim_scale": dim_scale,
        "layer": layer.strip(),
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("drafting/dimension", payload)


@domain.tool(name="create_mleader")
async def create_mleader(
    arrow_x: float,
    arrow_y: float,
    landing_x: float,
    landing_y: float,
    text: str,
    text_height: float = 0.25,
    arrow_size: float | None = None,
    layer: str = "0",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a native MLeader callout annotation from arrow point to landing text point."""
    if not text or not text.strip():
        return error_json("'text' cannot be empty.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name '{layer}'.")
    if (
        not all_finite(arrow_x, arrow_y, landing_x, landing_y, text_height)
        or text_height <= 0
        or not all_optional_finite(arrow_size)
        or (arrow_size is not None and arrow_size <= 0)
    ):
        return error_json("Coordinates and positive text_height/arrow_size must be finite numbers.")

    payload = {
        "arrow_point": [arrow_x, arrow_y, 0.0],
        "landing_point": [landing_x, landing_y, 0.0],
        "text": text,
        "text_height": text_height,
        "arrow_size": arrow_size,
        "layer": layer.strip(),
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("drafting/mleader", payload)


@domain.tool(name="create_hatch")
async def create_hatch(
    handles: list[str] | str | None = None,
    points: str | None = None,
    pattern: str = "ANSI31",
    scale: float = 1.0,
    angle: float = 0.0,
    associative: bool | None = None,
    layer: str = "0",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a native solid or patterned Hatch inside closed boundary handles or 'x1,y1;x2,y2;...' vertices."""
    handle_list, handle_err = parse_valid_handles(handles)
    if handle_err:
        return error_json(handle_err)

    has_handles = bool(handle_list)
    has_points = bool(points and points.strip())
    if has_handles and has_points:
        return error_json("Provide either boundary 'handles' or polygon 'points', not both.")
    if not has_handles and not has_points:
        return error_json("Provide either boundary 'handles' or closed 'points' vertices.")
    if not is_valid_symbol_name(pattern) or not is_valid_symbol_name(layer):
        return error_json("Invalid hatch pattern or layer name.")
    if not all_finite(scale, angle) or scale <= 0:
        return error_json("Hatch scale must be > 0 and angle must be finite.")

    poly_pts = None
    if has_points:
        parsed_pts = parse_points(points, 2)
        if not parsed_pts or len(parsed_pts) < 3:
            return error_json("Hatch polygon boundary requires at least 3 'x,y' vertices.")
        poly_pts = [[p[0], p[1]] for p in parsed_pts]

    payload = {
        "handles": handle_list,
        "points": poly_pts,
        "pattern": pattern.strip(),
        "scale": scale,
        "angle": angle,
        "associative": associative,
        "layer": layer.strip(),
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("drafting/hatch", payload)


@domain.tool(name="create_table")
async def create_table(
    x: float,
    y: float,
    rows: int,
    cols: int,
    cells_json: list[list[Any]] | str,
    headers_json: list[Any] | str | None = None,
    title: str | None = None,
    row_height: float = 1.0,
    col_width: float = 5.0,
    text_height: float = 0.35,
    layer: str = "0",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a native AutoCAD Table entity at (x, y) populated with headers and a 2D JSON cells matrix."""
    if not all_finite(x, y, row_height, col_width, text_height) or row_height <= 0 or col_width <= 0 or text_height <= 0:
        return error_json("Position and positive cell dimensions must be finite numbers.")
    if rows < 1 or rows > MAX_TABLE_ROWS or cols < 1 or cols > MAX_TABLE_COLS:
        return error_json(f"'rows' (1..{MAX_TABLE_ROWS}) and 'cols' (1..{MAX_TABLE_COLS}) are required.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name '{layer}'.")

    cells, cells_err = parse_json_value(cells_json, list, "cells_json")
    if cells_err:
        return error_json(cells_err)
    if len(cells) > rows or any(not isinstance(row, list) or len(row) > cols for row in cells):
        return error_json(f"'cells_json' matrix cannot exceed rows={rows} by cols={cols}.")

    headers = None
    if headers_json is not None and (not isinstance(headers_json, str) or headers_json.strip()):
        headers, headers_err = parse_json_value(headers_json, list, "headers_json")
        if headers_err:
            return error_json(headers_err)
        if len(headers) > cols:
            return error_json(f"'headers_json' cannot exceed cols={cols}.")

    payload = {
        "position": [x, y, 0.0],
        "rows": int(rows),
        "cols": int(cols),
        "cells": cells,
        "headers": headers,
        "title": title,
        "row_height": row_height,
        "col_width": col_width,
        "text_height": text_height,
        "layer": layer.strip(),
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("drafting/table", payload)


@domain.tool(name="define_block")
async def define_block(
    name: str,
    handles: list[str] | str | None = None,
    operations_json: list[dict[str, Any]] | str | None = None,
    base_x: float = 0.0,
    base_y: float = 0.0,
    base_z: float = 0.0,
    overwrite: bool = False,
    attributes_json: list[dict[str, Any]] | str | None = None,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Define a reusable Block definition from existing handles or a JSON array of inline geometry operations."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid block name '{name}'.")
    if not all_finite(base_x, base_y, base_z):
        return error_json("Block base point coordinates must be finite numbers.")

    handle_list, handle_err = parse_valid_handles(handles)
    if handle_err:
        return error_json(handle_err)

    has_handles = bool(handle_list)
    has_ops = operations_json is not None and (not isinstance(operations_json, str) or bool(operations_json.strip()))
    if not has_handles and not has_ops:
        return error_json("Supply either non-empty source 'handles' or 'operations_json' to define a block.")

    ops = None
    if has_ops:
        ops, ops_err = parse_json_value(operations_json, list, "operations_json")
        if ops_err:
            return error_json(ops_err)

    attrs = None
    if attributes_json is not None and (not isinstance(attributes_json, str) or attributes_json.strip()):
        attrs, attrs_err = parse_json_value(attributes_json, list, "attributes_json")
        if attrs_err:
            return error_json(attrs_err)

    payload = {
        "name": name.strip(),
        "handles": handle_list,
        "operations": ops,
        "base_point": [base_x, base_y, base_z],
        "overwrite": overwrite,
        "attributes": attrs,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("blocks/define", payload)


@domain.tool(name="get_block_definition")
async def get_block_definition(name: str, document: str | None = None) -> str:
    """Inspect geometry summary, origin, Xref status, and attribute definitions of a named block."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid block name '{name}'.")
    q = f"blocks/definition?name={quote(name.strip(), safe='')}"
    if document and document.strip():
        q += f"&document={quote(document.strip(), safe='')}"
    return await runtime.gateway.get_context(q)


@domain.tool(name="get_block_attributes")
async def get_block_attributes(handle: str, document: str | None = None) -> str:
    """Read all AttributeReference tag values on a BlockReference identified by hexadecimal handle."""
    if not is_valid_entity_handle(handle):
        return error_json(f"Invalid hexadecimal handle: '{handle}'.")
    q = f"blocks/attributes?handle={quote(handle.strip(), safe='')}"
    if document and document.strip():
        q += f"&document={quote(document.strip(), safe='')}"
    return await runtime.gateway.get_context(q)


@domain.tool(name="set_block_attributes")
async def set_block_attributes(
    handle: str,
    attributes_json: dict[str, Any] | str,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Update AttributeReference tag values on a BlockReference handle from a JSON object of {tag: value}."""
    if not is_valid_entity_handle(handle):
        return error_json(f"Invalid hexadecimal handle: '{handle}'.")

    attrs, attrs_err = parse_json_value(attributes_json, dict, "attributes_json")
    if attrs_err or not attrs:
        return error_json(attrs_err or "'attributes_json' must be a non-empty JSON object.")

    payload = {
        "handle": handle.strip(),
        "attributes": attrs,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("blocks/attributes", payload)


@domain.tool(name="list_xrefs")
async def list_xrefs(document: str | None = None) -> str:
    """List external references (Xrefs) with name, file path, resolution status, and unloaded state."""
    q = "blocks/xrefs"
    if document and document.strip():
        q += f"?document={quote(document.strip(), safe='')}"
    return await runtime.gateway.get_context(q)


@domain.tool(name="explode_entities")
async def explode_entities(
    handles: list[str] | str,
    erase_source: bool = True,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Explode one or more compound entities by handle inside a single atomic transaction."""
    handle_list, handle_err = parse_valid_handles(handles, required=True)
    if handle_err or not handle_list:
        return error_json(handle_err or "'handles' must contain valid non-empty hexadecimal entity handles.")

    payload = {
        "handles": handle_list,
        "erase_source": erase_source,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("entities/explode", payload)
