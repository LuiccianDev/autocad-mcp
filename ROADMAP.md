# Hoja de Ruta (Roadmap) — AutoCAD MCP (`autocad-mcp`)

Este documento describe la visión técnica, hitos estratégicos y prioridades de desarrollo futuras para el ecosistema **AutoCAD MCP Server & Gateway**.

---

## 🎯 Visión del Proyecto

Convertir a **AutoCAD MCP** en el estándar abierto de referencia mundial para la interacción fluida, segura y bidireccional entre Modelos de Inteligencia Artificial (LLMs) y los entornos de diseño y modelado de ingeniería de Autodesk (AutoCAD y Civil 3D), permitiendo desde la asistencia en dibujo arquitectónico hasta la ingeniería civil de precisión totalmente automatizada.

---

## 🛣️ Hitos Planificados

### Fase 1: Consolidación y Robustez Open Source (Q4 2026) — *En curso*
- [x] Unificación de identidad bajo el nombre canónico `autocad-mcp`.
- [x] Suite completa de gobernanza comunitaria (.github/, CI, Code of Conduct, Security, Support).
- [x] Documentación exhaustiva de arquitectura, catálogo de 27 herramientas y resolución de problemas.
- [x] Colección de plantillas de configuración y ejemplos listos para usar en clientes de IA.
- [ ] Incorporación de suite de pruebas unitarias con mocks de `IAutoCADGatewayClient` para el servidor MCP.

### Fase 2: Expansión de Autodesk Civil 3D (Q1 2027)
- [ ] **Corredores Viales (`Corridors`):** Herramientas MCP para inspeccionar y parametrizar ensamblajes, regiones y frecuencias de muestreo.
- [ ] **Redes de Tuberías y Presión (`Pipe Networks`):** Creación e inspección de buzones/cámaras y tuberías gravitacionales.
- [ ] **Volúmenes de Corte y Relleno:** Herramienta para cálculo directo de cubicación entre superficies TIN (superficie de comparación vs terreno natural).
- [ ] **Explanaciones y Perfiles Transversales:** Lectura y extracción paramétrica de secciones de muestreo (*Sample Lines*).

### Fase 3: Rendimiento y Experiencia de Usuario (Q2 2027)
- [ ] **Streaming de Progreso (MCP Notifications):** Notificaciones en tiempo real al cliente de IA durante operaciones de dibujo prolongadas o regeneración de superficies grandes.
- [ ] **Caché Inteligente de Entidades:** Optimización de `leer_objetos_autocad` y `leer_capas_autocad` mediante marcas de tiempo de modificación en la base de datos de AutoCAD.
- [ ] **Instalador Automatizado (.msi / PowerShell Package):** Script de despliegue en un solo clic que configure el archivo `PackageContents.xml` (Autodesk Autoloader) para que el Gateway se cargue automáticamente al iniciar AutoCAD sin requerir `NETLOAD` manual.

### Fase 4: Ecosistema Multimodal y CAD Avanzado (Q3 2027)
- [ ] **Visión por Computadora Aumentada:** Análisis automático de las capturas de pantalla de AutoCAD con detección de elementos superpuestos o cotas ilegibles.
- [ ] **Soporte para Bloques Dinámicos:** Lectura y modificación de parámetros dinámicos (longitudes, visibilidades y matrices paramétricas).
- [ ] **Compatibilidad con AutoCAD 2028:** Pruebas tempranas sobre el nuevo ObjectARX SDK y .NET Core Runtime.

---

## 💡 Cómo Proponer una Nueva Iniciativa

Si deseas proponer una nueva característica para la hoja de ruta:
1. Revisa que no esté ya listada arriba.
2. Abre una [Feature Request](.github/ISSUE_TEMPLATE/feature_request.md) o inicia un hilo en **GitHub Discussions**.
3. Las propuestas con mayor respaldo de la comunidad y casos de uso de ingeniería bien fundamentados serán priorizadas en cada versión menor.
