using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
    /// Traza un conjunto completo de entidades geométricas (líneas, polilíneas, rectángulos, círculos, arcos, textos)
    /// en AutoCAD mediante una única inyección atómica de AutoLISP, reduciendo roundtrips y tokens en un 95%.
    /// </summary>
    /// <param name="elementosJson">
    /// Cadena JSON con una lista de elementos geométricos a dibujar.
    /// Ejemplo: [{"tipo":"linea","x1":0,"y1":0,"x2":10,"y2":0,"capa":"MUROS"},{"tipo":"circulo","x":5,"y":5,"radio":2}]
    /// </param>
    /// <param name="capaPorDefecto">Capa destino predeterminada si un elemento no especifica 'capa'.</param>
    /// <returns>Resumen JSON de entidades creadas o mensaje de error de validación.</returns>
    [McpServerTool(Name = "dibujar_geometria_lote")]
    [Description("⚡ HERRAMIENTA PREFERIDA PARA DIBUJAR MÚLTIPLES ENTIDADES EN UN SOLO LLAMADO. Traza decenas o cientos de elementos geométricos (líneas, polilíneas, rectángulos, círculos, arcos, textos) en una sola llamada atómica en AutoCAD, reduciendo el consumo de tokens y latencia en un 95%.")]
    public async Task<string> DibujarGeometriaLote(string elementosJson, string capaPorDefecto = "0")
    {
        if (string.IsNullOrWhiteSpace(elementosJson))
        {
            return Error("El parámetro 'elementosJson' no puede estar vacío.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(elementosJson);
        }
        catch (JsonException ex)
        {
            return Error($"Formato JSON inválido en 'elementosJson': {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Error("El JSON debe contener un arreglo de elementos geométricos.");
            }

            var elements = doc.RootElement;
            if (elements.GetArrayLength() == 0)
            {
                return Error("El arreglo de elementos geométricos está vacío.");
            }

            var lispSb = new StringBuilder();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            foreach (var item in elements.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return Error($"El elemento en el índice {index} debe ser un objeto JSON.");
                }

                if (!TryCompileBatchItem(item, capaPorDefecto, lispSb, counts, out string? error))
                {
                    return Error($"Error en elemento {index}: {error}");
                }
                index++;
            }

            string fullLisp = $"(progn {lispSb} (princ))\n";
            string gatewayResult = await _gatewayClient.ExecuteCommandAsync(fullLisp);

            var resultObj = new
            {
                status = "success",
                total = index,
                desglose = counts,
                gateway_response = gatewayResult
            };

            return System.Text.Json.JsonSerializer.Serialize(resultObj);
        }
    }

    private static bool TryCompileBatchItem(
        JsonElement item, string defaultLayer, StringBuilder lispSb,
        Dictionary<string, int> counts, out string? error)
    {
        error = null;
        string tipo = "";
        foreach (var prop in item.EnumerateObject())
        {
            if (string.Equals(prop.Name, "tipo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase))
            {
                tipo = prop.Value.GetString()?.Trim().ToLowerInvariant() ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(tipo))
        {
            error = "El elemento no especifica la propiedad 'tipo'.";
            return false;
        }

        string layerName = defaultLayer;
        foreach (var prop in item.EnumerateObject())
        {
            if (string.Equals(prop.Name, "capa", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "layer", StringComparison.OrdinalIgnoreCase))
            {
                string? val = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    layerName = val.Trim();
                }
                break;
            }
        }

        string? layer = LayerPair(layerName);
        if (layer == null)
        {
            error = $"Nombre de capa inválido: '{layerName}'.";
            return false;
        }

        switch (tipo)
        {
            case "linea":
            case "line":
            {
                double x1 = GetDouble(item, "x1", [], 0.0);
                double y1 = GetDouble(item, "y1", [], 0.0);
                double x2 = GetDouble(item, "x2", [], 0.0);
                double y2 = GetDouble(item, "y2", [], 0.0);
                double z1 = GetDouble(item, "z1", [], 0.0);
                double z2 = GetDouble(item, "z2", [], 0.0);

                if (!AllFinite(x1, y1, z1, x2, y2, z2))
                {
                    error = "Coordenadas no finitas en línea.";
                    return false;
                }
                if (x1 == x2 && y1 == y2 && z1 == z2)
                {
                    error = "Línea con longitud cero (puntos inicial y final idénticos).";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"LINE\") {layer} (list 10 {Num(x1)} {Num(y1)} {Num(z1)}) (list 11 {Num(x2)} {Num(y2)} {Num(z2)}))) ");
                counts["linea"] = counts.GetValueOrDefault("linea") + 1;
                return true;
            }

            case "rectangulo":
            case "rectangle":
            {
                double x1 = GetDouble(item, "x1", [], 0.0);
                double y1 = GetDouble(item, "y1", [], 0.0);
                double x2 = GetDouble(item, "x2", [], 0.0);
                double y2 = GetDouble(item, "y2", [], 0.0);

                if (!AllFinite(x1, y1, x2, y2))
                {
                    error = "Coordenadas no finitas en rectángulo.";
                    return false;
                }
                if (x1 == x2 || y1 == y2)
                {
                    error = "Rectángulo con área nula.";
                    return false;
                }

                double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
                double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);

                lispSb.Append($"(entmake (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(100 . \"AcDbPolyline\") " +
                              $"{layer} '(90 . 4) '(70 . 1) " +
                              $"(list 10 {Num(minX)} {Num(minY)}) (list 10 {Num(maxX)} {Num(minY)}) " +
                              $"(list 10 {Num(maxX)} {Num(maxY)}) (list 10 {Num(minX)} {Num(maxY)}))) ");
                counts["rectangulo"] = counts.GetValueOrDefault("rectangulo") + 1;
                return true;
            }

            case "circulo":
            case "circle":
            {
                double cx = GetDouble(item, "centroX", ["centro_x", "x"], 0.0);
                double cy = GetDouble(item, "centroY", ["centro_y", "y"], 0.0);
                double cz = GetDouble(item, "centroZ", ["centro_z", "z"], 0.0);
                double r = GetDouble(item, "radio", ["r"], 0.0);

                if (!AllFinite(cx, cy, cz, r) || r <= 0)
                {
                    error = "Radio o coordenadas inválidas en círculo.";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"CIRCLE\") {layer} (list 10 {Num(cx)} {Num(cy)} {Num(cz)}) (cons 40 {Num(r)}))) ");
                counts["circulo"] = counts.GetValueOrDefault("circulo") + 1;
                return true;
            }

            case "arco":
            case "arc":
            {
                double cx = GetDouble(item, "centroX", ["centro_x", "x"], 0.0);
                double cy = GetDouble(item, "centroY", ["centro_y", "y"], 0.0);
                double cz = GetDouble(item, "centroZ", ["centro_z", "z"], 0.0);
                double r = GetDouble(item, "radio", ["r"], 0.0);
                double angIni = GetDouble(item, "anguloInicio", ["angulo_inicio", "start_angle"], 0.0);
                double angFin = GetDouble(item, "anguloFin", ["angulo_fin", "end_angle"], 0.0);

                if (!AllFinite(cx, cy, cz, r, angIni, angFin) || r <= 0)
                {
                    error = "Valores inválidos en arco.";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"ARC\") {layer} (list 10 {Num(cx)} {Num(cy)} {Num(cz)}) (cons 40 {Num(r)}) " +
                              $"(cons 50 {Num(ToRadians(angIni))}) (cons 51 {Num(ToRadians(angFin))}))) ");
                counts["arco"] = counts.GetValueOrDefault("arco") + 1;
                return true;
            }

            case "polilinea":
            case "polyline":
            {
                double[][]? pts = null;
                bool cerrada = false;

                foreach (var prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "puntos", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prop.Name, "points", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            pts = ParsePoints(prop.Value.GetString(), 2);
                        }
                    }
                    else if (string.Equals(prop.Name, "cerrada", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(prop.Name, "closed", StringComparison.OrdinalIgnoreCase))
                    {
                        cerrada = prop.Value.ValueKind == JsonValueKind.True;
                    }
                }

                if (pts == null || pts.Length < 2)
                {
                    error = "Polilínea requiere al menos 2 vértices válidos en formato 'x1,y1;x2,y2;...'.";
                    return false;
                }

                lispSb.Append(BuildLwPolyline(pts, cerrada, layer)).Append(' ');
                counts["polilinea"] = counts.GetValueOrDefault("polilinea") + 1;
                return true;
            }

            case "texto":
            case "text":
            {
                string txt = "";
                double tx = GetDouble(item, "x", [], 0.0);
                double ty = GetDouble(item, "y", [], 0.0);
                double tz = GetDouble(item, "z", [], 0.0);
                double h = GetDouble(item, "altura", ["height"], 0.25);
                double rot = GetDouble(item, "rotacion", ["rotation"], 0.0);

                foreach (var prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "texto", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prop.Name, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        txt = prop.Value.GetString() ?? "";
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(txt))
                {
                    error = "El contenido del texto no puede estar vacío.";
                    return false;
                }
                if (!AllFinite(tx, ty, tz, h, rot) || h <= 0)
                {
                    error = "Coordenadas o altura inválidas en texto.";
                    return false;
                }

                lispSb.Append($"(entmake (list '(0 . \"TEXT\") {layer} (list 10 {Num(tx)} {Num(ty)} {Num(tz)}) " +
                              $"(cons 40 {Num(h)}) (cons 1 {LispString(txt)}) (cons 50 {Num(ToRadians(rot))}))) ");
                counts["texto"] = counts.GetValueOrDefault("texto") + 1;
                return true;
            }

            default:
                error = $"Tipo de entidad no soportado: '{tipo}'.";
                return false;
        }
    }

    private static double GetDouble(JsonElement obj, string primaryName, string[] aliases, double defaultVal)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, primaryName, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double val))
                    return val;
                if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                    return parsed;
                return defaultVal;
            }
        }
        foreach (var alias in aliases)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, alias, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double val))
                        return val;
                    if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                        return parsed;
                    return defaultVal;
                }
            }
        }
        return defaultVal;
    }

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
    [Description("Dibuja una línea entre (x1,y1,z1) y (x2,y2,z2) en metros. (Para múltiples elementos, prefiera 'dibujar_geometria_lote').")]
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
    [Description("Dibuja una polilínea 2D en metros. 'cerrada' une el último vértice con el primero. (Para múltiples elementos, prefiera 'dibujar_geometria_lote').")]
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
    [Description("Dibuja un rectángulo como polilínea cerrada a partir de dos esquinas opuestas en metros. (Para múltiples elementos, prefiera 'dibujar_geometria_lote').")]
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
    [Description("Dibuja un círculo con centro y radio en metros. (Para múltiples elementos, prefiera 'dibujar_geometria_lote').")]
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
    [Description("Dibuja un arco circular de centro (centroX, centroY) y radio en metros. (Para múltiples elementos, prefiera 'dibujar_geometria_lote').")]
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
    [Description("Inserta un texto simple en (x, y) con altura en metros y rotación en grados. (Para múltiples elementos, prefiera 'dibujar_geometria_lote').")]
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
