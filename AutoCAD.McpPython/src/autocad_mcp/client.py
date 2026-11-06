"""Cliente HTTP asíncrono para interactuar con la API REST de AutoCAD AI Gateway (puerto 5055)."""

from __future__ import annotations

import json
import os
from typing import Any
import httpx


class AutoCADGatewayClient:
    """Cliente HTTP tipado para el AutoCAD AI Gateway."""

    def __init__(
        self,
        port: int | str | None = None,
        base_url: str | None = None,
        token: str | None = None,
        timeout: float = 15.0,
    ) -> None:
        if base_url:
            raw_url = base_url
        else:
            resolved_port = port or os.getenv("AUTOCAD_GATEWAY_PORT")
            if resolved_port is not None and str(resolved_port).strip():
                raw_url = f"http://localhost:{str(resolved_port).strip()}/"
            else:
                env_url = os.getenv("AUTOCAD_GATEWAY_URL")
                raw_url = env_url if env_url else "http://localhost:5055/"

        self.base_url = raw_url.rstrip("/") + "/"
        self.token = token or os.getenv("AUTOCAD_GATEWAY_TOKEN", "luiccian-autocad-token-2027")
        self.timeout = timeout

    def _headers(self) -> dict[str, str]:
        return {
            "X-Gateway-Token": self.token,
            "Content-Type": "application/json",
        }

    async def get_context(self, endpoint: str) -> str:
        """Consulta un endpoint de lectura de contexto (ej. 'context/layers')."""
        url = self.base_url + endpoint.lstrip("/")
        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                res = await client.get(url, headers=self._headers())
                if res.is_success or res.text:
                    return res.text
                return json.dumps(
                    {"status": "error", "code": res.status_code, "message": f"Gateway devolvió error {res.status_code}"}
                )
        except httpx.ConnectError:
            return json.dumps(
                {
                    "status": "error",
                    "message": "No se pudo conectar con AutoCAD AI Gateway en el puerto 5055. "
                    "Verifica que AutoCAD esté abierto y que hayas cargado la extensión con NETLOAD.",
                }
            )
        except Exception as ex:
            return json.dumps({"status": "error", "message": f"Error de comunicación con el Gateway: {ex}"})

    async def execute_command(self, command: str) -> str:
        """Inyecta un comando de dibujo o AutoLISP en AutoCAD mediante /execute."""
        url = self.base_url + "execute"
        payload = {"command": command}
        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                res = await client.post(url, headers=self._headers(), json=payload)
                if res.is_success:
                    return "Éxito: El comando se inyectó correctamente en AutoCAD."
                return f"Error del Gateway ({res.status_code}): {res.text}"
        except httpx.ConnectError:
            return (
                "Fallo crítico de conexión con el Gateway de AutoCAD. "
                "Asegúrate de que AutoCAD esté abierto y escuchando en el puerto 5055."
            )
        except Exception as ex:
            return f"Error al ejecutar comando en AutoCAD: {ex}"

    async def post_json(self, endpoint: str, data: dict[str, Any]) -> str:
        """Envía un cuerpo JSON por POST a un endpoint del Gateway (ej. /civil/alignment/create)."""
        url = self.base_url + endpoint.lstrip("/")
        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                res = await client.post(url, headers=self._headers(), json=data)
                return res.text
        except httpx.ConnectError:
            return json.dumps(
                {"status": "error", "message": "No se pudo conectar con el Gateway en el puerto 5055."}
            )
        except Exception as ex:
            return json.dumps({"status": "error", "message": f"Fallo al invocar {endpoint}: {ex}"})

    async def ping(self) -> str:
        """Envía una señal de prueba rápida para diagnosticar la conectividad."""
        url = self.base_url + "execute"
        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                res = await client.post(url, headers=self._headers(), json={"command": " \n"})
                if res.is_success:
                    return "¡Conexión exitosa con el AutoCAD AI Gateway!"
                return f"Conectado al Gateway, pero devolvió estado: {res.status_code}"
        except Exception as ex:
            return f"Fallo al conectar con el Gateway de AutoCAD (puerto 5055): {ex}"
