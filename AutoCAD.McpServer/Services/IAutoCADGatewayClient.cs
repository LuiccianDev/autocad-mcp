using System.Threading;
using System.Threading.Tasks;

namespace AutoCAD.McpServer.Services;

/// <summary>
/// Contrato de cliente HTTP para interactuar con la API REST del AutoCAD AI Gateway.
/// </summary>
public interface IAutoCADGatewayClient
{
    /// <summary>
    /// Consulta un endpoint de lectura de contexto en el Gateway de AutoCAD.
    /// </summary>
    /// <param name="endpoint">La ruta relativa del endpoint (por ejemplo, "context/layers").</param>
    /// <param name="cancellationToken">Token de cancelación opcional.</param>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON devuelta por el Gateway.</returns>
    Task<string> GetContextAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un comando o script CAD para su ejecución en AutoCAD a través del Gateway.
    /// </summary>
    /// <param name="command">La cadena del comando con sus respectivos saltos de línea (\n).</param>
    /// <param name="cancellationToken">Token de cancelación opcional.</param>
    /// <returns>Una tarea asíncrona cuyo resultado es el mensaje de confirmación o error de ejecución.</returns>
    Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía una señal de prueba (ping) al Gateway para verificar conectividad activa en el puerto configurado.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación opcional.</param>
    /// <returns>Una tarea asíncrona cuyo resultado es el diagnóstico de conectividad.</returns>
    Task<string> PingGatewayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un cuerpo JSON por POST a un endpoint del Gateway (por ejemplo, "civil/alignment/create").
    /// </summary>
    /// <param name="endpoint">La ruta relativa del endpoint.</param>
    /// <param name="json">El cuerpo JSON ya serializado.</param>
    /// <param name="cancellationToken">Token de cancelación opcional.</param>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta JSON del Gateway (o un JSON de error).</returns>
    Task<string> PostJsonAsync(string endpoint, string json, CancellationToken cancellationToken = default);
}
