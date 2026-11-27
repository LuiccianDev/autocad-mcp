"""Native ObjectARX layer management MCP tools for AutoCAD."""

from __future__ import annotations

from typing import Any

from autocad_mcp import runtime
from autocad_mcp.cad_math import is_valid_symbol_name
from autocad_mcp.policy import (
    error_json,
    has_control_chars,
    is_valid_layer_linetype_name,
    parse_lineweight_arg,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("layers")


def _populate_layer_properties(
    payload: dict[str, Any],
    *,
    color_aci: int | None,
    linetype: str | None,
    lineweight: str | float | None,
    is_plottable: bool | None,
    description: str | None,
    set_current: bool | None,
) -> tuple[bool, str | None]:
    has_prop = False

    if color_aci is not None:
        if not (1 <= color_aci <= 255):
            return False, "ACI color must be between 1 and 255."
        payload["color_index"] = int(color_aci)
        has_prop = True

    if linetype is not None and linetype.strip():
        lt_ok, lt_err = is_valid_layer_linetype_name(linetype)
        if not lt_ok:
            return False, lt_err
        payload["linetype"] = linetype.strip()
        has_prop = True

    if lineweight is not None:
        norm_lw, lw_err = parse_lineweight_arg(lineweight)
        if lw_err:
            return False, lw_err
        if norm_lw is not None:
            payload["lineweight"] = norm_lw
            has_prop = True

    if is_plottable is not None:
        payload["is_plottable"] = bool(is_plottable)
        has_prop = True

    if description is not None:
        if has_control_chars(description):
            return False, "Layer description cannot contain control characters."
        payload["description"] = description
        has_prop = True

    if set_current is not None:
        payload["set_current"] = bool(set_current)
        has_prop = True

    return has_prop, None


@domain.tool(name="create_layer", core=True)
async def create_layer(
    name: str,
    color_aci: int = 7,
    linetype: str = "Continuous",
    lineweight: str | float | None = None,
    is_plottable: bool | None = None,
    description: str = "",
    set_current: bool = True,
    document: str | None = None,
) -> str:
    """Create or update a layer with ACI color (1-255), linetype, lineweight, and plottability."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")

    payload: dict[str, Any] = {"name": name.strip(), "document": document}
    _, err = _populate_layer_properties(
        payload,
        color_aci=color_aci,
        linetype=linetype,
        lineweight=lineweight,
        is_plottable=is_plottable,
        description=description.strip() if description.strip() else None,
        set_current=set_current,
    )
    if err:
        return error_json(err)
    return await runtime.gateway.post_json("layers/create", payload)


@domain.tool(name="set_current_layer")
async def set_current_layer(name: str) -> str:
    """Set an existing layer as the current active drawing layer (CLAYER)."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")
    return await runtime.gateway.post_json("layers/state", {"name": name.strip(), "set_current": True})


@domain.tool(name="set_layer_state", core=True)
async def set_layer_state(
    name: str,
    action: str | None = None,
    color_aci: int | None = None,
    linetype: str | None = None,
    lineweight: str | float | None = None,
    is_plottable: bool | None = None,
    description: str | None = None,
    set_current: bool | None = None,
    document: str | None = None,
) -> str:
    """Modify layer state ('on', 'off', 'freeze', 'thaw', 'lock', 'unlock') and/or color, linetype, lineweight."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")

    payload: dict[str, Any] = {"name": name.strip(), "document": document}
    has_mutation = False

    if action is not None and action.strip():
        act = action.strip().lower()
        if act in ("on", "activar"):
            payload["on"] = True
        elif act in ("off", "desactivar"):
            payload["on"] = False
        elif act in ("freeze", "congelar"):
            payload["frozen"] = True
        elif act in ("thaw", "descongelar"):
            payload["frozen"] = False
        elif act in ("lock", "bloquear"):
            payload["locked"] = True
        elif act in ("unlock", "desbloquear"):
            payload["locked"] = False
        else:
            return error_json(f"Invalid action '{action}'. Use: on, off, freeze, thaw, lock, unlock.")
        has_mutation = True

    has_prop, err = _populate_layer_properties(
        payload,
        color_aci=color_aci,
        linetype=linetype,
        lineweight=lineweight,
        is_plottable=is_plottable,
        description=description,
        set_current=set_current,
    )
    if err:
        return error_json(err)

    if not has_mutation and not has_prop:
        return error_json("Specify 'action' or at least one layer property (color_aci, linetype, lineweight, is_plottable, description, set_current).")

    return await runtime.gateway.post_json("layers/state", payload)


@domain.tool(name="delete_layer")
async def delete_layer(name: str, document: str | None = None) -> str:
    """Delete an unused AutoCAD layer (refuses protected layers '0'/'Defpoints', active layer, or referenced layers)."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")
    return await runtime.gateway.post_json("layers/delete", {"name": name.strip(), "document": document})
