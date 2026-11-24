using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for native layer creation, state modification, activation, and deletion in AutoCAD.
/// </summary>
[McpServerToolType]
[ToolDomain("layers", "autocad_layers", "Create, activate, toggle visibility/lock/freeze states, and delete AutoCAD layers.")]
public sealed class LayerTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="LayerTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with AutoCAD Gateway.</param>
    public LayerTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Creates or updates a layer with standard color and linetype, making it current.</summary>
    [McpServerTool(Name = "create_layer")]
    [ToolExposure(Core = true)]
    [Description("Create a new layer (or update it if existing) with ACI color (1-255) and linetype, and activate it as current layer.")]
    public async Task<string> CreateLayer(
        string name, int colorAci = 7, string linetype = "Continuous", string description = "")
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        if (colorAci is < 1 or > 255)
        {
            return Error("ACI color must be between 1 and 255.");
        }

        if (!SecurityPolicy.IsValidSymbolName(linetype))
        {
            return Error($"Invalid linetype name: '{linetype}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            color_index = colorAci,
            linetype = linetype.Trim(),
            description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            set_current = true,
        });

        return await _gatewayClient.PostJsonAsync("layers/create", payload);
    }

    /// <summary>Sets an existing layer as the current active layer (CLAYER).</summary>
    [McpServerTool(Name = "set_current_layer")]
    [Description("Set an existing layer as the current active layer (CLAYER).")]
    public async Task<string> SetCurrentLayer(string name)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            set_current = true,
        });

        return await _gatewayClient.PostJsonAsync("layers/state", payload);
    }

    /// <summary>Modifies the visibility/editability state (on, off, freeze, thaw, lock, unlock) of an existing layer.</summary>
    [McpServerTool(Name = "set_layer_state")]
    [ToolExposure(Core = true)]
    [Description("Modify visibility/editability state of an existing layer: 'on', 'off', 'freeze', 'thaw', 'lock', 'unlock'.")]
    public async Task<string> SetLayerState(string name, string action)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        var body = new Dictionary<string, object?> { ["name"] = name.Trim() };
        switch (action?.Trim().ToLowerInvariant())
        {
            case "on" or "activar":
                body["on"] = true;
                break;
            case "off" or "desactivar":
                body["on"] = false;
                break;
            case "freeze" or "congelar":
                body["frozen"] = true;
                break;
            case "thaw" or "descongelar":
                body["frozen"] = false;
                break;
            case "lock" or "bloquear":
                body["locked"] = true;
                break;
            case "unlock" or "desbloquear":
                body["locked"] = false;
                break;
            default:
                return Error($"Invalid action '{action}'. Use: on, off, freeze, thaw, lock, unlock.");
        }

        return await _gatewayClient.PostJsonAsync("layers/state", JsonSerializer.Serialize(body));
    }

    /// <summary>Deletes an unused non-system, non-current layer via ObjectARX Purge check.</summary>
    [McpServerTool(Name = "delete_layer")]
    [Description("Delete an unused AutoCAD layer (refuses protected layers '0'/'Defpoints', the active layer, or layers referenced by objects).")]
    public async Task<string> DeleteLayer(string name, string? document = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        string payload = JsonSerializer.Serialize(new { name = name.Trim(), document });
        return await _gatewayClient.PostJsonAsync("layers/delete", payload);
    }
}
