using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Core.Models;
using AutoCAD.Gateway.Services;

namespace AutoCAD.Gateway.Server;

/// <summary>
/// Enruta las solicitudes HTTP entrantes hacia el despachador de comandos o el lector de contexto correspondiente.
/// </summary>
public sealed class RequestRouter
{
    private readonly IAutoCADCommandDispatcher _dispatcher;
    private readonly IAutoCADContextReader _contextReader;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="RequestRouter"/>.
    /// </summary>
    /// <param name="dispatcher">El despachador de comandos de AutoCAD.</param>
    /// <param name="contextReader">El lector de contexto y base de datos de AutoCAD.</param>
    public RequestRouter(IAutoCADCommandDispatcher dispatcher, IAutoCADContextReader contextReader)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
    }

    /// <summary>
    /// Procesa una solicitud HTTP y genera la tupla de código de estado y cuerpo de respuesta JSON.
    /// </summary>
    /// <param name="request">La solicitud HTTP recibida por el listener.</param>
    /// <returns>Una tarea asíncrona con el código de estado HTTP y el contenido JSON devuelto.</returns>
    public async Task<(int StatusCode, string ResponseBody)> RouteRequestAsync(HttpListenerRequest request)
    {
        string path = request.Url?.AbsolutePath ?? string.Empty;
        string method = request.HttpMethod;

        try
        {
            string expectedToken = HttpGatewayServer.ActiveToken;
            string? providedToken = request.Headers["X-Gateway-Token"];

            if (!string.Equals(providedToken, expectedToken, StringComparison.Ordinal))
            {
                return (401, "{\"status\": \"error\", \"message\": \"No autorizado: X-Gateway-Token invalido o ausente.\"}");
            }

            if (method == "POST" && path == "/execute")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                string payload = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(payload))
                {
                    return (400, "{\"status\": \"error\", \"message\": \"El cuerpo de la solicitud no puede estar vacio.\"}");
                }

                CommandRequest? cmdRequest;
                try
                {
                    cmdRequest = JsonSerializer.Deserialize<CommandRequest>(payload);
                }
                catch (JsonException ex)
                {
                    return (400, $"{{\"status\": \"error\", \"message\": \"JSON invalido: {ex.Message}\"}}");
                }

                if (cmdRequest == null || string.IsNullOrEmpty(cmdRequest.Command))
                {
                    return (400, "{\"status\": \"error\", \"message\": \"JSON invalido. Se requiere la clave 'command' con contenido.\"}");
                }

                _dispatcher.EnqueueCommand(cmdRequest.Command);
                return (200, "{\"status\": \"success\", \"message\": \"Comando encolado correctamente\"}");
            }

            if (path.StartsWith("/civil/", StringComparison.Ordinal))
            {
                return await RouteCivilAsync(request, method, path);
            }

            if (method == "GET")
            {
                return path switch
                {
                    "/context/selection" => (200, await _contextReader.ReadSelectionCountAsync()),
                    "/context/entities"  => (200, await _contextReader.ReadEntityCountsAsync()),
                    "/context/info"      => (200, await _contextReader.ReadDrawingInfoAsync()),
                    "/context/layers"    => (200, await _contextReader.ReadLayersAsync()),
                    "/context/extents"   => (200, await _contextReader.ReadExtentsAsync()),
                    "/context/texts"      => (200, await _contextReader.ReadTextsAsync()),
                    "/context/blocks"     => (200, await _contextReader.ReadBlocksAsync()),
                    "/context/screenshot" => (200, await _contextReader.CaptureScreenshotAsync()),
                    _                     => (404, "{\"status\": \"error\", \"message\": \"Ruta no encontrada.\"}")
                };
            }

            return (405, "{\"status\": \"error\", \"message\": \"Metodo HTTP no permitido.\"}");
        }
        catch (Exception ex)
        {
            return (500, $"{{\"status\": \"error\", \"message\": \"Error interno en el Gateway: {ex.Message}\"}}");
        }
    }

    /// <summary>
    /// Procesa las rutas /civil/*. Responde 409 si Civil 3D no está cargado (salvo /civil/status).
    /// </summary>
    /// <param name="request">Solicitud HTTP entrante.</param>
    /// <param name="method">Método HTTP.</param>
    /// <param name="path">Ruta absoluta de la solicitud.</param>
    private static async Task<(int StatusCode, string ResponseBody)> RouteCivilAsync(
        HttpListenerRequest request, string method, string path)
    {
        ICivil3DService civil = Civil3DServiceFactory.Resolve();

        if (method == "GET" && path == "/civil/status")
        {
            return (200, await civil.GetStatusAsync());
        }

        if (!civil.IsAvailable)
        {
            return (409, await civil.ListSurfacesAsync());
        }

        var q = request.QueryString;
        if (method == "GET")
        {
            switch (path)
            {
                case "/civil/surfaces":
                    return (200, await civil.ListSurfacesAsync());
                case "/civil/alignments":
                    return (200, await civil.ListAlignmentsAsync());
                case "/civil/surface/elevation":
                    return TryNumbers(q, out var xy, "x", "y")
                        ? (200, await civil.GetElevationAsync(q["surface"], xy[0], xy[1]))
                        : BadQuery("x", "y");
                case "/civil/alignment/station-offset":
                    return TryNumbers(q, out var pt, "x", "y")
                        ? (200, await civil.GetStationOffsetAsync(q["alignment"], pt[0], pt[1]))
                        : BadQuery("x", "y");
                case "/civil/alignment/point-at-station":
                    return TryNumbers(q, out var so, "station", "offset")
                        ? (200, await civil.GetPointAtStationAsync(q["alignment"], so[0], so[1]))
                        : BadQuery("station", "offset");
            }
        }
        else if (method == "POST" && (path == "/civil/alignment/create" || path == "/civil/surface/create"))
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string payload = await reader.ReadToEndAsync();
            try
            {
                if (path == "/civil/alignment/create")
                {
                    var req = JsonSerializer.Deserialize<CrearAlineamientoRequest>(payload);
                    return req == null ? BadJson() : (200, await civil.CreateAlignmentAsync(req));
                }

                var sreq = JsonSerializer.Deserialize<CrearSuperficieRequest>(payload);
                return sreq == null ? BadJson() : (200, await civil.CreateSurfaceAsync(sreq));
            }
            catch (JsonException ex)
            {
                return (400, JsonSerializer.Serialize(new { status = "error", message = $"JSON invalido: {ex.Message}" }));
            }
        }

        return (404, "{\"status\": \"error\", \"message\": \"Ruta no encontrada.\"}");
    }

    private static bool TryNumbers(
        System.Collections.Specialized.NameValueCollection q, out double[] valores, params string[] claves)
    {
        valores = new double[claves.Length];
        for (int i = 0; i < claves.Length; i++)
        {
            if (!double.TryParse(q[claves[i]], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out valores[i]) ||
                !double.IsFinite(valores[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static (int, string) BadQuery(params string[] claves) =>
        (400, $"{{\"status\": \"error\", \"message\": \"Parametros numericos requeridos: {string.Join(", ", claves)}.\"}}");

    private static (int, string) BadJson() =>
        (400, "{\"status\": \"error\", \"message\": \"Cuerpo JSON vacio o invalido.\"}");
}
