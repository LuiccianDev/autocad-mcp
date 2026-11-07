using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
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

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { status = "error", message });

    private static CivilDocument CivilDoc => CivilApplication.ActiveDocument;

    /// <inheritdoc/>
    public Task<string> GetStatusAsync() => UiThreadInvoker.RunOnIdleAsync(doc =>
    {
        var civil = CivilDoc;
        return JsonSerializer.Serialize(new
        {
            status = "success",
            civil_available = true,
            document = doc.Name,
            surfaces = civil.GetSurfaceIds().Count,
            alignments = civil.GetAlignmentIds().Count,
            // Backward-compatibility aliases
            documento = doc.Name,
            superficies = civil.GetSurfaceIds().Count,
            alineamientos = civil.GetAlignmentIds().Count
        });
    });

    /// <inheritdoc/>
    public Task<string> ListSurfacesAsync() => UiThreadInvoker.RunOnIdleAsync(doc =>
    {
        var list = new List<object>();
        using var tr = doc.TransactionManager.StartTransaction();
        foreach (ObjectId id in CivilDoc.GetSurfaceIds())
        {
            var s = (CivilDb.Surface)tr.GetObject(id, OpenMode.ForRead);
            var gp = s.GetGeneralProperties();
            int? triangles = null;
            if (s is CivilDb.TinSurface tin)
            {
                triangles = tin.GetTinProperties().NumberOfTriangles;
            }

            list.Add(new
            {
                name = s.Name,
                type = s.GetType().Name,
                min_elevation = gp.MinimumElevation,
                max_elevation = gp.MaximumElevation,
                triangles,
                // Backward-compatibility aliases
                nombre = s.Name,
                tipo = s.GetType().Name,
                cota_minima = gp.MinimumElevation,
                cota_maxima = gp.MaximumElevation,
                triangulos = triangles
            });
        }

        tr.Commit();
        return JsonSerializer.Serialize(new { status = "success", data = list });
    });

    /// <inheritdoc/>
    public Task<string> GetElevationAsync(string? surfaceName, double x, double y) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            using var tr = doc.TransactionManager.StartTransaction();
            var surface = FindSurface(tr, surfaceName);
            if (surface == null)
            {
                return Error("Surface not found.");
            }

            try
            {
                double z = surface.FindElevationAtXY(x, y);
                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    surface = surface.Name,
                    x,
                    y,
                    elevation = z,
                    // Backward-compatibility aliases
                    superficie = surface.Name,
                    elevacion = z
                });
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return Error($"Point ({x}, {y}) is outside surface '{surface.Name}'.");
            }
        });

    /// <inheritdoc/>
    public Task<string> ListAlignmentsAsync() => UiThreadInvoker.RunOnIdleAsync(doc =>
    {
        var list = new List<object>();
        using var tr = doc.TransactionManager.StartTransaction();
        foreach (ObjectId id in CivilDoc.GetAlignmentIds())
        {
            var a = (CivilDb.Alignment)tr.GetObject(id, OpenMode.ForRead);
            list.Add(new
            {
                name = a.Name,
                start_station = a.StartingStation,
                end_station = a.EndingStation,
                start_stake = AlignmentGeometry.FormatStation(a.StartingStation),
                end_stake = AlignmentGeometry.FormatStation(a.EndingStation),
                length = a.Length,
                // Backward-compatibility aliases
                nombre = a.Name,
                progresiva_inicial = a.StartingStation,
                progresiva_final = a.EndingStation,
                estaca_inicial = AlignmentGeometry.FormatStation(a.StartingStation),
                estaca_final = AlignmentGeometry.FormatStation(a.EndingStation),
                longitud = a.Length
            });
        }

        tr.Commit();
        return JsonSerializer.Serialize(new { status = "success", data = list });
    });

    /// <inheritdoc/>
    public Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            using var tr = doc.TransactionManager.StartTransaction();
            var al = FindAlignment(tr, alignmentName);
            if (al == null)
            {
                return Error("Alignment not found.");
            }

            double station = 0, offset = 0;
            try
            {
                al.StationOffset(x, y, ref station, ref offset);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return Error($"Point ({x}, {y}) does not project onto '{al.Name}'.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                alignment = al.Name,
                station,
                stake = AlignmentGeometry.FormatStation(station),
                offset,
                // Backward-compatibility aliases
                alineamiento = al.Name,
                progresiva = station,
                estaca = AlignmentGeometry.FormatStation(station),
                desfase = offset
            });
        });

    /// <inheritdoc/>
    public Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            using var tr = doc.TransactionManager.StartTransaction();
            var al = FindAlignment(tr, alignmentName);
            if (al == null)
            {
                return Error("Alignment not found.");
            }

            if (station < al.StartingStation || station > al.EndingStation)
            {
                return Error($"Station {station} is out of range [{al.StartingStation}, {al.EndingStation}].");
            }

            double x = 0, y = 0;
            al.PointLocation(station, offset, ref x, ref y);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                alignment = al.Name,
                station,
                offset,
                x,
                y,
                // Backward-compatibility aliases
                alineamiento = al.Name,
                progresiva = station,
                desfase = offset
            });
        });

    /// <inheritdoc/>
    public Task<string> CreateAlignmentAsync(CrearAlineamientoRequest request) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            string name = (request.Name ?? request.Nombre)?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                return Error("'name' is required.");
            }

            var pis = request.Points ?? request.Puntos;
            if (pis == null || pis.Count < 2)
            {
                return Error("At least 2 points (PIs) are required.");
            }

            double radius = request.Radius > 0 ? request.Radius : request.Radio;
            if (pis.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)) || radius < 0 || !double.IsFinite(radius))
            {
                return Error("Coordinates or radius are invalid (must be finite; radius >= 0).");
            }

            var vertices = AlignmentGeometry.BuildVertices(pis, radius, out string? err);
            if (vertices == null)
            {
                return Error(err ?? "Invalid alignment geometry.");
            }

            var civil = CivilDoc;
            using var lockDoc = doc.LockDocument();

            using (var trCheck = doc.TransactionManager.StartTransaction())
            {
                if (FindAlignment(trCheck, name, exacto: true) != null)
                {
                    return Error($"An alignment named '{name}' already exists.");
                }
            }

            ObjectId plineId;
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                using var pl = new Polyline();
                for (int i = 0; i < vertices.Count; i++)
                {
                    pl.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), vertices[i].Bulge, 0, 0);
                }

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                plineId = pl.ObjectId;
                tr.Commit();
            }

            var options = new CivilDb.PolylineOptions
            {
                PlineId = plineId,
                AddCurvesBetweenTangents = false,
                EraseExistingEntities = true
            };
            ObjectId styleId = civil.Styles.AlignmentStyles[0];
            ObjectId labelSetId = civil.Styles.LabelSetStyles.AlignmentLabelSetStyles[0];
            string layerName = string.IsNullOrWhiteSpace(request.Layer ?? request.Capa) ? "0" : (request.Layer ?? request.Capa)!.Trim();
            ObjectId layerId = EnsureLayer(doc, layerName);
            ObjectId alId = CivilDb.Alignment.Create(
                civil, options, name, ObjectId.Null, layerId, styleId, labelSetId);

            using var trR = doc.TransactionManager.StartTransaction();
            var al = (CivilDb.Alignment)trR.GetObject(alId, OpenMode.ForRead);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                alignment = al.Name,
                length = al.Length,
                starting_station = al.StartingStation,
                ending_station = al.EndingStation,
                ending_station_formatted = AlignmentGeometry.FormatStation(al.EndingStation),
                // Backward-compatibility aliases
                alineamiento = al.Name,
                longitud = al.Length,
                progresiva_inicial = al.StartingStation,
                progresiva_final = al.EndingStation,
                estaca_final = AlignmentGeometry.FormatStation(al.EndingStation)
            });
        });

    /// <inheritdoc/>
    public Task<string> CreateSurfaceAsync(CrearSuperficieRequest request) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            string name = (request.Name ?? request.Nombre)?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                return Error("'name' is required.");
            }

            var pts = request.Points ?? request.Puntos;
            if (pts == null || pts.Count < 3)
            {
                return Error("At least 3 points are required to create a TIN surface.");
            }

            if (pts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)))
            {
                return Error("Coordinates are invalid (must be finite numbers).");
            }

            var civil = CivilDoc;
            using var lockDoc = doc.LockDocument();

            using (var trCheck = doc.TransactionManager.StartTransaction())
            {
                if (FindSurface(trCheck, name, exacto: true) != null)
                {
                    return Error($"A surface named '{name}' already exists.");
                }
            }

            ObjectId sid = CivilDb.TinSurface.Create(doc.Database, name);
            using var tr = doc.TransactionManager.StartTransaction();
            var tin = (CivilDb.TinSurface)tr.GetObject(sid, OpenMode.ForWrite);
            var collection = new Point3dCollection();
            foreach (var p in pts)
            {
                collection.Add(new Point3d(p.X, p.Y, p.Z));
            }

            tin.AddVertices(collection);
            tr.Commit();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                surface = name,
                points = pts.Count,
                // Backward-compatibility aliases
                superficie = name,
                puntos = pts.Count
            });
        });

    private static ObjectId EnsureLayer(Document doc, string name)
    {
        using var tr = doc.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
        {
            ObjectId existing = lt[name];
            tr.Commit();
            return existing;
        }

        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        tr.Commit();
        return id;
    }

    private static CivilDb.Surface? FindSurface(Transaction tr, string? name, bool exacto = false)
    {
        CivilDb.Surface? first = null;
        foreach (ObjectId id in CivilDoc.GetSurfaceIds())
        {
            var s = (CivilDb.Surface)tr.GetObject(id, OpenMode.ForRead);
            first ??= s;
            if (name != null && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        return exacto || name != null ? null : first;
    }

    private static CivilDb.Alignment? FindAlignment(Transaction tr, string? name, bool exacto = false)
    {
        CivilDb.Alignment? first = null;
        foreach (ObjectId id in CivilDoc.GetAlignmentIds())
        {
            var a = (CivilDb.Alignment)tr.GetObject(id, OpenMode.ForRead);
            first ??= a;
            if (name != null && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }

        return exacto || name != null ? null : first;
    }
}
