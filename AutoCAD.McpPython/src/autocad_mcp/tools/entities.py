"""Native ObjectARX entity CRUD, filtered/spatial query, and atomic batch MCP tools."""

from __future__ import annotations

from typing import Any

from autocad_mcp import runtime
from autocad_mcp.cad_math import is_valid_symbol_name
from autocad_mcp.policy import (
    MAX_BATCH_OPERATIONS,
    MAX_QUERY_LIMIT,
    all_optional_finite,
    error_json,
    is_valid_entity_handle,
    parse_json_value,
    parse_string_list_strict,
    parse_valid_handles,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("entities")


@domain.tool(name="query_entities", core=True)
async def query_entities(
    types: list[str] | str | None = None,
    layers: list[str] | str | None = None,
    space: str = "model",
    min_x: float | None = None,
    min_y: float | None = None,
    max_x: float | None = None,
    max_y: float | None = None,
    text_contains: str | None = None,
    limit: int = 100,
    cursor: str | None = None,
    document: str | None = None,
) -> str:
    """Query drawing entities using optional filters (types, layers, space, 2D bbox, text) with revision-bound cursors."""
    if limit < 1 or limit > MAX_QUERY_LIMIT:
        return error_json(f"'limit' must be between 1 and {MAX_QUERY_LIMIT}.")

    bbox: list[float] | None = None
    if min_x is not None or min_y is not None or max_x is not None or max_y is not None:
        if min_x is None or min_y is None or max_x is None or max_y is None or not all_optional_finite(
            min_x, min_y, max_x, max_y
        ):
            return error_json("All four finite bounds (min_x, min_y, max_x, max_y) are required for bbox.")
        bbox = [float(min_x), float(min_y), float(max_x), float(max_y)]

    types_list, types_err = parse_string_list_strict(types)
    if types_err:
        return error_json(f"Invalid 'types': {types_err}")
    layers_list, layers_err = parse_string_list_strict(layers)
    if layers_err:
        return error_json(f"Invalid 'layers': {layers_err}")

    payload: dict[str, Any] = {
        "types": types_list,
        "layers": layers_list,
        "space": space,
        "bbox": bbox,
        "text_contains": text_contains,
        "limit": int(limit),
        "cursor": cursor,
        "document": document,
    }
    return await runtime.gateway.post_json("entities/query", payload)


@domain.tool(name="get_entity", core=True)
async def get_entity(handle: str, document: str | None = None) -> str:
    """Inspect detailed geometry, layer, attributes, and revision metadata of a single entity by hexadecimal handle."""
    if not is_valid_entity_handle(handle):
        return error_json(f"Invalid hexadecimal handle: '{handle}'.")
    return await runtime.gateway.post_json("entities/get", {"handle": handle.strip(), "document": document})


@domain.tool(name="modify_entity", core=True)
async def modify_entity(
    handle: str,
    layer: str | None = None,
    color_index: int | None = None,
    linetype: str | None = None,
    text: str | None = None,
    radius: float | None = None,
    height: float | None = None,
    width: float | None = None,
    elevation: float | None = None,
    rotation: float | None = None,
    scale: float | None = None,
    closed: bool | None = None,
    x: float | None = None,
    y: float | None = None,
    z: float | None = None,
    start_x: float | None = None,
    start_y: float | None = None,
    start_z: float | None = None,
    end_x: float | None = None,
    end_y: float | None = None,
    end_z: float | None = None,
    start_angle: float | None = None,
    end_angle: float | None = None,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Modify properties or individual coordinates of an entity by handle without overwriting omitted axes."""
    if not is_valid_entity_handle(handle):
        return error_json(f"Invalid hexadecimal handle: '{handle}'.")
    if layer is not None and not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name '{layer}'.")
    if linetype is not None and not is_valid_symbol_name(linetype):
        return error_json(f"Invalid linetype name '{linetype}'.")
    if color_index is not None and not (0 <= color_index <= 256):
        return error_json("'color_index' must be between 0 and 256.")
    if not all_optional_finite(
        radius, height, width, elevation, rotation, scale, x, y, z, start_x, start_y, start_z, end_x, end_y, end_z, start_angle, end_angle
    ):
        return error_json("Numeric geometry parameters must be finite.")

    raw_payload: dict[str, Any] = {
        "handle": handle.strip(),
        "layer": layer.strip() if layer is not None else None,
        "color_index": color_index,
        "linetype": linetype.strip() if linetype is not None else None,
        "text": text,
        "radius": radius,
        "height": height,
        "width": width,
        "elevation": elevation,
        "rotation": rotation,
        "scale": scale,
        "closed": closed,
        "x": x,
        "y": y,
        "z": z,
        "start_x": start_x,
        "start_y": start_y,
        "start_z": start_z,
        "end_x": end_x,
        "end_y": end_y,
        "end_z": end_z,
        "start_angle": start_angle,
        "end_angle": end_angle,
        "expected_revision": expected_revision,
        "document": document,
    }
    payload = {k: v for k, v in raw_payload.items() if v is not None}
    return await runtime.gateway.post_json("entities/modify", payload)


@domain.tool(name="transform_entities")
async def transform_entities(
    handles: list[str] | str,
    operation: str,
    dx: float = 0.0,
    dy: float = 0.0,
    dz: float = 0.0,
    base_x: float | None = None,
    base_y: float | None = None,
    base_z: float | None = None,
    angle: float | None = None,
    factor: float | None = None,
    p1_x: float | None = None,
    p1_y: float | None = None,
    p2_x: float | None = None,
    p2_y: float | None = None,
    erase_source: bool = False,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """
    Apply move, rotate, scale, copy, or mirror transforms to one or more handles atomically.
    For mirror, provide explicit axis endpoints (p1_x, p1_y) and (p2_x, p2_y).
    """
    handle_list, handle_err = parse_valid_handles(handles, required=True)
    if handle_err or not handle_list:
        return error_json(handle_err or "'handles' must contain valid non-empty hexadecimal entity handles.")
    if not operation or not operation.strip():
        return error_json("'operation' is required (move, rotate, scale, copy, mirror).")
    if not all_optional_finite(dx, dy, dz, base_x, base_y, base_z, angle, factor, p1_x, p1_y, p2_x, p2_y):
        return error_json("Transform parameters must be finite numbers.")

    norm_op = operation.strip().lower()
    if norm_op == "mirror" and (p1_x is None or p1_y is None or p2_x is None or p2_y is None):
        return error_json("Mirror operation requires explicit axis points (p1_x, p1_y, p2_x, p2_y).")

    base_pt = [base_x, base_y, base_z or 0.0] if (base_x is not None and base_y is not None) else None
    p1 = [p1_x, p1_y, 0.0] if (p1_x is not None and p1_y is not None) else None
    p2 = [p2_x, p2_y, 0.0] if (p2_x is not None and p2_y is not None) else None

    payload = {
        "handles": handle_list,
        "operation": norm_op,
        "dx": dx,
        "dy": dy,
        "dz": dz,
        "base_point": base_pt,
        "angle": angle,
        "factor": factor,
        "p1": p1,
        "p2": p2,
        "erase_source": erase_source,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("entities/transform", payload)


@domain.tool(name="delete_entities", core=True)
async def delete_entities(
    handles: list[str] | str,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Delete one or more entities identified by hexadecimal handle in one atomic transaction."""
    handle_list, handle_err = parse_valid_handles(handles, required=True)
    if handle_err or not handle_list:
        return error_json(handle_err or "'handles' must contain valid non-empty hexadecimal entity handles.")
    return await runtime.gateway.post_json(
        "entities/delete",
        {"handles": handle_list, "expected_revision": expected_revision, "document": document},
    )


@domain.tool(name="execute_native_batch", core=True)
async def execute_native_batch(
    operations: list[dict[str, Any]] | str,
    default_layer: str = "0",
    rollback_on_error: bool = True,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Execute up to 500 native ObjectARX operations inside a single atomic transaction (one Ctrl+Z undo step)."""
    if not is_valid_symbol_name(default_layer):
        return error_json(f"Invalid default layer name: '{default_layer}'.")

    parsed_ops, ops_err = parse_json_value(operations, list, "operations")
    if ops_err or parsed_ops is None or not (1 <= len(parsed_ops) <= MAX_BATCH_OPERATIONS):
        return error_json(ops_err or f"'operations' must contain between 1 and {MAX_BATCH_OPERATIONS} items.")

    payload = {
        "operations": parsed_ops,
        "default_layer": default_layer.strip(),
        "rollback_on_error": rollback_on_error,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("batch", payload, timeout=60.0)
