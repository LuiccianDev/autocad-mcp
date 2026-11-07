"""Pruebas de introspección de herramientas y validación de configuración de red del Gateway."""

import os
import pytest
from autocad_mcp.client import AutoCADGatewayClient
from autocad_mcp.server import mcp

EXPECTED_TOOLS = [
    # Módulo 1 (8)
    "obtener_info_dibujo", "leer_capas_autocad", "leer_objetos_autocad", "obtener_limites_dibujo",
    "leer_textos_autocad", "listar_bloques_disponibles", "leer_seleccion_autocad", "capturar_pantalla_autocad",
    # Módulo 2 (8)
    "dibujar_geometria_lote", "dibujar_linea", "dibujar_polilinea", "dibujar_rectangulo", "dibujar_circulo",
    "dibujar_arco", "insertar_texto", "insertar_bloque",
    # Módulo 3 (3)
    "crear_capa", "establecer_capa_actual", "establecer_estado_capa",
    # Módulo 4 (4)
    "medir_distancia", "calcular_area_poligono", "limpiar_y_purgar_dibujo", "regenerar_todo",
    # Módulo 5 (8)
    "ping_autocad_gateway", "ejecutar_comandos_lote", "ejecutar_comando_autocad", "ejecutar_lisp", "cargar_archivo_lisp",
    "configurar_entorno_dibujo", "zoom_extension", "guardar_dibujo",
    # Módulo 6 (5)
    "crear_layout", "configurar_hoja_layout", "crear_viewport", "insertar_cajetin_parametrico", "exportar_pdf",
    # Módulo 7 (9)
    "verificar_estado_civil3d", "listar_superficies_civil3d", "consultar_elevacion_terreno",
    "listar_alineamientos_civil3d", "consultar_estacion_alineamiento", "obtener_coordenada_en_estacion",
    "consultar_perfil_longitudinal", "crear_alineamiento_civil3d", "crear_superficie_civil3d",
]


@pytest.mark.asyncio
async def test_tool_count_and_names():
    tools = await mcp.list_tools()
    tool_names = [t.name for t in tools]

    assert len(tool_names) == 45, f"Se esperaban 45 herramientas pero se encontraron {len(tool_names)}: {tool_names}"

    for expected in EXPECTED_TOOLS:
        assert expected in tool_names, f"Falta la herramienta esperada: {expected}"


def test_gateway_client_default_configuration():
    # Asegurar que sin variables de entorno se usan los defaults oficiales
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
        # Test con AUTOCAD_GATEWAY_URL personalizada
        os.environ["AUTOCAD_GATEWAY_URL"] = "http://localhost:5060"
        os.environ["AUTOCAD_GATEWAY_TOKEN"] = "mi-token-personalizado"
        client = AutoCADGatewayClient()
        assert client.base_url == "http://localhost:5060/"
        assert client.token == "mi-token-personalizado"
        assert client._headers()["X-Gateway-Token"] == "mi-token-personalizado"

        # Test con AUTOCAD_GATEWAY_PORT
        del os.environ["AUTOCAD_GATEWAY_URL"]
        os.environ["AUTOCAD_GATEWAY_PORT"] = "5070"
        client_port = AutoCADGatewayClient()
        assert client_port.base_url == "http://localhost:5070/"

        # Test pasando el puerto como argumento directo al constructor
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
