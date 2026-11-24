"""Asynchronous HTTP client for AutoCAD AI Gateway with live instance auto-discovery."""

from __future__ import annotations

import asyncio
import ctypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import stat as stat_mod
import sys
import time
from typing import Any
from urllib.parse import urlparse
import httpx

DEFAULT_PORT = 5055
TOKEN_HEADER = "X-Gateway-Token"
MAX_DESCRIPTOR_BYTES = 16 * 1024
STILL_ACTIVE = 259
WAIT_TIMEOUT = 0x00000102
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
PROCESS_SYNCHRONIZE = 0x00100000

if sys.platform == "win32":
    from ctypes import wintypes

    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    _kernel32.OpenProcess.restype = wintypes.HANDLE
    _kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    _kernel32.GetExitCodeProcess.restype = wintypes.BOOL
    _kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    _kernel32.WaitForSingleObject.restype = wintypes.DWORD
    _kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    _kernel32.CloseHandle.restype = wintypes.BOOL
else:
    _kernel32 = None


def get_instance_directory() -> Path:
    """Return the directory storing Gateway {pid}.json instance descriptors."""
    override = os.getenv("AUTOCAD_GATEWAY_INSTANCE_DIR")
    if override and override.strip():
        return Path(override.strip())

    if sys.platform == "win32":
        local_app_data = os.getenv("LOCALAPPDATA")
        if local_app_data and local_app_data.strip():
            return Path(local_app_data.strip()) / "AutoCAD.Gateway" / "instances"
        return Path.home() / "AppData" / "Local" / "AutoCAD.Gateway" / "instances"

    xdg_data = os.getenv("XDG_DATA_HOME")
    base = Path(xdg_data.strip()) if xdg_data and xdg_data.strip() else Path.home() / ".local" / "share"
    return base / "AutoCAD.Gateway" / "instances"


def is_pid_alive(pid: int) -> bool:
    """Return True if an OS process with the given PID appears alive."""
    if isinstance(pid, bool) or not isinstance(pid, int) or pid <= 0:
        return False

    if sys.platform == "win32" and _kernel32 is not None:
        handle = _kernel32.OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SYNCHRONIZE, False, pid
        )
        if not handle:
            err = ctypes.get_last_error()
            # ERROR_ACCESS_DENIED (5) proves the process exists under another privilege level.
            return err == 5
        try:
            wait = _kernel32.WaitForSingleObject(handle, 0)
            if wait == WAIT_TIMEOUT:
                return True
            exit_code = wintypes.DWORD(0)
            if _kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                return exit_code.value == STILL_ACTIVE
            return False
        finally:
            _kernel32.CloseHandle(handle)

    try:
        os.kill(pid, 0)
        return True
    except PermissionError:
        return True
    except OSError:
        return False


def _parse_started_utc(raw: Any, fallback_mtime: float) -> datetime:
    if isinstance(raw, str) and raw.strip():
        text = raw.strip()
        if text.endswith("Z"):
            text = text[:-1] + "+00:00"
        try:
            parsed = datetime.fromisoformat(text)
            if parsed.tzinfo is None:
                parsed = parsed.replace(tzinfo=timezone.utc)
            return parsed.astimezone(timezone.utc)
        except ValueError:
            pass
    return datetime.fromtimestamp(fallback_mtime, tz=timezone.utc)


def _valid_port(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value if 1 <= value <= 65535 else None
    if isinstance(value, str):
        text = value.strip()
        if text.isascii() and text.isdigit():
            parsed = int(text)
            return parsed if 1 <= parsed <= 65535 else None
    return None


def _is_reparse_point(path: Path) -> bool:
    try:
        if path.is_symlink():
            return True
        st = os.lstat(path)
        attrs = getattr(st, "st_file_attributes", 0)
        reparse_flag = getattr(stat_mod, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
        return bool(attrs & reparse_flag)
    except OSError:
        return True


def _normalize_http_url(raw: str | None) -> tuple[str, int | None] | None:
    if not raw or not raw.strip():
        return None
    candidate = raw.strip()
    try:
        parsed = urlparse(candidate)
        port = parsed.port
        host = (parsed.hostname or "").lower()
    except ValueError:
        return None
    if parsed.scheme.lower() not in ("http", "https") or not parsed.netloc:
        return None
    if parsed.params or parsed.query or parsed.fragment or parsed.username or parsed.password:
        return None
    if host not in ("127.0.0.1", "localhost", "::1"):
        return None
    if port is None:
        port = 443 if parsed.scheme.lower() == "https" else 80
    elif not (1 <= port <= 65535):
        return None
    return candidate.rstrip("/") + "/", port


def discover_live_instance(
    instance_dir: str | Path | None = None,
    pid_alive_fn: Any = None,
    target_port: int | None = None,
) -> dict[str, Any] | None:
    """Return the newest valid live Gateway instance descriptor (optionally filtered by port)."""
    directory = Path(instance_dir) if instance_dir is not None else get_instance_directory()
    try:
        if not directory.exists() or not directory.is_dir() or _is_reparse_point(directory):
            return None
        entries = list(directory.glob("*.json"))
    except OSError:
        return None

    probe = pid_alive_fn or is_pid_alive
    candidates: list[tuple[datetime, dict[str, Any]]] = []

    for path in entries:
        if not (path.stem.isascii() and path.stem.isdigit()) or _is_reparse_point(path):
            continue
        pid = int(path.stem)
        if not probe(pid):
            continue

        try:
            stat_res = os.lstat(path)
            if stat_res.st_size > MAX_DESCRIPTOR_BYTES:
                continue
            with path.open("r", encoding="utf-8") as fh:
                raw_text = fh.read(MAX_DESCRIPTOR_BYTES + 1)
            if len(raw_text.encode("utf-8")) > MAX_DESCRIPTOR_BYTES:
                continue
            data = json.loads(raw_text)
        except (OSError, UnicodeDecodeError, json.JSONDecodeError):
            continue

        if not isinstance(data, dict):
            continue
        raw_pid = data.get("pid")
        if isinstance(raw_pid, bool) or raw_pid != pid:
            continue
        port = _valid_port(data.get("port"))
        token = data.get("token")
        if port is None or not isinstance(token, str) or not token:
            continue

        started = _parse_started_utc(data.get("started_utc"), stat_res.st_mtime)
        candidates.append((started, data))

    if not candidates:
        return None

    candidates.sort(key=lambda item: item[0], reverse=True)
    if target_port is not None:
        for _, desc in candidates:
            if _valid_port(desc.get("port")) == target_port:
                return desc
        return None

    return candidates[0][1]


class AutoCADGatewayClient:
    """Typed HTTP client for AutoCAD AI Gateway."""

    FAST_READ_TIMEOUT = 10.0
    DEFAULT_WRITE_TIMEOUT = 30.0
    HEAVY_OPERATION_TIMEOUT = 60.0
    DISCOVERY_CACHE_TTL_SEC = 2.0

    _HEAVY_ENDPOINTS = frozenset(
        {
            "batch",
            "plotting/pdf",
            "documents/open",
            "documents/new",
            "documents/purge-audit",
        }
    )

    def __init__(
        self,
        port: int | str | None = None,
        base_url: str | None = None,
        token: str | None = None,
        timeout: float = 30.0,
        instance_dir: str | Path | None = None,
    ) -> None:
        self._arg_port = port
        self._arg_base_url = base_url
        self._arg_token = token
        self.timeout = timeout
        self.instance_dir = instance_dir
        self._cached_conn: tuple[str, str] | None = None
        self._cached_conn_at: float = 0.0
        self._http_client: httpx.AsyncClient | None = None
        self._http_client_loop: Any = None

        resolved_url, resolved_token = self._resolve_connection(force_refresh=True)
        self.base_url = resolved_url
        self.token = resolved_token

    def invalidate_discovery_cache(self) -> None:
        """Clear cached Gateway endpoint and token so the next request re-discovers."""
        self._cached_conn = None
        self._cached_conn_at = 0.0

    async def aclose(self) -> None:
        """Close the persistent underlying HTTP client if open."""
        if self._http_client is not None and not self._http_client.is_closed:
            await self._http_client.aclose()
        self._http_client = None
        self._http_client_loop = None

    async def _get_http_client(self) -> httpx.AsyncClient:
        try:
            loop = asyncio.get_running_loop()
        except RuntimeError:
            loop = None

        if (
            self._http_client is None
            or self._http_client.is_closed
            or self._http_client_loop is not loop
        ):
            old_client = self._http_client
            self._http_client = None
            self._http_client_loop = None
            if old_client is not None and not old_client.is_closed:
                try:
                    await old_client.aclose()
                except Exception:
                    pass

            self._http_client = httpx.AsyncClient(
                timeout=self.timeout,
                trust_env=False,
                follow_redirects=False,
                limits=httpx.Limits(
                    max_keepalive_connections=8,
                    max_connections=16,
                    keepalive_expiry=30.0,
                ),
            )
            self._http_client_loop = loop
        return self._http_client

    def resolve_timeout(
        self,
        method: str,
        endpoint: str,
        explicit_timeout: float | None = None,
    ) -> float:
        """Return tiered timeout based on explicit override or operation category."""
        if explicit_timeout is not None:
            return float(explicit_timeout)

        ep = endpoint.strip("/").lower().split("?", 1)[0]
        if ep in self._HEAVY_ENDPOINTS:
            return max(self.timeout, self.HEAVY_OPERATION_TIMEOUT)
        if method.upper() == "GET":
            return min(self.timeout, self.FAST_READ_TIMEOUT)
        return self.timeout

    def _explicit_url_and_port(self) -> tuple[str | None, int | None]:
        if self._arg_base_url:
            normalized = _normalize_http_url(self._arg_base_url)
            if normalized is not None:
                return normalized
        arg_port = _valid_port(self._arg_port)
        if arg_port is not None:
            return f"http://127.0.0.1:{arg_port}/", arg_port

        env_port = _valid_port(os.getenv("AUTOCAD_GATEWAY_PORT"))
        if env_port is not None:
            return f"http://127.0.0.1:{env_port}/", env_port

        env_url = os.getenv("AUTOCAD_GATEWAY_URL")
        normalized_env = _normalize_http_url(env_url)
        if normalized_env is not None:
            return normalized_env

        return None, None

    def _explicit_token(self) -> str | None:
        if self._arg_token is not None:
            return self._arg_token
        env_tok = os.getenv("AUTOCAD_GATEWAY_TOKEN")
        if env_tok and env_tok.strip():
            return env_tok.strip()
        return None

    def _resolve_connection(self, force_refresh: bool = False) -> tuple[str, str]:
        now = time.monotonic()
        if (
            not force_refresh
            and self._cached_conn is not None
            and (now - self._cached_conn_at) < self.DISCOVERY_CACHE_TTL_SEC
        ):
            return self._cached_conn

        url, target_port = self._explicit_url_and_port()
        token = self._explicit_token()

        if url is None or token is None:
            live = discover_live_instance(self.instance_dir, target_port=target_port)
            if url is None:
                url = f"http://127.0.0.1:{live['port']}/" if live else f"http://127.0.0.1:{DEFAULT_PORT}/"
            if token is None:
                token = str(live["token"]) if live else ""

        self._cached_conn = (url, token)
        self._cached_conn_at = now
        self.base_url = url
        self.token = token
        return url, token

    def _headers(self, token: str | None = None, include_content_type: bool = True) -> dict[str, str]:
        resolved_token = self.token if token is None else token
        headers: dict[str, str] = {}
        if include_content_type:
            headers["Content-Type"] = "application/json"
        if resolved_token:
            headers[TOKEN_HEADER] = resolved_token
        return headers

    async def _request(
        self,
        method: str,
        endpoint: str,
        json_body: dict[str, Any] | None = None,
        timeout: float | None = None,
    ) -> tuple[bool, int | None, str, str]:
        is_post = method.upper() == "POST"
        effective_timeout = self.resolve_timeout(method, endpoint, timeout)
        max_attempts = 1 if is_post else 2
        base_url = self.base_url

        for attempt in range(max_attempts):
            force_refresh = attempt > 0
            base_url, token = self._resolve_connection(force_refresh=force_refresh)
            url = base_url + endpoint.lstrip("/")
            headers = self._headers(token, include_content_type=is_post)
            client = await self._get_http_client()

            try:
                if is_post:
                    res = await client.post(url, headers=headers, json=json_body, timeout=effective_timeout)
                else:
                    res = await client.get(url, headers=headers, timeout=effective_timeout)

                if res.status_code in (401, 403):
                    self.invalidate_discovery_cache()
                    if not is_post and attempt + 1 < max_attempts:
                        continue

                return True, res.status_code, res.text, base_url
            except httpx.ConnectError:
                self.invalidate_discovery_cache()
                if not is_post and attempt + 1 < max_attempts:
                    continue
                return False, None, "connect_error", base_url
            except httpx.RemoteProtocolError as ex:
                self.invalidate_discovery_cache()
                if not is_post and attempt + 1 < max_attempts:
                    continue
                return False, None, str(ex) or type(ex).__name__, base_url
            except httpx.TimeoutException:
                if not is_post:
                    self.invalidate_discovery_cache()
                return False, None, f"Gateway request timed out after {effective_timeout:g}s.", base_url
            except httpx.HTTPError as ex:
                return False, None, str(ex) or type(ex).__name__, base_url

        return False, None, "connect_error", base_url

    async def get_context(self, endpoint: str) -> str:
        """Query a Gateway GET endpoint (for example, 'context/layers' or '_ping')."""
        ok, status_code, text, base_url = await self._request("GET", endpoint)
        if not ok:
            if text == "connect_error":
                return json.dumps(
                    {
                        "status": "error",
                        "message": (
                            f"Could not connect to AutoCAD Gateway at {base_url}. "
                            "Ensure AutoCAD is open and AI_START_SERVER has been started."
                        ),
                    }
                )
            return json.dumps({"status": "error", "message": f"Gateway communication error: {text}"})

        if text:
            return text
        return json.dumps(
            {"status": "error", "code": status_code, "message": f"Gateway returned HTTP {status_code}."}
        )

    async def execute_command(self, command: str) -> str:
        """Send a command or AutoLISP macro to AutoCAD via POST /execute."""
        ok, status_code, text, base_url = await self._request("POST", "execute", {"command": command})
        if not ok:
            if text == "connect_error":
                return (
                    f"Connection failed to AutoCAD Gateway at {base_url}. "
                    "Ensure AutoCAD is open and AI_START_SERVER is active."
                )
            return f"Error executing AutoCAD command: {text}"

        if status_code is not None and 200 <= status_code < 300:
            return "Success: Command enqueued in AutoCAD."
        clipped = text if len(text) <= 4096 else f"{text[:4096]}... [truncated]"
        return f"Gateway error ({status_code}): {clipped}"

    async def post_json(
        self,
        endpoint: str,
        data: dict[str, Any],
        timeout: float | None = None,
    ) -> str:
        """Send a JSON POST request to a Gateway endpoint."""
        ok, status_code, text, base_url = await self._request("POST", endpoint, data, timeout=timeout)
        if not ok:
            if text == "connect_error":
                return json.dumps({"status": "error", "message": f"Could not connect to AutoCAD Gateway at {base_url}."})
            return json.dumps({"status": "error", "message": f"Request to {endpoint} failed: {text}"})

        if text:
            return text
        return json.dumps(
            {"status": "error", "code": status_code, "message": f"Gateway returned HTTP {status_code}."}
        )

    async def ping(self) -> str:
        """Check Gateway connectivity and active session status via GET /_ping."""
        return await self.get_context("_ping")
