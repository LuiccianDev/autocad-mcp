;;; ==========================================================================
;;; Rutina AutoLISP de Muestra: Trazado Paramétrico de Sección de Túnel
;;; Proyecto: autocad-mcp (AutoCAD MCP Server & Gateway)
;;; Estándar Métrico: Metros (INSUNITS = 4)
;;; ==========================================================================

(defun c:DIBUJAR_SECCION_TUNEL ( / ptX ptY radio espesor)
  (vl-load-com)
  (setvar "CMDECHO" 0)

  ;; 1. Asegurar Capas Normalizadas
  (if (not (tblsearch "LAYER" "TUN-EXCAVACION"))
    (command "_.-LAYER" "M" "TUN-EXCAVACION" "C" "1" "" "")
  )
  (if (not (tblsearch "LAYER" "TUN-REVESTIMIENTO"))
    (command "_.-LAYER" "M" "TUN-REVESTIMIENTO" "C" "4" "" "")
  )
  (if (not (tblsearch "LAYER" "TUN-EJE"))
    (command "_.-LAYER" "M" "TUN-EJE" "C" "6" "" "")
  )

  ;; 2. Parámetros Métricos (en metros)
  (setq ptX 0.0)
  (setq ptY 0.0)
  (setq radio 4.50)     ; Radio interior: 4.50 m (Diámetro 9.00 m)
  (setq espesor 0.35)   ; Espesor dovela hormigón: 35 cm

  ;; 3. Dibujar Bóveda Interior con entmake (Capa TUN-REVESTIMIENTO)
  (entmake (list
    '(0 . "CIRCLE")
    '(100 . "AcDbEntity")
    '(8 . "TUN-REVESTIMIENTO")
    '(100 . "AcDbCircle")
    (list 10 ptX ptY 0.0)
    (cons 40 radio)
  ))

  ;; 4. Dibujar Límite de Excavación Exterior (Capa TUN-EXCAVACION)
  (entmake (list
    '(0 . "CIRCLE")
    '(100 . "AcDbEntity")
    '(8 . "TUN-EXCAVACION")
    '(100 . "AcDbCircle")
    (list 10 ptX ptY 0.0)
    (cons 40 (+ radio espesor))
  ))

  ;; 5. Dibujar Ejes de Referencia (Capa TUN-EJE)
  (entmake (list
    '(0 . "LINE")
    '(100 . "AcDbEntity")
    '(8 . "TUN-EJE")
    '(100 . "AcDbLine")
    (list 10 (- ptX (+ radio espesor 1.0)) ptY 0.0)
    (list 11 (+ ptX (+ radio espesor 1.0)) ptY 0.0)
  ))
  (entmake (list
    '(0 . "LINE")
    '(100 . "AcDbEntity")
    '(8 . "TUN-EJE")
    '(100 . "AcDbLine")
    (list 10 ptX (- ptY (+ radio espesor 1.0)) 0.0)
    (list 11 ptX (+ ptY (+ radio espesor 1.0)) 0.0)
  ))

  (setvar "CMDECHO" 1)
  (princ "\n[AutoCAD MCP] Sección de túnel paramétrico generada exitosamente en metros.\n")
  (princ)
)

(princ "\nRutina DIBUJAR_SECCION_TUNEL cargada. Ejecute el comando o invoque (c:DIBUJAR_SECCION_TUNEL).\n")
(princ)
