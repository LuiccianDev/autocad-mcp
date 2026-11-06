using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Utilidades compartidas de formato para construir expresiones AutoLISP y comandos CAD seguros
/// (números en cultura invariante, cadenas LISP escapadas, parseo de listas de puntos y respuestas JSON).
/// </summary>
internal static class CadFormat
{
    /// <summary>Formatea un número en cultura invariante con precisión de ida y vuelta.</summary>
    /// <param name="value">Valor numérico a formatear.</param>
    /// <returns>Representación textual con punto decimal.</returns>
    public static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Convierte un texto en un literal de cadena AutoLISP entre comillas, escapando barras invertidas y comillas.
    /// </summary>
    /// <param name="value">Texto a escapar.</param>
    /// <returns>Literal LISP, por ejemplo <c>"MUROS"</c>.</returns>
    public static string LispString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n':
                case '\r': sb.Append(' '); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Valida un nombre de capa, bloque o tipo de línea de AutoCAD (no vacío, sin caracteres reservados).
    /// </summary>
    /// <param name="name">Nombre a validar.</param>
    /// <returns><c>true</c> si el nombre es aceptable para la tabla de símbolos.</returns>
    public static bool IsValidSymbolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return false;
        }

        return name.IndexOfAny(['<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`']) < 0;
    }

    /// <summary>Convierte grados sexagesimales a radianes.</summary>
    /// <param name="degrees">Ángulo en grados.</param>
    /// <returns>Ángulo en radianes.</returns>
    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// Convierte <c>"v1,v2[,v3];..."</c> en filas numéricas. Devuelve <c>null</c> si el formato es inválido.
    /// </summary>
    /// <param name="points">Cadena de puntos separados por punto y coma.</param>
    /// <param name="columns">Número de componentes por punto (2 para XY, 3 para XYZ).</param>
    /// <returns>Matriz de puntos o <c>null</c> ante cualquier error de formato.</returns>
    public static double[][]? ParsePoints(string? points, int columns)
    {
        if (string.IsNullOrWhiteSpace(points))
        {
            return null;
        }

        var rows = points.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new double[rows.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            var parts = rows[i].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != columns)
            {
                return null;
            }

            result[i] = new double[columns];
            for (int j = 0; j < columns; j++)
            {
                if (!double.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i][j])
                    || double.IsNaN(result[i][j]) || double.IsInfinity(result[i][j]))
                {
                    return null;
                }
            }
        }

        return result;
    }

    /// <summary>Indica si todos los valores son números finitos.</summary>
    /// <param name="values">Valores a comprobar.</param>
    /// <returns><c>true</c> si ninguno es NaN ni infinito.</returns>
    public static bool AllFinite(params double[] values)
    {
        foreach (double v in values)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Serializa una respuesta de error estándar.</summary>
    /// <param name="message">Mensaje descriptivo del error.</param>
    /// <returns>JSON <c>{"status":"error","message":...}</c>.</returns>
    public static string Error(string message) =>
        JsonSerializer.Serialize(new { status = "error", message });
}
