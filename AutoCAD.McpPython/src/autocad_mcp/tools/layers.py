"""Native ObjectARX layer management MCP tools for AutoCAD."""

from __future__ import annotations

from typing import Any

from autocad_mcp import runtime
from autocad_mcp.cad_math import is_valid_symbol_name
from autocad_mcp.policy import error_json
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("layers")


@domain.tool(name="create_layer", core=True)
async def create_layer(
    name: str,
    color_aci: int = 7,
    linetype: str = "Continuous",
    description: str = "",
) -> str:
    """Create or update a layer with ACI color (1-255) and linetype, and set it active."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")
    if not (1 <= color_aci <= 255):
        return error_json("ACI color must be between 1 and 255.")
    if not is_valid_symbol_name(linetype):
        return error_json(f"Invalid linetype name: '{linetype}'.")

    payload: dict[str, Any] = {
        "name": name.strip(),
        "color_index": int(color_aci),
        "linetype": linetype.strip(),
        "set_current": True,
    }
    if description.strip():
        payload["description"] = description.strip()
    return await runtime.gateway.post_json("layers/create", payload)


@domain.tool(name="set_current_layer")
async def set_current_layer(name: str) -> str:
    """Set an existing layer as the current active drawing layer (CLAYER)."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")
    return await runtime.gateway.post_json("layers/state", {"name": name.strip(), "set_current": True})


@domain.tool(name="set_layer_state", core=True)
async def set_layer_state(name: str, action: str) -> str:
    """Modify layer state ('on', 'off', 'freeze', 'thaw', 'lock', 'unlock')."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")

    act = (action or "").strip().lower()
    payload: dict[str, Any] = {"name": name.strip()}
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

    return await runtime.gateway.post_json("layers/state", payload)


@domain.tool(name="delete_layer")
async def delete_layer(name: str, document: str | None = None) -> str:
    """Delete an unused AutoCAD layer (refuses protected layers '0'/'Defpoints', active layer, or referenced layers)."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid layer name: '{name}'.")
    return await runtime.gateway.post_json("layers/delete", {"name": name.strip(), "document": document})
