using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Herramientas MCP de dibujo paramétrico tipado (líneas, polilíneas, rectángulos, círculos, arcos, textos y bloques).
/// </summary>
/// <remarks>
/// La geometría se crea con <c>entmake</c> de AutoLISP en lugar de comandos interactivos: así el resultado
/// no depende de los modos de referencia a objetos (OSNAP), no altera la capa actual y crea la capa destino
/// automáticamente si no existe. Todas las coordenadas se expresan en metros.
/// </remarks>
[McpServerToolType]
public sealed class DrawingTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="DrawingTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway de AutoCAD.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="gatewayClient"/> es nulo.</exception>
    public DrawingTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Envuelve una expresión entmake en un bloque LISP silencioso y la envía al Gateway.</summary>
    private Task<string> SendLispAsync(string body) =>
        _gatewayClient.ExecuteCommandAsync($"(progn {body} (princ))\n");

    /// <summary>Par DXF de capa validado, o <c>null</c> si el nombre es inválido.</summary>
    private static string? LayerPair(string capa) =>
        IsValidSymbolName(capa) ? $"(cons 8 {LispString(capa.Trim())})" : null;

    /// <summary>
    /// Dibuja un segmento de línea entre dos puntos 3D.
    /// </summary>
    /// <param name="x1">Coordenada X inicial (m).</param>
    /// <param name="y1">Coordenada Y inicial (m).</param>
    /// <param name="x2">Coordenada X final (m).</param>
    /// <param name="y2">Coordenada Y final (m).</param>
    /// <param name="z1">Coordenada Z inicial (m), por defecto 0.</param>
    /// <param name="z2">Coordenada Z final (m), por defecto 0.</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "dibujar_linea")]
    [Description("Dibuja una línea entre (x1,y1,z1) y (x2,y2,z2) en metros, en la capa indicada (se crea si no existe). No depende de OSNAP ni cambia la capa actual.")]
    public async Task<string> DibujarLinea(
        double x1, double y1, double x2, double y2,
        double z1 = 0, double z2 = 0, string capa = "0")
    {
        if (!AllFinite(x1, y1, z1, x2, y2, z2))
        {
            return Error("Las coordenadas deben ser números finitos.");
        }

        if (x1 == x2 && y1 == y2 && z1 == z2)
        {
            return Error("Los puntos inicial y final son idénticos: la línea tendría longitud cero.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"LINE\") {layer} (list 10 {Num(x1)} {Num(y1)} {Num(z1)}) (list 11 {Num(x2)} {Num(y2)} {Num(z2)})))");
    }

    /// <summary>
    /// Dibuja una polilínea ligera (LWPOLYLINE) a partir de una lista de vértices.
    /// </summary>
    /// <param name="puntos">Vértices en formato <c>"x1,y1;x2,y2;..."</c> (mínimo 2).</param>
    /// <param name="cerrada">Si es <c>true</c>, cierra la polilínea uniendo el último vértice con el primero.</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "dibujar_polilinea")]
    [Description("Dibuja una polilínea 2D a partir de vértices 'x1,y1;x2,y2;...' (mínimo 2, en metros). 'cerrada' une el último vértice con el primero.")]
    public async Task<string> DibujarPolilinea(string puntos, bool cerrada = false, string capa = "0")
    {
        var pts = ParsePoints(puntos, 2);
        if (pts == null || pts.Length < 2)
        {
            return Error("Formato de 'puntos' inválido. Use 'x1,y1;x2,y2;...' con al menos 2 vértices numéricos.");
        }

        if (cerrada && pts.Length < 3)
        {
            return Error("Una polilínea cerrada requiere al menos 3 vértices.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        return await SendLispAsync(BuildLwPolyline(pts, cerrada, layer));
    }

    /// <summary>
    /// Dibuja un rectángulo (polilínea cerrada) definido por dos esquinas opuestas.
    /// </summary>
    /// <param name="x1">X de la primera esquina (m).</param>
    /// <param name="y1">Y de la primera esquina (m).</param>
    /// <param name="x2">X de la esquina opuesta (m).</param>
    /// <param name="y2">Y de la esquina opuesta (m).</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "dibujar_rectangulo")]
    [Description("Dibuja un rectángulo como polilínea cerrada a partir de dos esquinas opuestas (x1,y1) y (x2,y2) en metros.")]
    public async Task<string> DibujarRectangulo(double x1, double y1, double x2, double y2, string capa = "0")
    {
        if (!AllFinite(x1, y1, x2, y2))
        {
            return Error("Las coordenadas deben ser números finitos.");
        }

        if (x1 == x2 || y1 == y2)
        {
            return Error("Las esquinas deben diferir en X y en Y: el rectángulo tendría área cero.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        double[][] pts = [[x1, y1], [x2, y1], [x2, y2], [x1, y2]];
        return await SendLispAsync(BuildLwPolyline(pts, closed: true, layer));
    }

    /// <summary>
    /// Dibuja un círculo dado su centro y radio.
    /// </summary>
    /// <param name="centroX">X del centro (m).</param>
    /// <param name="centroY">Y del centro (m).</param>
    /// <param name="radio">Radio en metros (mayor que cero).</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "dibujar_circulo")]
    [Description("Dibuja un círculo con centro (centroX, centroY) y radio en metros.")]
    public async Task<string> DibujarCirculo(double centroX, double centroY, double radio, string capa = "0")
    {
        if (!AllFinite(centroX, centroY, radio) || radio <= 0)
        {
            return Error("El centro debe ser numérico y el radio mayor que cero.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"CIRCLE\") {layer} (list 10 {Num(centroX)} {Num(centroY)} 0.0) (cons 40 {Num(radio)})))");
    }

    /// <summary>
    /// Dibuja un arco circular en sentido antihorario desde el ángulo inicial hasta el final.
    /// </summary>
    /// <param name="centroX">X del centro (m).</param>
    /// <param name="centroY">Y del centro (m).</param>
    /// <param name="radio">Radio en metros (mayor que cero).</param>
    /// <param name="anguloInicio">Ángulo inicial en grados (0 = Este, antihorario).</param>
    /// <param name="anguloFin">Ángulo final en grados (0 = Este, antihorario).</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "dibujar_arco")]
    [Description("Dibuja un arco circular de centro (centroX, centroY) y radio en metros, recorrido en sentido antihorario desde anguloInicio hasta anguloFin (grados, 0 = Este).")]
    public async Task<string> DibujarArco(
        double centroX, double centroY, double radio,
        double anguloInicio, double anguloFin, string capa = "0")
    {
        if (!AllFinite(centroX, centroY, radio, anguloInicio, anguloFin) || radio <= 0)
        {
            return Error("Los valores deben ser numéricos y el radio mayor que cero.");
        }

        if (Math.Abs(anguloFin - anguloInicio) % 360.0 == 0)
        {
            return Error("Los ángulos inicial y final coinciden; use 'dibujar_circulo' para un círculo completo.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"ARC\") {layer} (list 10 {Num(centroX)} {Num(centroY)} 0.0) (cons 40 {Num(radio)}) " +
            $"(cons 50 {Num(ToRadians(anguloInicio))}) (cons 51 {Num(ToRadians(anguloFin))})))");
    }

    /// <summary>
    /// Inserta un texto de una línea (DBText) en el espacio modelo.
    /// </summary>
    /// <param name="texto">Contenido del texto.</param>
    /// <param name="x">X del punto de inserción (m).</param>
    /// <param name="y">Y del punto de inserción (m).</param>
    /// <param name="altura">Altura del texto en metros, por defecto 0.25.</param>
    /// <param name="rotacion">Rotación en grados, por defecto 0.</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "insertar_texto")]
    [Description("Inserta un texto de una línea en (x, y) con altura en metros (por defecto 0.25) y rotación en grados, en la capa indicada.")]
    public async Task<string> InsertarTexto(
        string texto, double x, double y,
        double altura = 0.25, double rotacion = 0.0, string capa = "0")
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return Error("El texto no puede estar vacío.");
        }

        if (!AllFinite(x, y, altura, rotacion) || altura <= 0)
        {
            return Error("Las coordenadas deben ser numéricas y la altura mayor que cero.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        return await SendLispAsync(
            $"(entmake (list '(0 . \"TEXT\") {layer} (list 10 {Num(x)} {Num(y)} 0.0) (cons 40 {Num(altura)}) " +
            $"(cons 1 {LispString(texto)}) (cons 50 {Num(ToRadians(rotacion))})))");
    }

    /// <summary>
    /// Inserta una referencia a un bloque ya definido en el dibujo.
    /// </summary>
    /// <param name="nombreBloque">Nombre de la definición de bloque existente (ver <c>listar_bloques_disponibles</c>).</param>
    /// <param name="x">X del punto de inserción (m).</param>
    /// <param name="y">Y del punto de inserción (m).</param>
    /// <param name="escala">Factor de escala uniforme (mayor que cero), por defecto 1.</param>
    /// <param name="rotacion">Rotación en grados, por defecto 0.</param>
    /// <param name="capa">Capa destino (se crea si no existe).</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "insertar_bloque")]
    [Description("Inserta una referencia de un bloque YA DEFINIDO en el dibujo (consulte 'listar_bloques_disponibles') en (x, y) con escala uniforme y rotación en grados. Si el bloque no existe, AutoCAD lo informa en la línea de comandos y no se inserta nada. Los atributos del bloque no se rellenan.")]
    public async Task<string> InsertarBloque(
        string nombreBloque, double x, double y,
        double escala = 1.0, double rotacion = 0.0, string capa = "0")
    {
        if (!IsValidSymbolName(nombreBloque))
        {
            return Error($"Nombre de bloque inválido: '{nombreBloque}'.");
        }

        if (!AllFinite(x, y, escala, rotacion) || escala <= 0)
        {
            return Error("Las coordenadas deben ser numéricas y la escala mayor que cero.");
        }

        if (LayerPair(capa) is not { } layer)
        {
            return Error($"Nombre de capa inválido: '{capa}'.");
        }

        string name = LispString(nombreBloque.Trim());
        string s = Num(escala);
        return await SendLispAsync(
            $"(if (tblsearch \"BLOCK\" {name}) " +
            $"(entmake (list '(0 . \"INSERT\") (cons 2 {name}) {layer} (list 10 {Num(x)} {Num(y)} 0.0) " +
            $"(cons 41 {s}) (cons 42 {s}) (cons 43 {s}) (cons 50 {Num(ToRadians(rotacion))}))) " +
            $"(princ (strcat \"\\n[AutoCAD MCP] Bloque no definido: \" {name})))");
    }

    /// <summary>Construye la expresión entmake de una LWPOLYLINE.</summary>
    private static string BuildLwPolyline(double[][] pts, bool closed, string layer)
    {
        var sb = new StringBuilder();
        sb.Append("(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") ")
          .Append(layer)
          .Append(" '(100 . \"AcDbPolyline\") (cons 90 ")
          .Append(pts.Length.ToString(CultureInfo.InvariantCulture))
          .Append(") (cons 70 ")
          .Append(closed ? '1' : '0')
          .Append(')');

        foreach (var p in pts)
        {
            sb.Append(" (list 10 ").Append(Num(p[0])).Append(' ').Append(Num(p[1])).Append(')');
        }

        sb.Append("))");
        return sb.ToString();
    }
}
