"""Tool introspection tests and Gateway network configuration validation."""

import json
import httpx
import pytest
from autocad_mcp.client import AutoCADGatewayClient, discover_live_instance, is_pid_alive
from autocad_mcp.server import mcp

EXPECTED_TOOLS = [
    # Module 1: Inspection, Context & Vision (8)
    "get_drawing_info",
    "get_layers",
    "get_entities",
    "get_drawing_extents",
    "get_texts",
    "list_blocks",
    "get_selection_count",
    "capture_screenshot",
    # Module 2: Parametric Geometry & Drawing (8)
    "draw_batch_geometry",
    "draw_line",
    "draw_polyline",
    "draw_rectangle",
    "draw_circle",
    "draw_arc",
    "insert_text",
    "insert_block",
    # Module 3: Advanced Layer Management (3)
    "create_layer",
    "set_current_layer",
    "set_layer_state",
    # Module 4: Measurement, Computational Geometry & Audit (4)
    "measure_distance",
    "calculate_polygon_area",
    "purge_and_audit_drawing",
    "regen_all",
    # Module 5: Execution, Control & AutoLISP (8)
    "ping_gateway",
    "execute_batch_commands",
    "execute_command",
    "evaluate_lisp",
    "load_lisp_file",
    "configure_drawing_units",
    "zoom_extents",
    "save_drawing",
    # Module 6: Sheet Layouts, Title Blocks & PDF Plotting (5)
    "create_layout",
    "configure_layout_page",
    "create_viewport",
    "insert_title_block",
    "export_pdf",
    # Module 7: Autodesk Civil 3D (9)
    "check_civil3d_status",
    "list_civil3d_surfaces",
    "get_surface_elevation",
    "list_civil3d_alignments",
    "get_alignment_station_offset",
    "get_point_at_station",
    "sample_longitudinal_profile",
    "create_civil3d_alignment",
    "create_civil3d_surface",
]


EXPECTED_PHASE3_TOOLS = [
    "query_entities",
    "get_entity",
    "modify_entity",
    "transform_entities",
    "delete_entities",
    "execute_native_batch",
    "delete_layer",
    "list_documents",
    "cancel_command",
    "allow_edit_document",
    "new_drawing",
    "open_drawing",
    "activate_document",
    "close_document",
    "get_system_variable",
    "set_system_variable",
]


EXPECTED_PHASE4_TOOLS = [
    "get_gateway_tools",
    "get_gateway_guide",
]


EXPECTED_PHASE5_TOOLS = [
    "create_dimension",
    "create_mleader",
    "create_hatch",
    "create_table",
    "insert_raster_image",
    "define_block",
    "get_block_definition",
    "get_block_attributes",
    "set_block_attributes",
    "list_xrefs",
    "explode_entities",
    "get_civil3d_surface_stats",
    "sample_civil3d_cross_section",
]


EXPECTED_ALIASES = [
    "read_layers",
    "read_objects",
    "read_texts",
    "read_selection",
    "list_available_blocks",
    "capture_screen",
    "clean_and_purge_drawing",
    "ping_autocad_gateway",
    "execute_lisp",
    "configure_drawing_environment",
    "configure_layout_sheet",
    "insert_parametric_titleblock",
    "get_station_coordinate",
    "get_longitudinal_profile",
]


@pytest.mark.asyncio
async def test_tool_count_and_names():
    tools = await mcp.list_tools()
    tool_names = [t.name for t in tools]

    expected_total = (
        len(EXPECTED_TOOLS)
        + len(EXPECTED_ALIASES)
        + len(EXPECTED_PHASE3_TOOLS)
        + len(EXPECTED_PHASE4_TOOLS)
        + len(EXPECTED_PHASE5_TOOLS)
    )
    assert len(tool_names) == expected_total, (
        f"Expected {expected_total} tools, found {len(tool_names)}: {tool_names}"
    )

    for expected in EXPECTED_TOOLS:
        assert expected in tool_names, f"Missing expected tool: {expected}"

    for expected in EXPECTED_PHASE3_TOOLS:
        assert expected in tool_names, f"Missing Phase 3 tool: {expected}"

    for expected in EXPECTED_PHASE4_TOOLS:
        assert expected in tool_names, f"Missing Phase 4 tool: {expected}"

    for expected in EXPECTED_PHASE5_TOOLS:
        assert expected in tool_names, f"Missing Phase 5 tool: {expected}"

    for alias in EXPECTED_ALIASES:
        assert alias in tool_names, f"Missing expected alias tool: {alias}"


def test_gateway_client_default_configuration(tmp_path, monkeypatch):
    monkeypatch.delenv("AUTOCAD_GATEWAY_TOKEN", raising=False)
    monkeypatch.delenv("AUTOCAD_GATEWAY_URL", raising=False)
    monkeypatch.delenv("AUTOCAD_GATEWAY_PORT", raising=False)

    client = AutoCADGatewayClient(instance_dir=tmp_path)
    assert client.base_url == "http://127.0.0.1:5055/"
    assert client.token == ""
    get_headers = client._headers(include_content_type=False)
    assert "X-Gateway-Token" not in get_headers
    assert "Content-Type" not in get_headers
    post_headers = client._headers(include_content_type=True)
    assert post_headers["Content-Type"] == "application/json"


def test_gateway_client_instance_discovery_and_port_match(tmp_path, monkeypatch):
    monkeypatch.delenv("AUTOCAD_GATEWAY_TOKEN", raising=False)
    monkeypatch.delenv("AUTOCAD_GATEWAY_URL", raising=False)
    monkeypatch.delenv("AUTOCAD_GATEWAY_PORT", raising=False)

    (tmp_path / "99999.json").write_text(
        json.dumps({"pid": 99999, "port": 5111, "token": "dead-tok", "started_utc": "2026-11-21T12:00:00Z"}),
        encoding="utf-8",
    )
    (tmp_path / "200.json").write_text(
        json.dumps({"pid": 200, "port": 5120, "token": "tok-200", "started_utc": "2026-11-21T10:00:00.500Z"}),
        encoding="utf-8",
    )
    (tmp_path / "300.json").write_text(
        json.dumps({"pid": 300, "port": 5130, "token": "tok-300", "started_utc": "2026-11-21T10:00:00Z"}),
        encoding="utf-8",
    )
    monkeypatch.setattr("autocad_mcp.client.is_pid_alive", lambda pid: pid in (200, 300))

    # ISO parsing must rank .500Z AFTER plain :00Z rather than failing to lexicographic '.' < 'Z'
    found = discover_live_instance(tmp_path)
    assert found is not None
    assert found["pid"] == 200

    client = AutoCADGatewayClient(instance_dir=tmp_path)
    assert client.base_url == "http://127.0.0.1:5120/"
    assert client.token == "tok-200"

    # Targeting port 5130 without a token must pull tok-300, not tok-200
    monkeypatch.setenv("AUTOCAD_GATEWAY_PORT", "5130")
    port_client = AutoCADGatewayClient(instance_dir=tmp_path)
    assert port_client.base_url == "http://127.0.0.1:5130/"
    assert port_client.token == "tok-300"


def test_gateway_client_environment_overrides_and_validation(tmp_path, monkeypatch):
    monkeypatch.setenv("AUTOCAD_GATEWAY_URL", "http://localhost:5060")
    monkeypatch.setenv("AUTOCAD_GATEWAY_TOKEN", "my-custom-token")
    client = AutoCADGatewayClient(instance_dir=tmp_path)
    assert client.base_url == "http://localhost:5060/"
    assert client.token == "my-custom-token"
    assert client._headers()["X-Gateway-Token"] == "my-custom-token"

    monkeypatch.delenv("AUTOCAD_GATEWAY_URL", raising=False)
    monkeypatch.setenv("AUTOCAD_GATEWAY_PORT", "5070")
    client_port = AutoCADGatewayClient(instance_dir=tmp_path)
    assert client_port.base_url == "http://127.0.0.1:5070/"

    # Invalid port and non-HTTP URL fall back safely
    monkeypatch.setenv("AUTOCAD_GATEWAY_PORT", "not-a-port")
    monkeypatch.setenv("AUTOCAD_GATEWAY_URL", "file:///C:/secret")
    fallback = AutoCADGatewayClient(instance_dir=tmp_path)
    assert fallback.base_url == "http://127.0.0.1:5055/"

    assert AutoCADGatewayClient(port=5080, instance_dir=tmp_path).base_url == "http://127.0.0.1:5080/"
    assert AutoCADGatewayClient(port="5090", instance_dir=tmp_path).base_url == "http://127.0.0.1:5090/"
    assert is_pid_alive(True) is False
    assert is_pid_alive(-1) is False


@pytest.mark.asyncio
async def test_client_http_headers_and_ping_encoding(monkeypatch, tmp_path):
    from autocad_mcp.server import get_surface_elevation, ping_gateway
    import autocad_mcp.server as srv

    called_endpoints = []

    async def mock_get_context(endpoint: str):
        called_endpoints.append(endpoint)
        return '{"status": "success"}'

    monkeypatch.setattr(srv.gateway, "get_context", mock_get_context)

    await ping_gateway()
    await get_surface_elevation(10.0, 20.0, surface_name="EG Topo & 2")
    bad_coord = json.loads(await get_surface_elevation(float("nan"), 20.0))

    assert called_endpoints[0] == "_ping"
    assert called_endpoints[1] == "civil/surface/elevation?x=10.0&y=20.0&surface=EG%20Topo%20%26%202"
    assert bad_coord["status"] == "error"

    # Verify HTTP headers sent over httpx transport (GET omits Content-Type)
    seen = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append((request.method, dict(request.headers), str(request.url)))
        return httpx.Response(200, json={"status": "success", "alive": True})

    real_client = AutoCADGatewayClient(port=5055, token="t-123", instance_dir=tmp_path)
    transport = httpx.MockTransport(handler)
    orig_async_client = httpx.AsyncClient

    def patched_async_client(*args, **kwargs):
        assert kwargs.get("trust_env") is False
        kwargs["transport"] = transport
        return orig_async_client(*args, **kwargs)

    monkeypatch.setattr("autocad_mcp.client.httpx.AsyncClient", patched_async_client)
    await real_client.ping()
    assert seen[0][0] == "GET"
    assert "content-type" not in {k.lower() for k in seen[0][1]}
    assert seen[0][1]["x-gateway-token"] == "t-123"


@pytest.mark.asyncio
async def test_purge_and_clean_alias_parity(monkeypatch):
    from autocad_mcp.server import purge_and_audit_drawing, clean_and_purge_drawing
    import autocad_mcp.server as srv

    executed_commands = []

    async def mock_execute(cmd: str):
        executed_commands.append(cmd)
        return '{"status": "success"}'

    monkeypatch.setattr(srv.gateway, "execute_command", mock_execute)

    await purge_and_audit_drawing(target="all", audit=True)
    assert len(executed_commands) == 1
    assert "(vla-PurgeAll" in executed_commands[0]
    assert "_.AUDIT\n_Y\n" in executed_commands[0]

    executed_commands.clear()
    await clean_and_purge_drawing(target="all", audit=True)
    assert len(executed_commands) == 1
    assert "(vla-PurgeAll" in executed_commands[0]
    assert "_.AUDIT\n_Y\n" in executed_commands[0]


@pytest.mark.asyncio
async def test_evaluate_lisp_uses_sync_bridge_and_clamps_timeout(monkeypatch):
    from autocad_mcp.server import calculate_polygon_area, evaluate_lisp, execute_lisp
    import autocad_mcp.server as srv

    posts = []

    async def mock_post(endpoint: str, data: dict, timeout: float | None = None):
        posts.append((endpoint, data, timeout))
        return '{"status": "success", "value": 42}'

    monkeypatch.setattr(srv.gateway, "post_json", mock_post)

    res = json.loads(await evaluate_lisp("(+ 40 2)", timeout_ms=5))
    assert res["value"] == 42
    assert posts[0] == ("lisp/eval", {"code": "(+ 40 2)", "timeout_ms": 250}, 30.0)

    await execute_lisp("(+ 1 1)", timeout_ms=999999)
    assert posts[1] == ("lisp/eval", {"code": "(+ 1 1)", "timeout_ms": 300000}, 315.0)

    empty_res = json.loads(await evaluate_lisp("   "))
    assert empty_res["status"] == "error"

    poly = json.loads(calculate_polygon_area("0,0; 10,0; 10,5; 0,5"))
    assert poly["status"] == "success"
    assert poly["area_m2"] == pytest.approx(50.0)


@pytest.mark.asyncio
async def test_phase3_native_payloads_and_security_guards(monkeypatch):
    from autocad_mcp.server import (
        draw_batch_geometry,
        draw_line,
        get_system_variable,
        open_drawing,
        query_entities,
        set_system_variable,
    )
    import autocad_mcp.server as srv

    posts = []

    async def mock_post(endpoint: str, data: dict, timeout: float | None = None):
        posts.append((endpoint, data))
        return json.dumps({"status": "success", "ok": True})

    monkeypatch.setattr(srv.gateway, "post_json", mock_post)

    await draw_line(0, 0, 10, 5, layer="WALLS")
    assert posts[0][0] == "entities/create"
    assert posts[0][1]["type"] == "line"
    assert posts[0][1]["layer"] == "WALLS"

    await draw_batch_geometry([{"tipo": "linea", "x1": 0, "y1": 0, "x2": 5, "y2": 0}], default_layer="AXIS")
    assert posts[1][0] == "batch"
    assert posts[1][1]["rollback_on_error"] is True
    assert posts[1][1]["operations"][0]["type"] == "line"

    await query_entities(types="LINE,CIRCLE", min_x=0, min_y=0, max_x=20, max_y=20, limit=50)
    assert posts[2][0] == "entities/query"
    assert posts[2][1]["types"] == ["LINE", "CIRCLE"]
    assert posts[2][1]["bbox"] == [0.0, 0.0, 20.0, 20.0]

    blocked_get = json.loads(await get_system_variable("TRUSTEDPATHS"))
    assert blocked_get["status"] == "error"

    blocked_set = json.loads(await set_system_variable("secureload", 0))
    assert blocked_set["status"] == "error"

    bad_path = json.loads(await open_drawing(r"C:\drawings\..\secret.dwg"))
    assert bad_path["status"] == "error"

    from autocad_mcp.policy import is_valid_entity_handle, parse_string_list
    from autocad_mcp.server import export_pdf, load_lisp_file, set_system_variable, transform_entities

    assert parse_string_list("[]") is None
    assert not is_valid_entity_handle("+1")
    assert not is_valid_entity_handle("0x1A")

    await draw_batch_geometry([{"type": "polyline", "points": [[0, 0, 0.5], [10, 0, 0.0]], "closed": True}])
    assert posts[-1][1]["operations"][0]["points"] == [[0.0, 0.0, 0.5], [10.0, 0.0, 0.0]]

    await set_system_variable("CLAYER", "1")
    assert posts[-1] == ("sysvar", {"name": "CLAYER", "value": "1", "document": None})

    nan_sysvar = json.loads(await set_system_variable("DIMSCALE", float("nan")))
    assert nan_sysvar["status"] == "error"

    bad_mirror = json.loads(await transform_entities("1A", "mirror", p2_x=10.0, p2_y=0.0))
    assert bad_mirror["status"] == "error"

    bad_lisp_path = json.loads(await load_lisp_file(r"\\evil\share\payload.lsp"))
    assert bad_lisp_path["status"] == "error"

    await load_lisp_file(r"C:\scripts\test.lsp")
    assert posts[-1][0] == "lisp/load"
    assert posts[-1][1] == {"file_path": r"C:\scripts\test.lsp", "timeout_ms": 60000}

    bad_pdf = json.loads(await export_pdf("Layout1\n_QUIT", r"C:\out\plan.pdf"))
    assert bad_pdf["status"] == "error"


@pytest.mark.asyncio
async def test_phase4_catalog_and_guide_tools(monkeypatch):
    import autocad_mcp.server as srv

    called = []

    async def mock_get(endpoint: str):
        called.append(endpoint)
        return json.dumps({"status": "success", "endpoint": endpoint})

    monkeypatch.setattr(srv.gateway, "get_context", mock_get)

    res_tools = await mcp.call_tool("get_gateway_tools", {})
    res_guide = await mcp.call_tool("get_gateway_guide", {})
    assert called == ["_tools", "_guide"]
    assert res_tools.isError is False
    assert res_guide.isError is False
    assert res_tools.structuredContent == {"status": "success", "endpoint": "_tools"}
    assert res_guide.structuredContent == {"status": "success", "endpoint": "_guide"}


@pytest.mark.asyncio
async def test_phase5_drafting_blocks_plotting_and_civil3d_tools(monkeypatch):
    from autocad_mcp.server import (
        capture_screenshot,
        create_dimension,
        create_table,
        create_viewport,
        define_block,
        explode_entities,
        export_pdf,
        get_civil3d_surface_stats,
        sample_civil3d_cross_section,
        set_block_attributes,
    )
    import autocad_mcp.server as srv

    posts = []
    gets = []

    async def mock_post(endpoint: str, data: dict, timeout: float | None = None):
        posts.append((endpoint, data, timeout))
        return json.dumps({"status": "success", "ok": True})

    async def mock_get(endpoint: str):
        gets.append(endpoint)
        return json.dumps({"status": "success", "ok": True})

    monkeypatch.setattr(srv.gateway, "post_json", mock_post)
    monkeypatch.setattr(srv.gateway, "get_context", mock_get)

    await create_dimension("aligned", x1=0, y1=0, x2=10, y2=0, dim_line_x=5, dim_line_y=2, layer="ANNO")
    assert posts[-1][0] == "drafting/dimension"
    assert posts[-1][1]["type"] == "aligned"
    assert posts[-1][1]["dim_line_point"] == [5.0, 2.0, 0.0]

    await create_table(0, 0, rows=2, cols=2, cells_json=[["A", "1"], ["B", "2"]], headers_json=["Key", "Val"])
    assert posts[-1][0] == "drafting/table"
    assert posts[-1][1]["rows"] == 2
    assert posts[-1][1]["headers"] == ["Key", "Val"]

    bad_table = json.loads(await create_table(0, 0, rows=250, cols=2, cells_json=[["x"]]))
    assert bad_table["status"] == "error"

    await define_block(
        "VALVE",
        operations_json=[{"type": "circle", "center": [0, 0], "radius": 0.5}],
        attributes_json=[{"tag": "ID", "prompt": "Valve ID", "default": "V-01"}],
    )
    assert posts[-1][0] == "blocks/define"
    assert posts[-1][1]["name"] == "VALVE"
    assert len(posts[-1][1]["operations"]) == 1

    await set_block_attributes("1A", {"ID": "V-99"})
    assert posts[-1][0] == "blocks/attributes"
    assert posts[-1][1]["handle"] == "1A"

    bad_explode = json.loads(await explode_entities("ZZZZ"))
    assert bad_explode["status"] == "error"

    await explode_entities("1A,2B", erase_source=False)
    assert posts[-1][0] == "entities/explode"
    assert posts[-1][1]["handles"] == ["1A", "2B"]
    assert posts[-1][1]["erase_source"] is False

    await create_viewport(10, 10, 200, 150, 500, 1000, 2000, layout_name="SHEET-01")
    assert posts[-1][0] == "plotting/viewport"
    assert posts[-1][1]["scale"] == 500
    assert posts[-1][1]["layout_name"] == "SHEET-01"

    await export_pdf("SHEET-01", r"C:\out\sheet01.pdf")
    assert posts[-1][0] == "plotting/pdf"
    assert posts[-1][1]["pdf_path"] == r"C:\out\sheet01.pdf"
    assert posts[-1][2] == 60.0

    bad_pdf_trav = json.loads(await export_pdf("SHEET-01", r"C:\out\..\secret.pdf"))
    assert bad_pdf_trav["status"] == "error"

    await capture_screenshot(inline=True)
    assert gets[-1] == "context/screenshot?inline=true"

    await get_civil3d_surface_stats("EG Terrain")
    assert gets[-1] == "civil/surface/stats?surface=EG%20Terrain"

    await sample_civil3d_cross_section(100.0, left_width=15.0, right_width=15.0, step=3.0, alignment_name="Axis 1", surface_name="EG")
    assert gets[-1] == "civil/cross-section?station=100.0&left_width=15.0&right_width=15.0&step=3.0&alignment=Axis%201&surface=EG"

    bad_xs = json.loads(await sample_civil3d_cross_section(100.0, left_width=0.0, right_width=0.0))
    assert bad_xs["status"] == "error"

    # Thermos parity & boundary regressions
    from autocad_mcp.server import (
        configure_layout_page,
        create_hatch,
        create_layout,
        draw_batch_geometry,
        insert_title_block,
        sample_longitudinal_profile,
    )

    bad_handles_syntax = json.loads(await explode_entities("[bad"))
    assert bad_handles_syntax["status"] == "error"

    empty_hatch_handles = json.loads(await create_hatch(handles=[]))
    assert empty_hatch_handles["status"] == "error"

    both_hatch_inputs = json.loads(await create_hatch(handles=["1A"], points="0,0;5,0;5,5"))
    assert both_hatch_inputs["status"] == "error"

    partial_dim = json.loads(await create_dimension("aligned", x1=0, y1=0, x2=10, y2=0, dim_line_x=5.0))
    assert partial_dim["status"] == "error"

    await create_table(0, 0, rows=1, cols=2, cells_json=[[123, True]])
    assert posts[-1][1]["cells"] == [[123, True]]

    bad_plot_style = json.loads(await export_pdf("SHEET-01", r"C:\out\s.pdf", plot_style="~"))
    assert bad_plot_style["status"] == "error"

    bad_model_pdf = json.loads(await export_pdf("Model", r"C:\out\s.pdf"))
    assert bad_model_pdf["status"] == "error"

    bad_paper = json.loads(await configure_layout_page("SHEET-01", paper_size="LETTER"))
    assert bad_paper["status"] == "error"

    bad_layout_nl = json.loads(await create_layout("SHEET\n_.QUIT"))
    assert bad_layout_nl["status"] == "error"

    bad_title_nl = json.loads(await insert_title_block(0, 0, project_title="PROJ\n_LINE"))
    assert bad_title_nl["status"] == "error"

    await draw_batch_geometry([{"type": "polyline", "points": [{"x": 0, "y": 0}, {"x": 5, "y": 5}], "closed": "false"}])
    assert posts[-1][1]["operations"][0]["closed"] is False

    zero_line_batch = json.loads(await draw_batch_geometry([{"type": "line", "x1": 1, "y1": 1, "x2": 1, "y2": 1}]))
    assert zero_line_batch["status"] == "error"

    await sample_longitudinal_profile(interval=10.0, alignment_name="Main", surface_name="EG")
    assert gets[-1] == "civil/profile?interval=10.0&alignment=Main&surface=EG"


@pytest.mark.asyncio
async def test_phase6_security_regressions(tmp_path, monkeypatch):
    from autocad_mcp.client import MAX_DESCRIPTOR_BYTES
    from autocad_mcp.policy import is_blocked_sysvar, validate_local_file_path
    from autocad_mcp.server import execute_command

    assert validate_local_file_path(r"C:\out\CON.pdf", (".pdf",))[0] is False
    assert validate_local_file_path(r"C:\out\plan.dwg.", (".dwg",))[0] is False
    assert is_blocked_sysvar("_.secureload") is True
    assert is_blocked_sysvar("LOGFILENAME") is True

    monkeypatch.delenv("AUTOCAD_GATEWAY_TOKEN", raising=False)
    monkeypatch.delenv("AUTOCAD_GATEWAY_URL", raising=False)
    monkeypatch.delenv("AUTOCAD_GATEWAY_PORT", raising=False)
    client = AutoCADGatewayClient(base_url="http://evil.example:5055", instance_dir=tmp_path)
    assert client.base_url == "http://127.0.0.1:5055/"
    assert AutoCADGatewayClient(base_url="http://127.0.0.1:99999", instance_dir=tmp_path).base_url == "http://127.0.0.1:5055/"
    assert AutoCADGatewayClient(base_url="http://127.0.0.1:0", instance_dir=tmp_path).base_url == "http://127.0.0.1:5055/"

    bad_nul = json.loads(await execute_command("LINE\x00"))
    assert bad_nul["status"] == "error"

    oversized_pad = "x" * (MAX_DESCRIPTOR_BYTES + 256)
    (tmp_path / "4242.json").write_text(
        json.dumps({"pid": 4242, "port": 5055, "token": "tok", "pad": oversized_pad}),
        encoding="utf-8",
    )
    monkeypatch.setattr("autocad_mcp.client.is_pid_alive", lambda _: True)
    assert discover_live_instance(tmp_path) is None


@pytest.mark.asyncio
async def test_phase6_parity_and_payload_regressions(monkeypatch):
    from autocad_mcp.cad_math import build_batch_geometry_lisp
    from autocad_mcp.server import configure_layout_page, create_dimension, export_pdf, modify_entity
    import autocad_mcp.server as srv

    posts = []
    cmds = []

    async def mock_post(endpoint: str, data: dict, timeout: float | None = None):
        posts.append((endpoint, data))
        return '{"status": "success"}'

    async def mock_exec(cmd: str):
        cmds.append(cmd)
        return "Success"

    monkeypatch.setattr(srv.gateway, "post_json", mock_post)
    monkeypatch.setattr(srv.gateway, "execute_command", mock_exec)

    await modify_entity("1A", layer="WALLS")
    assert posts[-1][0] == "entities/modify"
    assert posts[-1][1] == {"handle": "1A", "layer": "WALLS"}

    bad_mod = json.loads(await modify_entity("1A", layer="BAD/LAYER"))
    assert bad_mod["status"] == "error"
    assert len(posts) == 1

    bad_ang = json.loads(await create_dimension("angular", x1=1, y1=0, x2=0, y2=1))
    assert bad_ang["status"] == "error"

    await export_pdf("Layout1", r"C:\Temp\out.pdf", expected_revision=3, document="Drawing1.dwg")
    assert posts[-1][0] == "plotting/pdf"
    assert posts[-1][1]["expected_revision"] == 3
    assert posts[-1][1]["document"] == "Drawing1.dwg"

    await configure_layout_page("Layout1", paper_size="A3", orientation="landscape")
    assert posts[-1][0] == "plotting/page-setup"
    assert posts[-1][1]["paper_size"] == "A3"
    assert posts[-1][1]["orientation"] == "landscape"

    lisp_code, summary = build_batch_geometry_lisp(
        [{"type": "polyline", "points": [[0, 0, 0.5], [10, 0]], "closed": False}]
    )
    assert summary["total"] == 1
    assert "(cons 42 0.5)" in lisp_code


@pytest.mark.asyncio
async def test_v250_security_hardening_regressions():
    from autocad_mcp.compound import dispatch_compound_action
    from autocad_mcp.policy import parse_json_value, parse_string_list_strict
    from autocad_mcp.server import configure_layout_page, create_layout, create_table, execute_batch_commands, export_pdf
    from autocad_mcp.tool_mode import catalog

    nan_list, nan_err = parse_json_value("[NaN, Infinity]", list, "pts")
    assert nan_list is None and nan_err is not None

    mixed_list, mixed_err = parse_string_list_strict("[true, null, 123]")
    assert mixed_list is None and mixed_err is not None

    deep_json = '{"a":' * 1500 + "1" + "}" * 1500
    deep_res = json.loads(await dispatch_compound_action(catalog, "drawing", "draw_line", deep_json))
    assert deep_res["status"] == "error"

    unc_plot = json.loads(await export_pdf("Layout1", r"C:\Temp\out.pdf", plot_style=r"\\evil\share\x.ctb"))
    assert unc_plot["status"] == "error"

    pause_plot = json.loads(await export_pdf("Layout1", r"C:\Temp\out.pdf", plot_style="\\"))
    assert pause_plot["status"] == "error"

    model_layout = json.loads(await create_layout("Model"))
    assert model_layout["status"] == "error"
    assert model_layout["ok"] is False
    assert model_layout["error"]["code"] == "bad_argument"

    prompt_layout = json.loads(await configure_layout_page("?", plot_style="monochrome.ctb"))
    assert prompt_layout["status"] == "error"

    non_str_cmds = json.loads(await execute_batch_commands("[123]"))
    assert non_str_cmds["status"] == "error"

    oversized_cells = json.loads(await create_table(0, 0, rows=1, cols=1, cells_json=[["a", "b"]]))
    assert oversized_cells["status"] == "error"


@pytest.mark.asyncio
async def test_v260_native_tools_and_unified_outputs(monkeypatch):
    from autocad_mcp.server import (
        configure_layout_page,
        create_layout,
        create_viewport,
        export_pdf,
        insert_raster_image,
        insert_title_block,
        regen_all,
        save_drawing,
        set_layer_state,
    )
    import autocad_mcp.server as srv

    posts = []

    async def mock_post(endpoint: str, data: dict, timeout: float | None = None):
        posts.append((endpoint, data, timeout))
        return json.dumps({"status": "success", "ok": True, "endpoint": endpoint})

    monkeypatch.setattr(srv.gateway, "post_json", mock_post)

    await create_layout("SHEET 01", clear_default_viewport=True, activate=False)
    assert posts[-1][0] == "plotting/layout"
    assert posts[-1][1]["layout_name"] == "SHEET 01"
    assert posts[-1][1]["clear_default_viewport"] is True
    assert posts[-1][1]["activate"] is False

    await configure_layout_page("SHEET 01", paper_size="A3", orientation="portrait")
    assert posts[-1][0] == "plotting/page-setup"
    assert posts[-1][1]["orientation"] == "portrait"

    await create_viewport(
        10, 10, 200, 150, 500, 100, 200,
        center_model_z=3200.0,
        view_direction="sw_iso",
        shade_plot="hidden",
        visual_style="Realistic",
        lock_viewport=True,
    )
    assert posts[-1][0] == "plotting/viewport"
    assert posts[-1][1]["center_model_z"] == 3200.0
    assert posts[-1][1]["view_direction"] == "sw_iso"
    assert posts[-1][1]["shade_plot"] == "hidden"

    await insert_raster_image(r"C:\assets\logo.png", 10, 20, width=25, image_frame=0, space="paper", layout_name="SHEET 01")
    assert posts[-1][0] == "drafting/raster-image"
    assert posts[-1][1]["width"] == 25
    assert posts[-1][1]["image_frame"] == 0

    await insert_title_block(400, 10, layout_name="SHEET 01", template="detailed", logo_path=r"C:\assets\logo.png")
    assert posts[-1][0] == "plotting/title-block"
    assert posts[-1][1]["template"] == "detailed"
    assert posts[-1][1]["logo_path"] == r"C:\assets\logo.png"

    await export_pdf("SHEET 01", r"C:\out\a3.pdf", paper_size="A3", orientation="landscape")
    assert posts[-1][0] == "plotting/pdf"
    assert posts[-1][1]["paper_size"] == "A3"
    assert posts[-1][1]["orientation"] == "landscape"

    await save_drawing(r"C:\out\saved.dwg", overwrite=True, dwg_version="2018")
    assert posts[-1][0] == "documents/save"
    assert posts[-1][1]["file_path"] == r"C:\out\saved.dwg"
    assert posts[-1][1]["overwrite"] is True

    await set_layer_state("AXIS", color_aci=1, linetype="DASHED", lineweight="0.35", is_plottable=False)
    assert posts[-1][0] == "layers/state"
    assert posts[-1][1]["color_index"] == 1
    assert posts[-1][1]["lineweight"] == "0.35"
    assert posts[-1][1]["is_plottable"] is False

    await regen_all(document="Plan.dwg")
    assert posts[-1][0] == "session/regen"
    assert posts[-1][1]["document"] == "Plan.dwg"

    client = AutoCADGatewayClient(port=5055, token="t")
    exec_res = json.loads(
        client._error_envelope("gateway_unreachable", "fail", hint="retry")
    )
    assert exec_res["ok"] is False
    assert exec_res["error"]["code"] == "gateway_unreachable"
    assert exec_res["error"]["hint"] == "retry"

    bad_dir = json.loads(await create_viewport(10, 10, 200, 150, 500, 100, 200, view_direction="diagonal"))
    assert bad_dir["status"] == "error"
    assert bad_dir["ok"] is False

    bad_lt = json.loads(await set_layer_state("AXIS", linetype="ByLayer"))
    assert bad_lt["status"] == "error"
    assert bad_lt["ok"] is False

    # Empty lineweight is treated as omitted rather than failing Gateway validation.
    await set_layer_state("AXIS", action="on", lineweight="")
    assert "lineweight" not in posts[-1][1]

    # Omitting space when layout_name is supplied infers paper space automatically.
    await insert_raster_image(r"C:\assets\logo.png", 10, 20, layout_name="SHEET 01")
    assert posts[-1][1]["space"] == "paper"
