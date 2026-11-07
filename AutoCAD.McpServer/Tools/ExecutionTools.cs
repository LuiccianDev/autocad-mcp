using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using AutoCAD.McpServer.Services;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Proporciona herramientas MCP para la ejecución de comandos, navegación y configuración en AutoCAD.
/// </summary>
[McpServerToolType]
public sealed class ExecutionTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="ExecutionTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway de AutoCAD.</param>
    public ExecutionTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Envía un ping al Gateway de AutoCAD para verificar la conectividad activa.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es el estado de la conexión.</returns>
    [McpServerTool(Name = "ping_autocad_gateway")]
    [Description("Envía un ping al Gateway de AutoCAD para verificar la conectividad.")]
    public async Task<string> PingAutoCAD() => await _gatewayClient.PingGatewayAsync();

    /// <summary>
    /// Ejecuta una lista secuencial de comandos o macros de AutoCAD en un único envío por lotes.
    /// </summary>
    /// <param name="comandosJson">Arreglo JSON de comandos a ejecutar en secuencia.</param>
    /// <returns>Una tarea asíncrona con el resultado de la inyección por lotes.</returns>
    [McpServerTool(Name = "ejecutar_comandos_lote")]
    [Description("⚡ Ejecuta una lista secuencial de comandos o macros de AutoCAD en un único envío por lotes. 'comandosJson' es un arreglo JSON con cadenas de comandos.")]
    public async Task<string> EjecutarComandosLote(string comandosJson)
    {
        if (string.IsNullOrWhiteSpace(comandosJson))
        {
            return "{\"status\": \"error\", \"message\": \"El parámetro 'comandosJson' no puede estar vacío.\"}";
        }

        var cmdSb = new System.Text.StringBuilder();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(comandosJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string s = el.GetString()?.TrimEnd('\r', '\n') ?? "";
                    if (!string.IsNullOrEmpty(s))
                    {
                        cmdSb.Append(s).Append('\n');
                    }
                }
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                cmdSb.Append(doc.RootElement.GetString()?.TrimEnd('\r', '\n')).Append('\n');
            }
            else
            {
                return "{\"status\": \"error\", \"message\": \"'comandosJson' debe ser un arreglo de cadenas de comandos.\"}";
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Si el cliente envió una cadena con saltos de línea directa
            cmdSb.Append(comandosJson.TrimEnd('\r', '\n')).Append('\n');
        }

        return await _gatewayClient.ExecuteCommandAsync(cmdSb.ToString());
    }

    /// <summary>
    /// Envía comandos de dibujo, macros AutoLISP o comandos YQArch directamente a AutoCAD.
    /// </summary>
    /// <param name="command">Comandos a enviar (deben incluir \n donde se requiera pulsar Enter).</param>
    /// <returns>Una tarea asíncrona con el resultado de la inyección del comando.</returns>
    [McpServerTool(Name = "ejecutar_comando_autocad")]
    [Description("Envía comandos de dibujo, macros AutoLISP o comandos YQArch (ej. WW, JJ, AD, WD, ZXW) directamente a AutoCAD a través del Gateway. Los comandos deben incluir saltos de línea (\\n) donde se requiera pulsar Enter.")]
    public async Task<string> EjecutarComandoAutoCAD(string command) => await _gatewayClient.ExecuteCommandAsync(command);

    /// <summary>
    /// Evalúa e inyecta expresiones o rutinas AutoLISP dinámicas en el espacio de trabajo de AutoCAD.
    /// </summary>
    /// <param name="codigoLisp">Código AutoLISP a evaluar (ej. '(entmake ...)', '(setq ...)' o funciones completas).</param>
    /// <returns>Una tarea asíncrona con la confirmación de la inyección de la expresión LISP.</returns>
    [McpServerTool(Name = "ejecutar_lisp")]
    [Description("Evalúa e inyecta expresiones o rutinas AutoLISP dinámicas directamente en AutoCAD (ej. creación de entidades ultrarrápidas con entmake, definición de variables o funciones).")]
    public async Task<string> EjecutarLisp(string codigoLisp)
    {
        if (string.IsNullOrWhiteSpace(codigoLisp))
        {
            return "{\"status\": \"error\", \"message\": \"El código AutoLISP no puede estar vacío.\"}";
        }

        string lispCommand = codigoLisp.Trim();
        if (!lispCommand.EndsWith('\n'))
        {
            lispCommand += "\n";
        }

        return await _gatewayClient.ExecuteCommandAsync(lispCommand);
    }

    /// <summary>
    /// Carga y ejecuta un archivo AutoLISP (.lsp) externo en la sesión activa de AutoCAD.
    /// </summary>
    /// <param name="rutaArchivoLsp">Ruta completa en disco del archivo .lsp (ej. 'C:/scripts/rutina.lsp').</param>
    /// <returns>Una tarea asíncrona con el resultado de la carga del archivo.</returns>
    [McpServerTool(Name = "cargar_archivo_lisp")]
    [Description("Carga y evalúa un archivo de script AutoLISP (.lsp) desde el sistema de archivos dentro de AutoCAD mediante la función (load ...).")]
    public async Task<string> CargarArchivoLisp(string rutaArchivoLsp)
    {
        if (string.IsNullOrWhiteSpace(rutaArchivoLsp))
        {
            return "{\"status\": \"error\", \"message\": \"La ruta del archivo .lsp no puede estar vacía.\"}";
        }

        // AutoLISP requiere barras diagonales estándar '/' en las rutas de archivo
        string normalizedPath = rutaArchivoLsp.Replace('\\', '/');
        string command = $"(load \"{normalizedPath}\")\n";
        return await _gatewayClient.ExecuteCommandAsync(command);
    }

    /// <summary>
    /// Ejecuta un Zoom Extensión ('_.ZOOM E') en AutoCAD para encuadrar y centrar automáticamente todos los elementos en pantalla.
    /// </summary>
    /// <returns>Una tarea asíncrona con la confirmación de la operación.</returns>
    [McpServerTool(Name = "zoom_extension")]
    [Description("Ejecuta un Zoom Extensión ('_.ZOOM E') en AutoCAD para encuadrar y centrar automáticamente todos los elementos en pantalla.")]
    public async Task<string> ZoomExtension() => await _gatewayClient.ExecuteCommandAsync("_.ZOOM\nE\n");

    /// <summary>
    /// Guarda el archivo de dibujo actual en AutoCAD ('_.QSAVE').
    /// </summary>
    /// <returns>Una tarea asíncrona con la confirmación de la operación.</returns>
    [McpServerTool(Name = "guardar_dibujo")]
    [Description("Guarda el archivo de dibujo actual en AutoCAD ('_.QSAVE').")]
    public async Task<string> GuardarDibujo() => await _gatewayClient.ExecuteCommandAsync("_.QSAVE\n");

    /// <summary>
    /// Configura el entorno de trabajo activo en AutoCAD a METROS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) y calibra las cotas y textos.
    /// </summary>
    /// <returns>Una tarea asíncrona con la confirmación de la configuración.</returns>
    [McpServerTool(Name = "configurar_entorno_dibujo")]
    [Description("Configura el entorno de trabajo activo en AutoCAD a METROS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) y calibra las escalas de cotas/textos para evitar anotaciones gigantescas o microscópicas.")]
    public async Task<string> ConfigurarEntornoDibujo()
    {
        const string script =
            "_.-SETVAR\nINSUNITS\n4\n" +
            "_.-SETVAR\nMEASUREMENT\n1\n" +
            "_.-SETVAR\nLUNITS\n2\n" +
            "_.-SETVAR\nLUPREC\n3\n" +
            "_.-SETVAR\nAUNITS\n0\n" +
            "_.-SETVAR\nANGBASE\n0.0\n" +
            "_.-SETVAR\nANGDIR\n0\n" +
            "_.-SETVAR\nDIMTXT\n0.22\n" +
            "_.-SETVAR\nDIMASZ\n0.18\n" +
            "_.-SETVAR\nDIMEXE\n0.11\n" +
            "_.-SETVAR\nDIMEXO\n0.06\n" +
            "_.-SETVAR\nDIMGAP\n0.08\n" +
            "_.-SETVAR\nDIMDEC\n2\n" +
            "_.-SETVAR\nDIMTAD\n1\n" +
            "_.UCS\nW\n";

        return await _gatewayClient.ExecuteCommandAsync(script);
    }
}
