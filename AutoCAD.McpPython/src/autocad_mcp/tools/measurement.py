"""Measurement, computational geometry, drawing purge/audit, and regen MCP tools."""

from __future__ import annotations

import json

from autocad_mcp import runtime
from autocad_mcp.cad_math import calculate_distance, calculate_polygon_area as compute_polygon_area
from autocad_mcp.policy import error_json
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("measurement")


@domain.tool(name="measure_distance", core=True)
def measure_distance(
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    z1: float = 0.0,
    z2: float = 0.0,
) -> str:
    """Calculate 2D horizontal distance, 3D distance, coordinate deltas, azimuth, and slope % between two points (without CAD)."""
    return json.dumps(calculate_distance(x1, y1, x2, y2, z1, z2))


@domain.tool(name="calculate_polygon_area")
def calculate_polygon_area(points: str) -> str:
    """Calculate area in m2, hectares, perimeter, centroid, and orientation of a closed polygon (minimum 3 vertices, without CAD)."""
    return json.dumps(compute_polygon_area(points))


@domain.tool(name="purge_and_audit_drawing", core=True)
async def purge_and_audit_drawing(target: str = "all", audit: bool = True) -> str:
    """Purge unused symbol table definitions safely and audit database errors."""
    tp = (target or "").strip().lower()
    if tp in ("all", "todo"):
        cmd = "(progn (vl-load-com) (vla-PurgeAll (vla-get-ActiveDocument (vlax-get-acad-object))) (princ))\n"
    else:
        codes = {
            "blocks": "_B",
            "layers": "_LA",
            "linetypes": "_LT",
            "text_styles": "_ST",
            "dim_styles": "_D",
            "bloques": "_B",
            "capas": "_LA",
            "tipos_linea": "_LT",
            "estilos_texto": "_ST",
            "estilos_cota": "_D",
        }
        if tp not in codes:
            return error_json(
                "Invalid target. Use: all, blocks, layers, linetypes, text_styles, or dim_styles."
            )
        cmd = f"_.-PURGE\n{codes[tp]}\n*\n_N\n"

    if audit:
        cmd += "_.AUDIT\n_Y\n"
    return await runtime.gateway.execute_command(cmd)


@domain.alias(name="clean_and_purge_drawing", alias_of="purge_and_audit_drawing")
async def clean_and_purge_drawing(target: str = "all", audit: bool = True) -> str:
    """Alias for purge_and_audit_drawing: Purge unused definitions safely and audit errors."""
    return await purge_and_audit_drawing(target=target, audit=audit)


@domain.tool(name="regen_all")
async def regen_all() -> str:
    """Regenerate all open viewports and recalculate drawing display curves and text metrics (_.REGENALL)."""
    return await runtime.gateway.execute_command("_.REGENALL\n")
