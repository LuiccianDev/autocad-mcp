---
name: yqarch-fast-drafting
description: Flujo de trabajo estándar y rápido para crear planos arquitectónicos utilizando el plugin YQArch en AutoCAD.
---

# YQArch Fast Drafting Workflow

Utiliza este skill cuando el usuario solicite crear planos, plantas arquitectónicas, distribuciones completas o esqueletos estructurales utilizando **YQArch** a través del AutoCAD Gateway.

## 1. Principios de Capas y Orden (Layering)
- **Separación estricta:** Antes de dibujar un elemento, asegúrate de utilizar o crear las capas correspondientes para mantener el proyecto limpio y profesional.
  - Ejes estructurales: Capa `EJES` (Color 1 - rojo o colores tenues).
  - Muros: Capa `MUROS` (Color 7 - blanco/negro o magenta para cortes).
  - Columnas: Capa `COLUMNAS` (Color 4 - cyan).
  - Carpintería (Puertas y ventanas): Capa `CARPINTERIA` (Color 5 - azul o cyan).
  - Anotaciones (Cotas, Ejes, Títulos): Capa `ANOTACIONES` (Color 3 - verde, garantizando textos a escala correcta).
  - Mobiliario: Capa `MUEBLES` (Color 2 - amarillo u 8 - gris).

## 2. Flujo de Trabajo Secuencial (Paso a Paso)
Al diseñar una planta desde cero, **DEBES** seguir este orden para maximizar la velocidad y aprovechar la automatización paramétrica de YQArch:

0. **Inicialización Métrica e Idioma (Configuración Obligatoria):**
   - **Idioma Inglés:** Ejecuta `YQLANG` y selecciona `English` (evita diálogos y textos en chino).
   - **Unidades en Metros:** Configura `YQUNITS` en metros y define la escala con `SD` (ej. 1:100 o 1:50).
   - **Anotaciones Métricas:** Utiliza `configurar_entorno_dibujo` para asegurar que el tamaño de cotas y textos no aparezca desmedido en pantalla.

1. **Grilla y Ejes (Skeleton):**
   - **Comando YQArch:** `ZXW` (Draw Grid Axis) o `AX`.
   - **Propósito:** Crea la cuadrícula estructural del proyecto de manera inmediata. Todo proyecto arquitectónico con YQArch debe empezar estableciendo los ejes, ya que los muros se adhieren magnéticamente a ellos.

2. **Muros (Walls):**
   - **Comando YQArch:** `WW` (Draw Wall).
   - **Propósito:** Dibuja muros paramétricos de doble línea alineados a los ejes. Puedes ajustar el grosor y justificación desde la línea de comandos en AutoCAD.

3. **Columnas Estructurales:**
   - **Comando YQArch:** `ZZBZ` (Insert columns at axis intersections) o `ZZR` para columnas rectangulares aisladas.
   - **Propósito:** Coloca columnas automáticamente en las intersecciones o puntos críticos necesarios.

4. **Puertas y Ventanas (Openings):**
   - **Comandos YQArch:** `AD` (Door) y `WD` (Window).
   - **Propósito:** Inserta aberturas dinámicas que cortan el muro automáticamente y colocan el bloque de carpintería a escala en un solo paso. Si estos comandos abren un pop-up interactivo en AutoCAD, recuerda usar el flujo híbrido (pedir al usuario que confirme en pantalla antes de seguir enviando comandos).

5. **Mobiliario (Furnishing):**
   - **Comandos YQArch:** `JJ` (Auto furniture) u otros módulos de la librería (como `WC` para acomodar baños).

6. **Anotaciones y Acotado Automático:**
   - **Comandos YQArch:** `AZH` (Auto axis dim) para generar y acotar burbujas de ejes.
   - **Comandos YQArch:** `DD` (Dim linear) o `DDWW` (Dim interior walls) para un acotado interno súper rápido.

## 3. Uso del Catálogo Local
- Consulta SIEMPRE el archivo [yqarch_commands.md](./references/yqarch_commands.md) ubicado en el directorio `references/` de este skill para verificar la nomenclatura exacta y el nombre interno del comando antes de enviarlo por el Gateway.
- Los comandos nativos de YQArch a menudo se ingresan como alias sin el prefijo `_`, pero asegúrate de respetar la sintaxis de escapes requerida por el Gateway.
