using System.Threading.Tasks;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Contrato de acceso a Autodesk Civil 3D (superficies y alineamientos). Todas las respuestas son JSON.
/// </summary>
public interface ICivil3DService
{
    /// <summary>Indica si la API de Civil 3D está disponible en la sesión actual.</summary>
    bool IsAvailable { get; }

    /// <summary>Devuelve el estado de disponibilidad de Civil 3D y del documento activo.</summary>
    Task<string> GetStatusAsync();

    /// <summary>Lista las superficies del dibujo con cotas mínima y máxima.</summary>
    Task<string> ListSurfacesAsync();

    /// <summary>Obtiene la cota del terreno en (x, y).</summary>
    /// <param name="surfaceName">Nombre de la superficie; null usa la primera.</param>
    /// <param name="x">Coordenada Este.</param>
    /// <param name="y">Coordenada Norte.</param>
    Task<string> GetElevationAsync(string? surfaceName, double x, double y);

    /// <summary>Lista los alineamientos con sus progresivas inicial y final.</summary>
    Task<string> ListAlignmentsAsync();

    /// <summary>Calcula progresiva y desfase de un punto respecto a un alineamiento.</summary>
    /// <param name="alignmentName">Nombre del alineamiento; null usa el primero.</param>
    /// <param name="x">Coordenada Este.</param>
    /// <param name="y">Coordenada Norte.</param>
    Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y);

    /// <summary>Calcula la coordenada (x, y) de una progresiva y desfase dados.</summary>
    /// <param name="alignmentName">Nombre del alineamiento; null usa el primero.</param>
    /// <param name="station">Progresiva en metros.</param>
    /// <param name="offset">Desfase lateral en metros (+ derecha).</param>
    Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset);

    /// <summary>Crea un alineamiento horizontal desde PIs, con curvas circulares opcionales.</summary>
    /// <param name="request">Datos de creación validados por el servicio.</param>
    Task<string> CreateAlignmentAsync(CrearAlineamientoRequest request);

    /// <summary>Crea una superficie TIN desde puntos topográficos.</summary>
    /// <param name="request">Datos de creación validados por el servicio.</param>
    Task<string> CreateSurfaceAsync(CrearSuperficieRequest request);
}
