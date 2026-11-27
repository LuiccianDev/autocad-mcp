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

    /// <summary>Creates or updates a layer with ACI color, linetype, lineweight, and plottability.</summary>
    [McpServerTool(Name = "create_layer")]
    [ToolExposure(Core = true)]
    [Description("Create a new layer (or update it if existing) with ACI color (1-255), linetype, lineweight, and plottability.")]
    public async Task<string> CreateLayer(
        string name,
        int colorAci = 7,
        string linetype = "Continuous",
        [CompoundParam(AcceptScalarAsString = true)] string? lineweight = null,
        bool? isPlottable = null,
        string description = "",
        bool setCurrent = true,
        string? document = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        var body = new Dictionary<string, object?>
        {
            ["name"] = name.Trim(),
            ["document"] = document,
        };

        if (!TryPopulateLayerProperties(
                body,
                colorAci,
                linetype,
                lineweight,
                isPlottable,
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                setCurrent,
                out _,
                out string? propErr))
        {
            return Error(propErr ?? "Invalid layer property.");
        }

        return await _gatewayClient.PostJsonAsync("layers/create", JsonSerializer.Serialize(body));
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

    /// <summary>Modifies visibility/editability state and/or color, linetype, lineweight, or plottability of a layer.</summary>
    [McpServerTool(Name = "set_layer_state")]
    [ToolExposure(Core = true)]
    [Description("Modify layer state ('on', 'off', 'freeze', 'thaw', 'lock', 'unlock') and/or colorAci, linetype, lineweight, isPlottable.")]
    public async Task<string> SetLayerState(
        string name,
        string? action = null,
        int? colorAci = null,
        string? linetype = null,
        [CompoundParam(AcceptScalarAsString = true)] string? lineweight = null,
        bool? isPlottable = null,
        string? description = null,
        bool? setCurrent = null,
        string? document = null)
    {
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        var body = new Dictionary<string, object?>
        {
            ["name"] = name.Trim(),
            ["document"] = document,
        };
        bool hasMutation = false;

        if (!string.IsNullOrWhiteSpace(action))
        {
            switch (action.Trim().ToLowerInvariant())
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

            hasMutation = true;
        }

        if (!TryPopulateLayerProperties(
                body,
                colorAci,
                linetype,
                lineweight,
                isPlottable,
                description,
                setCurrent,
                out bool anyPropAdded,
                out string? propErr))
        {
            return Error(propErr ?? "Invalid layer property.");
        }

        if (!hasMutation && !anyPropAdded)
        {
            return Error("Specify 'action' or at least one layer property (colorAci, linetype, lineweight, isPlottable, description, setCurrent).");
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

    private static bool TryPopulateLayerProperties(
        Dictionary<string, object?> body,
        int? colorAci,
        string? linetype,
        string? lineweight,
        bool? isPlottable,
        string? description,
        bool? setCurrent,
        out bool anyAdded,
        out string? error)
    {
        anyAdded = false;
        error = null;

        if (colorAci.HasValue)
        {
            if (colorAci.Value is < 1 or > 255)
            {
                error = "ACI color must be between 1 and 255.";
                return false;
            }

            body["color_index"] = colorAci.Value;
            anyAdded = true;
        }

        if (!string.IsNullOrWhiteSpace(linetype))
        {
            if (!SecurityPolicy.IsValidLayerLinetypeName(linetype, out error))
            {
                return false;
            }

            body["linetype"] = linetype.Trim();
            anyAdded = true;
        }

        if (!string.IsNullOrWhiteSpace(lineweight))
        {
            if (!LineWeightPolicy.TryParseLineWeight(lineweight, out _, out string display, out error))
            {
                return false;
            }

            body["lineweight"] = display;
            anyAdded = true;
        }

        if (isPlottable.HasValue)
        {
            body["is_plottable"] = isPlottable.Value;
            anyAdded = true;
        }

        if (description is not null)
        {
            if (HasControlChars(description))
            {
                error = "Layer description cannot contain control characters.";
                return false;
            }

            body["description"] = description;
            anyAdded = true;
        }

        if (setCurrent.HasValue)
        {
            body["set_current"] = setCurrent.Value;
            anyAdded = true;
        }

        return true;
    }
}
