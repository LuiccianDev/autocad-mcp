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
/// Acceso a la API nativa de Civil 3D 2027. Solo debe instanciarse vía <see cref="Civil3DServiceFactory"/>
/// (nunca directamente), para no forzar la carga de AeccDbMgd en AutoCAD estándar.
/// </summary>
public sealed class Civil3DService : ICivil3DService
{
    /// <inheritdoc/>
    public bool IsAvailable => true;

    private static string Error(string mensaje) =>
        JsonSerializer.Serialize(new { status = "error", message = mensaje });

    private static CivilDocument CivilDoc => CivilApplication.ActiveDocument;

    /// <inheritdoc/>
    public Task<string> GetStatusAsync() => UiThreadInvoker.RunOnIdleAsync(doc =>
    {
        var civil = CivilDoc;
        return JsonSerializer.Serialize(new
        {
            status = "success",
            civil_available = true,
            documento = doc.Name,
            superficies = civil.GetSurfaceIds().Count,
            alineamientos = civil.GetAlignmentIds().Count
        });
    });

    /// <inheritdoc/>
    public Task<string> ListSurfacesAsync() => UiThreadInvoker.RunOnIdleAsync(doc =>
    {
        var lista = new List<object>();
        using var tr = doc.TransactionManager.StartTransaction();
        foreach (ObjectId id in CivilDoc.GetSurfaceIds())
        {
            var s = (CivilDb.Surface)tr.GetObject(id, OpenMode.ForRead);
            var gp = s.GetGeneralProperties();
            int? triangulos = null;
            if (s is CivilDb.TinSurface tin)
            {
                triangulos = tin.GetTinProperties().NumberOfTriangles;
            }

            lista.Add(new
            {
                nombre = s.Name,
                tipo = s.GetType().Name,
                cota_minima = gp.MinimumElevation,
                cota_maxima = gp.MaximumElevation,
                triangulos
            });
        }

        tr.Commit();
        return JsonSerializer.Serialize(new { status = "success", data = lista });
    });

    /// <inheritdoc/>
    public Task<string> GetElevationAsync(string? surfaceName, double x, double y) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            using var tr = doc.TransactionManager.StartTransaction();
            var surface = FindSurface(tr, surfaceName);
            if (surface == null)
            {
                return Error("Superficie no encontrada.");
            }

            try
            {
                double z = surface.FindElevationAtXY(x, y);
                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    superficie = surface.Name,
                    x,
                    y,
                    elevacion = z
                });
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return Error($"El punto ({x}, {y}) esta fuera de la superficie '{surface.Name}'.");
            }
        });

    /// <inheritdoc/>
    public Task<string> ListAlignmentsAsync() => UiThreadInvoker.RunOnIdleAsync(doc =>
    {
        var lista = new List<object>();
        using var tr = doc.TransactionManager.StartTransaction();
        foreach (ObjectId id in CivilDoc.GetAlignmentIds())
        {
            var a = (CivilDb.Alignment)tr.GetObject(id, OpenMode.ForRead);
            lista.Add(new
            {
                nombre = a.Name,
                progresiva_inicial = a.StartingStation,
                progresiva_final = a.EndingStation,
                estaca_inicial = AlignmentGeometry.FormatStation(a.StartingStation),
                estaca_final = AlignmentGeometry.FormatStation(a.EndingStation),
                longitud = a.Length
            });
        }

        tr.Commit();
        return JsonSerializer.Serialize(new { status = "success", data = lista });
    });

    /// <inheritdoc/>
    public Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            using var tr = doc.TransactionManager.StartTransaction();
            var al = FindAlignment(tr, alignmentName);
            if (al == null)
            {
                return Error("Alineamiento no encontrado.");
            }

            double station = 0, offset = 0;
            try
            {
                al.StationOffset(x, y, ref station, ref offset);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return Error($"El punto ({x}, {y}) no se proyecta sobre '{al.Name}'.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
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
                return Error("Alineamiento no encontrado.");
            }

            if (station < al.StartingStation || station > al.EndingStation)
            {
                return Error($"Progresiva {station} fuera del rango [{al.StartingStation}, {al.EndingStation}].");
            }

            double x = 0, y = 0;
            al.PointLocation(station, offset, ref x, ref y);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                alineamiento = al.Name,
                progresiva = station,
                desfase = offset,
                x,
                y
            });
        });

    /// <inheritdoc/>
    public Task<string> CreateAlignmentAsync(CrearAlineamientoRequest request) =>
        UiThreadInvoker.RunOnIdleAsync(doc =>
        {
            string nombre = request.Nombre?.Trim() ?? string.Empty;
            if (nombre.Length == 0)
            {
                return Error("Se requiere 'nombre'.");
            }

            var pis = request.Puntos;
            if (pis == null || pis.Count < 2)
            {
                return Error("Se requieren al menos 2 puntos (PI).");
            }

            if (pis.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)) || request.Radio < 0 ||
                !double.IsFinite(request.Radio))
            {
                return Error("Coordenadas o radio invalidos (deben ser finitos; radio >= 0).");
            }

            var vertices = AlignmentGeometry.BuildVertices(pis, request.Radio, out string? err);
            if (vertices == null)
            {
                return Error(err ?? "Trazo invalido.");
            }

            var civil = CivilDoc;
            using var bloqueo = doc.LockDocument();

            using (var trCheck = doc.TransactionManager.StartTransaction())
            {
                if (FindAlignment(trCheck, nombre, exacto: true) != null)
                {
                    return Error($"Ya existe un alineamiento llamado '{nombre}'.");
                }
            }

            ObjectId plineId;
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                var pl = new Polyline();
                for (int i = 0; i < vertices.Count; i++)
                {
                    pl.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), vertices[i].Bulge, 0, 0);
                }

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                plineId = pl.ObjectId;
                tr.Commit();
            }

            var opciones = new CivilDb.PolylineOptions
            {
                PlineId = plineId,
                AddCurvesBetweenTangents = false,
                EraseExistingEntities = true
            };
            ObjectId styleId = civil.Styles.AlignmentStyles[0];
            ObjectId labelSetId = civil.Styles.LabelSetStyles.AlignmentLabelSetStyles[0];
            string capa = string.IsNullOrWhiteSpace(request.Capa) ? "0" : request.Capa.Trim();
            ObjectId layerId = EnsureLayer(doc, capa);
            ObjectId alId = CivilDb.Alignment.Create(
                civil, opciones, nombre, ObjectId.Null, layerId, styleId, labelSetId);

            using var trR = doc.TransactionManager.StartTransaction();
            var al = (CivilDb.Alignment)trR.GetObject(alId, OpenMode.ForRead);
            return JsonSerializer.Serialize(new
            {
                status = "success",
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
            string nombre = request.Nombre?.Trim() ?? string.Empty;
            if (nombre.Length == 0)
            {
                return Error("Se requiere 'nombre'.");
            }

            var pts = request.Puntos;
            if (pts == null || pts.Count < 3)
            {
                return Error("Se requieren al menos 3 puntos (X, Y, Z).");
            }

            if (pts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)))
            {
                return Error("Coordenadas invalidas (deben ser finitas).");
            }

            var civil = CivilDoc;
            using var bloqueo = doc.LockDocument();

            using (var trCheck = doc.TransactionManager.StartTransaction())
            {
                if (FindSurface(trCheck, nombre, exacto: true) != null)
                {
                    return Error($"Ya existe una superficie llamada '{nombre}'.");
                }
            }

            ObjectId sid = CivilDb.TinSurface.Create(doc.Database, nombre);
            using var tr = doc.TransactionManager.StartTransaction();
            var tin = (CivilDb.TinSurface)tr.GetObject(sid, OpenMode.ForWrite);
            var coleccion = new Point3dCollection();
            foreach (var p in pts)
            {
                coleccion.Add(new Point3d(p.X, p.Y, p.Z));
            }

            tin.AddVertices(coleccion);
            tr.Commit();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                superficie = nombre,
                puntos = pts.Count
            });
        });

    private static ObjectId EnsureLayer(Document doc, string nombre)
    {
        using var tr = doc.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (lt.Has(nombre))
        {
            ObjectId existente = lt[nombre];
            tr.Commit();
            return existente;
        }

        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = nombre };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        tr.Commit();
        return id;
    }

    private static CivilDb.Surface? FindSurface(Transaction tr, string? nombre, bool exacto = false)
    {
        CivilDb.Surface? primera = null;
        foreach (ObjectId id in CivilDoc.GetSurfaceIds())
        {
            var s = (CivilDb.Surface)tr.GetObject(id, OpenMode.ForRead);
            primera ??= s;
            if (nombre != null && string.Equals(s.Name, nombre, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        return exacto || nombre != null ? null : primera;
    }

    private static CivilDb.Alignment? FindAlignment(Transaction tr, string? nombre, bool exacto = false)
    {
        CivilDb.Alignment? primero = null;
        foreach (ObjectId id in CivilDoc.GetAlignmentIds())
        {
            var a = (CivilDb.Alignment)tr.GetObject(id, OpenMode.ForRead);
            primero ??= a;
            if (nombre != null && string.Equals(a.Name, nombre, StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }

        return exacto || nombre != null ? null : primero;
    }
}
