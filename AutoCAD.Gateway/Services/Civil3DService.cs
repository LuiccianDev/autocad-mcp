using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Core.Models;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Direct access to native Civil 3D 2027 .NET API. Must only be instantiated via <see cref="Civil3DServiceFactory"/>
/// to avoid forcing AeccDbMgd assembly load in standard AutoCAD.
/// </summary>
public sealed class Civil3DService : ICivil3DService
{
    /// <inheritdoc/>
    public bool IsAvailable => true;

    private static CivilDocument RequireCivilDoc() =>
        CivilApplication.ActiveDocument
        ?? throw new AcadGatewayException("civil_unavailable", "No active Civil 3D document.");

    /// <inheritdoc/>
    public Task<string> GetStatusAsync() =>
        Acad.RunOnDocumentAsync((doc, _) =>
        {
            CivilDocument? civil = CivilApplication.ActiveDocument;
            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                civil_available = civil is not null,
                document = doc.Name,
                surfaces = civil?.GetSurfaceIds().Count ?? 0,
                alignments = civil?.GetAlignmentIds().Count ?? 0,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ListSurfacesAsync() =>
        Acad.Read(ctx =>
        {
            CivilDocument civil = RequireCivilDoc();
            var list = new List<object>();
            foreach (ObjectId id in civil.GetSurfaceIds())
            {
                if (id.IsNull || id.IsErased) continue;
                var s = (CivilDb.Surface)ctx.Tr.GetObject(id, OpenMode.ForRead);
                var gp = s.GetGeneralProperties();
                int? triangles = s is CivilDb.TinSurface tin ? tin.GetTinProperties().NumberOfTriangles : null;

                list.Add(new
                {
                    name = s.Name,
                    type = s.GetType().Name,
                    min_elevation = double.IsFinite(gp.MinimumElevation) ? gp.MinimumElevation : (double?)null,
                    max_elevation = double.IsFinite(gp.MaximumElevation) ? gp.MaximumElevation : (double?)null,
                    triangles,
                });
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = list,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> GetElevationAsync(string? surfaceName, double x, double y) =>
        Acad.Read(ctx =>
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new AcadGatewayException("bad_argument", "Coordinates 'x' and 'y' must be finite numbers.");
            }

            CivilDb.Surface surface = FindSurface(ctx.Tr, surfaceName)
                ?? throw new AcadGatewayException("not_found", "Surface not found.");

            double z;
            try
            {
                z = surface.FindElevationAtXY(x, y);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new AcadGatewayException("bad_argument", $"Point ({x}, {y}) is outside surface '{surface.Name}': {ex.Message}");
            }

            if (!double.IsFinite(z))
            {
                throw new AcadGatewayException("bad_argument", $"Point ({x}, {y}) yielded non-finite elevation on surface '{surface.Name}'.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                surface = surface.Name,
                x,
                y,
                elevation = z,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ListAlignmentsAsync() =>
        Acad.Read(ctx =>
        {
            CivilDocument civil = RequireCivilDoc();
            var list = new List<object>();
            foreach (ObjectId id in civil.GetAlignmentIds())
            {
                if (id.IsNull || id.IsErased) continue;
                var a = (CivilDb.Alignment)ctx.Tr.GetObject(id, OpenMode.ForRead);
                list.Add(new
                {
                    name = a.Name,
                    start_station = a.StartingStation,
                    end_station = a.EndingStation,
                    start_stake = AlignmentGeometry.FormatStation(a.StartingStation),
                    end_stake = AlignmentGeometry.FormatStation(a.EndingStation),
                    length = a.Length,
                });
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = list,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y) =>
        Acad.Read(ctx =>
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new AcadGatewayException("bad_argument", "Coordinates 'x' and 'y' must be finite numbers.");
            }

            CivilDb.Alignment al = FindAlignment(ctx.Tr, alignmentName)
                ?? throw new AcadGatewayException("not_found", "Alignment not found.");

            double station = 0, offset = 0;
            try
            {
                al.StationOffset(x, y, ref station, ref offset);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new AcadGatewayException("bad_argument", $"Point ({x}, {y}) does not project onto '{al.Name}': {ex.Message}");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                alignment = al.Name,
                station,
                stake = AlignmentGeometry.FormatStation(station),
                offset,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> GetProfileAsync(string? alignmentName, string? surfaceName, double interval) =>
        Acad.Read(ctx =>
        {
            if (!double.IsFinite(interval) || interval < 1e-6)
            {
                throw new AcadGatewayException("bad_argument", "'interval' must be a finite number >= 1e-6 meters.");
            }

            CivilDb.Alignment al = FindAlignment(ctx.Tr, alignmentName)
                ?? throw new AcadGatewayException("not_found", "Alignment not found.");
            CivilDb.Surface sf = FindSurface(ctx.Tr, surfaceName)
                ?? throw new AcadGatewayException("not_found", "Surface not found.");

            double startStation = al.StartingStation;
            double endStation = al.EndingStation;
            double length = endStation - startStation;
            if (length <= 0)
            {
                throw new AcadGatewayException("bad_argument", "Alignment length must be greater than zero.");
            }

            double estimatedSamples = Math.Ceiling(length / interval) + 1.0;
            if (estimatedSamples > SecurityPolicy.MaxProfileSamples)
            {
                throw new AcadGatewayException(
                    "bad_argument",
                    $"Interval {interval}m generates too many samples (max {SecurityPolicy.MaxProfileSamples}). Increase 'interval'.");
            }

            int intervalCount = (int)Math.Floor(length / interval);
            var samples = new List<object>(intervalCount + 2);
            var elevations = new List<double>(intervalCount + 2);

            for (int k = 0; k <= intervalCount; k++)
            {
                double st = Math.Min(startStation + k * interval, endStation);
                SampleProfilePoint(al, sf, st, samples, elevations);
                if (Math.Abs(endStation - st) < 1e-6)
                {
                    break;
                }
            }

            double lastSampled = startStation + intervalCount * interval;
            if (endStation - lastSampled >= 1e-6)
            {
                SampleProfilePoint(al, sf, endStation, samples, elevations);
            }

            double? minElev = elevations.Count > 0 ? elevations.Min() : null;
            double? maxElev = elevations.Count > 0 ? elevations.Max() : null;
            double? elevDelta = minElev.HasValue && maxElev.HasValue ? maxElev.Value - minElev.Value : null;
            double? avgSlope = elevations.Count >= 2 && length > 0 ? ((elevations[^1] - elevations[0]) / length * 100.0) : null;

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                alignment = al.Name,
                surface = sf.Name,
                start_station = Math.Round(startStation, 3),
                end_station = Math.Round(endStation, 3),
                length = Math.Round(length, 3),
                interval,
                sample_count = samples.Count,
                summary = new
                {
                    min_elevation = minElev.HasValue ? Math.Round(minElev.Value, 3) : (double?)null,
                    max_elevation = maxElev.HasValue ? Math.Round(maxElev.Value, 3) : (double?)null,
                    elevation_delta = elevDelta.HasValue ? Math.Round(elevDelta.Value, 3) : (double?)null,
                    average_slope_percent = avgSlope.HasValue ? Math.Round(avgSlope.Value, 4) : (double?)null,
                },
                samples,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset) =>
        Acad.Read(ctx =>
        {
            if (!double.IsFinite(station) || !double.IsFinite(offset))
            {
                throw new AcadGatewayException("bad_argument", "Parameters 'station' and 'offset' must be finite numbers.");
            }

            CivilDb.Alignment al = FindAlignment(ctx.Tr, alignmentName)
                ?? throw new AcadGatewayException("not_found", "Alignment not found.");

            if (station < al.StartingStation - 1e-6 || station > al.EndingStation + 1e-6)
            {
                throw new AcadGatewayException("bad_argument", $"Station {station} is out of range [{al.StartingStation}, {al.EndingStation}].");
            }

            double clampedStation = Math.Clamp(station, al.StartingStation, al.EndingStation);
            double x = 0, y = 0;
            al.PointLocation(clampedStation, offset, ref x, ref y);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                alignment = al.Name,
                station = clampedStation,
                offset,
                x,
                y,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> CreateAlignmentAsync(CreateAlignmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = request.Name?.Trim() ?? string.Empty;
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", "Valid alignment 'name' is required.");
        }

        List<PointXY>? pis = request.Points;
        if (pis == null || pis.Count < 2 || pis.Count > SecurityPolicy.MaxGeometryVertices)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"Alignment requires between 2 and {SecurityPolicy.MaxGeometryVertices} PI points.");
        }

        if (pis.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)) ||
            !double.IsFinite(request.Radius) ||
            request.Radius < 0)
        {
            throw new AcadGatewayException("bad_argument", "Coordinates and non-negative radius must be finite numbers.");
        }

        string layerName = string.IsNullOrWhiteSpace(request.Layer) ? "0" : request.Layer.Trim();
        if (!SecurityPolicy.IsValidSymbolName(layerName))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid layer name '{layerName}'.");
        }

        List<(double X, double Y, double Bulge)>? vertices = AlignmentGeometry.BuildVertices(pis, request.Radius, out string? geomErr);
        if (vertices == null)
        {
            throw new AcadGatewayException("bad_argument", geomErr ?? "Invalid alignment geometry.");
        }

        return Acad.Write(ctx =>
        {
            CivilDocument civil = RequireCivilDoc();
            if (FindAlignment(ctx.Tr, name, exactMatch: true) != null)
            {
                throw new AcadGatewayException("already_exists", $"An alignment named '{name}' already exists.");
            }

            if (civil.Styles.AlignmentStyles.Count == 0 || civil.Styles.LabelSetStyles.AlignmentLabelSetStyles.Count == 0)
            {
                throw new AcadGatewayException("not_found", "Active Civil 3D template contains no AlignmentStyles or AlignmentLabelSetStyles.");
            }

            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)ctx.Tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var pl = new Polyline();
            pl.SetDatabaseDefaults(ctx.Db);
            for (int i = 0; i < vertices.Count; i++)
            {
                pl.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), vertices[i].Bulge, 0, 0);
            }

            ObjectId plineId = DatabaseHelpers.AppendEntity(ms, ctx.Tr, pl);
            ObjectId layerId = DatabaseHelpers.EnsureLayer(ctx.Db, ctx.Tr, layerName);

            var options = new CivilDb.PolylineOptions
            {
                PlineId = plineId,
                AddCurvesBetweenTangents = false,
                EraseExistingEntities = true,
            };
            ObjectId styleId = civil.Styles.AlignmentStyles[0];
            ObjectId labelSetId = civil.Styles.LabelSetStyles.AlignmentLabelSetStyles[0];
            ObjectId alId = CivilDb.Alignment.Create(
                civil, options, name, ObjectId.Null, layerId, styleId, labelSetId);

            var al = (CivilDb.Alignment)ctx.Tr.GetObject(alId, OpenMode.ForRead);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = alId.Handle.ToString(),
                alignment = al.Name,
                length = al.Length,
                starting_station = al.StartingStation,
                ending_station = al.EndingStation,
                ending_station_formatted = AlignmentGeometry.FormatStation(al.EndingStation),
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, requireIdle: true);
    }

    /// <inheritdoc/>
    public Task<string> CreateSurfaceAsync(CreateSurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = request.Name?.Trim() ?? string.Empty;
        if (!SecurityPolicy.IsValidSymbolName(name))
        {
            throw new AcadGatewayException("bad_argument", "Valid surface 'name' is required.");
        }

        List<PointXYZ>? pts = request.Points;
        if (pts == null || pts.Count < 3 || pts.Count > SecurityPolicy.MaxGeometryVertices)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"Surface creation requires between 3 and {SecurityPolicy.MaxGeometryVertices} points.");
        }

        if (pts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)))
        {
            throw new AcadGatewayException("bad_argument", "All survey coordinates must be finite numbers.");
        }

        return Acad.Write(ctx =>
        {
            if (FindSurface(ctx.Tr, name, exactMatch: true) != null)
            {
                throw new AcadGatewayException("already_exists", $"A surface named '{name}' already exists.");
            }

            ObjectId sid = CivilDb.TinSurface.Create(ctx.Db, name);
            var tin = (CivilDb.TinSurface)ctx.Tr.GetObject(sid, OpenMode.ForWrite);
            using var collection = new Point3dCollection();
            foreach (PointXYZ p in pts)
            {
                collection.Add(new Point3d(p.X, p.Y, p.Z));
            }

            tin.AddVertices(collection);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = sid.Handle.ToString(),
                surface = name,
                points = pts.Count,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, requireIdle: true);
    }

    /// <inheritdoc/>
    public Task<string> GetSurfaceStatsAsync(string? surfaceName) =>
        Acad.Read(ctx =>
        {
            CivilDb.Surface surface = FindSurface(ctx.Tr, surfaceName)
                ?? throw new AcadGatewayException("not_found", "Surface not found.");

            var gp = surface.GetGeneralProperties();
            double? minElev = double.IsFinite(gp.MinimumElevation) ? gp.MinimumElevation : null;
            double? maxElev = double.IsFinite(gp.MaximumElevation) ? gp.MaximumElevation : null;
            double? meanElev = double.IsFinite(gp.MeanElevation) ? gp.MeanElevation : null;
            int? triangles = null;
            double? area2d = null;
            double? area3d = null;

            if (surface is CivilDb.TinSurface tin)
            {
                triangles = tin.GetTinProperties().NumberOfTriangles;
                var terrainProps = tin.GetTerrainProperties();
                if (double.IsFinite(terrainProps.SurfaceArea2D)) area2d = terrainProps.SurfaceArea2D;
                if (double.IsFinite(terrainProps.SurfaceArea3D)) area3d = terrainProps.SurfaceArea3D;
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                surface = surface.Name,
                type = surface.GetType().Name,
                min_elevation = minElev,
                max_elevation = maxElev,
                mean_elevation = meanElev,
                point_count = gp.NumberOfPoints,
                triangle_count = triangles,
                area_2d = area2d,
                area_3d = area3d,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> GetCrossSectionAsync(
        string? alignmentName,
        string? surfaceName,
        double station,
        double leftWidth,
        double rightWidth,
        double step) =>
        Acad.Read(ctx =>
        {
            if (!double.IsFinite(station) || !double.IsFinite(leftWidth) || !double.IsFinite(rightWidth) || !double.IsFinite(step))
            {
                throw new AcadGatewayException("bad_argument", "Parameters station, left_width, right_width, and step must be finite numbers.");
            }

            if (leftWidth < 0 || rightWidth < 0 || leftWidth + rightWidth <= 0 || step < 1e-6)
            {
                throw new AcadGatewayException("bad_argument", "Require left_width >= 0, right_width >= 0, left_width + right_width > 0, and step >= 1e-6.");
            }

            double leftCount = leftWidth > 0 ? Math.Ceiling(leftWidth / step) : 0.0;
            double rightCount = rightWidth > 0 ? Math.Ceiling(rightWidth / step) : 0.0;
            if (leftCount + rightCount + 1.0 > SecurityPolicy.MaxCrossSectionSamples)
            {
                throw new AcadGatewayException(
                    "bad_argument",
                    $"Cross-section exceeds max {SecurityPolicy.MaxCrossSectionSamples} samples. Increase 'step'.");
            }

            int leftSteps = (int)leftCount;
            int rightSteps = (int)rightCount;

            CivilDb.Alignment al = FindAlignment(ctx.Tr, alignmentName)
                ?? throw new AcadGatewayException("not_found", "Alignment not found.");
            CivilDb.Surface sf = FindSurface(ctx.Tr, surfaceName)
                ?? throw new AcadGatewayException("not_found", "Surface not found.");

            if (station < al.StartingStation - 1e-6 || station > al.EndingStation + 1e-6)
            {
                throw new AcadGatewayException("bad_argument", $"Station {station} is out of alignment range [{al.StartingStation}, {al.EndingStation}].");
            }

            var offsets = BuildCrossSectionOffsets(leftWidth, rightWidth, step, leftSteps, rightSteps);
            double clampedStation = Math.Clamp(station, al.StartingStation, al.EndingStation);
            var samples = new List<object>(offsets.Count);
            double? zMin = null;
            double? zMax = null;

            foreach (double off in offsets)
            {
                double x = 0, y = 0;
                al.PointLocation(clampedStation, off, ref x, ref y);
                double? z = TrySampleSurfaceElevation(sf, x, y);
                if (z.HasValue)
                {
                    zMin = zMin.HasValue ? Math.Min(zMin.Value, z.Value) : z.Value;
                    zMax = zMax.HasValue ? Math.Max(zMax.Value, z.Value) : z.Value;
                }

                samples.Add(new
                {
                    offset = Math.Round(off, 4),
                    x = Math.Round(x, 4),
                    y = Math.Round(y, 4),
                    elevation = z.HasValue ? Math.Round(z.Value, 4) : (double?)null,
                });
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                alignment = al.Name,
                surface = sf.Name,
                station = clampedStation,
                left_width = leftWidth,
                right_width = rightWidth,
                step,
                total_samples = samples.Count,
                min_elevation = zMin.HasValue ? Math.Round(zMin.Value, 4) : (double?)null,
                max_elevation = zMax.HasValue ? Math.Round(zMax.Value, 4) : (double?)null,
                samples,
            }, GatewayProtocol.WireJsonOptions);
        });

    private static List<double> BuildCrossSectionOffsets(
        double leftWidth, double rightWidth, double step, int leftSteps, int rightSteps)
    {
        var ordered = new SortedSet<double> { 0.0 };
        for (int k = 1; k <= leftSteps; k++)
        {
            ordered.Add(Math.Round(-Math.Min(k * step, leftWidth), 6));
        }

        for (int k = 1; k <= rightSteps; k++)
        {
            ordered.Add(Math.Round(Math.Min(k * step, rightWidth), 6));
        }

        return [.. ordered];
    }

    private static void SampleProfilePoint(
        CivilDb.Alignment al,
        CivilDb.Surface sf,
        double station,
        List<object> samples,
        List<double> elevations)
    {
        double x = 0, y = 0;
        al.PointLocation(station, 0, ref x, ref y);
        double? z = TrySampleSurfaceElevation(sf, x, y);
        if (z.HasValue)
        {
            elevations.Add(z.Value);
        }

        samples.Add(new
        {
            station = Math.Round(station, 3),
            stake = AlignmentGeometry.FormatStation(station),
            x = Math.Round(x, 4),
            y = Math.Round(y, 4),
            z = z.HasValue ? Math.Round(z.Value, 3) : (double?)null,
        });
    }

    private static double? TrySampleSurfaceElevation(CivilDb.Surface sf, double x, double y)
    {
        try
        {
            double rawZ = sf.FindElevationAtXY(x, y);
            return double.IsFinite(rawZ) ? rawZ : null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Point lies outside TIN surface convex boundary
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static CivilDb.Surface? FindSurface(Transaction tr, string? name, bool exactMatch = false) =>
        FindCivilEntity<CivilDb.Surface>(tr, RequireCivilDoc().GetSurfaceIds(), static s => s.Name, name, exactMatch);

    private static CivilDb.Alignment? FindAlignment(Transaction tr, string? name, bool exactMatch = false) =>
        FindCivilEntity<CivilDb.Alignment>(tr, RequireCivilDoc().GetAlignmentIds(), static a => a.Name, name, exactMatch);

    private static T? FindCivilEntity<T>(
        Transaction tr,
        ObjectIdCollection ids,
        Func<T, string> getName,
        string? name,
        bool exactMatch = false) where T : Autodesk.AutoCAD.DatabaseServices.DBObject
    {
        T? first = null;
        string? clean = name?.Trim();
        foreach (ObjectId id in ids)
        {
            if (id.IsNull || id.IsErased) continue;
            var item = (T)tr.GetObject(id, OpenMode.ForRead);
            first ??= item;
            if (!string.IsNullOrEmpty(clean) && string.Equals(getName(item), clean, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return exactMatch || !string.IsNullOrEmpty(clean) ? null : first;
    }
}
