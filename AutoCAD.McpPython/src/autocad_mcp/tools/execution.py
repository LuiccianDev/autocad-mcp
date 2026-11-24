"""Execution, synchronous AutoLISP evaluation, viewport navigation, and drawing setup MCP tools."""

from __future__ import annotations

import json

from autocad_mcp import runtime
from autocad_mcp.cad_math import lisp_string
from autocad_mcp.policy import (
    DEFAULT_LISP_TIMEOUT_MS,
    MAX_BATCH_OPERATIONS,
    MAX_LISP_TIMEOUT_MS,
    MIN_LISP_TIMEOUT_MS,
    error_json,
    json_loads_strict,
    validate_local_file_path,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("execution")


@domain.tool(name="ping_gateway", core=True)
async def ping_gateway() -> str:
    """Ping the native AutoCAD Gateway server via GET /_ping to verify connectivity and active session."""
    return await runtime.gateway.ping()


@domain.alias(name="ping_autocad_gateway", alias_of="ping_gateway")
async def ping_autocad_gateway() -> str:
    """Alias for ping_gateway: Ping the native AutoCAD Gateway server to verify HTTP connectivity."""
    return await ping_gateway()


@domain.tool(name="execute_batch_commands")
async def execute_batch_commands(commands: list[str] | str) -> str:
    """Execute a sequential list of AutoCAD command strings or macros in a single batch payload."""
    if isinstance(commands, str):
        trimmed = commands.strip()
        try:
            parsed = json_loads_strict(trimmed)
            if isinstance(parsed, list):
                commands = parsed
            elif isinstance(parsed, str):
                commands = [parsed]
            else:
                return error_json("'commands' must be a JSON array of command strings.")
        except (json.JSONDecodeError, ValueError, RecursionError):
            if trimmed.startswith("[") or trimmed.startswith("{"):
                return error_json("Malformed JSON in 'commands'.")
            commands = [commands]

    if not isinstance(commands, list) or not commands:
        return error_json("'commands' must be a non-empty list of command strings.")
    if len(commands) > MAX_BATCH_OPERATIONS:
        return error_json(f"'commands' cannot exceed {MAX_BATCH_OPERATIONS} items.")

    cleaned: list[str] = []
    for c in commands:
        if not isinstance(c, str):
            return error_json("Each item in 'commands' must be a string.")
        if "\x00" in c:
            return error_json("Commands cannot contain NUL characters.")
        if c.strip():
            cleaned.append(c.rstrip("\r\n") + "\n")

    if not cleaned:
        return error_json("'commands' must contain at least one non-empty command string.")
    return await runtime.gateway.execute_command("".join(cleaned))


@domain.tool(name="execute_command", core=True)
async def execute_command(command: str) -> str:
    """Enqueue native non-modal AutoCAD commands, AutoLISP macros, or YQArch commands to AutoCAD."""
    if not command or not command.strip() or "\x00" in command:
        return error_json("'command' cannot be empty or contain NUL characters.")
    return await runtime.gateway.execute_command(command)


@domain.tool(name="evaluate_lisp", core=True)
async def evaluate_lisp(code: str, timeout_ms: int = DEFAULT_LISP_TIMEOUT_MS) -> str:
    """Evaluate AutoLISP expressions synchronously inside the active AutoCAD document and return the converted result."""
    if not code or not code.strip():
        return error_json("AutoLISP code cannot be empty.")
    clamped_timeout = max(MIN_LISP_TIMEOUT_MS, min(MAX_LISP_TIMEOUT_MS, int(timeout_ms)))
    http_timeout = max(30.0, clamped_timeout / 1000.0 + 15.0)
    return await runtime.gateway.post_json(
        "lisp/eval",
        {"code": code.strip(), "timeout_ms": clamped_timeout},
        timeout=http_timeout,
    )


@domain.alias(name="execute_lisp", alias_of="evaluate_lisp")
async def execute_lisp(code: str, timeout_ms: int = DEFAULT_LISP_TIMEOUT_MS) -> str:
    """Alias for evaluate_lisp: Evaluate AutoLISP expressions synchronously inside AutoCAD."""
    return await evaluate_lisp(code=code, timeout_ms=timeout_ms)


@domain.tool(name="load_lisp_file")
async def load_lisp_file(file_path: str) -> str:
    """Load and execute an external AutoLISP script file (.lsp) from disk into AutoCAD via synchronous LISP evaluation."""
    ok, norm_path, err = validate_local_file_path(file_path, (".lsp",))
    if not ok:
        return error_json(err or "Invalid LISP file path.")
    fwd = norm_path.replace("\\", "/")
    return await evaluate_lisp(f"(load {lisp_string(fwd)})")


@domain.tool(name="zoom_extents")
async def zoom_extents() -> str:
    """Execute Zoom Extents ('_.ZOOM E') in AutoCAD to center and fit all drawing entities in the current viewport."""
    return await runtime.gateway.execute_command("_.ZOOM\nE\n")


@domain.tool(name="save_drawing", core=True)
async def save_drawing() -> str:
    """Save the active AutoCAD drawing file ('_.QSAVE')."""
    return await runtime.gateway.execute_command("_.QSAVE\n")


@domain.tool(name="configure_drawing_units")
async def configure_drawing_units() -> str:
    """Configure AutoCAD drawing environment to METRIC METERS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) and calibrate dimensions."""
    script = (
        "_.-SETVAR\nINSUNITS\n4\n"
        "_.-SETVAR\nMEASUREMENT\n1\n"
        "_.-SETVAR\nLUNITS\n2\n"
        "_.-SETVAR\nLUPREC\n3\n"
        "_.-SETVAR\nAUNITS\n0\n"
        "_.-SETVAR\nANGBASE\n0.0\n"
        "_.-SETVAR\nANGDIR\n0\n"
        "_.-SETVAR\nDIMTXT\n0.22\n"
        "_.-SETVAR\nDIMASZ\n0.18\n"
        "_.-SETVAR\nDIMEXE\n0.11\n"
        "_.-SETVAR\nDIMEXO\n0.06\n"
        "_.-SETVAR\nDIMGAP\n0.08\n"
        "_.-SETVAR\nDIMDEC\n2\n"
        "_.-SETVAR\nDIMTAD\n1\n"
        "_.UCS\nW\n"
    )
    return await runtime.gateway.execute_command(script)


@domain.alias(name="configure_drawing_environment", alias_of="configure_drawing_units")
async def configure_drawing_environment() -> str:
    """Alias for configure_drawing_units: Configure AutoCAD drawing environment to metric meters."""
    return await configure_drawing_units()
