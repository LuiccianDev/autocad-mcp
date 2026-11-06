using System;
using System.ComponentModel;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Herramientas MCP para la gestión de capas: creación con color, tipo de línea y descripción,
/// cambio de capa actual y control de estados (encendida, congelada, bloqueada).
/// </summary>
/// <remarks>
/// Se usa la API ActiveX de AutoLISP (<c>vla-*</c>) en lugar de <c>-LAYER</c> para evitar secuencias de
/// preguntas que varían entre versiones e idiomas, y para cargar tipos de línea sin abrir cuadros de diálogo.
/// </remarks>
[McpServerToolType]
public sealed class LayerTools
{
    private const string LispPrelude =
        "(vl-load-com) (setq *mcp-doc* (vla-get-ActiveDocument (vlax-get-acad-object))) ";

    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="LayerTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway de AutoCAD.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="gatewayClient"/> es nulo.</exception>
    public LayerTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    private Task<string> SendLispAsync(string body) =>
        _gatewayClient.ExecuteCommandAsync($"(progn {LispPrelude}{body} (princ))\n");

    /// <summary>
    /// Crea una capa (o actualiza la existente) con color ACI, tipo de línea y descripción.
    /// </summary>
    /// <param name="nombre">Nombre de la capa (ej. <c>ARQ-MUROS</c>).</param>
    /// <param name="colorAci">Color ACI entre 1 y 255 (7 = blanco/negro).</param>
    /// <param name="tipoLinea">Tipo de línea; si no está cargado se intenta cargar desde <c>acadiso.lin</c>.</param>
    /// <param name="descripcion">Descripción opcional de la capa.</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "crear_capa")]
    [Description("Crea una capa o actualiza una existente con color ACI (1-255), tipo de línea (se carga desde acadiso.lin si falta; si no existe se mantiene Continuous) y descripción. No cambia la capa actual.")]
    public async Task<string> CrearCapa(
        string nombre, int colorAci = 7, string tipoLinea = "Continuous", string descripcion = "")
    {
        if (!IsValidSymbolName(nombre))
        {
            return Error($"Nombre de capa inválido: '{nombre}'.");
        }

        if (colorAci is < 1 or > 255)
        {
            return Error("El color ACI debe estar entre 1 y 255.");
        }

        if (!IsValidSymbolName(tipoLinea))
        {
            return Error($"Nombre de tipo de línea inválido: '{tipoLinea}'.");
        }

        string lt = LispString(tipoLinea.Trim());
        string body =
            $"(if (not (tblsearch \"LTYPE\" {lt})) " +
            $"(vl-catch-all-apply 'vla-load (list (vla-get-Linetypes *mcp-doc*) {lt} \"acadiso.lin\"))) " +
            $"(setq *mcp-lay* (vla-add (vla-get-Layers *mcp-doc*) {LispString(nombre.Trim())})) " +
            $"(vla-put-Color *mcp-lay* {colorAci}) " +
            $"(if (tblsearch \"LTYPE\" {lt}) (vla-put-Linetype *mcp-lay* {lt})) " +
            $"(vla-put-Description *mcp-lay* {LispString(descripcion ?? string.Empty)})";

        return await SendLispAsync(body);
    }

    /// <summary>
    /// Establece la capa actual de trabajo (variable <c>CLAYER</c>).
    /// </summary>
    /// <param name="nombre">Nombre de una capa existente y no congelada.</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "establecer_capa_actual")]
    [Description("Establece la capa actual (CLAYER). La capa debe existir y no estar congelada; en caso contrario AutoCAD lo informa en la línea de comandos sin cambios.")]
    public async Task<string> EstablecerCapaActual(string nombre)
    {
        if (!IsValidSymbolName(nombre))
        {
            return Error($"Nombre de capa inválido: '{nombre}'.");
        }

        string name = LispString(nombre.Trim());
        string body =
            $"(if (tblsearch \"LAYER\" {name}) " +
            $"(if (vl-catch-all-error-p (vl-catch-all-apply 'setvar (list \"CLAYER\" {name}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] No se pudo activar la capa (congelada?): \" {name}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] La capa no existe: \" {name})))";

        return await SendLispAsync(body);
    }

    /// <summary>
    /// Cambia el estado de visibilidad, congelación o bloqueo de una capa.
    /// </summary>
    /// <param name="nombre">Nombre de una capa existente.</param>
    /// <param name="accion">Una de: <c>activar</c>, <c>desactivar</c>, <c>congelar</c>, <c>descongelar</c>, <c>bloquear</c>, <c>desbloquear</c>.</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "establecer_estado_capa")]
    [Description("Cambia el estado de una capa. 'accion' admite: activar, desactivar, congelar, descongelar, bloquear, desbloquear. La capa actual no se puede congelar.")]
    public async Task<string> EstablecerEstadoCapa(string nombre, string accion)
    {
        if (!IsValidSymbolName(nombre))
        {
            return Error($"Nombre de capa inválido: '{nombre}'.");
        }

        (string Property, string Value)? op = accion?.Trim().ToLowerInvariant() switch
        {
            "activar" => ("LayerOn", ":vlax-true"),
            "desactivar" => ("LayerOn", ":vlax-false"),
            "congelar" => ("Freeze", ":vlax-true"),
            "descongelar" => ("Freeze", ":vlax-false"),
            "bloquear" => ("Lock", ":vlax-true"),
            "desbloquear" => ("Lock", ":vlax-false"),
            _ => null
        };

        if (op is not { } o)
        {
            return Error("Acción inválida. Use: activar, desactivar, congelar, descongelar, bloquear o desbloquear.");
        }

        string name = LispString(nombre.Trim());
        string body =
            $"(if (tblsearch \"LAYER\" {name}) " +
            $"(if (vl-catch-all-error-p (vl-catch-all-apply 'vlax-put-property " +
            $"(list (vla-item (vla-get-Layers *mcp-doc*) {name}) \"{o.Property}\" {o.Value}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] No se pudo cambiar el estado de la capa: \" {name})) " +
            $"(vla-regen *mcp-doc* acActiveViewport)) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] La capa no existe: \" {name})))";

        return await SendLispAsync(body);
    }
}
