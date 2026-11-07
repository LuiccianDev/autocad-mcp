using System.Threading.Tasks;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Null implementation used when Civil 3D is not loaded (standard AutoCAD). Does not reference Civil 3D types.
/// </summary>
public sealed class NullCivil3DService : ICivil3DService
{
    private const string NotAvailable =
        "{\"status\": \"error\", \"civil_available\": false, \"message\": \"This function requires Autodesk Civil 3D 2027.\"}";

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<string> GetStatusAsync() =>
        Task.FromResult("{\"status\": \"success\", \"civil_available\": false, \"message\": \"Standard AutoCAD: Civil 3D is not loaded.\"}");

    /// <inheritdoc/>
    public Task<string> ListSurfacesAsync() => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> GetElevationAsync(string? surfaceName, double x, double y) => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> ListAlignmentsAsync() => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y) => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset) => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> CreateAlignmentAsync(CrearAlineamientoRequest request) => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> CreateSurfaceAsync(CrearSuperficieRequest request) => Task.FromResult(NotAvailable);

    /// <inheritdoc/>
    public Task<string> GetProfileAsync(string? alignmentName, string? surfaceName, double interval) => Task.FromResult(NotAvailable);
}
