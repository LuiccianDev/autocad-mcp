using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Services;

namespace AutoCAD.Gateway.Server;

/// <summary>
/// HTTP server based on <see cref="HttpListener"/> exposing the communication API for AutoCAD.
/// </summary>
public sealed class HttpGatewayServer
{
    private static readonly Lazy<HttpGatewayServer> _lazyInstance = new(() => new HttpGatewayServer());

    /// <summary>
    /// Gets the shared singleton instance of the Gateway server.
    /// </summary>
    public static HttpGatewayServer Instance => _lazyInstance.Value;

    /// <summary>
    /// Gets or sets the active token required in the 'X-Gateway-Token' header.
    /// Can be dynamically updated at runtime without restarting AutoCAD.
    /// </summary>
    public static string ActiveToken { get; set; } =
        Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN") ?? "luiccian-autocad-token-2027";

    private readonly IAutoCADCommandDispatcher _dispatcher;
    private readonly IAutoCADContextReader _contextReader;
    private readonly RequestRouter _router;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpGatewayServer"/> class.
    /// </summary>
    public HttpGatewayServer()
    {
        _dispatcher = new AutoCADCommandDispatcher();
        _contextReader = new AutoCADContextReader();
        _router = new RequestRouter(_dispatcher, _contextReader);
    }

    /// <summary>
    /// Gets a value indicating whether the HTTP server is currently running and listening for requests.
    /// </summary>
    public bool IsRunning => _listener != null && _listener.IsListening;

    /// <summary>
    /// Starts the HTTP server on the specified URL prefix.
    /// </summary>
    /// <param name="urlPrefix">The listening URL prefix (e.g., "http://localhost:5055/").</param>
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
    /// Stops and releases the HTTP server.
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
            // Ignore if already disposed
        }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Asynchronous loop listening for incoming HTTP requests.
    /// </summary>
    /// <param name="token">Cancellation token to terminate the loop.</param>
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
                // Catch unexpected exceptions to keep loop stable
            }
        }
    }

    /// <summary>
    /// Handles an individual HTTP request, applies CORS headers, routes, and writes the response.
    /// </summary>
    /// <param name="context">The received HTTP context.</param>
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
    /// Serializes and writes the response payload to the HTTP response stream.
    /// </summary>
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
            // Client may have terminated connection early
        }
    }
}
