using System;
using System.ComponentModel;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// MCP tools for layer management: creation with ACI color, linetype, and description,
/// setting active layer, and controlling layer states (on/off, frozen/thawed, locked/unlocked).
/// </summary>
[McpServerToolType]
public sealed class LayerTools
{
    private const string LispPrelude =
        "(vl-load-com) (setq *mcp-doc* (vla-get-ActiveDocument (vlax-get-acad-object))) ";

    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the AutoCAD Gateway.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gatewayClient"/> is null.</exception>
    public LayerTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    private Task<string> SendLispAsync(string body) =>
        _gatewayClient.ExecuteCommandAsync($"(progn {LispPrelude}{body} (princ))\n");

    /// <summary>
    /// Creates a layer (or updates existing) with ACI color, linetype, and description.
    /// </summary>
    /// <param name="name">Layer name (e.g. <c>ARCH-WALLS</c>).</param>
    /// <param name="colorAci">ACI color index between 1 and 255 (7 = white/black).</param>
    /// <param name="linetype">Linetype name; loaded automatically from <c>acadiso.lin</c> if not loaded.</param>
    /// <param name="description">Optional layer description.</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "create_layer")]
    [Description("Create a layer or update an existing one with ACI color (1-255), linetype, and description. Does not change current layer.")]
    public async Task<string> CreateLayer(
        string name, int colorAci = 7, string linetype = "Continuous", string description = "")
    {
        if (!IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        if (colorAci is < 1 or > 255)
        {
            return Error("ACI color must be between 1 and 255.");
        }

        if (!IsValidSymbolName(linetype))
        {
            return Error($"Invalid linetype name: '{linetype}'.");
        }

        string lt = LispString(linetype.Trim());
        string body =
            $"(if (not (tblsearch \"LTYPE\" {lt})) " +
            $"(vl-catch-all-apply 'vla-load (list (vla-get-Linetypes *mcp-doc*) {lt} \"acadiso.lin\"))) " +
            $"(setq *mcp-lay* (vla-add (vla-get-Layers *mcp-doc*) {LispString(name.Trim())})) " +
            $"(vla-put-Color *mcp-lay* {colorAci}) " +
            $"(if (tblsearch \"LTYPE\" {lt}) (vla-put-Linetype *mcp-lay* {lt})) " +
            $"(vla-put-Description *mcp-lay* {LispString(description ?? string.Empty)})";

        return await SendLispAsync(body);
    }

    /// <summary>
    /// Sets the active drawing layer (CLAYER variable).
    /// </summary>
    /// <param name="name">Name of an existing, non-frozen layer.</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "set_current_layer")]
    [Description("Set the active drawing layer (CLAYER). Layer must exist and not be frozen.")]
    public async Task<string> SetCurrentLayer(string name)
    {
        if (!IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        string nm = LispString(name.Trim());
        string body =
            $"(if (tblsearch \"LAYER\" {nm}) " +
            $"(if (vl-catch-all-error-p (vl-catch-all-apply 'setvar (list \"CLAYER\" {nm}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] Could not activate layer (frozen?): \" {nm}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] Layer does not exist: \" {nm})))";

        return await SendLispAsync(body);
    }

    /// <summary>
    /// Modifies the visibility, frozen state, or lock state of a layer.
    /// </summary>
    /// <param name="name">Name of an existing layer.</param>
    /// <param name="action">One of: <c>on</c>, <c>off</c>, <c>freeze</c>, <c>thaw</c>, <c>lock</c>, <c>unlock</c>.</param>
    /// <returns>JSON response from Gateway or validation error.</returns>
    [McpServerTool(Name = "set_layer_state")]
    [Description("Modify the state of a layer: on, off, freeze, thaw, lock, or unlock.")]
    public async Task<string> SetLayerState(string name, string action)
    {
        if (!IsValidSymbolName(name))
        {
            return Error($"Invalid layer name: '{name}'.");
        }

        (string Property, string Value)? op = action?.Trim().ToLowerInvariant() switch
        {
            "on" or "activar" => ("LayerOn", ":vlax-true"),
            "off" or "desactivar" => ("LayerOn", ":vlax-false"),
            "freeze" or "congelar" => ("Freeze", ":vlax-true"),
            "thaw" or "descongelar" => ("Freeze", ":vlax-false"),
            "lock" or "bloquear" => ("Lock", ":vlax-true"),
            "unlock" or "desbloquear" => ("Lock", ":vlax-false"),
            _ => null
        };

        if (op is not { } o)
        {
            return Error("Invalid action. Use: on, off, freeze, thaw, lock, or unlock.");
        }

        string nm = LispString(name.Trim());
        string body =
            $"(if (tblsearch \"LAYER\" {nm}) " +
            $"(if (vl-catch-all-error-p (vl-catch-all-apply 'vlax-put-property " +
            $"(list (vla-item (vla-get-Layers *mcp-doc*) {nm}) \"{o.Property}\" {o.Value}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] Could not change layer state: \" {nm})) " +
            $"(vla-regen *mcp-doc* acActiveViewport)) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] Layer does not exist: \" {nm})))";

        return await SendLispAsync(body);
    }
}
