"""Compound domain tool router, on-demand schema help, and boundary validation."""

from __future__ import annotations

import json
from collections.abc import Awaitable, Callable
from typing import Annotated, Any

from mcp.server.fastmcp.exceptions import ToolError
from pydantic import Field, ValidationError

from autocad_mcp.policy import error_json, parse_json_value
from autocad_mcp.tool_mode import DomainSpec, ToolCatalog


def _first_line(text: str) -> str:
    return next((line.strip() for line in text.splitlines() if line.strip()), "")


def _valid_action_names(target_catalog: ToolCatalog, domain: str) -> list[str]:
    return [entry.name for entry in target_catalog.entries(domain=domain, include_aliases=False)]


def _format_unknown_action_error(
    target_catalog: ToolCatalog,
    spec: DomainSpec,
    requested_action: str,
) -> str:
    actions_csv = ", ".join(_valid_action_names(target_catalog, spec.key))
    cross = target_catalog.find_other_domain(spec.key, requested_action)
    if cross is not None:
        other_spec, other_entry, resolved_from = cross
        origin = (
            f"'{requested_action}' (alias of '{other_entry.name}')"
            if resolved_from is not None
            else f"'{requested_action}'"
        )
        return error_json(
            f"Action {origin} does not belong to '{spec.compound_name}' (domain '{spec.key}'); "
            f"use '{other_spec.compound_name}' instead. "
            f"Valid actions for '{spec.compound_name}': {actions_csv}. "
            f"Call '{spec.compound_name}' with action='help' to inspect parameter schemas."
        )
    return error_json(
        f"Unknown action '{requested_action}' for '{spec.compound_name}'. "
        f"Valid actions: {actions_csv}. "
        f"Call '{spec.compound_name}' with action='help' to inspect parameter schemas."
    )


def _format_validation_error(tool_name: str, exc: Exception) -> str:
    cause = exc.__cause__
    if isinstance(cause, ValidationError):
        details = "; ".join(
            f"{'.'.join(str(part) for part in err['loc']) or 'params'}: {err['msg']}"
            for err in cause.errors(include_url=False)
        )
        return error_json(f"Invalid parameters for '{tool_name}': {details}.")
    return error_json(f"Failed executing '{tool_name}': {exc}")


def parse_params_object(
    raw_params: dict[str, Any] | str | None,
) -> tuple[dict[str, Any] | None, str | None]:
    """Normalize compound params from None, dict, or JSON object string."""
    if raw_params is None:
        return {}, None

    parsed, err = parse_json_value(raw_params, dict, "params")
    if err is not None or parsed is None:
        return None, error_json(err or "'params' must be a JSON object.")

    if not all(isinstance(k, str) for k in parsed):
        return None, error_json("'params' keys must be strings.")

    return dict(parsed), None


def format_domain_help(
    target_catalog: ToolCatalog,
    domain: str,
    params_dict: dict[str, Any],
) -> str:
    """Return overview action summaries or the full JSON input schema for one action."""
    spec = target_catalog.get_domain(domain)
    if spec is None:
        return error_json(f"Unknown AutoCAD domain '{domain}'.")

    unexpected_help_keys = sorted(set(params_dict.keys()) - {"action"})
    if unexpected_help_keys:
        return error_json(
            f"Unexpected parameter(s) for action='help': {', '.join(unexpected_help_keys)}. "
            "Only optional 'action' is accepted."
        )

    all_entries = target_catalog.entries(domain=domain, include_aliases=True)
    canonical_entries = [entry for entry in all_entries if not entry.is_alias]

    target_raw = params_dict.get("action")
    if "action" not in params_dict or target_raw is None:
        actions_payload: dict[str, dict[str, Any]] = {}
        for entry in canonical_entries:
            schema = entry.tool_meta.parameters
            required = list(schema.get("required", []))
            required_set = set(required)
            properties = schema.get("properties", {})
            optional = [name for name in properties if name not in required_set]
            actions_payload[entry.name] = {
                "description": _first_line(entry.tool_meta.description),
                "required": required,
                "optional": optional,
            }

        aliases_payload = {
            entry.name: entry.alias_of
            for entry in all_entries
            if entry.alias_of is not None
        }
        return json.dumps(
            {
                "status": "success",
                "ok": True,
                "domain": spec.key,
                "tool": spec.compound_name,
                "actions": actions_payload,
                "aliases": aliases_payload,
            }
        )

    if not isinstance(target_raw, str) or not target_raw.strip():
        valid_csv = ", ".join(entry.name for entry in canonical_entries)
        return error_json(
            f"'params.action' must be a non-empty action name string. "
            f"Valid actions for '{spec.compound_name}': {valid_csv}."
        )

    requested = target_raw.strip()
    entry, resolved_from = target_catalog.resolve_action(domain, requested)
    if entry is None:
        return _format_unknown_action_error(target_catalog, spec, requested)

    payload: dict[str, Any] = {
        "status": "success",
        "ok": True,
        "domain": spec.key,
        "action": entry.name,
        "description": entry.tool_meta.description,
        "input_schema": entry.tool_meta.parameters,
    }
    if resolved_from is not None:
        payload["resolved_from"] = resolved_from
    return json.dumps(payload)


async def dispatch_compound_action(
    target_catalog: ToolCatalog,
    domain: str,
    action: str,
    raw_params: dict[str, Any] | str | None = None,
) -> str:
    """Validate boundary arguments and execute a domain action or help request."""
    spec = target_catalog.get_domain(domain)
    if spec is None:
        return error_json(f"Unknown AutoCAD domain '{domain}'.")

    if not isinstance(action, str) or not action.strip():
        valid_csv = ", ".join(_valid_action_names(target_catalog, spec.key))
        return error_json(
            f"Action cannot be empty for '{spec.compound_name}'. "
            f"Valid actions: {valid_csv}. "
            f"Call '{spec.compound_name}' with action='help' to inspect parameter schemas."
        )

    trimmed_action = action.strip()
    if trimmed_action.lower() == "help":
        params_dict, err_json = parse_params_object(raw_params)
        if err_json is not None or params_dict is None:
            return err_json or error_json("'params' must be a JSON object.")
        return format_domain_help(target_catalog, domain, params_dict)

    entry, _ = target_catalog.resolve_action(domain, trimmed_action)
    if entry is None:
        return _format_unknown_action_error(target_catalog, spec, trimmed_action)

    params_dict, err_json = parse_params_object(raw_params)
    if err_json is not None or params_dict is None:
        return err_json or error_json("'params' must be a JSON object.")

    schema = entry.tool_meta.parameters
    allowed_keys = set(schema.get("properties", {}).keys())
    # Pydantic's default extra='ignore' silently drops unknown keys, so reject typos at the boundary.
    unexpected_keys = sorted(set(params_dict.keys()) - allowed_keys)
    if unexpected_keys:
        allowed_csv = ", ".join(sorted(allowed_keys)) if allowed_keys else "(none)"
        return error_json(
            f"Unexpected parameter(s) for '{entry.name}': {', '.join(unexpected_keys)}. "
            f"Allowed parameters: {allowed_csv}. "
            f"Call '{spec.compound_name}' with action='help' and params={{\"action\": \"{entry.name}\"}} "
            "to inspect the schema."
        )

    missing_keys = [key for key in schema.get("required", []) if key not in params_dict]
    if missing_keys:
        return error_json(
            f"Missing required parameter(s) for '{entry.name}': {', '.join(missing_keys)}. "
            f"Call '{spec.compound_name}' with action='help' and params={{\"action\": \"{entry.name}\"}} "
            "to inspect required parameters."
        )

    try:
        result = await entry.tool_meta.run(params_dict, convert_result=False)
    except (ToolError, TypeError, ValueError, RecursionError) as exc:
        return _format_validation_error(entry.name, exc)

    return result if isinstance(result, str) else json.dumps(result)


def make_compound_tool(
    target_catalog: ToolCatalog,
    domain_spec: DomainSpec,
) -> Callable[[str, Any], Awaitable[str]]:
    """Create a FastMCP-compatible async router function for one domain."""
    canonical_names = _valid_action_names(target_catalog, domain_spec.key)
    actions_csv = ", ".join(canonical_names)

    # Keep params typed as Any so malformed non-object inputs (lists, numbers, bad JSON) reach
    # parse_params_object and return uniform error_json instead of FastMCP pre-validation ToolError.
    async def _handler(
        action: str = "help",
        params: Annotated[
            Any,
            Field(
                description=(
                    "JSON object of parameters matching the chosen action. "
                    "For action='help', optionally pass {\"action\": \"<name>\"}."
                )
            ),
        ] = None,
    ) -> str:
        return await dispatch_compound_action(target_catalog, domain_spec.key, action, params)

    _handler.__name__ = domain_spec.compound_name
    _handler.__doc__ = (
        f"{domain_spec.summary} "
        f"Available actions: {actions_csv}. "
        "Call with action='help' (optional params={'action': '<name>'}) to inspect parameter schemas."
    )
    return _handler


__all__ = [
    "dispatch_compound_action",
    "format_domain_help",
    "make_compound_tool",
    "parse_params_object",
]
