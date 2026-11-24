---
name: yqarch-fast-drafting
description: Standard fast-drafting workflow for architectural floor plans using the YQArch plugin in AutoCAD.
---

# YQArch Fast Drafting Workflow

Use this skill whenever the user asks to draft architectural floor plans, layouts, or structural grids using **YQArch** through the AutoCAD Gateway.

## 1. Layering and Discipline Separation
- **Strict Separation:** Select or create dedicated layers before drawing elements:
  - Structural axes: `AXIS` (ACI color `1` - red or muted grid color).
  - Walls: `WALLS` (ACI color `7` - white/black or magenta for cuts).
  - Columns: `COLUMNS` (ACI color `4` - cyan).
  - Openings (doors and windows): `OPENINGS` (ACI color `5` - blue or cyan).
  - Annotations (dimensions, grid bubbles, titles): `ANNOTATIONS` (ACI color `3` - green).
  - Furniture: `FURNITURE` (ACI color `2` - yellow or `8` - gray).

## 2. Sequential Drafting Workflow (Step by Step)
When drafting a floor plan from scratch with YQArch, follow this sequence:

0. **Metric and Language Initialization (Mandatory):**
   - **English UI:** Run `YQLANG` and select `English` to avoid localized CJK dialogs.
   - **Metric Units:** Set `YQUNITS` to meters and configure drawing scale with `SD` (e.g., `1:100` or `1:50`).
   - **Metric Annotations:** Call `configure_drawing_units` (or `execute_command` in `core` mode) so `INSUNITS = 4` and dimension scaling remain proportional.

1. **Grid and Structural Axes:**
   - **YQArch Command:** `ZXW` (Draw Grid Axis) or `AX`.
   - **Purpose:** Establish the structural grid first so parametric walls snap cleanly to axis lines.

2. **Walls:**
   - **YQArch Command:** `WW` (Draw Wall).
   - **Purpose:** Draw double-line parametric walls aligned to grid axes with configured thickness and justification.

3. **Structural Columns:**
   - **YQArch Command:** `ZZBZ` (Insert columns at axis intersections) or `ZZR` for standalone rectangular columns.

4. **Doors and Windows:**
   - **YQArch Commands:** `AD` (Door) and `WD` (Window).
   - **Purpose:** Insert dynamic openings that automatically split walls and place scaled carpentry blocks. If a command opens a modal dialog in AutoCAD, pause and ask the user to confirm on screen before sending further coordinates.

5. **Furnishing:**
   - **YQArch Commands:** `JJ` (Auto furniture) or specialized fixture commands (`WC`).

6. **Annotations and Dimensioning:**
   - **YQArch Commands:** `AZH` (Auto axis dimensioning) and `DD` / `DDWW` (wall linear dimensions).

## 3. Local Command Reference
- Always consult [yqarch_commands.md](./references/yqarch_commands.md) before sending YQArch aliases to verify exact command names.
