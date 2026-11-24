"""Shared validation policies and payload normalization helpers for AutoCAD MCP tools."""

from __future__ import annotations

import json
import math
import ntpath
import re
from typing import Any
from autocad_mcp.cad_math import normalize_batch_element

MAX_BATCH_OPERATIONS = 500
MAX_QUERY_LIMIT = 500
MAX_PROFILE_SAMPLES = 500
MAX_TABLE_ROWS = 200
MAX_TABLE_COLS = 50
MAX_CROSS_SECTION_SAMPLES = 200
MAX_GEOMETRY_VERTICES = 10_000
MAX_JSON_INPUT_BYTES = 16 * 1024 * 1024
MIN_LISP_TIMEOUT_MS = 250
DEFAULT_LISP_TIMEOUT_MS = 15_000
MAX_LISP_TIMEOUT_MS = 120_000
TOOLS_ROUTE = "_tools"
GUIDE_ROUTE = "_guide"

_HANDLE_RE = re.compile(r"^[0-9a-fA-F]{1,16}$")

BLOCKED_SYSTEM_VARIABLES = frozenset({
    "TRUSTEDPATHS",
    "TRUSTEDDOMAINS",
    "SECURELOAD",
    "LEGACYCODESEARCH",
    "SAFEMODE",
    "ACAD",
    "ACADPREFIX",
    "APPAUTOLOAD",
    "DEMANDLOAD",
    "LOGFILENAME",
    "LOGFILEMODE",
    "SAVEFILEPATH",
})

_RESERVED_DEVICE_NAMES = frozenset({
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
})


def error_json(message: str) -> str:
    """Serialize a standard error JSON response."""
    return json.dumps({"status": "error", "message": message})


def has_control_chars(value: str | None) -> bool:
    """Return True when value contains ASCII C0 or C1 control characters."""
    return value is not None and any(ord(c) < 0x20 or 0x7F <= ord(c) <= 0x9F for c in value)


def is_blocked_sysvar(name: str | None) -> bool:
    """Return True when the AutoCAD system variable is security-sensitive and blocked."""
    return bool(name and name.strip().lstrip("_.").upper() in BLOCKED_SYSTEM_VARIABLES)


def is_valid_entity_handle(handle: str | None) -> bool:
    """Return True for valid 1..16 character hexadecimal DXF entity handles (> 0)."""
    if not handle:
        return False
    h = handle.strip()
    if not _HANDLE_RE.fullmatch(h):
        return False
    return int(h, 16) > 0


def validate_local_file_path(
    path: str | None,
    allowed_extensions: tuple[str, ...] = (),
) -> tuple[bool, str, str | None]:
    """Validate and canonicalize an absolute Windows local path without traversal or UNC prefixes."""
    if not path or not path.strip():
        return False, "", "File path cannot be empty."

    trimmed = path.strip()
    for ch in trimmed:
        if ord(ch) < 0x20 or ch in ('\0', '"', '<', '>', '|', '*', '?'):
            return False, "", "File path contains invalid or control characters."

    if trimmed.startswith(("\\\\", "//", "\\/", "/\\")):
        return False, "", "UNC or device network paths are not permitted."

    colon_idx = trimmed.find(":")
    if colon_idx >= 0 and (colon_idx != 1 or not trimmed[0].isascii() or not trimmed[0].isalpha() or ":" in trimmed[2:]):
        return False, "", "Path contains an unexpected colon specifier."

    segments = [s for s in trimmed.replace("/", "\\").split("\\") if s]
    for seg in segments:
        if seg == ".." or seg.endswith("."):
            return False, "", "Path traversal ('..') or trailing-dot segments are not permitted."
        stem = seg.split(".", 1)[0].upper()
        if stem in _RESERVED_DEVICE_NAMES:
            return False, "", f"Reserved Windows device name '{stem}' is not permitted in paths."

    is_win_abs = (
        len(trimmed) >= 3
        and trimmed[0].isascii()
        and trimmed[0].isalpha()
        and trimmed[1] == ":"
        and trimmed[2] in ("\\", "/")
    )
    if not is_win_abs:
        return False, "", "File path must be an absolute local Windows path (for example C:\\folder\\file)."

    normalized = ntpath.normpath(trimmed)
    if allowed_extensions:
        ext = ntpath.splitext(normalized)[1].lower()
        allowed_lower = tuple(e.lower() for e in allowed_extensions)
        if ext not in allowed_lower:
            return False, "", f"File extension '{ext}' is not allowed. Expected: {', '.join(allowed_extensions)}."

    return True, normalized, None


def is_safe_plot_style_name(plot_style: str | None, *, allow_spaces: bool = False) -> bool:
    """Return True when plot_style is a safe .ctb or .stb filename without path or prompt injection chars."""
    if not plot_style or not plot_style.strip():
        return False

    trimmed = plot_style.strip()
    if len(trimmed) > 255 or ".." in trimmed:
        return False

    for c in trimmed:
        if ord(c) < 0x20 or c in ('~', '?', '\\', '/', ':', '"', '<', '>', '|', '*', ';', ',', '=', '`'):
            return False
        if not allow_spaces and c.isspace():
            return False

    stem = trimmed.split(".", 1)[0].upper()
    if stem in _RESERVED_DEVICE_NAMES:
        return False

    ext = ntpath.splitext(trimmed)[1].lower()
    return ext in (".ctb", ".stb")


def is_safe_layout_name(layout_name: str | None, *, allow_spaces: bool = False) -> bool:
    """Return True when layout_name is a safe AutoCAD symbol without '~', '?', or CLI space splitting."""
    if not layout_name or not layout_name.strip():
        return False

    trimmed = layout_name.strip()
    if len(trimmed) > 255 or "~" in trimmed or "?" in trimmed:
        return False

    from autocad_mcp.cad_math import FORBIDDEN_SYMBOL_CHARS

    for c in trimmed:
        if ord(c) < 0x20 or c in FORBIDDEN_SYMBOL_CHARS:
            return False
        if not allow_spaces and c.isspace():
            return False
    return True


def _reject_nan_constant(token: str) -> Any:
    raise ValueError(f"Non-standard JSON numeric constant '{token}' is not permitted.")


def json_loads_strict(raw: str) -> Any:
    """Parse JSON strictly rejecting NaN/Infinity constants and catching excessive nesting."""
    if len(raw.encode("utf-8")) > MAX_JSON_INPUT_BYTES:
        raise ValueError("JSON input exceeds maximum permitted byte length.")
    return json.loads(raw, parse_constant=_reject_nan_constant)


def all_optional_finite(*values: float | None) -> bool:
    """Return True when all non-None values are finite floats."""
    for v in values:
        if v is not None and not math.isfinite(v):
            return False
    return True


def parse_string_list_strict(raw: list[str] | str | None) -> tuple[list[str] | None, str | None]:
    """Parse a Python list, JSON array string, or delimited string without swallowing malformed JSON."""
    if raw is None:
        return None, None
    if isinstance(raw, list):
        if not all(isinstance(x, str) for x in raw):
            return None, "Expected a list of strings."
        items = [x.strip() for x in raw if x.strip()]
        return (items or None), None
    trimmed = raw.strip()
    if not trimmed:
        return None, None
    if trimmed.startswith("["):
        try:
            parsed = json_loads_strict(trimmed)
        except (json.JSONDecodeError, ValueError, RecursionError) as exc:
            return None, f"Invalid JSON array syntax: {exc}"
        if not isinstance(parsed, list):
            return None, "Expected a JSON array."
        if not all(isinstance(x, str) for x in parsed):
            return None, "Expected a JSON array of strings."
        items = [x.strip() for x in parsed if x.strip()]
        return (items or None), None
    parts = [p.strip() for p in trimmed.replace(";", ",").split(",") if p.strip()]
    return (parts or None), None


def parse_string_list(raw: list[str] | str | None) -> list[str] | None:
    """Parse a Python list, JSON string array, or comma/semicolon-separated string list (returns None on empty)."""
    items, err = parse_string_list_strict(raw)
    return None if err else items


def parse_valid_handles(
    raw: list[str] | str | None,
    *,
    required: bool = False,
    max_items: int = MAX_GEOMETRY_VERTICES,
) -> tuple[list[str] | None, str | None]:
    """Parse and validate a list of non-empty hexadecimal DXF entity handles."""
    if raw is None or (isinstance(raw, str) and not raw.strip()) or (isinstance(raw, list) and len(raw) == 0):
        if required:
            return None, "'handles' must contain valid non-empty hexadecimal entity handles."
        return None, None

    parsed, parse_err = parse_string_list_strict(raw)
    if parse_err:
        return None, f"Invalid 'handles': {parse_err}"
    if not parsed or any(not is_valid_entity_handle(h) for h in parsed):
        return None, "All entity handles must be valid non-empty hexadecimal handles."
    if len(parsed) > max_items:
        return None, f"'handles' cannot exceed {max_items} items."
    return parsed, None


def parse_json_value(raw: Any, expected_type: type, label: str) -> tuple[Any | None, str | None]:
    """Parse a Python collection or JSON string validating the expected top-level collection type."""
    if isinstance(raw, expected_type):
        return raw, None
    if isinstance(raw, str):
        try:
            loaded = json_loads_strict(raw)
        except (json.JSONDecodeError, ValueError, RecursionError) as exc:
            return None, f"Invalid JSON in '{label}': {exc}"
        if isinstance(loaded, expected_type):
            return loaded, None
    type_name = "JSON array" if expected_type is list else "JSON object"
    return None, f"'{label}' must be a {type_name}."


__all__ = [
    "BLOCKED_SYSTEM_VARIABLES",
    "DEFAULT_LISP_TIMEOUT_MS",
    "GUIDE_ROUTE",
    "MAX_BATCH_OPERATIONS",
    "MAX_CROSS_SECTION_SAMPLES",
    "MAX_GEOMETRY_VERTICES",
    "MAX_JSON_INPUT_BYTES",
    "MAX_LISP_TIMEOUT_MS",
    "MAX_PROFILE_SAMPLES",
    "MAX_QUERY_LIMIT",
    "MAX_TABLE_COLS",
    "MAX_TABLE_ROWS",
    "MIN_LISP_TIMEOUT_MS",
    "TOOLS_ROUTE",
    "all_optional_finite",
    "error_json",
    "has_control_chars",
    "is_blocked_sysvar",
    "is_safe_layout_name",
    "is_safe_plot_style_name",
    "is_valid_entity_handle",
    "json_loads_strict",
    "normalize_batch_element",
    "parse_json_value",
    "parse_string_list",
    "parse_string_list_strict",
    "parse_valid_handles",
    "validate_local_file_path",
]
