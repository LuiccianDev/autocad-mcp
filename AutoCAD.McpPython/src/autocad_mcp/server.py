"""FastMCP composition root for Autodesk AutoCAD 2027 and Civil 3D."""

from __future__ import annotations

import functools
import inspect
import json
import sys
from collections.abc import Callable
from typing import Any

from mcp.server.fastmcp import FastMCP
from mcp.types import CallToolResult

from autocad_mcp import tools as _tools
from autocad_mcp.compound import make_compound_tool
from autocad_mcp.mcp_result import adapt_tool_output
from autocad_mcp.runtime import gateway
from autocad_mcp.tool_mode import (
    OutputFormat,
    ToolMode,
    catalog,
    resolve_compound_annotations,
    resolve_output_format,
    resolve_tool_annotations,
    resolve_tool_mode,
)


def _wrap_tool_for_mcp(
    tool_name: str,
    fn: Callable[..., Any],
    output_format: OutputFormat,
) -> Callable[..., Any]:
    """Adapt a string-returning tool function to emit native MCP CallToolResult without altering inputSchema."""
    is_async = inspect.iscoroutinefunction(fn)

    @functools.wraps(fn)
    async def _wrapped(*args: Any, **kwargs: Any) -> CallToolResult:
        raw = await fn(*args, **kwargs) if is_async else fn(*args, **kwargs)
        if not isinstance(raw, str):
            raw = json.dumps(raw) if isinstance(raw, (dict, list)) else ("" if raw is None else str(raw))
        return adapt_tool_output(tool_name, raw, output_format)

    orig_sig = inspect.signature(fn)
    _wrapped.__signature__ = orig_sig.replace(return_annotation=CallToolResult)  # type: ignore[attr-defined]
    annotations = dict(getattr(fn, "__annotations__", {}))
    annotations["return"] = CallToolResult
    _wrapped.__annotations__ = annotations
    return _wrapped


def configure_mcp_server(
    target_mcp: FastMCP,
    mode: ToolMode | None = None,
    output_format: OutputFormat | None = None,
) -> ToolMode:
    """Idempotently replace registered MCP tools on target_mcp according to ToolMode and OutputFormat."""
    resolved_mode = resolve_tool_mode() if mode is None else mode
    resolved_format = resolve_output_format() if output_format is None else output_format

    # FastMCP.list_tools() is async; read registered tools synchronously when reconfiguring.
    for existing in list(target_mcp._tool_manager.list_tools()):
        target_mcp.remove_tool(existing.name)

    if resolved_mode in (ToolMode.GRANULAR, ToolMode.CORE):
        entries = (
            catalog.entries(include_aliases=True)
            if resolved_mode is ToolMode.GRANULAR
            else catalog.core_entries()
        )
        for entry in entries:
            target_mcp.add_tool(
                _wrap_tool_for_mcp(entry.name, entry.fn, resolved_format),
                name=entry.name,
                annotations=resolve_tool_annotations(entry.alias_of or entry.name),
                structured_output=False,
            )
    elif resolved_mode is ToolMode.COMPOUND:
        for domain_spec in catalog.domains():
            compound_fn = make_compound_tool(catalog, domain_spec)
            target_mcp.add_tool(
                _wrap_tool_for_mcp(domain_spec.compound_name, compound_fn, resolved_format),
                name=domain_spec.compound_name,
                annotations=resolve_compound_annotations(domain_spec.key, catalog),
                structured_output=False,
            )

    return resolved_mode


def create_mcp_server(
    mode: ToolMode | None = None,
    output_format: OutputFormat | None = None,
) -> FastMCP:
    """Build an isolated FastMCP instance configured for the specified ToolMode and OutputFormat."""
    server = FastMCP("AutoCAD_AI_Context_Server")
    configure_mcp_server(server, mode=mode, output_format=output_format)
    return server


def _resolve_startup_config_or_exit() -> tuple[ToolMode, OutputFormat]:
    try:
        return resolve_tool_mode(), resolve_output_format()
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1) from exc


_startup_mode, _startup_format = _resolve_startup_config_or_exit()
mcp = create_mcp_server(_startup_mode, _startup_format)


def __getattr__(name: str) -> Any:
    entry = catalog.get_entry(name)
    if entry is not None:
        return entry.fn
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")


def main() -> None:
    """Run MCP server over standard input/output (STDIO) transport."""
    current_mode, current_format = _resolve_startup_config_or_exit()
    if current_mode != _startup_mode or current_format != _startup_format:
        configure_mcp_server(mcp, current_mode, current_format)
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
