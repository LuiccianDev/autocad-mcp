using System;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

ToolMode toolMode;
try
{
    toolMode = ToolModeParser.FromEnvironment();
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// Required for STDIO MCP servers: clear console logging providers
// to protect JSON-RPC framing on stdout.
builder.Logging.ClearProviders();

builder.Services.AddHttpClient<IAutoCADGatewayClient, AutoCADGatewayClient>(client =>
{
    client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
{
    UseProxy = false,
    AllowAutoRedirect = false,
});

builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "AutoCAD_AI_Context_Server",
        Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "2.5.0"
    };
})
.WithStdioServerTransport()
.WithConfiguredAutoCADTools(toolMode);

var host = builder.Build();
await host.RunAsync();
return 0;
