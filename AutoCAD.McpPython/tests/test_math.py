"""Unit tests for mathematical and analytical CAD utilities."""

from autocad_mcp.cad_math import (
    calculate_distance,
    calculate_polygon_area,
    is_valid_symbol_name,
    lisp_string,
    num,
    parse_points,
)


def test_is_valid_symbol_name():
    assert is_valid_symbol_name("ARQ-MUROS") is True
    assert is_valid_symbol_name("TUNEL_01") is True
    assert is_valid_symbol_name("") is False
    assert is_valid_symbol_name("   ") is False
    assert is_valid_symbol_name("LAYER*WITH*ASTERISK") is False
    assert is_valid_symbol_name("LAYER/WITH/SLASH") is False


def test_lisp_string():
    assert lisp_string("WALLS") == '"WALLS"'
    assert lisp_string('TEXT "WITH QUOTES"') == '"TEXT \\"WITH QUOTES\\""'
    assert lisp_string("LINE1\nLINE2") == '"LINE1 LINE2"'


def test_num_formatting():
    assert num(0.0) == "0.0"
    assert num(25.5) == "25.5"
    assert num(100.0) == "100.0"


def test_parse_points():
    pts2 = parse_points("0,0;10,0;10,5", 2)
    assert pts2 == [[0.0, 0.0], [10.0, 0.0], [10.0, 5.0]]

    pts3 = parse_points("0,0,100;10,0,105", 3)
    assert pts3 == [[0.0, 0.0, 100.0], [10.0, 0.0, 105.0]]

    assert parse_points("invalid", 2) is None
    assert parse_points("1,2;3", 2) is None


def test_calculate_distance():
    # 3-4-5 right triangle with Z=12 (3D hypotenuse = 13)
    res = calculate_distance(0.0, 0.0, 3.0, 4.0, 0.0, 12.0)
    assert res["status"] == "success"
    assert res["distance_2d"] == 5.0
    assert res["distance_3d"] == 13.0
    assert res["delta_x"] == 3.0
    assert res["delta_y"] == 4.0
    assert res["delta_z"] == 12.0
    assert round(res["angle_degrees"], 2) == 53.13
    assert res["slope_percent"] == 240.0
    assert res["units"] == "meters"


def test_calculate_polygon_area():
    # 10x5m rectangle in large UTM coordinates
    pts_str = "500000,8000000;500010,8000000;500010,8000005;500000,8000005"
    res = calculate_polygon_area(pts_str)
    assert res["status"] == "success"
    assert res["area_m2"] == 50.0
    assert res["area_hectares"] == 0.005
    assert res["perimeter_m"] == 30.0
    assert res["centroid"]["x"] == 500005.0
    assert res["centroid"]["y"] == 8000002.5
    assert res["is_clockwise"] is False
    assert res["orientation"] == "counterclockwise"
    assert res["vertices"] == 4

