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
/// Routes incoming HTTP requests to the appropriate command dispatcher or context reader.
/// </summary>
public sealed class RequestRouter
{
    private readonly IAutoCADCommandDispatcher _dispatcher;
    private readonly IAutoCADContextReader _contextReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestRouter"/> class.
    /// </summary>
    /// <param name="dispatcher">The AutoCAD command dispatcher.</param>
    /// <param name="contextReader">The AutoCAD database and context reader.</param>
    public RequestRouter(IAutoCADCommandDispatcher dispatcher, IAutoCADContextReader contextReader)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
    }

    /// <summary>
    /// Processes an HTTP request and generates the tuple of status code and JSON response body.
    /// </summary>
    /// <param name="request">The HTTP request received by the listener.</param>
    /// <returns>Asynchronous task returning HTTP status code and JSON response body.</returns>
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
                return (401, "{\"status\": \"error\", \"message\": \"Unauthorized: Invalid or missing X-Gateway-Token.\"}");
            }

            if (method == "POST" && path == "/execute")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                string payload = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(payload))
                {
                    return (400, "{\"status\": \"error\", \"message\": \"Request body cannot be empty.\"}");
                }

                CommandRequest? cmdRequest;
                try
                {
                    cmdRequest = JsonSerializer.Deserialize<CommandRequest>(payload);
                }
                catch (JsonException)
                {
                    return (400, "{\"status\": \"error\", \"message\": \"Invalid or malformed JSON payload.\"}");
                }

                if (cmdRequest == null || string.IsNullOrEmpty(cmdRequest.Command))
                {
                    return (400, "{\"status\": \"error\", \"message\": \"Invalid JSON. 'command' key with content is required.\"}");
                }

                _dispatcher.EnqueueCommand(cmdRequest.Command);
                return (200, "{\"status\": \"success\", \"message\": \"Command successfully enqueued.\"}");
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
                    _                     => (404, "{\"status\": \"error\", \"message\": \"Route not found.\"}")
                };
            }

            return (405, "{\"status\": \"error\", \"message\": \"HTTP method not allowed.\"}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("AutoCAD Gateway route error: {0}", ex);
            return (500, "{\"status\": \"error\", \"message\": \"An internal gateway error occurred while processing the request.\"}");
        }
    }

    /// <summary>
    /// Processes /civil/* routes. Returns 409 if Civil 3D is not loaded (except /civil/status).
    /// </summary>
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
            catch (JsonException)
            {
                return (400, JsonSerializer.Serialize(new { status = "error", message = "Invalid or malformed JSON payload." }));
            }
        }

        return (404, "{\"status\": \"error\", \"message\": \"Route not found.\"}");
    }

    private static bool TryNumbers(
        System.Collections.Specialized.NameValueCollection q, out double[] values, params string[] keys)
    {
        values = new double[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!double.TryParse(q[keys[i]], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out values[i]) ||
                !double.IsFinite(values[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static (int, string) BadQuery(params string[] keys) =>
        (400, $"{{\"status\": \"error\", \"message\": \"Required numeric query parameters: {string.Join(", ", keys)}.\"}}");

    private static (int, string) BadJson() =>
        (400, "{\"status\": \"error\", \"message\": \"Empty or invalid JSON body.\"}");
}
