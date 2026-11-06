using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using AutoCAD.McpServer.Services;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Proporciona herramientas MCP de solo lectura para inspeccionar el estado, entidades y contexto de AutoCAD.
/// </summary>
[McpServerToolType]
public sealed class ContextTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="ContextTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway de AutoCAD.</param>
    public ContextTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Obtiene metadatos del dibujo activo en AutoCAD: nombre de archivo DWG, estado de solo lectura y unidades configuradas.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es el JSON con los metadatos del documento DWG.</returns>
    [McpServerTool(Name = "obtener_info_dibujo")]
    [Description("Obtiene metadatos del dibujo activo en AutoCAD: nombre de archivo DWG, estado de solo lectura y unidades configuradas.")]
    public async Task<string> ObtenerInfoDibujo() => await _gatewayClient.GetContextAsync("context/info");

    /// <summary>
    /// Lee y devuelve un resumen con el conteo por tipo de todas las entidades en el espacio modelo de AutoCAD.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es el resumen de entidades agrupadas por tipo.</returns>
    [McpServerTool(Name = "leer_objetos_autocad")]
    [Description("Lee y devuelve un resumen (conteos por tipo) de todas las entidades y objetos que existen actualmente en el dibujo de AutoCAD (ModelSpace).")]
    public async Task<string> LeerObjetosAutoCAD() => await _gatewayClient.GetContextAsync("context/entities");

    /// <summary>
    /// Obtiene la lista completa de capas del dibujo de AutoCAD con sus estados (bloqueada, congelada, apagada) y código de color.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la lista de capas.</returns>
    [McpServerTool(Name = "leer_capas_autocad")]
    [Description("Obtiene la lista completa de capas del dibujo de AutoCAD con sus estados (bloqueada, congelada, apagada) y código de color.")]
    public async Task<string> LeerCapasAutoCAD() => await _gatewayClient.GetContextAsync("context/layers");

    /// <summary>
    /// Lista todos los textos (DBText y MText) del dibujo con sus contenidos, coordenadas exactas y capa.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es el listado de textos y ubicaciones.</returns>
    [McpServerTool(Name = "leer_textos_autocad")]
    [Description("Lista todos los textos (DBText y MText) del dibujo con sus contenidos, coordenadas exactas (X, Y) y capa para ubicar ambientes (ej. DORMITORIO, SALA, BAÑO).")]
    public async Task<string> LeerTextosAutoCAD() => await _gatewayClient.GetContextAsync("context/texts");

    /// <summary>
    /// Obtiene las coordenadas mínimas (X, Y) y máximas (X, Y) del dibujo actual (Extmin y Extmax).
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado son las coordenadas de extensión del dibujo.</returns>
    [McpServerTool(Name = "obtener_limites_dibujo")]
    [Description("Obtiene las coordenadas mínimas (X, Y) y máximas (X, Y) del dibujo actual (Extmin y Extmax) para calcular áreas de diseño libres.")]
    public async Task<string> ObtenerLimitesDibujo() => await _gatewayClient.GetContextAsync("context/extents");

    /// <summary>
    /// Obtiene el número de entidades seleccionadas actualmente por el usuario con el ratón en AutoCAD.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la cantidad de entidades seleccionadas.</returns>
    [McpServerTool(Name = "leer_seleccion_autocad")]
    [Description("Obtiene el número de entidades seleccionadas actualmente por el usuario con el ratón en AutoCAD.")]
    public async Task<string> LeerSeleccionAutoCAD() => await _gatewayClient.GetContextAsync("context/selection");

    /// <summary>
    /// Obtiene los nombres de todas las definiciones de bloques cargadas en el archivo actual.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es el listado de nombres de bloques disponibles.</returns>
    [McpServerTool(Name = "listar_bloques_disponibles")]
    [Description("Obtiene los nombres de todas las definiciones de bloques cargadas en el archivo actual.")]
    public async Task<string> ListarBloquesDisponibles() => await _gatewayClient.GetContextAsync("context/blocks");

    /// <summary>
    /// Captura una imagen de la pantalla y ventana activa de AutoCAD y devuelve la ruta del archivo PNG generado para inspección visual.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es el JSON con la ruta del archivo PNG generado y sus dimensiones.</returns>
    [McpServerTool(Name = "capturar_pantalla_autocad")]
    [Description("Captura una imagen PNG de la ventana activa de AutoCAD ('ojos para la IA') y guarda la captura en disco. Devuelve la ruta absoluta del archivo para verificar visualmente planos, layouts o geometrías.")]
    public async Task<string> CapturarPantallaAutoCAD() => await _gatewayClient.GetContextAsync("context/screenshot");
}
