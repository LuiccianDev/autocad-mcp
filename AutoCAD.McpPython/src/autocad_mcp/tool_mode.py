"""Tool exposure mode selection and declarative domain tool catalog."""

from __future__ import annotations

import os
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from enum import Enum
from functools import cached_property
from typing import Any, TypeVar

from mcp.server.fastmcp.tools.base import Tool

TOOL_MODE_ENV_VAR = "AUTOCAD_MCP_TOOL_MODE"

F = TypeVar("F", bound=Callable[..., Any])


class ToolMode(str, Enum):
    """Supported MCP tool exposure profiles."""

    GRANULAR = "granular"
    COMPOUND = "compound"
    CORE = "core"


@dataclass(frozen=True, slots=True)
class DomainSpec:
    """Metadata describing one logical AutoCAD tool domain."""

    key: str
    compound_name: str
    summary: str


@dataclass
class ToolEntry:
    """Registered MCP tool definition linked to its domain and lazily derived SDK schema."""

    name: str
    domain: str
    fn: Callable[..., Any]
    core: bool = False
    alias_of: str | None = None

    @property
    def is_alias(self) -> bool:
        return self.alias_of is not None

    @cached_property
    def tool_meta(self) -> Tool:
        return Tool.from_function(self.fn, name=self.name)


DEFAULT_DOMAINS: tuple[DomainSpec, ...] = (
    DomainSpec(
        key="context",
        compound_name="autocad_context",
        summary=(
            "Inspect active AutoCAD drawing metadata, layers, entities, texts, blocks, "
            "extents, selection, screenshots, and Gateway reference endpoints."
        ),
    ),
    DomainSpec(
        key="entities",
        compound_name="autocad_entities",
        summary="Query, inspect, modify, transform, delete, and batch-execute ObjectARX entities by handle.",
    ),
    DomainSpec(
        key="drafting",
        compound_name="autocad_drafting",
        summary=(
            "Create dimensions, mleaders, hatches, tables, block definitions, attributes, "
            "Xref listings, and entity explosions."
        ),
    ),
    DomainSpec(
        key="drawing",
        compound_name="autocad_drawing",
        summary=(
            "Draw parametric 2D/3D lines, polylines, rectangles, circles, arcs, texts, "
            "blocks, and atomic geometry batches."
        ),
    ),
    DomainSpec(
        key="layers",
        compound_name="autocad_layers",
        summary="Create, activate, modify visibility/freeze/lock state, and delete AutoCAD layers.",
    ),
    DomainSpec(
        key="documents",
        compound_name="autocad_documents",
        summary="Manage open AutoCAD MDI documents, Edit Guard authorization, command cancellation, and system variables.",
    ),
    DomainSpec(
        key="measurement",
        compound_name="autocad_measurement",
        summary="Compute 2D/3D distances, polygon areas, purge/audit drawing databases, and regenerate viewports.",
    ),
    DomainSpec(
        key="plotting",
        compound_name="autocad_plotting",
        summary="Create PaperSpace layouts, configure sheet sizes, insert viewports and title blocks, and export PDFs.",
    ),
    DomainSpec(
        key="civil3d",
        compound_name="autocad_civil3d",
        summary="Query and create Autodesk Civil 3D 2027 TIN surfaces, alignments, profiles, and cross-sections.",
    ),
    DomainSpec(
        key="execution",
        compound_name="autocad_execution",
        summary=(
            "Ping the Gateway, run AutoCAD commands or batch macros, evaluate or load AutoLISP, "
            "zoom, save drawings, and configure metric units."
        ),
    ),
)


def parse_tool_mode(raw: str | None) -> ToolMode:
    """Parse AUTOCAD_MCP_TOOL_MODE value, defaulting unset or blank values to granular."""
    if raw is None:
        return ToolMode.GRANULAR
    if not isinstance(raw, str):
        raise ValueError(f"Invalid {TOOL_MODE_ENV_VAR} value {raw!r}. Expected one of: granular, compound, core.")

    normalized = raw.strip().lower()
    if not normalized:
        return ToolMode.GRANULAR

    try:
        return ToolMode(normalized)
    except ValueError as exc:
        raise ValueError(
            f"Invalid {TOOL_MODE_ENV_VAR} value '{raw.strip()}'. Expected one of: granular, compound, core."
        ) from exc


def resolve_tool_mode() -> ToolMode:
    """Read and parse the active tool exposure mode from the process environment."""
    return parse_tool_mode(os.environ.get(TOOL_MODE_ENV_VAR))


class DomainRegistrar:
    """Declarative decorator facade bound to a single ToolCatalog domain."""

    def __init__(self, target_catalog: ToolCatalog, spec: DomainSpec) -> None:
        self._catalog = target_catalog
        self._spec = spec

    @property
    def spec(self) -> DomainSpec:
        return self._spec

    def tool(self, name: str | None = None, *, core: bool = False) -> Callable[[F], F]:
        """Register a canonical tool in this domain without wrapping the function."""

        def _decorator(fn: F) -> F:
            self._catalog.register(domain=self._spec.key, fn=fn, name=name, core=core)
            return fn

        return _decorator

    def alias(self, name: str | None = None, alias_of: str = "") -> Callable[[F], F]:
        """Register an alias tool in this domain pointing to a canonical tool."""

        def _decorator(fn: F) -> F:
            self._catalog.register(domain=self._spec.key, fn=fn, name=name, core=False, alias_of=alias_of)
            return fn

        return _decorator


class ToolCatalog:
    """Authoritative registry of domains, canonical tools, aliases, and core exposure flags."""

    def __init__(self, domains: Sequence[DomainSpec] = DEFAULT_DOMAINS) -> None:
        self._domains: dict[str, DomainSpec] = {}
        self._domain_entries: dict[str, list[ToolEntry]] = {}
        self._entries: dict[str, ToolEntry] = {}

        seen_compound_names: set[str] = set()
        for spec in domains:
            normalized_key = spec.key.strip().lower()
            if normalized_key in self._domains:
                raise ValueError(f"Duplicate domain key '{normalized_key}'.")
            if spec.compound_name in seen_compound_names:
                raise ValueError(f"Duplicate compound tool name '{spec.compound_name}'.")
            self._domains[normalized_key] = spec
            self._domain_entries[normalized_key] = []
            seen_compound_names.add(spec.compound_name)

    def domain(self, key: str) -> DomainRegistrar:
        spec = self.get_domain(key)
        if spec is None:
            raise ValueError(f"Unknown domain '{key}'.")
        return DomainRegistrar(self, spec)

    def register(
        self,
        *,
        domain: str,
        fn: Callable[..., Any],
        name: str | None = None,
        core: bool = False,
        alias_of: str | None = None,
    ) -> ToolEntry:
        """Validate and register a canonical or alias tool entry."""
        norm_domain = domain.strip().lower()
        if norm_domain not in self._domains:
            raise ValueError(f"Cannot register tool in unknown domain '{domain}'.")

        resolved_name = (name if name is not None else fn.__name__).strip()
        if not resolved_name:
            raise ValueError("Tool name cannot be empty.")
        if resolved_name in self._entries:
            raise ValueError(f"Duplicate tool registration '{resolved_name}'.")

        resolved_alias: str | None = None
        if alias_of is not None:
            resolved_alias = alias_of.strip()
            if not resolved_alias:
                raise ValueError(f"Alias '{resolved_name}' must define a non-empty 'alias_of' target.")
            if core:
                raise ValueError(f"Alias '{resolved_name}' cannot be marked core=True.")
            target = self._entries.get(resolved_alias)
            if target is None:
                raise ValueError(f"Alias '{resolved_name}' references unknown tool '{resolved_alias}'.")
            if target.is_alias:
                raise ValueError(f"Alias '{resolved_name}' cannot target another alias '{resolved_alias}'.")
            if target.domain != norm_domain:
                raise ValueError(
                    f"Alias '{resolved_name}' in domain '{norm_domain}' cannot target '{resolved_alias}' "
                    f"in domain '{target.domain}'."
                )

        entry = ToolEntry(
            name=resolved_name,
            domain=norm_domain,
            fn=fn,
            core=core,
            alias_of=resolved_alias,
        )
        self._entries[resolved_name] = entry
        self._domain_entries[norm_domain].append(entry)
        return entry

    def domains(self) -> tuple[DomainSpec, ...]:
        return tuple(self._domains.values())

    def get_domain(self, key: str) -> DomainSpec | None:
        return self._domains.get(key.strip().lower()) if isinstance(key, str) else None

    def get_entry(self, name: str) -> ToolEntry | None:
        return self._entries.get(name)

    def entries(self, *, domain: str | None = None, include_aliases: bool = True) -> tuple[ToolEntry, ...]:
        """Return registered entries globally or within one domain."""
        if domain is None:
            source = self._entries.values()
        else:
            norm_domain = domain.strip().lower()
            if norm_domain not in self._domain_entries:
                raise ValueError(f"Unknown domain '{domain}'.")
            source = self._domain_entries[norm_domain]
        if include_aliases:
            return tuple(source)
        return tuple(entry for entry in source if not entry.is_alias)

    def core_entries(self) -> tuple[ToolEntry, ...]:
        return tuple(entry for entry in self._entries.values() if entry.core)

    def _resolve_global(self, action: str) -> tuple[ToolEntry, str | None] | None:
        entry = self._entries.get(action.strip())
        if entry is None:
            return None
        if entry.alias_of is not None:
            return self._entries[entry.alias_of], entry.name
        return entry, None

    def resolve_action(self, domain: str, action: str) -> tuple[ToolEntry | None, str | None]:
        """Resolve a canonical action or alias inside one domain, returning (canonical_entry, resolved_from)."""
        norm_domain = domain.strip().lower()
        found = self._resolve_global(action)
        if found is None or found[0].domain != norm_domain:
            return None, None
        return found

    def find_other_domain(self, domain: str, action: str) -> tuple[DomainSpec, ToolEntry, str | None] | None:
        """Locate an action belonging to a different domain for actionable error hints."""
        norm_domain = domain.strip().lower()
        found = self._resolve_global(action)
        if found is None or found[0].domain == norm_domain:
            return None
        canonical, resolved_from = found
        return self._domains[canonical.domain], canonical, resolved_from


catalog = ToolCatalog()

__all__ = [
    "DEFAULT_DOMAINS",
    "DomainRegistrar",
    "DomainSpec",
    "TOOL_MODE_ENV_VAR",
    "ToolCatalog",
    "ToolEntry",
    "ToolMode",
    "catalog",
    "parse_tool_mode",
    "resolve_tool_mode",
]
