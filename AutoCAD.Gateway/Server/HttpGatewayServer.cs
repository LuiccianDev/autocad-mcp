using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Services;

namespace AutoCAD.Gateway.Server;

/// <summary>
/// Servidor HTTP basado en <see cref="HttpListener"/> que expone el API de comunicación con AutoCAD.
/// </summary>
public sealed class HttpGatewayServer
{
    private static readonly Lazy<HttpGatewayServer> _lazyInstance = new(() => new HttpGatewayServer());

    /// <summary>
    /// Obtiene la instancia única compartida del servidor Gateway.
    /// </summary>
    public static HttpGatewayServer Instance => _lazyInstance.Value;

    /// <summary>
    /// Obtiene o establece el token activo requerido en la cabecera 'X-Gateway-Token'.
    /// Puede modificarse dinámicamente en tiempo de ejecución sin reiniciar AutoCAD.
    /// </summary>
    public static string ActiveToken { get; set; } =
        Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN") ?? "luiccian-autocad-token-2027";

    private readonly IAutoCADCommandDispatcher _dispatcher;
    private readonly IAutoCADContextReader _contextReader;
    private readonly RequestRouter _router;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="HttpGatewayServer"/>.
    /// </summary>
    public HttpGatewayServer()
    {
        _dispatcher = new AutoCADCommandDispatcher();
        _contextReader = new AutoCADContextReader();
        _router = new RequestRouter(_dispatcher, _contextReader);
    }

    /// <summary>
    /// Obtiene un valor que indica si el servidor HTTP se encuentra en ejecución y escuchando peticiones.
    /// </summary>
    public bool IsRunning => _listener != null && _listener.IsListening;

    /// <summary>
    /// Inicia el servidor HTTP en el prefijo URL especificado.
    /// </summary>
    /// <param name="urlPrefix">El prefijo URL de escucha (por ejemplo, "http://localhost:5055/").</param>
    /// <exception cref="InvalidOperationException">El servidor ya se encuentra en ejecución.</exception>
    public void Start(string urlPrefix)
    {
        if (IsRunning) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add(urlPrefix);
        _listener.Start();

        _cts = new CancellationTokenSource();
        Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Detiene y libera el servidor HTTP.
    /// </summary>
    public void Stop()
    {
        if (_listener == null) return;

        try
        {
            _cts?.Cancel();
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Ignorar si ya fue cerrado previamente
        }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Ciclo asíncrono de escucha y despacho de solicitudes HTTP.
    /// </summary>
    /// <param name="token">Token de cancelación para detener el ciclo.</param>
    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_listener == null || !_listener.IsListening) break;

                HttpListenerContext context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context), token);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Manejar excepciones inesperadas en el ciclo para mantener la estabilidad del hilo
            }
        }
    }

    /// <summary>
    /// Procesa una solicitud individual, aplica cabeceras CORS y envía la respuesta.
    /// </summary>
    /// <param name="context">El contexto HTTP recibido.</param>
    private async Task HandleContextAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try
        {
            response.AppendHeader("Access-Control-Allow-Origin", "*");
            response.AppendHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AppendHeader("Access-Control-Allow-Headers", "Content-Type, X-Gateway-Token");

            if (request.HttpMethod == "OPTIONS")
            {
                SendResponse(response, 200, string.Empty);
                return;
            }

            var (statusCode, body) = await _router.RouteRequestAsync(request);
            SendResponse(response, statusCode, body);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("AutoCAD Gateway internal error: {0}", ex);
            SendResponse(response, 500, "{\"status\": \"error\", \"message\": \"An internal gateway error occurred while processing the request.\"}");
        }
    }

    /// <summary>
    /// Serializa y escribe el búfer de respuesta hacia el cliente HTTP.
    /// </summary>
    /// <param name="response">La respuesta HTTP.</param>
    /// <param name="statusCode">El código de estado HTTP.</param>
    /// <param name="content">El cuerpo de texto a emitir.</param>
    private static void SendResponse(HttpListenerResponse response, int statusCode, string content)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            using System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            output.Close();
        }
        catch
        {
            // El cliente pudo haber cerrado la conexión prematuramente
        }
    }
}
