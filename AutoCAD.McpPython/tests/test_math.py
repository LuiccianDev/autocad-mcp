"""Pruebas unitarias para utilidades matemáticas y analíticas de autocad-mcp."""

from autocad_mcp.cad_math import (
    calcular_area_poligono_analitico,
    is_valid_symbol_name,
    lisp_string,
    medir_distancia_analitica,
    num,
    parse_points,
)


def test_is_valid_symbol_name():
    assert is_valid_symbol_name("ARQ-MUROS") is True
    assert is_valid_symbol_name("TUNEL_01") is True
    assert is_valid_symbol_name("") is False
    assert is_valid_symbol_name("   ") is False
    assert is_valid_symbol_name("CAPA*CON*ASTERISCO") is False
    assert is_valid_symbol_name("CAPA/CON/SLASH") is False


def test_lisp_string():
    assert lisp_string("MUROS") == '"MUROS"'
    assert lisp_string('TEXTO "ENTRE COMILLAS"') == '"TEXTO \\"ENTRE COMILLAS\\""'
    assert lisp_string("LINEA1\nLINEA2") == '"LINEA1 LINEA2"'


def test_num_formatting():
    assert num(0.0) == "0.0"
    assert num(25.5) == "25.5"
    assert num(100.0) == "100.0"


def test_parse_points():
    pts2 = parse_points("0,0;10,0;10,5", 2)
    assert pts2 == [[0.0, 0.0], [10.0, 0.0], [10.0, 5.0]]

    pts3 = parse_points("0,0,100;10,0,105", 3)
    assert pts3 == [[0.0, 0.0, 100.0], [10.0, 0.0, 105.0]]

    assert parse_points("invalido", 2) is None
    assert parse_points("1,2;3", 2) is None


def test_medir_distancia_analitica():
    # Triángulo rectángulo 3-4-5 con Z=12 (hipotenusa 3D = 13)
    res = medir_distancia_analitica(0.0, 0.0, 3.0, 4.0, 0.0, 12.0)
    assert res["status"] == "success"
    assert res["distancia_2d"] == 5.0
    assert res["distancia_3d"] == 13.0
    assert res["delta_x"] == 3.0
    assert res["delta_y"] == 4.0
    assert res["delta_z"] == 12.0
    assert round(res["angulo_grados"], 2) == 53.13
    assert res["pendiente_porcentaje"] == 240.0


def test_calcular_area_poligono_analitico():
    # Rectángulo 10x5 metros en coordenadas UTM grandes
    pts_str = "500000,8000000;500010,8000000;500010,8000005;500000,8000005"
    res = calcular_area_poligono_analitico(pts_str)
    assert res["status"] == "success"
    assert res["area_m2"] == 50.0
    assert res["area_hectareas"] == 0.005
    assert res["perimetro_m"] == 30.0
    assert res["centroide"]["x"] == 500005.0
    assert res["centroide"]["y"] == 8000002.5
    assert res["sentido"] == "antihorario"
    assert res["vertices"] == 4
