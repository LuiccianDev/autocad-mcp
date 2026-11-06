---
name: autolisp-drafting
description: "Estándar de diseño y dibujo técnico en AutoLISP para túneles y campamentos en AutoCAD: entidades entmake, unidades métricas estrictas y capas PA14."
---

# Dibujante Técnico AutoLISP (PA14)

Esta habilidad gobierna la generación automatizada de planos en **AutoLISP** dentro del proyecto **Túnel Casmas Huaraz PA14**, garantizando compatibilidad total con AutoCAD y Civil 3D sin depender de versiones de idioma ni configuraciones locales.

---

## 1. Estándar Métrico Estricto (Unidades en Metros)

* **Obligatorio:** Todo dibujo se realiza **estrictamente en metros**.
* Configuración obligatoria en la cabecera LISP:
  ```lisp
  (setvar "INSUNITS" 4)     ; 4 = Metros
  (setvar "MEASUREMENT" 1)  ; 1 = Métrico (ISO)
  (setvar "LUNITS" 2)       ; 2 = Decimal
  (setvar "LUPREC" 3)       ; 3 = Milímetros de precisión
  ```
* Prohibido trabajar en milímetros o pulgadas.

---

## 2. Reglas de Generación de Geometría con `entmake`

1. **Independencia de OSNAP:** Todas las geometrías (líneas, polilíneas, círculos, arcos, textos, sombreados) deben crearse con `entmake`. Nunca utilizar `(command "_.LINE" ...)`, pues depende de los puntos de captura (OSNAP) y del zoom de la pantalla.
2. **Capas Normativas Institucionales:**
   * Todas las capas deben llevar el prefijo `PA14-` (ej. `PA14-EJE`, `PA14-MUROS`, `PA14-COTAS`, `PA14-TEXTO`).
   * Colores definidos según el estándar ACI (1 a 255).
3. **Calibración de Cotas y Textos:**
   * Textos de cota entre `0.20 m` y `0.25 m` (`DIMTXT = 0.22`, `DIMASZ = 0.18`, `DIMDEC = 2`).
   * Textos limpios en ASCII puro (sin caracteres especiales no compatibles con fuentes SHX).

---

## 3. Organización Modular en el Código Python

* **Planos del Túnel:** Centralizados en `src/casmas_pa14/lisp.py` $\rightarrow$ `salidas/lisp/PA14_dibujos.lsp`.
* **Planos de Campamento y Superficie:** Encapsulados en el subpaquete modular:
  👉 `src/casmas_pa14/lisp_campamento/`
  * `base.py`: Primitivas y motor `Lienzo`.
  * `plantas.py`: Plantas arquitectónicas.
  * `procesos.py`: Infraestructura técnica y procesos industriales.
  * `generador.py`: Compilador de `PA14_campamento_1.lsp` y `PA14_campamento_2.lsp`.
