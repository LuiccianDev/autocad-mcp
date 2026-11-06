using System.Threading.Tasks;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Implementación nula usada cuando Civil 3D no está cargado (AutoCAD estándar). No referencia tipos de Civil 3D.
/// </summary>
public sealed class NullCivil3DService : ICivil3DService
{
    private const string NoDisponible =
        "{\"status\": \"error\", \"civil_available\": false, \"message\": \"Esta funcion requiere Civil 3D 2027.\"}";

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<string> GetStatusAsync() =>
        Task.FromResult("{\"status\": \"success\", \"civil_available\": false, \"message\": \"AutoCAD estandar: Civil 3D no esta cargado.\"}");

    /// <inheritdoc/>
    public Task<string> ListSurfacesAsync() => Task.FromResult(NoDisponible);

    /// <inheritdoc/>
    public Task<string> GetElevationAsync(string? surfaceName, double x, double y) => Task.FromResult(NoDisponible);

    /// <inheritdoc/>
    public Task<string> ListAlignmentsAsync() => Task.FromResult(NoDisponible);

    /// <inheritdoc/>
    public Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y) => Task.FromResult(NoDisponible);

    /// <inheritdoc/>
    public Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset) => Task.FromResult(NoDisponible);

    /// <inheritdoc/>
    public Task<string> CreateAlignmentAsync(CrearAlineamientoRequest request) => Task.FromResult(NoDisponible);

    /// <inheritdoc/>
    public Task<string> CreateSurfaceAsync(CrearSuperficieRequest request) => Task.FromResult(NoDisponible);
}
