using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AutoCAD.McpServer.Services;
using AutoCAD.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

// Configuración obligatoria para servidores MCP STDIO:
// Se eliminan los proveedores de logging hacia Console.Out (salida estándar)
// para proteger el flujo de tramas JSON-RPC del protocolo Model Context Protocol.
builder.Logging.ClearProviders();

// Registro de servicios: Cliente HTTP tipado hacia el Gateway de AutoCAD (puerto 5055 por defecto)
builder.Services.AddHttpClient<IAutoCADGatewayClient, AutoCADGatewayClient>(client =>
{
    string baseUrl;
    string? envPort = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT");
    if (!string.IsNullOrWhiteSpace(envPort))
    {
        baseUrl = $"http://localhost:{envPort.Trim()}/";
    }
    else
    {
        baseUrl = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? "http://localhost:5055/";
    }

    if (!baseUrl.EndsWith("/"))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
    string token = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN") ?? "luiccian-autocad-token-2027";
    client.DefaultRequestHeaders.Add("X-Gateway-Token", token);
});

// Registro del servidor MCP con sus clases modulares de herramientas
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "AutoCAD_AI_Context_Server",
        Version = "1.0.0"
    };
})
.WithStdioServerTransport()
.WithTools<ContextTools>()
.WithTools<ExecutionTools>()
.WithTools<DrawingTools>()
.WithTools<LayerTools>()
.WithTools<MeasurementTools>()
.WithTools<Civil3DTools>()
.WithTools<PlottingTools>();

var host = builder.Build();
await host.RunAsync();
