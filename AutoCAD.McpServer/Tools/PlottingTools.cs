using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Proporciona herramientas MCP para la gestión de presentaciones (Layouts), configuración de hojas (A1-A4),
/// creación de ventanas gráficas (Viewports) con escalas métricas, inserción de cajetines y exportación a PDF.
/// </summary>
[McpServerToolType]
public sealed class PlottingTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="PlottingTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway de AutoCAD.</param>
    public PlottingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    private static string Num(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Crea una nueva pestaña de presentación (Layout) en espacio papel y la activa.
    /// </summary>
    /// <param name="nombreLayout">Nombre único para la pestaña de presentación (ej. 'LÁMINA-01' o 'PLANO_GENERAL').</param>
    /// <returns>Una tarea asíncrona con la confirmación de la creación del layout.</returns>
    [McpServerTool(Name = "crear_layout")]
    [Description("Crea una nueva pestaña de presentación (Layout) en espacio papel y cambia la vista activa a dicha presentación.")]
    public async Task<string> CrearLayout(string nombreLayout)
    {
        if (string.IsNullOrWhiteSpace(nombreLayout))
        {
            return "{\"status\": \"error\", \"message\": \"El nombre del layout no puede estar vacío.\"}";
        }

        string cleanName = nombreLayout.Trim();
        string command = $"_.-LAYOUT\nN\n{cleanName}\n_.-LAYOUT\nS\n{cleanName}\n_.TILEMODE\n0\n";
        return await _gatewayClient.ExecuteCommandAsync(command);
    }

    /// <summary>
    /// Configura el tamaño de papel de la presentación activa (ej. A1, A2, A3, A4), el trazador 'DWG To PDF.pc3' y la escala 1:1 en mm.
    /// </summary>
    /// <param name="nombreLayout">Nombre de la presentación a configurar.</param>
    /// <param name="tamanoHoja">Tamaño normalizado: 'A1' (841x594mm), 'A2' (594x420mm), 'A3' (420x297mm) o 'A4' (297x210mm).</param>
    /// <param name="estiloTrazado">Archivo de estilos de plumillas (.ctb), por defecto 'monochrome.ctb'.</param>
    /// <returns>Una tarea asíncrona con el resultado de la configuración de página.</returns>
    [McpServerTool(Name = "configurar_hoja_layout")]
    [Description("Configura el tamaño de página estandarizado (A1, A2, A3, A4) de un Layout para trazado en PDF a escala 1:1 (milímetros) con plumillas monocromáticas.")]
    public async Task<string> ConfigurarHojaLayout(
        string nombreLayout,
        string tamanoHoja = "A1",
        string estiloTrazado = "monochrome.ctb")
    {
        if (string.IsNullOrWhiteSpace(nombreLayout))
        {
            return "{\"status\": \"error\", \"message\": \"El nombre del layout no puede estar vacío.\"}";
        }

        string mediaName = tamanoHoja.ToUpperInvariant() switch
        {
            "A0" => "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
            "A1" => "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
            "A2" => "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
            "A3" => "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
            "A4" => "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
            _ => "ISO_full_bleed_A1_(841.00_x_594.00_MM)"
        };

        // Activación del layout y configuración de página mediante -PLOT
        var sb = new StringBuilder();
        sb.Append($"_.-LAYOUT\nS\n{nombreLayout.Trim()}\n");
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        sb.Append($"_.-PLOT\nN\n{nombreLayout.Trim()}\nDWG To PDF.pc3\n{mediaName}\nM\nL\nN\nL\n1:1\n0.00,0.00\nY\n{estiloTrazado}\nY\nN\nN\nY\nN\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Crea una ventana gráfica (Viewport) en el layout activo con tamaño en milímetros, escala métrica y centrado de coordenadas del modelo.
    /// </summary>
    /// <param name="xInferior">Coordenada X inferior izquierda en la hoja (mm).</param>
    /// <param name="yInferior">Coordenada Y inferior izquierda en la hoja (mm).</param>
    /// <param name="ancho">Ancho de la ventana gráfica en la hoja (mm).</param>
    /// <param name="alto">Alto de la ventana gráfica en la hoja (mm).</param>
    /// <param name="escala">Escala métrica de ingeniería (ej. 1000 para 1:1000, 500 para 1:500, 250 para 1:250, 100 para 1:100).</param>
    /// <param name="xCentroModelo">Coordenada Este (X) central del espacio modelo a encuadrar en metros.</param>
    /// <param name="yCentroModelo">Coordenada Norte (Y) central del espacio modelo a encuadrar en metros.</param>
    /// <returns>Una tarea asíncrona con el resultado de la creación y calibración del viewport.</returns>
    [McpServerTool(Name = "crear_viewport")]
    [Description("Crea una ventana gráfica (Viewport / MVIEW) en el layout activo, calcula la escala métrica exacta (1000/Escala XP) y centra el modelo en las coordenadas (X, Y) dadas.")]
    public async Task<string> CrearViewport(
        double xInferior,
        double yInferior,
        double ancho,
        double alto,
        double escala,
        double xCentroModelo,
        double yCentroModelo)
    {
        if (escala <= 0)
        {
            return "{\"status\": \"error\", \"message\": \"La escala métrica debe ser mayor a cero (ej. 1000, 500, 200).\"}";
        }

        double xSuperior = xInferior + ancho;
        double ySuperior = yInferior + alto;
        double factorZoomXp = 1000.0 / escala;

        var sb = new StringBuilder();
        // Asegurar que estamos en espacio papel
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        // Crear viewport rectangular
        sb.Append($"_.-MVIEW\n{Num(xInferior)},{Num(yInferior)}\n{Num(xSuperior)},{Num(ySuperior)}\n");
        // Entrar a espacio modelo flotante
        sb.Append("_.MSPACE\n");
        // Centrar en las coordenadas requeridas
        sb.Append($"_.ZOOM\nC\n{Num(xCentroModelo)},{Num(yCentroModelo)}\n{factorZoomXp.ToString("F6", CultureInfo.InvariantCulture)}XP\n");
        // Regresar a espacio papel seguro
        sb.Append("_.PSPACE\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Dibuja un cajetín / membrete estándar paramétrico en espacio papel con los metadatos técnicos del proyecto.
    /// </summary>
    /// <param name="xEsquina">Coordenada X de la esquina inferior derecha de la hoja (mm) donde se ancla el cajetín.</param>
    /// <param name="yEsquina">Coordenada Y de la esquina inferior derecha de la hoja (mm) donde se ancla el cajetín.</param>
    /// <param name="tituloProyecto">Nombre o título principal de la obra / proyecto.</param>
    /// <param name="nombrePlano">Contenido o descripción del plano (ej. 'PLANTA GENERAL Y ALINEAMIENTO').</param>
    /// <param name="codigoLamina">Código de lámina (ej. 'LAM-01', 'VI-01').</param>
    /// <param name="escalaTexto">Texto de escala indicado (ej. '1:1000', 'INDICADAS').</param>
    /// <param name="autor">Nombre del profesional, proyectista o dibujante.</param>
    /// <param name="fecha">Fecha del plano (ej. 'NOVIEMBRE 2026').</param>
    /// <returns>Una tarea asíncrona con el resultado del trazado del cajetín.</returns>
    [McpServerTool(Name = "insertar_cajetin_parametrico")]
    [Description("Inserta y dibuja un cajetín/membrete técnico en espacio papel con campos paramétricos (Título, Plano, Lámina, Escala, Diseñador, Fecha).")]
    public async Task<string> InsertarCajetinParametrico(
        double xEsquina,
        double yEsquina,
        string tituloProyecto = "PROYECTO DE INGENIERÍA",
        string nombrePlano = "PLANO GENERAL",
        string codigoLamina = "L-01",
        string escalaTexto = "1:1000",
        string autor = "INGENIERÍA",
        string? fecha = null)
    {
        string fechaFinal = string.IsNullOrWhiteSpace(fecha) ? DateTime.Now.ToString("yyyy-MM") : fecha.Trim();

        // Dimensiones estándar de cajetín técnico: 180 mm de ancho x 45 mm de alto
        double anchoCajetin = 180.0;
        double altoCajetin = 45.0;

        double x0 = xEsquina - anchoCajetin;
        double y0 = yEsquina;
        double x1 = xEsquina;
        double y1 = yEsquina + altoCajetin;

        var sb = new StringBuilder();
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        // Asegurar capa para cajetín
        sb.Append("_.-LAYER\nM\nFORMATO_CAJETIN\nC\n7\nFORMATO_CAJETIN\n\n");
        // Marco exterior del cajetín
        sb.Append($"_.RECTANG\n{Num(x0)},{Num(y0)}\n{Num(x1)},{Num(y1)}\n");
        // Línea divisoria horizontal para el título
        sb.Append($"_.LINE\n{Num(x0)},{Num(y0 + 25.0)}\n{Num(x1)},{Num(y0 + 25.0)}\n\n");
        // Línea divisoria horizontal para datos
        sb.Append($"_.LINE\n{Num(x0)},{Num(y0 + 12.0)}\n{Num(x1)},{Num(y0 + 12.0)}\n\n");
        // Línea divisoria vertical para lámina y escala
        sb.Append($"_.LINE\n{Num(x0 + 120.0)},{Num(y0)}\n{Num(x0 + 120.0)},{Num(y0 + 25.0)}\n\n");

        // Rótulos de texto
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 35.0)}\n3.5\n0\nPROYECTO: {tituloProyecto.ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 28.0)}\n3.0\n0\nPLANO: {nombrePlano.ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 16.0)}\n2.5\n0\nESPECIALIDAD / AUTOR: {autor.ToUpperInvariant()}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 5.0)},{Num(y0 + 4.0)}\n2.5\n0\nFECHA: {fechaFinal}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 125.0)},{Num(y0 + 16.0)}\n2.5\n0\nESCALA: {escalaTexto}\n");
        sb.Append($"_.-TEXT\n{Num(x0 + 125.0)},{Num(y0 + 4.0)}\n4.5\n0\nLÁMINA: {codigoLamina.ToUpperInvariant()}\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Exporta y traza la presentación activa o un layout a un archivo PDF en disco mediante 'DWG To PDF.pc3'.
    /// </summary>
    /// <param name="nombreLayout">Nombre de la presentación a trazar (ej. 'Layout1' o 'LÁMINA-01').</param>
    /// <param name="rutaPdf">Ruta absoluta del archivo PDF de salida (ej. 'C:/Planos/Lamina01.pdf').</param>
    /// <param name="estiloCtb">Estilo de trazado de plumillas, por defecto 'monochrome.ctb'.</param>
    /// <returns>Una tarea asíncrona con el resultado del ploteo a PDF.</returns>
    [McpServerTool(Name = "exportar_pdf")]
    [Description("Traza y genera un archivo PDF físico a partir de un Layout o presentación de AutoCAD utilizando el motor virtual 'DWG To PDF.pc3'.")]
    public async Task<string> ExportarPdf(
        string nombreLayout,
        string rutaPdf,
        string estiloCtb = "monochrome.ctb")
    {
        if (string.IsNullOrWhiteSpace(nombreLayout) || string.IsNullOrWhiteSpace(rutaPdf))
        {
            return "{\"status\": \"error\", \"message\": \"El nombre del layout y la ruta del archivo PDF son obligatorios.\"}";
        }

        string normalizedPath = rutaPdf.Replace('\\', '/');

        var sb = new StringBuilder();
        sb.Append($"_.-LAYOUT\nS\n{nombreLayout.Trim()}\n");
        sb.Append("_.TILEMODE\n0\n_.PSPACE\n");
        // Ejecución de _.-PLOT directa a archivo
        sb.Append($"_.-PLOT\nY\n{nombreLayout.Trim()}\nDWG To PDF.pc3\n\n\n\n\n\n\n\nY\n{estiloCtb}\nY\nN\n{normalizedPath}\nN\nY\n");

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }
}
