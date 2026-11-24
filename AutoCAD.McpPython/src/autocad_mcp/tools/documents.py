"""Multi-document session lifecycle, Edit Guard, and system variable MCP tools."""

from __future__ import annotations

import math
from urllib.parse import quote

from autocad_mcp import runtime
from autocad_mcp.policy import error_json, is_blocked_sysvar, validate_local_file_path
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("documents")


@domain.tool(name="list_documents", core=True)
async def list_documents() -> str:
    """List all open AutoCAD drawing documents with session IDs, revisions, and Edit Guard flags."""
    return await runtime.gateway.get_context("documents")


@domain.tool(name="cancel_command")
async def cancel_command() -> str:
    """Send Ctrl+C cancellation to clear an unfinished AutoCAD command-line prompt."""
    return await runtime.gateway.post_json("session/cancel", {})


@domain.tool(name="allow_edit_document", core=True)
async def allow_edit_document(document: str | None = None, allow: bool = True) -> str:
    """Authorize (or revoke) edits on an open drawing when Edit Guard protection is active."""
    return await runtime.gateway.post_json("session/allow-edit", {"document": document, "allow": bool(allow)})


@domain.tool(name="new_drawing")
async def new_drawing(template: str | None = None) -> str:
    """Create a new AutoCAD drawing (optionally from a validated .dwt template) and authorize edits on it."""
    norm_template: str | None = None
    if template and template.strip():
        ok, norm_template, err = validate_local_file_path(template, (".dwt",))
        if not ok:
            return error_json(err or "Invalid drawing template path.")
    return await runtime.gateway.post_json("documents/new", {"template": norm_template})


@domain.tool(name="open_drawing")
async def open_drawing(file_path: str, read_only: bool = False) -> str:
    """Open an existing local .dwg drawing in AutoCAD without automatically bypassing Edit Guard."""
    ok, norm_path, err = validate_local_file_path(file_path, (".dwg",))
    if not ok:
        return error_json(err or "Invalid drawing file path.")
    return await runtime.gateway.post_json("documents/open", {"file_path": norm_path, "read_only": bool(read_only)})


@domain.tool(name="activate_document", core=True)
async def activate_document(document: str) -> str:
    """Switch AutoCAD's active MDI drawing by full path, unique filename, or session ID."""
    if not document or not document.strip():
        return error_json("'document' cannot be empty.")
    return await runtime.gateway.post_json("documents/activate", {"document": document.strip()})


@domain.tool(name="close_document")
async def close_document(
    document: str | None = None,
    save: bool = False,
    discard_changes: bool = False,
    save_as: str | None = None,
) -> str:
    """Close an open drawing with explicit save=True, save_as=.dwg path, or discard_changes=True confirmation."""
    norm_save_as: str | None = None
    if save_as and save_as.strip():
        ok, norm_save_as, err = validate_local_file_path(save_as, (".dwg",))
        if not ok:
            return error_json(err or "Invalid save_as file path.")
    return await runtime.gateway.post_json(
        "documents/close",
        {
            "document": document,
            "save": bool(save),
            "discard_changes": bool(discard_changes),
            "save_as": norm_save_as,
        },
    )


@domain.tool(name="get_system_variable")
async def get_system_variable(name: str) -> str:
    """Read an AutoCAD system variable safely (blocks TRUSTEDPATHS, SECURELOAD, SAFEMODE, etc.)."""
    if not name or not name.strip():
        return error_json("'name' cannot be empty.")
    if is_blocked_sysvar(name):
        return error_json(f"Access to security-sensitive system variable '{name.strip()}' is blocked.")

    return await runtime.gateway.get_context(f"sysvar?name={quote(name.strip(), safe='')}")


@domain.tool(name="set_system_variable")
async def set_system_variable(name: str, value: str | int | float | bool, document: str | None = None) -> str:
    """Set a non-security AutoCAD system variable on the active drawing under Edit Guard protection."""
    if not name or not name.strip():
        return error_json("'name' cannot be empty.")
    if is_blocked_sysvar(name):
        return error_json(f"Modifying security-sensitive system variable '{name.strip()}' is blocked.")
    if isinstance(value, float) and not math.isfinite(value):
        return error_json("System variable numeric value must be finite.")

    return await runtime.gateway.post_json(
        "sysvar",
        {"name": name.strip(), "value": value, "document": document},
    )
