"""Adapt internal JSON tool strings into native MCP CallToolResult envelopes."""

from __future__ import annotations

import base64
import binascii
import json
from collections.abc import Callable
from typing import Any

from mcp.types import CallToolResult, ImageContent, TextContent

from autocad_mcp.tool_mode import OutputFormat

_MAX_CONTENT_CHARS = 4_000
_MAX_LIST_ROWS = 100
_MAX_LAYER_ROWS = 120
_MAX_PARTIAL_BATCH_DETAILS = 10
_EXPANDABLE_CHILD_OBJECTS = frozenset({"data", "geometry", "bounds", "summary"})


def adapt_tool_output(
    tool_name: str,
    raw_json: str,
    output_format: OutputFormat = OutputFormat.COMPACT,
) -> CallToolResult:
    """Convert a raw JSON string into a native MCP CallToolResult."""
    text = raw_json or ""
    if not text.strip():
        return _text_result(text, structured_content=None, is_error=False)

    try:
        parsed = json.loads(text)
    except json.JSONDecodeError:
        return _text_result(text, structured_content=None, is_error=False)

    if not isinstance(parsed, dict):
        return _text_result(text, structured_content=None, is_error=False)

    if _is_error_payload(parsed):
        err_text = text if output_format is OutputFormat.JSON else _clamp_output(_format_compact_error(parsed))
        return _text_result(err_text, structured_content=None, is_error=True)

    b64 = parsed.get("image_base64")
    if isinstance(b64, str) and b64.strip():
        return _build_screenshot_result(parsed, b64, output_format)

    out_text = text if output_format is OutputFormat.JSON else _format_compact_success(tool_name, parsed)
    return _text_result(out_text, structured_content=parsed, is_error=False)


def _text_result(
    text: str,
    *,
    structured_content: dict[str, Any] | None,
    is_error: bool,
) -> CallToolResult:
    return CallToolResult(
        content=[TextContent(type="text", text=text)],
        structuredContent=structured_content,
        isError=is_error,
    )


def _is_error_payload(payload: dict[str, Any]) -> bool:
    if payload.get("ok") is False:
        return True
    status = payload.get("status")
    return isinstance(status, str) and status.lower() in ("error", "partial_error")


def _build_screenshot_result(
    payload: dict[str, Any],
    b64: str,
    output_format: OutputFormat,
) -> CallToolResult:
    mime = payload.get("mime_type")
    mime_str = mime if isinstance(mime, str) and mime.strip() else "image/png"

    decode_warning: str | None = None
    try:
        base64.b64decode(b64, validate=True)
        inline_attached = True
    except (binascii.Error, ValueError):
        inline_attached = False
        decode_warning = "inline omitted: invalid base64 payload"

    sanitized = {k: v for k, v in payload.items() if k != "image_base64"}
    sanitized["inline_image_attached"] = inline_attached
    if not inline_attached:
        sanitized["inline_omitted_reason"] = "Invalid base64 image payload from Gateway."

    if output_format is OutputFormat.JSON:
        text_summary = json.dumps(sanitized, separators=(",", ":"))
    else:
        parts = ["Screenshot captured"]
        if isinstance(payload.get("file_path"), str) and payload["file_path"].strip():
            parts.append(f"file: {payload['file_path']}")
        if payload.get("width") is not None and payload.get("height") is not None:
            parts.append(f"{payload['width']}x{payload['height']}")
        parts.append(f"inline: {mime_str}" if inline_attached else str(decode_warning))
        if payload.get("duration_ms") is not None:
            parts.append(f"{payload['duration_ms']} ms")
        text_summary = " | ".join(parts)

    blocks: list[TextContent | ImageContent] = [TextContent(type="text", text=text_summary)]
    if inline_attached:
        blocks.append(ImageContent(type="image", data=b64, mimeType=mime_str))

    return CallToolResult(
        content=blocks,
        structuredContent=sanitized,
        isError=False,
    )


def _format_compact_error(payload: dict[str, Any]) -> str:
    err_obj = payload.get("error") if isinstance(payload.get("error"), dict) else {}
    code = str(err_obj.get("code") or "error")
    message = str(err_obj.get("message") or payload.get("message") or "Operation failed.")
    detail = err_obj.get("detail") or payload.get("detail")
    hint = err_obj.get("hint") or payload.get("hint")

    lines = [f"[{code}] {message}"]
    if isinstance(detail, str) and detail.strip():
        lines.append(f"Detail: {detail}")
    if isinstance(hint, str) and hint.strip():
        lines.append(f"Hint: {hint}")

    _append_partial_batch_summary(lines, payload)
    return "\n".join(lines)


def _append_partial_batch_summary(lines: list[str], payload: dict[str, Any]) -> None:
    succeeded = payload.get("succeeded")
    failed = payload.get("failed")
    if isinstance(succeeded, bool) or not isinstance(succeeded, int) or isinstance(failed, bool) or not isinstance(failed, int):
        return

    rolled_back = _scalar_to_str(bool(payload.get("rolled_back", True)))
    lines.append(f"Batch: succeeded={succeeded}, failed={failed}, rolled_back={rolled_back}")

    handles = payload.get("handles")
    if isinstance(handles, list) and handles:
        str_handles = [h for h in handles if isinstance(h, str)]
        preview = str_handles[:_MAX_PARTIAL_BATCH_DETAILS]
        if preview:
            extra = f", ... (+{len(str_handles) - len(preview)} more)" if len(str_handles) > len(preview) else ""
            lines.append(f"Committed handles: [{', '.join(preview)}{extra}]")

    results = payload.get("results")
    if isinstance(results, list):
        shown_fails = 0
        total_fails = 0
        for item in results:
            if not isinstance(item, dict) or bool(item.get("ok", True)):
                continue
            total_fails += 1
            if shown_fails >= _MAX_PARTIAL_BATCH_DETAILS:
                continue
            idx_val = item.get("index")
            idx_str = "?" if idx_val is None else str(idx_val)
            err_code = str(item.get("error_code") or "error")
            err_msg = _truncate_inline(str(item.get("error") or "Operation failed."), 120)
            lines.append(f"- item #{idx_str} [{err_code}]: {err_msg}")
            shown_fails += 1
        if total_fails > shown_fails:
            lines.append(f"... ({total_fails - shown_fails} more batch failures omitted)")


def _looks_like_layer_array(items: list[Any]) -> bool:
    if not items or not isinstance(items[0], dict):
        return False
    first = items[0]
    return any(k in first for k in ("is_off", "is_frozen", "is_locked", "is_on"))


def _looks_like_text_array(items: list[Any]) -> bool:
    if not items or not isinstance(items[0], dict):
        return False
    first = items[0]
    return "text" in first and "x" in first and "y" in first


def _format_compact_success(tool_name: str, payload: dict[str, Any]) -> str:
    if isinstance(payload.get("layers"), list):
        return _clamp_output(_format_layers(payload, payload["layers"]))
    if isinstance(payload.get("entities"), list):
        return _clamp_output(_format_entities(payload, payload["entities"]))
    if isinstance(payload.get("texts"), list):
        return _clamp_output(_format_texts(payload, payload["texts"]))
    if isinstance(payload.get("documents"), list):
        return _clamp_output(_format_documents(payload, payload["documents"]))
    data_val = payload.get("data")
    if isinstance(data_val, list):
        if _looks_like_layer_array(data_val) or (not data_val and tool_name in ("get_layers", "read_layers")):
            return _clamp_output(_format_layers(payload, data_val))
        if _looks_like_text_array(data_val) or (not data_val and tool_name in ("get_texts", "read_texts", "get_all_texts")):
            return _clamp_output(_format_texts(payload, data_val))
        if (data_val and all(isinstance(item, str) for item in data_val)) or (
            not data_val and tool_name in ("list_blocks", "list_available_blocks")
        ):
            header = _build_header(f"{tool_name} ({len(data_val)})", payload, ("document", "duration_ms"))
            return _clamp_output(
                _format_bounded_section(header, data_val, _MAX_LIST_ROWS, "items", str, lambda s: f"- {s}")
            )
    if isinstance(payload.get("actions"), dict) and isinstance(payload.get("domain"), str):
        return _clamp_output(_format_compound_help(payload))
    return _clamp_output(_format_generic_key_values(tool_name, payload))


def _format_bounded_section(
    header: str,
    items: list[Any],
    max_rows: int,
    noun: str,
    expected_type: type,
    row_formatter: Callable[[Any], str],
) -> str:
    lines = [header]
    eligible = 0
    shown = 0
    for item in items:
        if not isinstance(item, expected_type):
            continue
        eligible += 1
        if shown >= max_rows:
            continue
        lines.append(row_formatter(item))
        shown += 1

    if eligible > shown:
        lines.append(f"... ({eligible - shown} more {noun} omitted; inspect structuredContent)")
    return "\n".join(lines)


def _format_layers(payload: dict[str, Any], layers: list[Any]) -> str:
    raw_count = payload.get("count")
    if isinstance(raw_count, int) and not isinstance(raw_count, bool):
        count = raw_count
    elif isinstance(raw_count, str) and raw_count.strip().isdigit():
        count = int(raw_count.strip())
    else:
        count = len(layers)
    header = _build_header(f"Layers ({count})", payload, ("document", "revision", "duration_ms"))

    def format_row(item: dict[str, Any]) -> str:
        name = item.get("name") or "?"
        aci = item.get("color") if item.get("color") is not None else item.get("color_index")
        aci_str = aci if aci is not None else "-"
        lt = item.get("linetype") or "Continuous"
        lw = item.get("lineweight") if item.get("lineweight") is not None else "default"

        if isinstance(item.get("is_off"), bool):
            is_on = not item["is_off"]
        else:
            is_on = bool(item.get("is_on", True))

        flags = ["on" if is_on else "off"]
        if item.get("is_frozen"):
            flags.append("frozen")
        if item.get("is_locked"):
            flags.append("locked")
        if item.get("is_plottable") is False:
            flags.append("no-plot")
        if item.get("is_current"):
            flags.append("current")
        return f"- {name} | aci:{aci_str} | lt:{lt} | lw:{lw} | {', '.join(flags)}"

    return _format_bounded_section(header, layers, _MAX_LAYER_ROWS, "layers", dict, format_row)


def _format_entities(payload: dict[str, Any], entities: list[Any]) -> str:
    returned = len(entities)
    total = payload.get("total_matched")
    if total is None:
        total = payload.get("total_scanned")
    label = f"{returned}/{total}" if total is not None else str(returned)
    header = _build_header(
        f"Entities ({label})",
        payload,
        ("document", "has_more", "next_cursor", "revision", "duration_ms"),
    )

    def format_row(ent: dict[str, Any]) -> str:
        handle = ent.get("handle") or "?"
        etype = ent.get("type") or "Entity"
        layer = ent.get("layer") or "0"
        row = f"- [{handle}] {etype} | layer:{layer}"
        space = ent.get("space")
        if isinstance(space, str) and space and space.lower() != "model":
            row += f" | space:{space}"

        geom = ent.get("geometry") if isinstance(ent.get("geometry"), dict) else ent
        if isinstance(geom.get("text"), str) and geom["text"]:
            row += f' | text:"{_truncate_inline(geom["text"], 60)}"'
        elif isinstance(geom.get("block_name"), str) and geom["block_name"]:
            row += f" | block:{geom['block_name']}"
        elif geom.get("length") is not None:
            row += f" | len:{geom['length']}"
        elif geom.get("radius") is not None:
            row += f" | r:{geom['radius']}"
        elif geom.get("area") is not None:
            row += f" | area:{geom['area']}"
        return row

    return _format_bounded_section(header, entities, _MAX_LIST_ROWS, "entities", dict, format_row)


def _format_texts(payload: dict[str, Any], texts: list[Any]) -> str:
    header = _build_header(f"Texts ({len(texts)})", payload, ("document", "duration_ms"))

    def format_row(item: dict[str, Any]) -> str:
        tag = item.get("handle") or item.get("type") or "Text"
        layer = item.get("layer") or "0"
        x = item.get("x") if item.get("x") is not None else 0
        y = item.get("y") if item.get("y") is not None else 0
        raw_text = item.get("text")
        if not isinstance(raw_text, str) or not raw_text.strip():
            raw_text = item.get("content") or ""
        return f"- [{tag}] ({x}, {y}) | layer:{layer} | {_truncate_inline(str(raw_text), 80)}"

    return _format_bounded_section(header, texts, _MAX_LIST_ROWS, "texts", dict, format_row)


def _format_documents(payload: dict[str, Any], docs: list[Any]) -> str:
    header = _build_header(f"Documents ({len(docs)})", payload, ("active_document", "duration_ms"))

    def format_row(d: dict[str, Any]) -> str:
        name = d.get("name") or "?"
        sid = d.get("session_id") or "-"
        rev = d.get("revision") if d.get("revision") is not None else 0
        active = _scalar_to_str(bool(d.get("is_active", False)))
        managed = _scalar_to_str(bool(d.get("is_managed", False)))
        return f"- {name} | id:{sid} | rev:{rev} | active:{active} | managed:{managed}"

    return _format_bounded_section(header, docs, _MAX_LIST_ROWS, "documents", dict, format_row)


def _format_compound_help(payload: dict[str, Any]) -> str:
    domain = payload["domain"]
    tool = payload.get("tool") or f"autocad_{domain}"
    actions = payload["actions"]
    lines = [f"Domain '{domain}' ({tool}) | {len(actions)} actions"]
    for action_name, info in actions.items():
        if not isinstance(info, dict):
            continue
        req = ", ".join(str(x) for x in (info.get("required") or []) if x is not None)
        opt = ", ".join(str(x) for x in (info.get("optional") or []) if x is not None)
        desc = _truncate_inline(str(info.get("description") or ""), 100)
        lines.append(f"- {action_name} | req:[{req}] | opt:[{opt}] | {desc}")

    aliases = payload.get("aliases")
    if isinstance(aliases, dict) and aliases:
        pairs = ", ".join(f"{k}->{v}" for k, v in aliases.items() if isinstance(v, str))
        if pairs:
            lines.append(f"Aliases: {pairs}")
    return "\n".join(lines)


def _format_generic_key_values(tool_name: str, payload: dict[str, Any]) -> str:
    lines = [f"OK ({tool_name})"]
    _append_object_properties(lines, payload, prefix="", expand_children=True)
    return "\n".join(lines)


def _append_object_properties(
    lines: list[str],
    obj: dict[str, Any],
    *,
    prefix: str,
    expand_children: bool,
) -> None:
    for key, val in obj.items():
        if key in ("status", "ok") or val is None:
            continue
        key_name = f"{prefix}.{key}" if prefix else key
        if isinstance(val, (str, int, float, bool)):
            lines.append(f"{key_name}: {_scalar_to_str(val)}")
        elif isinstance(val, list):
            if len(val) <= 6 and all(isinstance(x, (str, int, float, bool)) for x in val):
                lines.append(f"{key_name}: [{', '.join(_scalar_to_str(x) for x in val)}]")
            else:
                lines.append(f"{key_name}: [{len(val)} items; inspect structuredContent]")
        elif isinstance(val, dict):
            raw_obj = json.dumps(val, separators=(",", ":"))
            if len(raw_obj) <= 220:
                lines.append(f"{key_name}: {raw_obj}")
            elif expand_children and key in _EXPANDABLE_CHILD_OBJECTS:
                _append_object_properties(lines, val, prefix=key, expand_children=False)
            else:
                lines.append(f"{key_name}: [object ({len(val)} keys); inspect structuredContent]")


def _build_header(prefix: str, payload: dict[str, Any], keys: tuple[str, ...]) -> str:
    parts = [prefix]
    for key in keys:
        val = payload.get(key)
        if val is None or val == "":
            continue
        label = {"document": "doc", "revision": "rev", "duration_ms": "ms"}.get(key, key)
        parts.append(f"{label}: {_scalar_to_str(val)}")
    return " | ".join(parts)


def _scalar_to_str(value: Any) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


def _truncate_inline(text: str, max_len: int) -> str:
    single = text.replace("\r", " ").replace("\n", " ").strip()
    return single if len(single) <= max_len else f"{single[: max_len - 3]}..."


def _clamp_output(text: str) -> str:
    if len(text) <= _MAX_CONTENT_CHARS:
        return text
    return f"{text[: _MAX_CONTENT_CHARS - 64]}\n... [truncated; full payload available in structuredContent]"


__all__ = ["adapt_tool_output"]
