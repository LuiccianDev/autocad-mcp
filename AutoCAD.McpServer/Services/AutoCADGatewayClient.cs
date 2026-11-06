using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.McpServer.Models;

namespace AutoCAD.McpServer.Services;

/// <summary>
/// Implementación del cliente HTTP tipado para comunicar con AutoCAD AI Gateway en el puerto 5055.
/// </summary>
public sealed class AutoCADGatewayClient : IAutoCADGatewayClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="AutoCADGatewayClient"/>.
    /// </summary>
    /// <param name="httpClient">Instancia configurada de <see cref="HttpClient"/>.</param>
    public AutoCADGatewayClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc/>
    public async Task<string> GetContextAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(endpoint, cancellationToken);
            string cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(cuerpo))
            {
                return cuerpo;
            }

            return $"{{\"status\": \"error\", \"code\": {(int)response.StatusCode}, \"message\": \"Gateway devolvio error {response.StatusCode}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"status\": \"error\", \"message\": \"Fallo al conectar con el Gateway en puerto 5055: {ex.Message}\"}}";
        }
    }

    /// <inheritdoc/>
    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new CommandPayload { Command = command };
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.PostAsync("execute", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return "Éxito: El comando se inyectó correctamente en AutoCAD.";
            }

            string errorDetalle = await response.Content.ReadAsStringAsync(cancellationToken);
            return $"Error del Gateway ({response.StatusCode}): {errorDetalle}";
        }
        catch (Exception ex)
        {
            return $"Fallo crítico de conexión con el Gateway de AutoCAD. Asegúrate de que el puerto 5055 esté encendido. Error: {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public async Task<string> PingGatewayAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new CommandPayload { Command = " \n" };
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.PostAsync("execute", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return "¡Conexión exitosa con el AutoCAD AI Gateway!";
            }

            return $"Conectado al Gateway, pero devolvió el estado: {response.StatusCode}";
        }
        catch (Exception ex)
        {
            return $"Fallo crítico al conectar con el Gateway de AutoCAD. Asegúrate de que AutoCAD esté abierto y el Gateway ejecutándose en el puerto 5055. Error: {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public async Task<string> PostJsonAsync(string endpoint, string json, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return $"{{\"status\": \"error\", \"message\": \"Fallo al conectar con el Gateway en puerto 5055: {ex.Message}\"}}";
        }
    }
}
