using System;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Herramientas MCP de medición geométrica (distancias, áreas, perímetros) y mantenimiento
/// del dibujo (purga, auditoría y regeneración).
/// </summary>
/// <remarks>
/// Las mediciones se calculan localmente en el servidor MCP: son deterministas, devuelven el resultado
/// numérico directamente a la IA y no requieren que AutoCAD esté abierto.
/// </remarks>
[McpServerToolType]
public sealed class MeasurementTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="MeasurementTools"/>.
    /// </summary>
    /// <param name="gatewayClient">El cliente para comunicación con el Gateway de AutoCAD.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="gatewayClient"/> es nulo.</exception>
    public MeasurementTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Calcula la distancia 2D y 3D, los incrementos y el acimut entre dos puntos.
    /// </summary>
    /// <param name="x1">X del primer punto (m).</param>
    /// <param name="y1">Y del primer punto (m).</param>
    /// <param name="x2">X del segundo punto (m).</param>
    /// <param name="y2">Y del segundo punto (m).</param>
    /// <param name="z1">Z del primer punto (m), por defecto 0.</param>
    /// <param name="z2">Z del segundo punto (m), por defecto 0.</param>
    /// <returns>JSON con distancias, deltas, ángulo y pendiente.</returns>
    [McpServerTool(Name = "medir_distancia")]
    [Description("Calcula (sin AutoCAD) la distancia horizontal 2D, la distancia 3D, los incrementos ΔX/ΔY/ΔZ, el ángulo en grados (0 = Este, antihorario) y la pendiente en % entre dos puntos en metros.")]
    public string MedirDistancia(double x1, double y1, double x2, double y2, double z1 = 0, double z2 = 0)
    {
        if (!AllFinite(x1, y1, z1, x2, y2, z2))
        {
            return Error("Las coordenadas deben ser números finitos.");
        }

        double dx = x2 - x1, dy = y2 - y1, dz = z2 - z1;
        double d2 = Math.Sqrt(dx * dx + dy * dy);
        double d3 = Math.Sqrt(d2 * d2 + dz * dz);
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360.0;
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            distancia_2d = Math.Round(d2, 4),
            distancia_3d = Math.Round(d3, 4),
            delta_x = Math.Round(dx, 4),
            delta_y = Math.Round(dy, 4),
            delta_z = Math.Round(dz, 4),
            angulo_grados = Math.Round(angle, 4),
            pendiente_porcentaje = d2 > 0 ? Math.Round(dz / d2 * 100.0, 4) : (double?)null,
            unidades = "metros"
        });
    }

    /// <summary>
    /// Calcula el área (fórmula del polígono de Gauss) y el perímetro de un polígono cerrado.
    /// </summary>
    /// <param name="puntos">Vértices en formato <c>"x1,y1;x2,y2;..."</c> (mínimo 3, sin repetir el primero al final).</param>
    /// <returns>JSON con área en m², perímetro en m, centroide y sentido de giro.</returns>
    [McpServerTool(Name = "calcular_area_poligono")]
    [Description("Calcula (sin AutoCAD) el área en m², el perímetro en m, el centroide y el sentido de giro de un polígono cerrado definido por vértices 'x1,y1;x2,y2;...' (mínimo 3). Útil para superficies de ambientes, parcelas o secciones.")]
    public string CalcularAreaPoligono(string puntos)
    {
        var pts = ParsePoints(puntos, 2);
        if (pts == null || pts.Length < 3)
        {
            return Error("Formato de 'puntos' inválido. Use 'x1,y1;x2,y2;...' con al menos 3 vértices numéricos.");
        }

        // Si el usuario repitió el primer vértice al final, se ignora para no duplicar el cierre.
        int n = pts.Length;
        if (n > 3 && pts[0][0] == pts[n - 1][0] && pts[0][1] == pts[n - 1][1])
        {
            n--;
        }

        // Se trasladan las coordenadas al primer vértice para minimizar errores de redondeo con coordenadas UTM grandes.
        double ox = pts[0][0], oy = pts[0][1];
        double twiceArea = 0, perimeter = 0, cx = 0, cy = 0;
        for (int i = 0; i < n; i++)
        {
            double xi = pts[i][0] - ox, yi = pts[i][1] - oy;
            double xj = pts[(i + 1) % n][0] - ox, yj = pts[(i + 1) % n][1] - oy;
            double cross = xi * yj - xj * yi;
            twiceArea += cross;
            cx += (xi + xj) * cross;
            cy += (yi + yj) * cross;
            perimeter += Math.Sqrt((xj - xi) * (xj - xi) + (yj - yi) * (yj - yi));
        }

        if (Math.Abs(twiceArea) < 1e-12)
        {
            return Error("El polígono es degenerado (vértices colineales o área nula).");
        }

        double area = twiceArea / 2.0;
        return JsonSerializer.Serialize(new
        {
            status = "success",
            area_m2 = Math.Round(Math.Abs(area), 4),
            area_hectareas = Math.Round(Math.Abs(area) / 10000.0, 6),
            perimetro_m = Math.Round(perimeter, 4),
            centroide = new
            {
                x = Math.Round(cx / (3.0 * twiceArea) + ox, 4),
                y = Math.Round(cy / (3.0 * twiceArea) + oy, 4)
            },
            sentido = area > 0 ? "antihorario" : "horario",
            vertices = n
        });
    }

    /// <summary>
    /// Purga objetos no utilizados y, opcionalmente, audita y repara la base de datos del dibujo.
    /// </summary>
    /// <param name="tipo">Qué purgar: <c>todo</c>, <c>bloques</c>, <c>capas</c>, <c>tipos_linea</c>, <c>estilos_texto</c> o <c>estilos_cota</c>.</param>
    /// <param name="auditar">Si es <c>true</c>, ejecuta <c>AUDIT</c> con corrección de errores después de purgar.</param>
    /// <returns>Respuesta JSON del Gateway o un JSON de error de validación.</returns>
    [McpServerTool(Name = "limpiar_y_purgar_dibujo")]
    [Description("Elimina definiciones no utilizadas con -PURGE (dos pasadas para elementos anidados). 'tipo' admite: todo, bloques, capas, tipos_linea, estilos_texto, estilos_cota. Con 'auditar'=true ejecuta además AUDIT corrigiendo errores. Operación destructiva sobre definiciones sin uso: guarde antes si lo necesita.")]
    public async Task<string> LimpiarYPurgarDibujo(string tipo = "todo", bool auditar = true)
    {
        string? code = tipo?.Trim().ToLowerInvariant() switch
        {
            "todo" or "all" => "_A",
            "bloques" => "_B",
            "capas" => "_LA",
            "tipos_linea" => "_LT",
            "estilos_texto" => "_ST",
            "estilos_cota" => "_D",
            _ => null
        };

        if (code == null)
        {
            return Error("Tipo inválido. Use: todo, bloques, capas, tipos_linea, estilos_texto o estilos_cota.");
        }

        var sb = new StringBuilder();
        for (int pass = 0; pass < 2; pass++)
        {
            sb.Append($"_.-PURGE\n{code}\n*\n_N\n");
        }

        if (auditar)
        {
            sb.Append("_.AUDIT\n_Y\n");
        }

        return await _gatewayClient.ExecuteCommandAsync(sb.ToString());
    }

    /// <summary>
    /// Regenera todas las ventanas gráficas del dibujo (<c>REGENALL</c>).
    /// </summary>
    /// <returns>Respuesta JSON del Gateway.</returns>
    [McpServerTool(Name = "regenerar_todo")]
    [Description("Regenera todas las ventanas gráficas (_.REGENALL) para recalcular la visualización de curvas, textos y cambios de estado de capas.")]
    public async Task<string> RegenerarTodo() => await _gatewayClient.ExecuteCommandAsync("_.REGENALL\n");
}
