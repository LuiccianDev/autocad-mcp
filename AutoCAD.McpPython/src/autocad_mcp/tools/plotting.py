"""PaperSpace layout, viewport, title block, and PDF export MCP tools."""

from __future__ import annotations

import datetime

from autocad_mcp import runtime
from autocad_mcp.cad_math import all_finite, num
from autocad_mcp.policy import (
    error_json,
    has_control_chars,
    is_safe_layout_name,
    is_safe_plot_style_name,
    validate_local_file_path,
)
from autocad_mcp.tool_mode import catalog

domain = catalog.domain("plotting")

_PAPER_MEDIA_MAP = {
    "A0": "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
    "A1": "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
    "A2": "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
    "A3": "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
    "A4": "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
}


@domain.tool(name="create_layout")
async def create_layout(layout_name: str) -> str:
    """Create a new PaperSpace Layout tab and switch the active view to that layout."""
    if not is_safe_layout_name(layout_name, allow_spaces=False):
        return error_json("Layout name must be a valid AutoCAD symbol free of spaces, control characters, or '~'/'?'.")
    clean = layout_name.strip()
    cmd = f"_.-LAYOUT\nN\n{clean}\n_.-LAYOUT\nS\n{clean}\n_.TILEMODE\n0\n"
    return await runtime.gateway.execute_command(cmd)


@domain.tool(name="configure_layout_page")
async def configure_layout_page(
    layout_name: str,
    paper_size: str = "A1",
    plot_style: str = "monochrome.ctb",
) -> str:
    """Configure standardized sheet size (A0 to A4) of a Layout for 1:1 metric PDF plotting."""
    if not is_safe_layout_name(layout_name, allow_spaces=False):
        return error_json("Layout name must be a valid AutoCAD symbol free of spaces, control characters, or '~'/'?'.")
    if not is_safe_plot_style_name(plot_style, allow_spaces=False):
        return error_json("Plot style must be a safe .ctb or .stb filename without spaces, paths, or '~'/'?'.")

    clean_paper = (paper_size or "").upper().strip()
    media = _PAPER_MEDIA_MAP.get(clean_paper)
    if not media:
        return error_json(f"Unsupported paper_size '{paper_size}'. Expected one of: A0, A1, A2, A3, A4.")

    clean = layout_name.strip()
    clean_style = plot_style.strip()
    cmd = (
        f"_.-LAYOUT\nS\n{clean}\n_.TILEMODE\n0\n_.PSPACE\n"
        f"_.-PLOT\nY\n{clean}\nDWG To PDF.pc3\n{media}\nM\nL\nN\nL\n1:1\n0.00,0.00\nY\n{clean_style}\nY\nN\nN\nN\n\nY\nN\n"
    )
    return await runtime.gateway.execute_command(cmd)


@domain.alias(name="configure_layout_sheet", alias_of="configure_layout_page")
async def configure_layout_sheet(
    layout_name: str,
    paper_size: str = "A1",
    plot_style: str = "monochrome.ctb",
) -> str:
    """Alias for configure_layout_page: Configure standardized sheet size (A0 to A4) of a Layout."""
    return await configure_layout_page(layout_name=layout_name, paper_size=paper_size, plot_style=plot_style)


@domain.tool(name="create_viewport", core=True)
async def create_viewport(
    lower_x: float,
    lower_y: float,
    width: float,
    height: float,
    scale: float,
    center_model_x: float,
    center_model_y: float,
    layout_name: str | None = None,
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Create a native PaperSpace Viewport entity with metric scale (1000/scale) inside one atomic transaction."""
    if not all_finite(lower_x, lower_y, width, height, scale, center_model_x, center_model_y) or width <= 0 or height <= 0 or scale <= 0:
        return error_json("Viewport coordinates and positive width, height, scale must be finite numbers.")
    if has_control_chars(layout_name):
        return error_json("Layout name cannot contain control characters.")

    payload = {
        "lower_x": lower_x,
        "lower_y": lower_y,
        "width": width,
        "height": height,
        "scale": scale,
        "center_model_x": center_model_x,
        "center_model_y": center_model_y,
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
) -> str:
    """Insert and draw a parametric engineering title block in PaperSpace with customizable metadata."""
    if not all_finite(corner_x, corner_y):
        return error_json("Title block corner coordinates must be finite numbers.")
    if any(has_control_chars(s) for s in (project_title, drawing_name, sheet_code, scale_text, author, date)):
        return error_json("Title block text fields cannot contain control characters or newlines.")

    final_date = date.strip() if date and date.strip() else datetime.datetime.now().strftime("%Y-%m")
    title_width = 180.0
    title_height = 45.0
    x0 = corner_x - title_width
    y0 = corner_y
    x1 = corner_x
    y1 = corner_y + title_height

    cmd = (
        "_.TILEMODE\n0\n_.PSPACE\n"
        "_.-LAYER\nM\nFORMAT_TITLEBLOCK\nC\n7\nFORMAT_TITLEBLOCK\n\n"
        f"_.RECTANG\n{num(x0)},{num(y0)}\n{num(x1)},{num(y1)}\n"
        f"_.LINE\n{num(x0)},{num(y0 + 25.0)}\n{num(x1)},{num(y0 + 25.0)}\n\n"
        f"_.LINE\n{num(x0)},{num(y0 + 12.0)}\n{num(x1)},{num(y0 + 12.0)}\n\n"
        f"_.LINE\n{num(x0 + 120.0)},{num(y0)}\n{num(x0 + 120.0)},{num(y0 + 25.0)}\n\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 35.0)}\n3.5\n0\nPROJECT: {project_title.strip().upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 28.0)}\n3.0\n0\nDRAWING: {drawing_name.strip().upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 16.0)}\n2.5\n0\nAUTHOR / DISCIPLINE: {author.strip().upper()}\n"
        f"_.-TEXT\n{num(x0 + 5.0)},{num(y0 + 4.0)}\n2.5\n0\nDATE: {final_date}\n"
        f"_.-TEXT\n{num(x0 + 125.0)},{num(y0 + 16.0)}\n2.5\n0\nSCALE: {scale_text.strip()}\n"
        f"_.-TEXT\n{num(x0 + 125.0)},{num(y0 + 4.0)}\n4.5\n0\nSHEET: {sheet_code.strip().upper()}\n"
    )
    return await runtime.gateway.execute_command(cmd)


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
) -> str:
    """Alias for insert_title_block: Insert and draw a parametric engineering title block in PaperSpace."""
    return await insert_title_block(
        corner_x=corner_x,
        corner_y=corner_y,
        project_title=project_title,
        drawing_name=drawing_name,
        sheet_code=sheet_code,
        scale_text=scale_text,
        author=author,
        date=date,
    )


@domain.tool(name="export_pdf", core=True)
async def export_pdf(
    layout_name: str,
    pdf_path: str,
    plot_style: str = "monochrome.ctb",
    expected_revision: int | None = None,
    document: str | None = None,
) -> str:
    """Plot and export a PaperSpace layout synchronously to PDF forcing and restoring BACKGROUNDPLOT=0."""
    if not is_safe_layout_name(layout_name, allow_spaces=True):
        return error_json("Layout name must be a valid AutoCAD symbol free of control characters or '~'/'?'.")
    if layout_name.strip().lower() == "model":
        return error_json("Export PDF requires a PaperSpace layout name; 'Model' is not supported.")
    if not is_safe_plot_style_name(plot_style, allow_spaces=True):
        return error_json("Plot style must be a safe .ctb or .stb filename without path separators, UNC prefixes, or '~'/'?'.")
    ok, norm_pdf, err = validate_local_file_path(pdf_path, (".pdf",))
    if not ok:
        return error_json(err or "Invalid PDF output path.")

    payload = {
        "layout_name": layout_name.strip(),
        "pdf_path": norm_pdf,
        "plot_style": plot_style.strip(),
        "expected_revision": expected_revision,
        "document": document,
    }
    return await runtime.gateway.post_json("plotting/pdf", payload, timeout=60.0)
