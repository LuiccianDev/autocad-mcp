"""Behavioral tests for AUTOCAD_MCP_TOOL_MODE across granular, core, and compound modes."""

from __future__ import annotations

import inspect
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import httpx
import pytest
from mcp.server.fastmcp import FastMCP

import autocad_mcp.server as srv
from autocad_mcp.compound import (
    dispatch_compound_action,
    format_domain_help,
    parse_params_object,
)
from autocad_mcp.tool_mode import (
    TOOL_MODE_ENV_VAR,
    DomainSpec,
    ToolCatalog,
    ToolMode,
    catalog,
    parse_tool_mode,
    resolve_tool_mode,
)
from autocad_mcp.tools import __all__ as ALL_TOOL_EXPORTS

EXPECTED_CORE_TOOLS = frozenset(
    {
        "get_drawing_info",
        "get_layers",
        "get_drawing_extents",
        "capture_screenshot",
        "query_entities",
        "get_entity",
        "modify_entity",
        "delete_entities",
        "execute_native_batch",
        "draw_batch_geometry",
        "create_layer",
        "set_layer_state",
        "list_documents",
        "allow_edit_document",
        "activate_document",
        "measure_distance",
        "purge_and_audit_drawing",
        "create_viewport",
        "export_pdf",
        "check_civil3d_status",
        "list_civil3d_surfaces",
        "list_civil3d_alignments",
        "get_surface_elevation",
        "sample_longitudinal_profile",
        "ping_gateway",
        "execute_command",
        "evaluate_lisp",
        "save_drawing",
    }
)

EXPECTED_COMPOUND_TOOLS = (
    "autocad_context",
    "autocad_entities",
    "autocad_drafting",
    "autocad_drawing",
    "autocad_layers",
    "autocad_documents",
    "autocad_measurement",
    "autocad_plotting",
    "autocad_civil3d",
    "autocad_execution",
)


def _extract_json_result(call_result: Any) -> dict[str, Any]:
    _, payload = call_result
    return json.loads(payload["result"])


def test_parse_and_resolve_tool_mode(monkeypatch: pytest.MonkeyPatch) -> None:
    assert parse_tool_mode(None) is ToolMode.GRANULAR
    assert parse_tool_mode("") is ToolMode.GRANULAR
    assert parse_tool_mode("   \t\n ") is ToolMode.GRANULAR
    assert parse_tool_mode("granular") is ToolMode.GRANULAR
    assert parse_tool_mode("COMPOUND") is ToolMode.COMPOUND
    assert parse_tool_mode("  Core  ") is ToolMode.CORE

    with pytest.raises(ValueError, match=TOOL_MODE_ENV_VAR):
        parse_tool_mode("invalid")
    with pytest.raises(ValueError, match=TOOL_MODE_ENV_VAR):
        parse_tool_mode("minimal")

    monkeypatch.delenv(TOOL_MODE_ENV_VAR, raising=False)
    assert resolve_tool_mode() is ToolMode.GRANULAR

    monkeypatch.setenv(TOOL_MODE_ENV_VAR, "compound")
    assert resolve_tool_mode() is ToolMode.COMPOUND

    monkeypatch.setenv(TOOL_MODE_ENV_VAR, "CORE")
    assert resolve_tool_mode() is ToolMode.CORE


def test_catalog_registration_validation_and_unwrapped_functions() -> None:
    assert len(ALL_TOOL_EXPORTS) == 89
    assert len(catalog.entries()) == 89
    assert {entry.name for entry in catalog.entries()} == set(ALL_TOOL_EXPORTS)
    assert {entry.name for entry in catalog.core_entries()} == EXPECTED_CORE_TOOLS

    assert inspect.iscoroutinefunction(srv.measure_distance) is False
    assert inspect.iscoroutinefunction(srv.calculate_polygon_area) is False
    assert inspect.iscoroutinefunction(srv.ping_gateway) is True
    assert list(inspect.signature(srv.modify_entity).parameters)[:2] == ["handle", "layer"]

    local_catalog = ToolCatalog((DomainSpec("alpha", "autocad_alpha", "Alpha domain."),))
    alpha = local_catalog.domain("alpha")

    @alpha.tool(name="canonical_op")
    def canonical_op(x: int) -> str:
        return str(x)

    with pytest.raises(ValueError, match="Duplicate tool registration"):
        @alpha.tool(name="canonical_op")
        def _dup() -> str:
            return ""

    with pytest.raises(ValueError, match="unknown tool"):
        @alpha.alias(name="bad_alias", alias_of="missing_tool")
        def _bad_alias() -> str:
            return ""

    with pytest.raises(ValueError, match="core=True"):
        local_catalog.register(domain="alpha", fn=canonical_op, name="core_alias", core=True, alias_of="canonical_op")

    @alpha.alias(name="valid_alias", alias_of="canonical_op")
    def _valid_alias(x: int) -> str:
        return str(x)

    with pytest.raises(ValueError, match="another alias"):
        @alpha.alias(name="chained_alias", alias_of="valid_alias")
        def _chained() -> str:
            return ""


@pytest.mark.asyncio
async def test_server_exposes_exact_tools_per_mode_and_reconfigures_idempotently() -> None:
    granular_server = srv.create_mcp_server(ToolMode.GRANULAR)
    granular_names = [tool.name for tool in await granular_server.list_tools()]
    assert len(granular_names) == 89
    assert set(granular_names) == set(ALL_TOOL_EXPORTS)

    core_server = srv.create_mcp_server(ToolMode.CORE)
    core_names = [tool.name for tool in await core_server.list_tools()]
    assert len(core_names) == 28
    assert set(core_names) == EXPECTED_CORE_TOOLS

    compound_server = srv.create_mcp_server(ToolMode.COMPOUND)
    compound_names = [tool.name for tool in await compound_server.list_tools()]
    assert tuple(compound_names) == EXPECTED_COMPOUND_TOOLS

    mutable_server = FastMCP("IdempotencyCheck")
    srv.configure_mcp_server(mutable_server, ToolMode.GRANULAR)
    assert len(await mutable_server.list_tools()) == 89

    srv.configure_mcp_server(mutable_server, ToolMode.COMPOUND)
    assert tuple(t.name for t in await mutable_server.list_tools()) == EXPECTED_COMPOUND_TOOLS

    srv.configure_mcp_server(mutable_server, ToolMode.CORE)
    assert {t.name for t in await mutable_server.list_tools()} == EXPECTED_CORE_TOOLS

    srv.configure_mcp_server(mutable_server, ToolMode.GRANULAR)
    assert len(await mutable_server.list_tools()) == 89


@pytest.mark.asyncio
async def test_compound_help_overview_targeted_alias_and_cross_domain_hints() -> None:
    compound_server = srv.create_mcp_server(ToolMode.COMPOUND)

    overview = _extract_json_result(await compound_server.call_tool("autocad_context", {}))
    assert overview["status"] == "success"
    assert overview["domain"] == "context"
    assert overview["tool"] == "autocad_context"
    assert "get_drawing_info" in overview["actions"]
    assert "capture_screenshot" in overview["actions"]
    assert "read_layers" not in overview["actions"]
    assert overview["actions"]["capture_screenshot"]["required"] == []
    assert overview["actions"]["capture_screenshot"]["optional"] == ["inline"]
    assert overview["aliases"]["read_layers"] == "get_layers"

    targeted = _extract_json_result(
        await compound_server.call_tool(
            "autocad_entities",
            {"action": "help", "params": {"action": "modify_entity"}},
        )
    )
    assert targeted["status"] == "success"
    assert targeted["domain"] == "entities"
    assert targeted["action"] == "modify_entity"
    assert "resolved_from" not in targeted
    assert targeted["input_schema"]["required"] == ["handle"]
    assert "expected_revision" in targeted["input_schema"]["properties"]
    assert "document" in targeted["input_schema"]["properties"]

    alias_help = _extract_json_result(
        await compound_server.call_tool(
            "autocad_context",
            {"action": "help", "params": '{"action": "read_layers"}'},
        )
    )
    assert alias_help["status"] == "success"
    assert alias_help["action"] == "get_layers"
    assert alias_help["resolved_from"] == "read_layers"
    assert alias_help["input_schema"]["type"] == "object"

    cross_help = json.loads(format_domain_help(catalog, "context", {"action": "measure_distance"}))
    assert cross_help["status"] == "error"
    assert "autocad_measurement" in cross_help["message"]


@pytest.mark.asyncio
async def test_compound_boundary_validation_rejects_malformed_unexpected_missing_and_wrong_types() -> None:
    parsed_none, err_none = parse_params_object(None)
    assert parsed_none == {} and err_none is None

    parsed_json, err_json = parse_params_object('{"x1": 1}')
    assert parsed_json == {"x1": 1} and err_json is None

    bad_inputs: tuple[Any, ...] = ("{bad json", "[1, 2]", 42)
    for bad_input in bad_inputs:
        bad_parsed, bad_err = parse_params_object(bad_input)
        assert bad_parsed is None
        assert bad_err is not None
        assert json.loads(bad_err)["status"] == "error"

    compound_server = srv.create_mcp_server(ToolMode.COMPOUND)

    empty_action = _extract_json_result(await compound_server.call_tool("autocad_context", {"action": "   "}))
    assert empty_action["status"] == "error"
    assert "action='help'" in empty_action["message"]
    assert "get_drawing_info" in empty_action["message"]

    unknown_action = _extract_json_result(
        await compound_server.call_tool("autocad_context", {"action": "nonexistent_tool"})
    )
    assert unknown_action["status"] == "error"
    assert "get_drawing_info" in unknown_action["message"]
    assert "action='help'" in unknown_action["message"]

    help_via_dispatch = json.loads(
        await dispatch_compound_action(catalog, "context", "help", {"action": "get_layers"})
    )
    assert help_via_dispatch["status"] == "success"
    assert help_via_dispatch["action"] == "get_layers"

    bad_help_action = json.loads(format_domain_help(catalog, "context", {"action": ""}))
    assert bad_help_action["status"] == "error"

    bad_help_key = _extract_json_result(
        await compound_server.call_tool("autocad_context", {"action": "help", "params": {"typo": "get_layers"}})
    )
    assert bad_help_key["status"] == "error"
    assert "typo" in bad_help_key["message"]

    bad_call_list_params = _extract_json_result(
        await compound_server.call_tool("autocad_context", {"action": "help", "params": [1, 2]})
    )
    assert bad_call_list_params["status"] == "error"

    bad_call_int_params = _extract_json_result(
        await compound_server.call_tool("autocad_context", {"action": "help", "params": 42})
    )
    assert bad_call_int_params["status"] == "error"

    cross_dispatch = _extract_json_result(
        await compound_server.call_tool(
            "autocad_context",
            {"action": "draw_batch_geometry", "params": {"elements": []}},
        )
    )
    assert cross_dispatch["status"] == "error"
    assert "autocad_drawing" in cross_dispatch["message"]

    unexpected_key = _extract_json_result(
        await compound_server.call_tool(
            "autocad_measurement",
            {
                "action": "measure_distance",
                "params": {"x1": 0, "y1": 0, "x2": 3, "y2": 4, "typo_param": 99},
            },
        )
    )
    assert unexpected_key["status"] == "error"
    assert "typo_param" in unexpected_key["message"]

    missing_required = _extract_json_result(
        await compound_server.call_tool(
            "autocad_measurement",
            {"action": "measure_distance", "params": {"x1": 0, "y1": 0}},
        )
    )
    assert missing_required["status"] == "error"
    assert "x2" in missing_required["message"]
    assert "y2" in missing_required["message"]

    wrong_type = _extract_json_result(
        await compound_server.call_tool(
            "autocad_measurement",
            {
                "action": "measure_distance",
                "params": {"x1": "not_a_number", "y1": 0, "x2": 3, "y2": 4},
            },
        )
    )
    assert wrong_type["status"] == "error"
    assert "Traceback" not in wrong_type["message"]

    malformed_params_dispatch = json.loads(
        await dispatch_compound_action(catalog, "measurement", "measure_distance", "[1, 2]")
    )
    assert malformed_params_dispatch["status"] == "error"


@pytest.mark.asyncio
async def test_compound_and_core_execution_sync_async_aliases_and_multidoc(monkeypatch: pytest.MonkeyPatch) -> None:
    gets: list[str] = []
    posts: list[tuple[str, dict[str, Any], float | None]] = []
    pings: list[str] = []

    async def mock_get(endpoint: str) -> str:
        gets.append(endpoint)
        return json.dumps({"status": "success", "endpoint": endpoint})

    async def mock_post(endpoint: str, data: dict[str, Any], timeout: float | None = None) -> str:
        posts.append((endpoint, data, timeout))
        return json.dumps({"status": "success", "endpoint": endpoint})

    async def mock_ping() -> str:
        pings.append("_ping")
        return json.dumps({"status": "success", "alive": True})

    monkeypatch.setattr(srv.gateway, "get_context", mock_get)
    monkeypatch.setattr(srv.gateway, "post_json", mock_post)
    monkeypatch.setattr(srv.gateway, "ping", mock_ping)

    compound_server = srv.create_mcp_server(ToolMode.COMPOUND)

    sync_res = _extract_json_result(
        await compound_server.call_tool(
            "autocad_measurement",
            {"action": "measure_distance", "params": {"x1": 0, "y1": 0, "x2": 3, "y2": 4}},
        )
    )
    assert sync_res["status"] == "success"
    assert sync_res["distance_2d"] == 5.0

    sync_json_str_res = _extract_json_result(
        await compound_server.call_tool(
            "autocad_measurement",
            {"action": "measure_distance", "params": '{"x1": 0, "y1": 0, "x2": 6, "y2": 8}'},
        )
    )
    assert sync_json_str_res["distance_2d"] == 10.0

    alias_res = _extract_json_result(
        await compound_server.call_tool("autocad_context", {"action": "read_layers"})
    )
    assert alias_res["status"] == "success"
    assert gets == ["context/layers"]

    no_param_res = _extract_json_result(
        await compound_server.call_tool("autocad_context", {"action": "get_drawing_info"})
    )
    assert no_param_res["status"] == "success"
    assert gets == ["context/layers", "context/info"]

    mod_res = _extract_json_result(
        await compound_server.call_tool(
            "autocad_entities",
            {
                "action": "modify_entity",
                "params": {
                    "handle": "1A",
                    "layer": "WALLS",
                    "expected_revision": 7,
                    "document": "Plan.dwg",
                },
            },
        )
    )
    assert mod_res["status"] == "success"
    assert posts[0] == (
        "entities/modify",
        {
            "handle": "1A",
            "layer": "WALLS",
            "expected_revision": 7,
            "document": "Plan.dwg",
        },
        None,
    )

    batch_res = _extract_json_result(
        await compound_server.call_tool(
            "autocad_drawing",
            {
                "action": "draw_batch_geometry",
                "params": {
                    "elements": [{"type": "line", "x1": 0, "y1": 0, "x2": 5, "y2": 0}],
                    "default_layer": "AXIS",
                },
            },
        )
    )
    assert batch_res["status"] == "success"
    assert posts[1][0] == "batch"
    assert posts[1][1]["default_layer"] == "AXIS"
    assert posts[1][1]["operations"][0]["type"] == "line"

    core_server = srv.create_mcp_server(ToolMode.CORE)
    core_ping = _extract_json_result(await core_server.call_tool("ping_gateway", {}))
    assert core_ping["alive"] is True
    assert pings == ["_ping"]


def _pythonpath_env(mode_value: str) -> dict[str, str]:
    pkg_src = str(Path(__file__).resolve().parents[1] / "src")
    env = os.environ.copy()
    env[TOOL_MODE_ENV_VAR] = mode_value
    existing_pp = env.get("PYTHONPATH", "")
    env["PYTHONPATH"] = f"{pkg_src}{os.pathsep}{existing_pp}" if existing_pp else pkg_src
    return env


def test_invalid_tool_mode_exits_nonzero_on_stderr_with_empty_stdout(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    env = _pythonpath_env("invalid")

    proc = subprocess.run(
        [sys.executable, "-m", "autocad_mcp"],
        env=env,
        capture_output=True,
        text=True,
        timeout=15,
    )
    assert proc.returncode == 1
    assert proc.stdout == ""
    assert TOOL_MODE_ENV_VAR in proc.stderr
    assert "invalid" in proc.stderr

    monkeypatch.setenv(TOOL_MODE_ENV_VAR, "bogus")
    with pytest.raises(SystemExit) as exc_info:
        srv.main()
    assert exc_info.value.code == 1
    captured = capsys.readouterr()
    assert captured.out == ""
    assert TOOL_MODE_ENV_VAR in captured.err


def test_subprocess_env_modes_configure_module_mcp_cleanly() -> None:
    probe = (
        "import asyncio, json; "
        "from autocad_mcp.server import mcp; "
        "tools = asyncio.run(mcp.list_tools()); "
        "print(json.dumps([t.name for t in tools]))"
    )
    for mode_val, expected_names in (
        ("compound", list(EXPECTED_COMPOUND_TOOLS)),
        ("core", sorted(EXPECTED_CORE_TOOLS)),
    ):
        env = _pythonpath_env(mode_val)
        proc = subprocess.run(
            [sys.executable, "-c", probe],
            env=env,
            capture_output=True,
            text=True,
            timeout=15,
        )
        assert proc.returncode == 0, proc.stderr
        names = json.loads(proc.stdout)
        if mode_val == "compound":
            assert names == expected_names
        else:
            assert sorted(names) == expected_names


@pytest.mark.asyncio
async def test_gateway_client_pooling_timeouts_and_get_retry(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from autocad_mcp.client import AutoCADGatewayClient, discover_live_instance

    (tmp_path / "²².json").write_text('{"pid": 2, "port": 5055, "token": "bad"}', encoding="utf-8")
    assert discover_live_instance(tmp_path) is None

    client = AutoCADGatewayClient(port=5055, token="tok-pool", instance_dir=tmp_path)
    assert client.resolve_timeout("GET", "context/info") == 10.0
    assert client.resolve_timeout("POST", "entities/create") == 30.0
    assert client.resolve_timeout("POST", "batch") == 60.0
    assert client.resolve_timeout("POST", "plotting/pdf") == 60.0
    assert client.resolve_timeout("POST", "batch", explicit_timeout=95.0) == 95.0

    created_clients = 0
    seen_calls: list[tuple[str, str]] = []
    fail_once = {"get": True, "post": True}

    def handler(request: httpx.Request) -> httpx.Response:
        seen_calls.append((request.method, request.url.path))
        if request.method == "GET" and fail_once["get"]:
            fail_once["get"] = False
            raise httpx.ConnectError("transient disconnect", request=request)
        if request.method == "POST" and fail_once["post"]:
            fail_once["post"] = False
            raise httpx.ConnectError("post must not retry", request=request)
        return httpx.Response(200, json={"status": "success", "ok": True})

    transport = httpx.MockTransport(handler)
    orig_async_client = httpx.AsyncClient

    def patched_async_client(*args: Any, **kwargs: Any) -> httpx.AsyncClient:
        nonlocal created_clients
        created_clients += 1
        kwargs["transport"] = transport
        return orig_async_client(*args, **kwargs)

    monkeypatch.setattr("autocad_mcp.client.httpx.AsyncClient", patched_async_client)

    try:
        res1 = json.loads(await client.get_context("context/info"))
        assert res1["ok"] is True
        assert seen_calls == [("GET", "/context/info"), ("GET", "/context/info")]

        res2 = json.loads(await client.get_context("context/layers"))
        assert res2["ok"] is True
        assert created_clients == 1

        res_post = json.loads(await client.post_json("entities/create", {"type": "line"}))
        assert res_post["status"] == "error"
        assert sum(1 for m, _ in seen_calls if m == "POST") == 1

        client.invalidate_discovery_cache()
        assert client._cached_conn is None
    finally:
        await client.aclose()


@pytest.mark.asyncio
async def test_create_dimension_metric_sizing_validation(monkeypatch: pytest.MonkeyPatch) -> None:
    from autocad_mcp import runtime
    from autocad_mcp.tools.drafting import create_dimension

    captured: list[tuple[str, dict[str, Any]]] = []

    async def fake_post_json(endpoint: str, payload: dict[str, Any], timeout: float | None = None) -> str:
        captured.append((endpoint, payload))
        return json.dumps({"status": "success", "ok": True, "handle": "A1"})

    monkeypatch.setattr(runtime.gateway, "post_json", fake_post_json)

    for bad_kwargs in (
        {"text_height": 0},
        {"arrow_size": -0.1},
        {"dim_scale": 0},
    ):
        err = json.loads(await create_dimension("aligned", 0, 0, 10, 0, **bad_kwargs))
        assert err["status"] == "error"

    assert len(captured) == 0

    ok = json.loads(
        await create_dimension(
            "aligned",
            0,
            0,
            10,
            0,
            text_height=0.28,
            arrow_size=0.2,
            dim_scale=1.0,
        )
    )
    assert ok["ok"] is True
    assert captured[0][0] == "drafting/dimension"
    assert captured[0][1]["text_height"] == 0.28
    assert captured[0][1]["arrow_size"] == 0.2
    assert captured[0][1]["dim_scale"] == 1.0
