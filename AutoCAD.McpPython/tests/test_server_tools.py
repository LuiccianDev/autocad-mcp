"""Tool introspection tests and Gateway network configuration validation."""

import os
import pytest
from autocad_mcp.client import AutoCADGatewayClient
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


@pytest.mark.asyncio
async def test_tool_count_and_names():
    tools = await mcp.list_tools()
    tool_names = [t.name for t in tools]

    assert len(tool_names) == 45, f"Expected 45 tools, found {len(tool_names)}: {tool_names}"

    for expected in EXPECTED_TOOLS:
        assert expected in tool_names, f"Missing expected tool: {expected}"


def test_gateway_client_default_configuration():
    old_token = os.environ.pop("AUTOCAD_GATEWAY_TOKEN", None)
    old_url = os.environ.pop("AUTOCAD_GATEWAY_URL", None)
    old_port = os.environ.pop("AUTOCAD_GATEWAY_PORT", None)

    try:
        client = AutoCADGatewayClient()
        assert client.base_url == "http://localhost:5055/"
        assert client.token == "luiccian-autocad-token-2027"
        headers = client._headers()
        assert headers["X-Gateway-Token"] == "luiccian-autocad-token-2027"
        assert headers["Content-Type"] == "application/json"
    finally:
        if old_token:
            os.environ["AUTOCAD_GATEWAY_TOKEN"] = old_token
        if old_url:
            os.environ["AUTOCAD_GATEWAY_URL"] = old_url
        if old_port:
            os.environ["AUTOCAD_GATEWAY_PORT"] = old_port


def test_gateway_client_environment_overrides():
    old_token = os.environ.get("AUTOCAD_GATEWAY_TOKEN")
    old_url = os.environ.get("AUTOCAD_GATEWAY_URL")
    old_port = os.environ.get("AUTOCAD_GATEWAY_PORT")

    try:
        os.environ["AUTOCAD_GATEWAY_URL"] = "http://localhost:5060"
        os.environ["AUTOCAD_GATEWAY_TOKEN"] = "my-custom-token"
        client = AutoCADGatewayClient()
        assert client.base_url == "http://localhost:5060/"
        assert client.token == "my-custom-token"
        assert client._headers()["X-Gateway-Token"] == "my-custom-token"

        del os.environ["AUTOCAD_GATEWAY_URL"]
        os.environ["AUTOCAD_GATEWAY_PORT"] = "5070"
        client_port = AutoCADGatewayClient()
        assert client_port.base_url == "http://localhost:5070/"

        client_arg_int = AutoCADGatewayClient(port=5080)
        assert client_arg_int.base_url == "http://localhost:5080/"

        client_arg_str = AutoCADGatewayClient(port="5090")
        assert client_arg_str.base_url == "http://localhost:5090/"
    finally:
        for k, v in [
            ("AUTOCAD_GATEWAY_TOKEN", old_token),
            ("AUTOCAD_GATEWAY_URL", old_url),
            ("AUTOCAD_GATEWAY_PORT", old_port),
        ]:
            if v is not None:
                os.environ[k] = v
            else:
                os.environ.pop(k, None)
