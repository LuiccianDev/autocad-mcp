"""Autodesk Civil 3D 2027 alignment, TIN surface, profile, and cross-section MCP tools."""

from __future__ import annotations

from urllib.parse import quote

from autocad_mcp import runtime
from autocad_mcp.cad_math import all_finite, is_valid_symbol_name, num, parse_points
from autocad_mcp.policy import error_json, has_control_chars
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("civil3d")


@domain.tool(name="check_civil3d_status", core=True)
async def check_civil3d_status() -> str:
    """Verify if Autodesk Civil 3D 2027 modules are loaded and report existing surfaces and alignments."""
    return await runtime.gateway.get_context("civil/status")


@domain.tool(name="list_civil3d_surfaces", core=True)
async def list_civil3d_surfaces() -> str:
    """List TIN surfaces in Civil 3D with name, minimum/maximum elevation, and triangle count."""
    return await runtime.gateway.get_context("civil/surfaces")


@domain.tool(name="get_surface_elevation", core=True)
async def get_surface_elevation(x: float, y: float, surface_name: str | None = None) -> str:
    """Query exact ground elevation (Z coordinate) at point (x, y) on a Civil 3D TIN surface."""
    if not all_finite(x, y):
        return error_json("Coordinates x and y must be finite numbers.")
    if has_control_chars(surface_name):
        return error_json("Surface name cannot contain control characters.")
    q = f"x={num(x)}&y={num(y)}"
    if surface_name and surface_name.strip():
        q += f"&surface={quote(surface_name.strip(), safe='')}"
    return await runtime.gateway.get_context(f"civil/surface/elevation?{q}")


@domain.tool(name="list_civil3d_alignments", core=True)
async def list_civil3d_alignments() -> str:
    """List horizontal alignments in Civil 3D with name, start station, end station, and length."""
    return await runtime.gateway.get_context("civil/alignments")


@domain.tool(name="get_alignment_station_offset")
async def get_alignment_station_offset(x: float, y: float, alignment_name: str | None = None) -> str:
    """Calculate station and lateral offset of point (x, y) relative to a Civil 3D horizontal alignment."""
    if not all_finite(x, y):
        return error_json("Coordinates x and y must be finite numbers.")
    if has_control_chars(alignment_name):
        return error_json("Alignment name cannot contain control characters.")
    q = f"x={num(x)}&y={num(y)}"
    if alignment_name and alignment_name.strip():
        q += f"&alignment={quote(alignment_name.strip(), safe='')}"
    return await runtime.gateway.get_context(f"civil/alignment/station-offset?{q}")


@domain.tool(name="get_point_at_station")
async def get_point_at_station(
    station: float,
    offset: float = 0.0,
    alignment_name: str | None = None,
) -> str:
    """Calculate world (X, Y) coordinate from a station and lateral offset (+ right, - left) on a Civil 3D alignment."""
    if not all_finite(station, offset):
        return error_json("Parameters station and offset must be finite numbers.")
    if has_control_chars(alignment_name):
        return error_json("Alignment name cannot contain control characters.")
    q = f"station={num(station)}&offset={num(offset)}"
    if alignment_name and alignment_name.strip():
        q += f"&alignment={quote(alignment_name.strip(), safe='')}"
    return await runtime.gateway.get_context(f"civil/alignment/point-at-station?{q}")


@domain.alias(name="get_station_coordinate", alias_of="get_point_at_station")
async def get_station_coordinate(
    station: float,
    offset: float = 0.0,
    alignment_name: str | None = None,
) -> str:
    """Alias for get_point_at_station: Calculate world (X, Y) coordinate given station and offset."""
    return await get_point_at_station(station=station, offset=offset, alignment_name=alignment_name)


@domain.tool(name="sample_longitudinal_profile", core=True)
async def sample_longitudinal_profile(
    interval: float = 20.0,
    alignment_name: str | None = None,
    surface_name: str | None = None,
) -> str:
    """Sample terrain longitudinal profile along a Civil 3D alignment every 'interval' meters."""
    if not all_finite(interval) or interval <= 0:
        return error_json("The interval must be a finite number greater than zero.")
    if has_control_chars(alignment_name) or has_control_chars(surface_name):
        return error_json("Alignment and surface names cannot contain control characters.")

    q = f"interval={num(interval)}"
    if alignment_name and alignment_name.strip():
        q += f"&alignment={quote(alignment_name.strip(), safe='')}"
    if surface_name and surface_name.strip():
        q += f"&surface={quote(surface_name.strip(), safe='')}"
    return await runtime.gateway.get_context(f"civil/profile?{q}")


@domain.alias(name="get_longitudinal_profile", alias_of="sample_longitudinal_profile")
async def get_longitudinal_profile(
    interval: float = 20.0,
    alignment_name: str | None = None,
    surface_name: str | None = None,
) -> str:
    """Alias for sample_longitudinal_profile: Sample terrain longitudinal profile along an alignment."""
    return await sample_longitudinal_profile(interval=interval, alignment_name=alignment_name, surface_name=surface_name)


@domain.tool(name="create_civil3d_alignment")
async def create_civil3d_alignment(
    name: str,
    points: str,
    radius: float = 0.0,
    layer: str | None = None,
) -> str:
    """Create a horizontal alignment in Civil 3D from PI vertices 'x1,y1;x2,y2;...' with fixed radius tangent circular curves."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid alignment name '{name}'.")
    if not all_finite(radius) or radius < 0:
        return error_json("'radius' must be a finite non-negative number.")
    if layer is not None and layer.strip() and not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name '{layer}'.")

    pts = parse_points(points, 2)
    if not pts or len(pts) < 2:
        return error_json("Invalid 'points' format. Use 'x1,y1;x2,y2;...' with at least 2 PIs.")

    payload = {
        "name": name.strip(),
        "points": [{"x": p[0], "y": p[1]} for p in pts],
        "radius": radius,
        "layer": layer.strip() if layer and layer.strip() else None,
    }
    return await runtime.gateway.post_json("civil/alignment/create", payload)


@domain.tool(name="create_civil3d_surface")
async def create_civil3d_surface(name: str, points: str) -> str:
    """Create a TIN surface in Civil 3D from survey points 'x1,y1,z1;x2,y2,z2;...' (minimum 3 points)."""
    if not is_valid_symbol_name(name):
        return error_json(f"Invalid surface name '{name}'.")

    pts = parse_points(points, 3)
    if not pts or len(pts) < 3:
        return error_json("Invalid 'points' format. Use 'x1,y1,z1;x2,y2,z2;...' with at least 3 points.")

    payload = {
        "name": name.strip(),
        "points": [{"x": p[0], "y": p[1], "z": p[2]} for p in pts],
    }
    return await runtime.gateway.post_json("civil/surface/create", payload)


@domain.tool(name="get_civil3d_surface_stats")
async def get_civil3d_surface_stats(surface_name: str | None = None) -> str:
    """Retrieve statistical properties of a Civil 3D surface (min/max/mean elevation, points, triangles, 2D/3D area)."""
    if has_control_chars(surface_name):
        return error_json("Surface name cannot contain control characters.")
    q = "civil/surface/stats"
    if surface_name and surface_name.strip():
        q += f"?surface={quote(surface_name.strip(), safe='')}"
    return await runtime.gateway.get_context(q)


@domain.tool(name="sample_civil3d_cross_section")
async def sample_civil3d_cross_section(
    station: float,
    left_width: float = 20.0,
    right_width: float = 20.0,
    step: float = 2.0,
    alignment_name: str | None = None,
    surface_name: str | None = None,
) -> str:
    """Sample terrain cross-section elevations perpendicular to a Civil 3D alignment at a station."""
    if not all_finite(station, left_width, right_width, step):
        return error_json("Parameters station, left_width, right_width, and step must be finite numbers.")
    if left_width < 0 or right_width < 0 or left_width + right_width <= 0 or step <= 0:
        return error_json("Require left_width >= 0, right_width >= 0, left_width + right_width > 0, and step > 0.")
    if has_control_chars(alignment_name) or has_control_chars(surface_name):
        return error_json("Alignment and surface names cannot contain control characters.")

    q = (
        f"civil/cross-section?station={num(station)}&left_width={num(left_width)}"
        f"&right_width={num(right_width)}&step={num(step)}"
    )
    if alignment_name and alignment_name.strip():
        q += f"&alignment={quote(alignment_name.strip(), safe='')}"
    if surface_name and surface_name.strip():
        q += f"&surface={quote(surface_name.strip(), safe='')}"
    return await runtime.gateway.get_context(q)
