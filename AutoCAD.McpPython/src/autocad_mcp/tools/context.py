"""Context inspection and vision MCP tools for AutoCAD."""

from __future__ import annotations

from autocad_mcp import runtime
from autocad_mcp.policy import GUIDE_ROUTE, TOOLS_ROUTE
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("context")


@domain.tool(name="get_drawing_info", core=True)
async def get_drawing_info() -> str:
    """Retrieve metadata of the active AutoCAD drawing: DWG filename, read-only state, and configured units."""
    return await runtime.gateway.get_context("context/info")


@domain.tool(name="get_entities")
async def get_entities() -> str:
    """Read and return a summary (counts by entity type) of all entities existing in AutoCAD ModelSpace."""
    return await runtime.gateway.get_context("context/entities")


@domain.tool(name="get_layers", core=True)
async def get_layers() -> str:
    """Retrieve the full list of AutoCAD drawing layers with their states (locked, frozen, off) and ACI color index."""
    return await runtime.gateway.get_context("context/layers")


@domain.tool(name="get_texts")
async def get_texts() -> str:
    """List all text entities (DBText and MText) in the drawing with text contents, exact (X, Y) coordinates, and layers."""
    return await runtime.gateway.get_context("context/texts")


@domain.tool(name="get_drawing_extents", core=True)
async def get_drawing_extents() -> str:
    """Retrieve minimum (X, Y) and maximum (X, Y) bounding coordinates of the active drawing (Extmin and Extmax)."""
    return await runtime.gateway.get_context("context/extents")


@domain.tool(name="get_selection_count")
async def get_selection_count() -> str:
    """Get the number of entities currently selected by the user in AutoCAD."""
    return await runtime.gateway.get_context("context/selection")


@domain.tool(name="list_blocks")
async def list_blocks() -> str:
    """Retrieve names of all block definitions loaded into the current drawing."""
    return await runtime.gateway.get_context("context/blocks")


@domain.tool(name="capture_screenshot", core=True)
async def capture_screenshot(inline: bool = False) -> str:
    """Capture a PNG screenshot of the active AutoCAD window and save it to disk. Set inline=True for base64 PNG."""
    endpoint = "context/screenshot?inline=true" if inline else "context/screenshot"
    return await runtime.gateway.get_context(endpoint)


@domain.alias(name="read_layers", alias_of="get_layers")
async def read_layers() -> str:
    """Alias for get_layers: Retrieve the full list of AutoCAD drawing layers with their states."""
    return await get_layers()


@domain.alias(name="read_objects", alias_of="get_entities")
async def read_objects() -> str:
    """Alias for get_entities: Read a summary of entities in ModelSpace."""
    return await get_entities()


@domain.alias(name="read_texts", alias_of="get_texts")
async def read_texts() -> str:
    """Alias for get_texts: List all text entities with contents and coordinates."""
    return await get_texts()


@domain.alias(name="read_selection", alias_of="get_selection_count")
async def read_selection() -> str:
    """Alias for get_selection_count: Get the number of currently selected entities."""
    return await get_selection_count()


@domain.alias(name="list_available_blocks", alias_of="list_blocks")
async def list_available_blocks() -> str:
    """Alias for list_blocks: Retrieve block definitions available in the drawing."""
    return await list_blocks()


@domain.alias(name="capture_screen", alias_of="capture_screenshot")
async def capture_screen(inline: bool = False) -> str:
    """Alias for capture_screenshot: Capture a PNG image of the active AutoCAD window."""
    return await capture_screenshot(inline=inline)


@domain.tool(name="get_gateway_tools")
async def get_gateway_tools() -> str:
    """Query the live AutoCAD Gateway dynamic endpoint catalog (GET /_tools) with undo semantics and Edit Guard flags."""
    return await runtime.gateway.get_context(TOOLS_ROUTE)


@domain.tool(name="get_gateway_guide")
async def get_gateway_guide() -> str:
    """Retrieve operational working rules and undo/Edit Guard conventions from the Gateway (GET /_guide)."""
    return await runtime.gateway.get_context(GUIDE_ROUTE)
