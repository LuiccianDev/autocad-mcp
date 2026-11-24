using System.Threading;
using System.Threading.Tasks;

namespace AutoCAD.McpServer.Services;

/// <summary>
/// HTTP client contract for communicating with the AutoCAD AI Gateway REST API.
/// </summary>
public interface IAutoCADGatewayClient
{
    /// <summary>Queries a read-only GET endpoint on AutoCAD Gateway.</summary>
    /// <param name="endpoint">Relative Gateway endpoint path (for example, "context/layers").</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Serialized JSON response returned by the Gateway.</returns>
    Task<string> GetContextAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Enqueues an AutoCAD command string or macro through POST /execute.</summary>
    /// <param name="command">Command string terminated with required newline characters.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Confirmation message or serialized error JSON.</returns>
    Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>Pings the Gateway server on GET /_ping to check liveness and session state.</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Serialized JSON diagnostic status.</returns>
    Task<string> PingGatewayAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a JSON POST request to a Gateway endpoint.</summary>
    /// <param name="endpoint">Relative Gateway endpoint path (for example, "lisp/eval").</param>
    /// <param name="json">Pre-serialized JSON payload.</param>
    /// <param name="timeoutMs">Optional server operation timeout in milliseconds to scale HTTP wait bounds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Serialized JSON response from the Gateway or error JSON.</returns>
    Task<string> PostJsonAsync(
        string endpoint,
        string json,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default);
}
