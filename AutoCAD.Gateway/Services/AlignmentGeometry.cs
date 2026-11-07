using System;
using System.Collections.Generic;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Pure centerline geometry calculations: converts PIs and radius into polyline vertices with tangent arc bulges.
/// Has no dependency on AutoCAD or Civil 3D runtime assemblies.
/// </summary>
public static class AlignmentGeometry
{
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Generates polyline vertices (x, y, bulge) with circular curves of fixed radius at each interior PI.
    /// </summary>
    /// <param name="pis">Points of intersection in station order (minimum 2).</param>
    /// <param name="radius">Curve radius in meters; 0 inserts no curves.</param>
    /// <param name="error">Output rejection reason if geometry is invalid.</param>
    /// <returns>List of polyline vertices, or null if invalid.</returns>
    public static List<(double X, double Y, double Bulge)>? BuildVertices(
        IReadOnlyList<PointXY> pis, double radius, out string? error)
    {
        error = null;
        int n = pis.Count;
        for (int i = 0; i < n - 1; i++)
        {
            if (Dist(pis[i], pis[i + 1]) < Epsilon)
            {
                error = $"PI {i} and PI {i + 1} coincide: zero-length segment.";
                return null;
            }
        }

        var tangentes = new double[n];
        var deflexiones = new double[n];
        var giro = new double[n];
        if (radius > 0)
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
                    continue; // Collinear PI: no curve required
                }

                deflexiones[i] = defl;
                giro[i] = Math.Sign(cross);
                tangentes[i] = radius * Math.Tan(defl / 2);
            }

            for (int i = 0; i < n - 1; i++)
            {
                if (tangentes[i] + tangentes[i + 1] > Dist(pis[i], pis[i + 1]) + Epsilon)
                {
                    error = $"Radius {radius} m is too large: tangents for PI {i} and {i + 1} " +
                            "exceed the segment length. Reduce radius.";
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

    /// <summary>Formats a station in standard stationing notation (e.g., 1250.5 -> "1+250.50").</summary>
    /// <param name="station">Station in meters.</param>
    public static string FormatStation(double station)
    {
        int km = (int)Math.Floor(station / 1000.0);
        double remainder = station - km * 1000.0;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{km}+{remainder:000.00}");
    }

    private static double Dist(PointXY a, PointXY b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
