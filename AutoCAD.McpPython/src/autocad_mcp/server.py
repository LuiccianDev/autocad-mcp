"""FastMCP composition root for Autodesk AutoCAD 2027 and Civil 3D."""

from __future__ import annotations

import sys
from typing import Any

from mcp.server.fastmcp import FastMCP

from autocad_mcp import tools as _tools
from autocad_mcp.compound import make_compound_tool
from autocad_mcp.runtime import gateway
from autocad_mcp.tool_mode import ToolMode, catalog, resolve_tool_mode


def configure_mcp_server(target_mcp: FastMCP, mode: ToolMode | None = None) -> ToolMode:
    """Idempotently replace registered MCP tools on target_mcp according to ToolMode."""
    resolved_mode = resolve_tool_mode() if mode is None else mode

    # FastMCP.list_tools() is async; read registered tools synchronously when reconfiguring.
    for existing in list(target_mcp._tool_manager.list_tools()):
        target_mcp.remove_tool(existing.name)

    if resolved_mode is ToolMode.GRANULAR:
        for entry in catalog.entries(include_aliases=True):
            target_mcp.add_tool(entry.fn, name=entry.name)
    elif resolved_mode is ToolMode.CORE:
        for entry in catalog.core_entries():
            target_mcp.add_tool(entry.fn, name=entry.name)
    elif resolved_mode is ToolMode.COMPOUND:
        for domain_spec in catalog.domains():
            target_mcp.add_tool(make_compound_tool(catalog, domain_spec), name=domain_spec.compound_name)

    return resolved_mode


def create_mcp_server(mode: ToolMode | None = None) -> FastMCP:
    """Build an isolated FastMCP instance configured for the specified or environment ToolMode."""
    server = FastMCP("AutoCAD_AI_Context_Server")
    configure_mcp_server(server, mode=mode)
    return server


def _resolve_mode_or_exit() -> ToolMode:
    try:
        return resolve_tool_mode()
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1) from exc


_startup_mode = _resolve_mode_or_exit()
mcp = create_mcp_server(_startup_mode)


def __getattr__(name: str) -> Any:
    entry = catalog.get_entry(name)
    if entry is not None:
        return entry.fn
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")


def main() -> None:
    """Run MCP server over standard input/output (STDIO) transport."""
    current_mode = _resolve_mode_or_exit()
    if current_mode != _startup_mode:
        configure_mcp_server(mcp, current_mode)
    mcp.run(transport="stdio")


__all__ = [
    "configure_mcp_server",
    "create_mcp_server",
    "gateway",
    "main",
    "mcp",
    *_tools.__all__,
]


if __name__ == "__main__":
    main()
