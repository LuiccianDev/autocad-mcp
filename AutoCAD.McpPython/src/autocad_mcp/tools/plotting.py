"""PaperSpace layout, page-setup, 3D DCS viewport, native title block, and silent PDF export MCP tools."""

from __future__ import annotations

import datetime
import json
from typing import Any

from autocad_mcp import runtime
from autocad_mcp.cad_math import all_finite, is_valid_symbol_name
from autocad_mcp.policy import (
    RASTER_IMAGE_EXTENSIONS,
    VALID_SHADE_PLOTS,
    VALID_TITLE_BLOCK_TEMPLATES,
    error_json,
    has_control_chars,
    is_safe_layout_name,
    is_safe_plot_style_name,
    normalize_orientation,
    normalize_paper_size,
    parse_view_direction,
    validate_local_file_path,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("plotting")


@domain.tool(name="create_layout")
async def create_layout(
    layout_name: str,
    clear_default_viewport: bool = True,
    activate: bool = True,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a new PaperSpace Layout tab natively and optionally erase its auto-generated default viewport."""
    if not is_safe_layout_name(layout_name, allow_spaces=True):
        return error_json("Layout name must be a valid AutoCAD symbol free of control characters or '~'/'?'.")
    if layout_name.strip().lower() == "model":
        return error_json("'Model' is reserved and cannot be created as a PaperSpace layout.")

    return await runtime.gateway.post_json(
        "plotting/layout",
        {
            "layout_name": layout_name.strip(),
            "clear_default_viewport": bool(clear_default_viewport),
            "activate": bool(activate),
            "expected_revision": expected_revision,
            "document": document,
        },
    )


@domain.tool(name="configure_layout_page")
async def configure_layout_page(
    layout_name: str,
    paper_size: str = "A1",
    plot_style: str = "monochrome.ctb",
    orientation: str = "landscape",
    plot_device: str = "DWG To PDF.pc3",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Configure PaperSpace layout PlotSettings (A0 to A4, landscape/portrait, 1:1 mm) natively."""
    if not is_safe_layout_name(layout_name, allow_spaces=True):
        return error_json("Layout name must be a valid AutoCAD symbol free of control characters or '~'/'?'.")
    if layout_name.strip().lower() == "model":
        return error_json("Page setup applies to PaperSpace layouts; 'Model' is not supported.")
    if not is_safe_plot_style_name(plot_style, allow_spaces=True):
        return error_json("Plot style must be a safe .ctb or .stb filename without path separators or '~'/'?'.")

    clean_paper, paper_err = normalize_paper_size(paper_size)
    if paper_err or not clean_paper:
        return error_json(paper_err or "Invalid paper_size.")

    clean_orientation, orient_err = normalize_orientation(orientation)
    if orient_err or not clean_orientation:
        return error_json(orient_err or "Invalid orientation.")

    if has_control_chars(plot_device) or not (plot_device or "").strip():
        return error_json("'plot_device' must be a non-empty plotter configuration name without control characters.")

    return await runtime.gateway.post_json(
        "plotting/page-setup",
        {
            "layout_name": layout_name.strip(),
            "paper_size": clean_paper,
            "plot_style": plot_style.strip(),
            "orientation": clean_orientation,
            "plot_device": plot_device.strip(),
            "expected_revision": expected_revision,
            "document": document,
        },
    )


@domain.alias(name="configure_layout_sheet", alias_of="configure_layout_page")
async def configure_layout_sheet(
    layout_name: str,
    paper_size: str = "A1",
    plot_style: str = "monochrome.ctb",
    orientation: str = "landscape",
    plot_device: str = "DWG To PDF.pc3",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Alias for configure_layout_page: Configure standardized sheet size (A0 to A4) of a Layout."""
    return await configure_layout_page(
        layout_name=layout_name,
        paper_size=paper_size,
        plot_style=plot_style,
        orientation=orientation,
        plot_device=plot_device,
        expected_revision=expected_revision,
        document=document,
    )


@domain.tool(name="create_viewport", core=True)
async def create_viewport(
    lower_x: float,
    lower_y: float,
    width: float,
    height: float,
    scale: float,
    center_model_x: float,
    center_model_y: float,
    center_model_z: float = 0.0,
    view_direction: list[float] | str | None = None,
    twist_angle: float = 0.0,
    shade_plot: str | None = None,
    visual_style: str | None = None,
    lock_viewport: bool = True,
    layer: str = "0",
    layout_name: str | None = None,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a native PaperSpace Viewport with 2D/3D DCS centering, metric scale (1000/scale), and visual controls."""
    if not all_finite(lower_x, lower_y, width, height, scale, center_model_x, center_model_y, center_model_z, twist_angle) or width <= 0 or height <= 0 or scale <= 0:
        return error_json("Viewport coordinates and positive width, height, scale must be finite numbers.")
    if has_control_chars(layout_name):
        return error_json("Layout name cannot contain control characters.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    parsed_dir, dir_err = parse_view_direction(view_direction)
    if dir_err:
        return error_json(dir_err)

    norm_shade: str | None = None
    if shade_plot is not None and shade_plot.strip():
        norm_shade = shade_plot.strip().lower()
        if norm_shade not in VALID_SHADE_PLOTS:
            return error_json(f"Unsupported shade_plot '{shade_plot}'. Expected: as_displayed, wireframe, hidden, rendered.")

    if visual_style is not None and visual_style.strip() and has_control_chars(visual_style):
        return error_json("Visual style name cannot contain control characters.")

    payload: dict[str, Any] = {
        "lower_x": lower_x,
        "lower_y": lower_y,
        "width": width,
        "height": height,
        "scale": scale,
        "center_model_x": center_model_x,
        "center_model_y": center_model_y,
        "center_model_z": center_model_z,
        "view_direction": parsed_dir,
        "twist_angle": twist_angle,
        "shade_plot": norm_shade,
        "visual_style": visual_style.strip() if visual_style and visual_style.strip() else None,
        "lock_viewport": bool(lock_viewport),
        "layer": layer.strip(),
        "layout_name": layout_name.strip() if layout_name and layout_name.strip() else None,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("plotting/viewport", payload)


@domain.tool(name="insert_title_block")
async def insert_title_block(
    corner_x: float,
    corner_y: float,
    project_title: str = "ENGINEERING PROJECT",
    drawing_name: str = "GENERAL PLAN",
    sheet_code: str = "L-01",
    scale_text: str = "1:1000",
    author: str = "ENGINEERING",
    date: str | None = None,
    layout_name: str | None = None,
    template: str = "iso_standard",
    logo_path: str | None = None,
    layer: str = "FORMAT_TITLEBLOCK",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Insert a native parametric PaperSpace title block with configurable template and optional raster logo."""
    if not all_finite(corner_x, corner_y):
        return error_json("Title block corner coordinates must be finite numbers.")
    if any(has_control_chars(s) for s in (project_title, drawing_name, sheet_code, scale_text, author, date, layout_name)):
        return error_json("Title block text fields cannot contain control characters or newlines.")
    if not is_valid_symbol_name(layer):
        return error_json(f"Invalid layer name: '{layer}'.")

    clean_template = (template or "iso_standard").strip().lower()
    if clean_template not in VALID_TITLE_BLOCK_TEMPLATES:
        return error_json(f"Unsupported template '{template}'. Expected: iso_standard, compact, detailed.")

    norm_logo: str | None = None
    if logo_path is not None and logo_path.strip():
        ok, norm_logo, err = validate_local_file_path(logo_path, RASTER_IMAGE_EXTENSIONS)
        if not ok:
            return error_json(err or "Invalid logo image path.", code="invalid_path")

    final_date = date.strip() if date and date.strip() else datetime.datetime.now().strftime("%Y-%m")
    return await runtime.gateway.post_json(
        "plotting/title-block",
        {
            "corner_x": corner_x,
            "corner_y": corner_y,
            "project_title": project_title.strip(),
            "drawing_name": drawing_name.strip(),
            "sheet_code": sheet_code.strip(),
            "scale_text": scale_text.strip(),
            "author": author.strip(),
            "date": final_date,
            "layout_name": layout_name.strip() if layout_name and layout_name.strip() else None,
            "template": clean_template,
            "logo_path": norm_logo,
            "layer": layer.strip(),
            "expected_revision": expected_revision,
            "document": document,
        },
    )


@domain.alias(name="insert_parametric_titleblock", alias_of="insert_title_block")
async def insert_parametric_titleblock(
    corner_x: float,
    corner_y: float,
    project_title: str = "ENGINEERING PROJECT",
    drawing_name: str = "GENERAL PLAN",
    sheet_code: str = "L-01",
    scale_text: str = "1:1000",
    author: str = "ENGINEERING",
    date: str | None = None,
    layout_name: str | None = None,
    template: str = "iso_standard",
    logo_path: str | None = None,
    layer: str = "FORMAT_TITLEBLOCK",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Alias for insert_title_block: Insert a native parametric engineering title block in PaperSpace."""
    return await insert_title_block(
        corner_x=corner_x,
        corner_y=corner_y,
        project_title=project_title,
        drawing_name=drawing_name,
        sheet_code=sheet_code,
        scale_text=scale_text,
        author=author,
        date=date,
        layout_name=layout_name,
        template=template,
        logo_path=logo_path,
        layer=layer,
        expected_revision=expected_revision,
        document=document,
    )


@domain.tool(name="export_pdf", core=True)
async def export_pdf(
    layout_name: str,
    pdf_path: str,
    plot_style: str = "monochrome.ctb",
    paper_size: str | None = None,
    orientation: str | None = None,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Plot and export a PaperSpace layout synchronously to PDF via PlotEngine forcing BACKGROUNDPLOT=0."""
    if not is_safe_layout_name(layout_name, allow_spaces=True):
        return error_json("Layout name must be a valid AutoCAD symbol free of control characters or '~'/'?'.")
    if layout_name.strip().lower() == "model":
        return error_json("Export PDF requires a PaperSpace layout name; 'Model' is not supported.")
    if not is_safe_plot_style_name(plot_style, allow_spaces=True):
        return error_json("Plot style must be a safe .ctb or .stb filename without path separators, UNC prefixes, or '~'/'?'.")
    ok, norm_pdf, err = validate_local_file_path(pdf_path, (".pdf",))
    if not ok:
        return error_json(err or "Invalid PDF output path.", code="invalid_path")

    clean_paper: str | None = None
    if paper_size is not None and paper_size.strip():
        clean_paper, paper_err = normalize_paper_size(paper_size)
        if paper_err:
            return error_json(paper_err)

    clean_orientation: str | None = None
    if orientation is not None and orientation.strip():
        clean_orientation, orient_err = normalize_orientation(orientation)
        if orient_err:
            return error_json(orient_err)

    payload = {
        "layout_name": layout_name.strip(),
        "pdf_path": norm_pdf,
        "plot_style": plot_style.strip(),
        "paper_size": clean_paper,
        "orientation": clean_orientation,
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("plotting/pdf", payload, timeout=60.0)
