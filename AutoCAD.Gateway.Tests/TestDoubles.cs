using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;

namespace AutoCAD.Gateway.Tests;

internal sealed record RecordedGatewayCall(string Route, string Body);

internal sealed class RecordingGatewayClient : IAutoCADGatewayClient
{
    public List<RecordedGatewayCall> Calls { get; } = [];

    public Task<string> GetContextAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        Calls.Add(new($"GET:{endpoint}", string.Empty));
        return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true, route = endpoint }));
    }

    public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        Calls.Add(new("POST:execute", command));
        return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true }));
    }

    public Task<string> PingGatewayAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(new("GET:_ping", string.Empty));
        return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true }));
    }

    public Task<string> PostJsonAsync(string endpoint, string json, int? timeoutMs = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(new($"POST:{endpoint}", json));
        return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true, route = endpoint }));
    }
}
