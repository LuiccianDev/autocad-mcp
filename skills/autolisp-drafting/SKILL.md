---
name: autolisp-drafting
description: "Technical AutoLISP drafting standards for metric infrastructure and architectural drawings in AutoCAD using OSNAP-independent entmake entities and strict meter units."
---

# AutoLISP Technical Drafting Standards

This skill governs automated **AutoLISP** script generation, ensuring full compatibility across localized AutoCAD and Civil 3D installations without relying on OSNAP or screen zoom.

---

## 1. Strict Metric Standard (Units in Meters)

- **Mandatory:** All drawings and calculations **must be in meters**.
- Required LISP header configuration:
  ```lisp
  (setvar "INSUNITS" 4)     ; 4 = Meters
  (setvar "MEASUREMENT" 1)  ; 1 = Metric (ISO)
  (setvar "LUNITS" 2)       ; 2 = Decimal
  (setvar "LUPREC" 3)       ; 3 = Millimeter precision
  ```
- Do not draft in millimeters or inches unless explicitly requested.

---

## 2. Geometry Generation Rules with `entmake`

1. **OSNAP Independence:** Create all geometry (lines, polylines, circles, arcs, texts, hatches) with `entmake`. Avoid `(command "_.LINE" ...)` in batch generators because it depends on active OSNAP settings and viewport zoom.
2. **Structured Layering:**
   - Assign explicit layers per discipline (e.g., `ARQ-WALLS`, `ARQ-DOORS`, `ARQ-DIMS`, `ARQ-TEXT`, `STR-AXIS`).
   - Use standard ACI colors (`1` through `255`).
3. **Dimension and Text Calibration:**
   - Keep model-space metric text heights between `0.20 m` and `0.30 m` (`DIMTXT = 0.25`, `DIMASZ = 0.18`, `DIMDEC = 2`).
   - Keep text strings in clean ASCII compatible with standard SHX/TTF fonts.
