"""Unit and integration tests for MCP v2.7 output adaptation, ImageContent, isError, and ToolAnnotations."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import httpx
import pytest
from mcp.types import CallToolResult, ImageContent, TextContent

import autocad_mcp.server as srv
from autocad_mcp.mcp_result import adapt_tool_output
from autocad_mcp.tool_mode import (
    OUTPUT_FORMAT_ENV_VAR,
    TOOL_MODE_ENV_VAR,
    OutputFormat,
    ToolMode,
    catalog,
    enumerate_classified_tools,
    parse_output_format,
    resolve_compound_annotations,
    resolve_output_format,
    resolve_tool_annotations,
)


def _pythonpath_env(mode_value: str, output_format: str | None = None) -> dict[str, str]:
    pkg_src = str(Path(__file__).resolve().parents[1] / "src")
    env = os.environ.copy()
    env[TOOL_MODE_ENV_VAR] = mode_value
    if output_format is not None:
        env[OUTPUT_FORMAT_ENV_VAR] = output_format
    existing_pp = env.get("PYTHONPATH", "")
    env["PYTHONPATH"] = f"{pkg_src}{os.pathsep}{existing_pp}" if existing_pp else pkg_src
    return env


def test_parse_and_resolve_output_format(monkeypatch: pytest.MonkeyPatch) -> None:
    assert parse_output_format(None) is OutputFormat.COMPACT
    assert parse_output_format("") is OutputFormat.COMPACT
    assert parse_output_format("  \t ") is OutputFormat.COMPACT
    assert parse_output_format("compact") is OutputFormat.COMPACT
    assert parse_output_format("JSON") is OutputFormat.JSON

    with pytest.raises(ValueError, match=OUTPUT_FORMAT_ENV_VAR):
        parse_output_format("yaml")

    monkeypatch.delenv(OUTPUT_FORMAT_ENV_VAR, raising=False)
    assert resolve_output_format() is OutputFormat.COMPACT
    monkeypatch.setenv(OUTPUT_FORMAT_ENV_VAR, "json")
    assert resolve_output_format() is OutputFormat.JSON


def test_annotation_policy_parity_and_dynamic_compound() -> None:
    with pytest.raises(ValueError, match="canonical_name"):
        resolve_tool_annotations("   ")

    for classified in enumerate_classified_tools():
        entry = catalog.get_entry(classified)
        assert entry is not None and not entry.is_alias, f"Unknown or alias tool in policy: {classified}"

    for entry in catalog.entries(include_aliases=True):
        ann = resolve_tool_annotations(entry.alias_of or entry.name)
        assert not (ann.readOnlyHint and ann.destructiveHint)

    assert resolve_tool_annotations("get_layers").readOnlyHint is True
    assert resolve_tool_annotations("delete_entities").destructiveHint is True
    assert resolve_tool_annotations("execute_native_batch").destructiveHint is True
    assert resolve_tool_annotations("new_drawing").openWorldHint is True
    assert resolve_tool_annotations("save_drawing").openWorldHint is True

    ctx_ann = resolve_compound_annotations("context", catalog)
    assert ctx_ann.readOnlyHint is True
    assert ctx_ann.destructiveHint is False
    assert ctx_ann.idempotentHint is True

    meas_ann = resolve_compound_annotations("measurement", catalog)
    assert meas_ann.readOnlyHint is False
    assert meas_ann.destructiveHint is True

    plot_ann = resolve_compound_annotations("plotting", catalog)
    assert plot_ann.destructiveHint is False
    assert plot_ann.openWorldHint is True


@pytest.mark.asyncio
async def test_mcp_result_is_error_image_content_and_real_gateway_schemas(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    granular = srv.create_mcp_server(ToolMode.GRANULAR, OutputFormat.COMPACT)
    tools_by_name = {t.name: t for t in await granular.list_tools()}
    assert tools_by_name["get_layers"].annotations is not None
    assert tools_by_name["get_layers"].annotations.readOnlyHint is True
    assert tools_by_name["delete_entities"].annotations.destructiveHint is True
    assert tools_by_name["execute_native_batch"].annotations.destructiveHint is True
    assert tools_by_name["new_drawing"].annotations.openWorldHint is True
    assert tools_by_name["save_drawing"].annotations.openWorldHint is True
    assert set(tools_by_name["create_viewport"].inputSchema["properties"].keys()) >= {"lower_x", "scale", "view_direction"}

    compound = srv.create_mcp_server(ToolMode.COMPOUND, OutputFormat.COMPACT)
    compound_by_name = {t.name: t for t in await compound.list_tools()}
    assert compound_by_name["autocad_context"].annotations.readOnlyHint is True
    assert compound_by_name["autocad_execution"].annotations.destructiveHint is True
    assert compound_by_name["autocad_measurement"].annotations.destructiveHint is True
    assert compound_by_name["autocad_plotting"].annotations.destructiveHint is False

    bad_call = await compound.call_tool("autocad_context", {"action": "nonexistent_tool"})
    assert isinstance(bad_call, CallToolResult)
    assert bad_call.isError is True
    assert bad_call.structuredContent is None
    assert "[bad_argument]" in bad_call.content[0].text

    one_px_b64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
    real_layers = [
        {
            "name": f"LAYER-{i:02d}",
            "is_locked": i == 3,
            "is_frozen": i == 2,
            "is_off": i == 4,
            "color": (i % 7) + 1,
            "linetype": "Continuous",
            "lineweight": "LineWeight025",
            "is_plottable": i != 5,
            "description": "",
        }
        for i in range(1, 51)
    ]

    async def fake_get(endpoint: str) -> str:
        if endpoint.startswith("context/screenshot"):
            return json.dumps({
                "status": "success",
                "ok": True,
                "file_path": "C:/shots/test.png",
                "width": 1920,
                "height": 1080,
                "mime_type": "image/png",
                "image_base64": one_px_b64,
                "duration_ms": 14.2,
            })
        return json.dumps({
            "status": "success",
            "ok": True,
            "data": real_layers,
        })

    monkeypatch.setattr(srv.gateway, "get_context", fake_get)

    shot_res = await granular.call_tool("capture_screenshot", {"inline": True})
    assert isinstance(shot_res, CallToolResult)
    assert shot_res.isError is False
    assert len(shot_res.content) == 2
    assert isinstance(shot_res.content[0], TextContent)
    assert "iVBOR" not in shot_res.content[0].text
    assert isinstance(shot_res.content[1], ImageContent)
    assert shot_res.content[1].mimeType == "image/png"
    assert shot_res.structuredContent is not None
    assert "image_base64" not in shot_res.structuredContent
    assert shot_res.structuredContent["inline_image_attached"] is True

    layers_compact = await granular.call_tool("get_layers", {})
    granular_json = srv.create_mcp_server(ToolMode.GRANULAR, OutputFormat.JSON)
    layers_json = await granular_json.call_tool("get_layers", {})
    assert isinstance(layers_compact, CallToolResult)
    assert isinstance(layers_json, CallToolResult)
    compact_text = layers_compact.content[0].text
    json_text = layers_json.content[0].text
    assert "aci:2" in compact_text and "| off" in compact_text and "no-plot" in compact_text
    assert len(compact_text) < len(json_text) * 0.65
    assert len(layers_compact.structuredContent["data"]) == 50

    bad_shot = adapt_tool_output(
        "capture_screenshot",
        json.dumps({
            "status": "success",
            "ok": True,
            "file_path": "C:/shots/corrupt.png",
            "mime_type": "image/png",
            "image_base64": "not-valid-base64!!!",
        }),
        OutputFormat.COMPACT,
    )
    assert bad_shot.isError is False
    assert len(bad_shot.content) == 1
    assert bad_shot.structuredContent is not None
    assert bad_shot.structuredContent["inline_image_attached"] is False
    assert "inline_omitted_reason" in bad_shot.structuredContent

    texts_res = adapt_tool_output(
        "get_texts",
        json.dumps({
            "status": "success",
            "ok": True,
            "data": [{"type": "MText", "text": "BEAM W12x26", "x": 10.5, "y": 20.25, "layer": "ANNO"}],
        }),
        OutputFormat.COMPACT,
    )
    assert "Texts (1)" in texts_res.content[0].text
    assert "BEAM W12x26" in texts_res.content[0].text

    entities_res = adapt_tool_output(
        "query_entities",
        json.dumps({
            "status": "success",
            "ok": True,
            "total_matched": 12,
            "has_more": True,
            "next_cursor": "1",
            "entities": [
                {
                    "handle": "1F4",
                    "type": "Line",
                    "layer": "WALLS",
                    "space": "model",
                    "geometry": {"length": 4.5},
                }
            ],
        }),
        OutputFormat.COMPACT,
    )
    assert "Entities (1/12)" in entities_res.content[0].text
    assert "len:4.5" in entities_res.content[0].text

    wrapped_dict = srv._wrap_tool_for_mcp("dict_tool", lambda: {"status": "success", "ok": True, "count": 3}, OutputFormat.COMPACT)
    dict_res = await wrapped_dict()
    assert dict_res.isError is False
    assert dict_res.structuredContent == {"status": "success", "ok": True, "count": 3}

    adapted_fallback = adapt_tool_output("raw_tool", "plain non-json text", OutputFormat.COMPACT)
    assert adapted_fallback.isError is False
    assert adapted_fallback.content[0].text == "plain non-json text"

    partial_res = adapt_tool_output(
        "execute_native_batch",
        json.dumps({
            "status": "partial_error",
            "ok": False,
            "rolled_back": False,
            "succeeded": 2,
            "failed": 1,
            "handles": ["1A1", "1A2"],
            "results": [
                {"index": 0, "ok": True, "handle": "1A1"},
                {"index": 1, "ok": True, "handle": "1A2"},
                {"index": 2, "ok": False, "error_code": "not_found", "error": "Layer 'MISSING' not found."},
            ],
            "error": {"code": "batch_partial_error", "message": "1 of 3 batch operations failed."},
        }),
        OutputFormat.COMPACT,
    )
    assert partial_res.isError is True
    assert "succeeded=2, failed=1" in partial_res.content[0].text
    assert "1A1, 1A2" in partial_res.content[0].text
    assert "item #2 [not_found]" in partial_res.content[0].text

    huge_err = adapt_tool_output(
        "get_layers",
        json.dumps({"status": "error", "ok": False, "error": {"code": "http_error", "message": "Bad Gateway", "detail": "X" * 5000}}),
        OutputFormat.COMPACT,
    )
    assert huge_err.isError is True
    assert len(huge_err.content[0].text) <= 4_000

    empty_layers = adapt_tool_output(
        "get_layers",
        json.dumps({"status": "success", "ok": True, "data": []}),
        OutputFormat.COMPACT,
    )
    assert empty_layers.content[0].text.startswith("Layers (0)")

    from autocad_mcp.policy import parse_lineweight_arg

    assert parse_lineweight_arg("0.25mm") == ("0.25", None)
    assert parse_lineweight_arg("default") == ("default", None)
    assert parse_lineweight_arg("ByLayer")[0] is None
    assert parse_lineweight_arg(True)[0] is None
    assert parse_lineweight_arg(float("nan"))[0] is None


@pytest.mark.asyncio
async def test_client_http_502_and_execute_error_envelopes(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    from autocad_mcp.client import AutoCADGatewayClient

    client = AutoCADGatewayClient(port=5055, token="tok-err", instance_dir=tmp_path)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/context/layers":
            return httpx.Response(502, text="Bad Gateway upstream HTML")
        if request.url.path == "/execute":
            return httpx.Response(200, json={"status": "error", "ok": False, "message": "Invalid AutoLISP syntax"})
        return httpx.Response(200, json={"status": "success", "ok": True})

    transport = httpx.MockTransport(handler)
    orig_async_client = httpx.AsyncClient
    monkeypatch.setattr(
        "autocad_mcp.client.httpx.AsyncClient",
        lambda *args, **kwargs: orig_async_client(*args, **{**kwargs, "transport": transport}),
    )

    try:
        ctx_err = await client.get_context("context/layers")
        adapted_ctx = adapt_tool_output("get_layers", ctx_err, OutputFormat.COMPACT)
        assert adapted_ctx.isError is True
        assert "[http_error]" in adapted_ctx.content[0].text

        exec_err = await client.execute_command("(broken")
        parsed_exec = json.loads(exec_err)
        assert parsed_exec["status"] == "error"
        assert parsed_exec["message"] == "Invalid AutoLISP syntax"
    finally:
        await client.aclose()


def test_invalid_output_format_exits_nonzero_on_stderr() -> None:
    env = _pythonpath_env("granular", output_format="yaml")
    bad_fmt = subprocess.run(
        [sys.executable, "-m", "autocad_mcp"],
        env=env,
        capture_output=True,
        text=True,
        timeout=15,
    )
    assert bad_fmt.returncode == 1
    assert bad_fmt.stdout == ""
    assert OUTPUT_FORMAT_ENV_VAR in bad_fmt.stderr
