using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Herramientas MCP para Autodesk Civil 3D 2027: superficies, cotas de terreno y alineamientos
/// (lectura y creación). Requieren Civil 3D con el Gateway activo.
/// </summary>
[McpServerToolType]
public sealed class Civil3DTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="Civil3DTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway.</param>
    public Civil3DTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Err(string mensaje) =>
        JsonSerializer.Serialize(new { status = "error", message = mensaje });

    /// <summary>
    /// Convierte "v1,v2[,v3];..." en filas de números. Devuelve null si el formato es inválido.
    /// </summary>
    private static double[][]? ParsePuntos(string puntos, int columnas) => CadFormat.ParsePoints(puntos, columnas);

    /// <summary>Número máximo de muestras por perfil para acotar el tiempo de bloqueo del hilo UI de AutoCAD.</summary>
    private const int MaxMuestrasPerfil = 500;

    /// <summary>Lee una propiedad numérica de un JSON del Gateway; null si no existe o no es numérica.</summary>
    private static double? LeerNumero(JsonElement root, string propiedad) =>
        root.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>Indica si la respuesta JSON del Gateway tiene status = "success".</summary>
    private static bool EsExito(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("status", out var s) && s.GetString() == "success";

    /// <summary>
    /// Analiza una respuesta del Gateway tolerando cuerpos no JSON (p. ej. errores de conexión con comillas sin escapar):
    /// en ese caso devuelve un documento de error equivalente.
    /// </summary>
    private static JsonDocument ParseSeguro(string respuesta)
    {
        try
        {
            return JsonDocument.Parse(respuesta);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(Err(respuesta));
        }
    }

    /// <summary>
    /// Muestrea el perfil longitudinal del terreno a lo largo de un alineamiento.
    /// </summary>
    /// <param name="intervalo">Distancia entre muestras en metros (mayor que cero).</param>
    /// <param name="alineamiento">Nombre del alineamiento; si se omite se usa el primero.</param>
    /// <param name="superficie">Nombre de la superficie TIN; si se omite se usa la primera.</param>
    /// <returns>JSON con la lista de progresivas, coordenadas y cotas, más un resumen de cotas y pendiente.</returns>
    [McpServerTool(Name = "consultar_perfil_longitudinal")]
    [Description("Muestrea el perfil longitudinal del terreno a lo largo de un alineamiento de Civil 3D: cada 'intervalo' metros (más la progresiva final) devuelve progresiva, estaca, X, Y y cota Z de la superficie. Incluye cota mínima/máxima y pendiente media. Máximo 500 muestras; los puntos fuera de la superficie se reportan con cota null.")]
    public async Task<string> ConsultarPerfilLongitudinal(
        double intervalo = 20.0, string? alineamiento = null, string? superficie = null)
    {
        if (double.IsNaN(intervalo) || double.IsInfinity(intervalo) || intervalo <= 0)
        {
            return Err("El intervalo debe ser un número mayor que cero.");
        }

        // 1. Rango de progresivas del alineamiento.
        using var listaDoc = ParseSeguro(await _gatewayClient.GetContextAsync("civil/alignments"));
        var listaRoot = listaDoc.RootElement;
        if (!EsExito(listaRoot) || !listaRoot.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return Err("No se encontraron alineamientos (¿Civil 3D activo y Gateway cargado?).");
        }

        JsonElement? elegido = null;
        foreach (var a in data.EnumerateArray())
        {
            if (string.IsNullOrWhiteSpace(alineamiento) ||
                string.Equals(a.GetProperty("nombre").GetString(), alineamiento, StringComparison.OrdinalIgnoreCase))
            {
                elegido = a;
                break;
            }
        }

        if (elegido is not { } al)
        {
            return Err($"Alineamiento no encontrado: '{alineamiento}'.");
        }

        string nombreAl = al.GetProperty("nombre").GetString() ?? string.Empty;
        double inicio = al.GetProperty("progresiva_inicial").GetDouble();
        double fin = al.GetProperty("progresiva_final").GetDouble();

        // 2. Progresivas a muestrear (incluye siempre la final).
        var progresivas = new System.Collections.Generic.List<double>();
        for (double st = inicio; st < fin - 1e-6; st += intervalo)
        {
            progresivas.Add(st);
        }

        progresivas.Add(fin);
        if (progresivas.Count > MaxMuestrasPerfil)
        {
            return Err($"El intervalo genera {progresivas.Count} muestras (máximo {MaxMuestrasPerfil}). " +
                       $"Use un intervalo de al menos {Math.Ceiling((fin - inicio) / (MaxMuestrasPerfil - 1))} m.");
        }

        string qAl = $"&alignment={Uri.EscapeDataString(nombreAl)}";
        string qSup = string.IsNullOrWhiteSpace(superficie) ? string.Empty : $"&surface={Uri.EscapeDataString(superficie)}";

        // 3. Coordenada y cota en cada progresiva.
        var muestras = new System.Collections.Generic.List<object>(progresivas.Count);
        double? zMin = null, zMax = null, zIni = null, zFin = null;
        foreach (double st in progresivas)
        {
            using var ptDoc = ParseSeguro(await _gatewayClient.GetContextAsync(
                $"civil/alignment/point-at-station?station={Num(st)}&offset=0{qAl}"));
            var pt = ptDoc.RootElement;
            if (!EsExito(pt) || LeerNumero(pt, "x") is not { } x || LeerNumero(pt, "y") is not { } y)
            {
                muestras.Add(new { progresiva = st, x = (double?)null, y = (double?)null, cota = (double?)null });
                continue;
            }

            using var zDoc = ParseSeguro(await _gatewayClient.GetContextAsync(
                $"civil/surface/elevation?x={Num(x)}&y={Num(y)}{qSup}"));
            double? z = EsExito(zDoc.RootElement) ? LeerNumero(zDoc.RootElement, "elevacion") : null;

            if (z is { } zv)
            {
                zMin = zMin is null ? zv : Math.Min(zMin.Value, zv);
                zMax = zMax is null ? zv : Math.Max(zMax.Value, zv);
                zIni ??= zv;
                zFin = zv;
            }

            muestras.Add(new
            {
                progresiva = Math.Round(st, 3),
                x = Math.Round(x, 4),
                y = Math.Round(y, 4),
                cota = z is null ? (double?)null : Math.Round(z.Value, 3)
            });
        }

        double longitud = fin - inicio;
        return JsonSerializer.Serialize(new
        {
            status = "success",
            alineamiento = nombreAl,
            superficie = string.IsNullOrWhiteSpace(superficie) ? "(primera disponible)" : superficie,
            intervalo,
            progresiva_inicial = inicio,
            progresiva_final = fin,
            muestras_total = muestras.Count,
            cota_minima = zMin,
            cota_maxima = zMax,
            pendiente_media_porcentaje = zIni is { } a0 && zFin is { } a1 && longitud > 0
                ? Math.Round((a1 - a0) / longitud * 100.0, 4)
                : (double?)null,
            muestras
        });
    }

    /// <summary>Verifica si el host activo es Civil 3D y resume superficies y alineamientos.</summary>
    [McpServerTool(Name = "verificar_estado_civil3d")]
    [Description("Verifica si el dibujo activo corre sobre Civil 3D 2027 y cuenta superficies y alineamientos. En AutoCAD estándar responde civil_available=false.")]
    public async Task<string> VerificarEstadoCivil3D() => await _gatewayClient.GetContextAsync("civil/status");

    /// <summary>Lista las superficies con cota mínima y máxima.</summary>
    [McpServerTool(Name = "listar_superficies_civil3d")]
    [Description("Lista las superficies topográficas (TIN) del dibujo con nombre, tipo, cota mínima, cota máxima y número de triángulos.")]
    public async Task<string> ListarSuperficies() => await _gatewayClient.GetContextAsync("civil/surfaces");

    /// <summary>Obtiene la cota del terreno en un punto.</summary>
    /// <param name="x">Coordenada Este (X) en metros.</param>
    /// <param name="y">Coordenada Norte (Y) en metros.</param>
    /// <param name="superficie">Nombre de la superficie; si se omite se usa la primera.</param>
    [McpServerTool(Name = "consultar_elevacion_terreno")]
    [Description("Devuelve la cota Z exacta del terreno (superficie TIN de Civil 3D) en el punto (x, y). Falla si el punto está fuera de la superficie.")]
    public async Task<string> ConsultarElevacion(double x, double y, string? superficie = null) =>
        await _gatewayClient.GetContextAsync(
            $"civil/surface/elevation?x={Num(x)}&y={Num(y)}" +
            (string.IsNullOrWhiteSpace(superficie) ? string.Empty : $"&surface={Uri.EscapeDataString(superficie)}"));

    /// <summary>Lista los alineamientos con sus progresivas.</summary>
    [McpServerTool(Name = "listar_alineamientos_civil3d")]
    [Description("Lista los alineamientos horizontales con nombre, progresiva inicial/final, estacas y longitud total.")]
    public async Task<string> ListarAlineamientos() => await _gatewayClient.GetContextAsync("civil/alignments");

    /// <summary>Calcula progresiva y desfase de un punto respecto a un alineamiento.</summary>
    /// <param name="x">Coordenada Este (X) en metros.</param>
    /// <param name="y">Coordenada Norte (Y) en metros.</param>
    /// <param name="alineamiento">Nombre del alineamiento; si se omite se usa el primero.</param>
    [McpServerTool(Name = "consultar_estacion_alineamiento")]
    [Description("Calcula la progresiva (estaca) y el desfase lateral de un punto (x, y) respecto a un alineamiento de Civil 3D.")]
    public async Task<string> ConsultarEstacion(double x, double y, string? alineamiento = null) =>
        await _gatewayClient.GetContextAsync(
            $"civil/alignment/station-offset?x={Num(x)}&y={Num(y)}" +
            (string.IsNullOrWhiteSpace(alineamiento) ? string.Empty : $"&alignment={Uri.EscapeDataString(alineamiento)}"));

    /// <summary>Obtiene la coordenada (X, Y) de una progresiva y desfase.</summary>
    /// <param name="progresiva">Progresiva en metros (por ejemplo 1250.5 = 1+250.50).</param>
    /// <param name="desfase">Desfase lateral en metros (+ derecha, - izquierda).</param>
    /// <param name="alineamiento">Nombre del alineamiento; si se omite se usa el primero.</param>
    [McpServerTool(Name = "obtener_coordenada_en_estacion")]
    [Description("Devuelve la coordenada (x, y) correspondiente a una progresiva y desfase sobre un alineamiento de Civil 3D.")]
    public async Task<string> ObtenerCoordenadaEnEstacion(double progresiva, double desfase = 0, string? alineamiento = null) =>
        await _gatewayClient.GetContextAsync(
            $"civil/alignment/point-at-station?station={Num(progresiva)}&offset={Num(desfase)}" +
            (string.IsNullOrWhiteSpace(alineamiento) ? string.Empty : $"&alignment={Uri.EscapeDataString(alineamiento)}"));

    /// <summary>Crea un alineamiento horizontal desde PIs.</summary>
    /// <param name="nombre">Nombre único del alineamiento.</param>
    /// <param name="puntos">PIs como "x1,y1;x2,y2;x3,y3" (mínimo 2, en orden de progresiva).</param>
    /// <param name="radio">Radio de curva circular en metros; 0 = sin curvas.</param>
    /// <param name="capa">Capa destino (se crea si no existe); por defecto "0".</param>
    [McpServerTool(Name = "crear_alineamiento_civil3d")]
    [Description("Crea un alineamiento horizontal en Civil 3D a partir de PIs 'x1,y1;x2,y2;...' con curvas circulares tangentes de radio fijo (0 = sin curvas). Rechaza nombres duplicados y radios que no caben en el trazo.")]
    public async Task<string> CrearAlineamiento(string nombre, string puntos, double radio = 0, string? capa = null)
    {
        var pts = ParsePuntos(puntos, 2);
        if (pts == null || pts.Length < 2)
        {
            return Err("Formato de 'puntos' invalido. Use 'x1,y1;x2,y2;...' con al menos 2 PIs numericos.");
        }

        string json = JsonSerializer.Serialize(new
        {
            nombre,
            puntos = pts.Select(p => new { x = p[0], y = p[1] }),
            radio,
            capa
        });
        return await _gatewayClient.PostJsonAsync("civil/alignment/create", json);
    }

    /// <summary>Crea una superficie TIN desde puntos topográficos.</summary>
    /// <param name="nombre">Nombre único de la superficie.</param>
    /// <param name="puntos">Puntos como "x1,y1,z1;x2,y2,z2;..." (mínimo 3, no colineales).</param>
    [McpServerTool(Name = "crear_superficie_civil3d")]
    [Description("Crea una superficie TIN en Civil 3D a partir de puntos topográficos 'x1,y1,z1;x2,y2,z2;...' (mínimo 3). Rechaza nombres duplicados.")]
    public async Task<string> CrearSuperficie(string nombre, string puntos)
    {
        var pts = ParsePuntos(puntos, 3);
        if (pts == null || pts.Length < 3)
        {
            return Err("Formato de 'puntos' invalido. Use 'x1,y1,z1;x2,y2,z2;...' con al menos 3 puntos numericos.");
        }

        string json = JsonSerializer.Serialize(new
        {
            nombre,
            puntos = pts.Select(p => new { x = p[0], y = p[1], z = p[2] })
        });
        return await _gatewayClient.PostJsonAsync("civil/surface/create", json);
    }
}
