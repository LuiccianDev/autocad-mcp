using System;
using System.Collections.Generic;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Geometría pura de ejes: convierte PIs y radio en vértices de polilínea con arcos tangentes (bulge).
/// No depende de la API de AutoCAD ni de Civil 3D.
/// </summary>
public static class AlignmentGeometry
{
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Genera vértices (x, y, bulge) con curvas circulares de radio fijo en cada PI interior.
    /// </summary>
    /// <param name="pis">Puntos de inflexión en orden (mínimo 2).</param>
    /// <param name="radio">Radio en metros; 0 no inserta curvas.</param>
    /// <param name="error">Motivo del rechazo si el trazo no es válido.</param>
    /// <returns>Lista de vértices, o null si hay error.</returns>
    public static List<(double X, double Y, double Bulge)>? BuildVertices(
        IReadOnlyList<PuntoXY> pis, double radio, out string? error)
    {
        error = null;
        int n = pis.Count;
        for (int i = 0; i < n - 1; i++)
        {
            if (Dist(pis[i], pis[i + 1]) < Epsilon)
            {
                error = $"PI {i} y PI {i + 1} coinciden: segmento de longitud cero.";
                return null;
            }
        }

        var tangentes = new double[n];
        var deflexiones = new double[n];
        var giro = new double[n];
        if (radio > 0)
        {
            for (int i = 1; i < n - 1; i++)
            {
                double d1x = pis[i].X - pis[i - 1].X, d1y = pis[i].Y - pis[i - 1].Y;
                double d2x = pis[i + 1].X - pis[i].X, d2y = pis[i + 1].Y - pis[i].Y;
                double l1 = Math.Sqrt(d1x * d1x + d1y * d1y);
                double l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                double cross = (d1x * d2y - d1y * d2x) / (l1 * l2);
                double dot = (d1x * d2x + d1y * d2y) / (l1 * l2);
                double defl = Math.Atan2(Math.Abs(cross), dot);
                if (defl < Epsilon)
                {
                    continue; // PI colineal: no requiere curva
                }

                deflexiones[i] = defl;
                giro[i] = Math.Sign(cross);
                tangentes[i] = radio * Math.Tan(defl / 2);
            }

            for (int i = 0; i < n - 1; i++)
            {
                if (tangentes[i] + tangentes[i + 1] > Dist(pis[i], pis[i + 1]) + Epsilon)
                {
                    error = $"Radio {radio} m demasiado grande: las tangentes de los PI {i} y {i + 1} " +
                            "exceden la longitud del segmento. Reduce el radio.";
                    return null;
                }
            }
        }

        var vertices = new List<(double, double, double)> { (pis[0].X, pis[0].Y, 0) };
        for (int i = 1; i < n - 1; i++)
        {
            if (tangentes[i] <= 0)
            {
                vertices.Add((pis[i].X, pis[i].Y, 0));
                continue;
            }

            double ax = pis[i - 1].X - pis[i].X, ay = pis[i - 1].Y - pis[i].Y;
            double bx = pis[i + 1].X - pis[i].X, by = pis[i + 1].Y - pis[i].Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            double t = tangentes[i];
            double bulge = giro[i] * Math.Tan(deflexiones[i] / 4);
            vertices.Add((pis[i].X + ax / la * t, pis[i].Y + ay / la * t, bulge));
            vertices.Add((pis[i].X + bx / lb * t, pis[i].Y + by / lb * t, 0));
        }

        vertices.Add((pis[n - 1].X, pis[n - 1].Y, 0));
        return vertices;
    }

    /// <summary>Formatea una progresiva en notación de estaca, por ejemplo 1250.5 → "1+250.50".</summary>
    /// <param name="progresiva">Progresiva en metros.</param>
    public static string FormatStation(double progresiva)
    {
        int km = (int)Math.Floor(progresiva / 1000.0);
        double resto = progresiva - km * 1000.0;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{km}+{resto:000.00}");
    }

    private static double Dist(PuntoXY a, PuntoXY b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
