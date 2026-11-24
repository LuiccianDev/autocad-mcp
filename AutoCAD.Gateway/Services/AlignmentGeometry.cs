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
                if (!NormalizeDelta(pis[i].X - pis[i - 1].X, pis[i].Y - pis[i - 1].Y, out double ux1, out double uy1, out _) ||
                    !NormalizeDelta(pis[i + 1].X - pis[i].X, pis[i + 1].Y - pis[i].Y, out double ux2, out double uy2, out _))
                {
                    error = $"Invalid PI segment length around PI {i}.";
                    return null;
                }

                double cross = ux1 * uy2 - uy1 * ux2;
                double dot = ux1 * ux2 + uy1 * uy2;
                double defl = Math.Atan2(Math.Abs(cross), dot);
                if (!double.IsFinite(defl))
                {
                    error = $"Invalid deflection angle at PI {i}.";
                    return null;
                }
                if (defl < Epsilon)
                {
                    continue; // Collinear PI: no curve required
                }

                deflexiones[i] = defl;
                giro[i] = Math.Sign(cross);
                double tanHalf = Math.Tan(defl / 2);
                double tangent = radius * tanHalf;
                if (!double.IsFinite(tangent))
                {
                    error = $"Tangent length overflow at PI {i}.";
                    return null;
                }
                tangentes[i] = tangent;
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

            if (!NormalizeDelta(pis[i - 1].X - pis[i].X, pis[i - 1].Y - pis[i].Y, out double uax, out double uay, out _) ||
                !NormalizeDelta(pis[i + 1].X - pis[i].X, pis[i + 1].Y - pis[i].Y, out double ubx, out double uby, out _))
            {
                error = $"Invalid tangent direction at PI {i}.";
                return null;
            }

            double t = tangentes[i];
            double bulge = giro[i] * Math.Tan(deflexiones[i] / 4);
            double x1 = pis[i].X + uax * t, y1 = pis[i].Y + uay * t;
            double x2 = pis[i].X + ubx * t, y2 = pis[i].Y + uby * t;
            if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2) || !double.IsFinite(bulge))
            {
                error = $"Alignment vertex calculation overflow at PI {i}.";
                return null;
            }

            vertices.Add((x1, y1, bulge));
            vertices.Add((x2, y2, 0));
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

    private static bool NormalizeDelta(double dx, double dy, out double ux, out double uy, out double length)
    {
        double ax = Math.Abs(dx), ay = Math.Abs(dy);
        double scale = Math.Max(ax, ay);
        if (scale < Epsilon || !double.IsFinite(scale))
        {
            ux = 0;
            uy = 0;
            length = 0;
            return false;
        }

        double sx = dx / scale, sy = dy / scale;
        double norm = Math.Sqrt(sx * sx + sy * sy);
        ux = sx / norm;
        uy = sy / norm;
        length = scale * norm;
        return double.IsFinite(ux) && double.IsFinite(uy) && length >= Epsilon;
    }

    private static double Dist(PointXY a, PointXY b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (scale == 0.0 || !double.IsFinite(dx) || !double.IsFinite(dy))
        {
            return double.IsFinite(dx) && double.IsFinite(dy) ? 0.0 : double.PositiveInfinity;
        }

        double sx = dx / scale, sy = dy / scale;
        return scale * Math.Sqrt(sx * sx + sy * sy);
    }
}
