using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Null implementation used when Civil 3D is not loaded (standard AutoCAD). Does not reference Civil 3D types.
/// </summary>
public sealed class NullCivil3DService : ICivil3DService
{
    private static AcadGatewayException Unavailable() =>
        new("civil_unavailable", "This function requires Autodesk Civil 3D 2027.", "civil_available=false");

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<string> GetStatusAsync() =>
        Task.FromResult(JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            civil_available = false,
            message = "Standard AutoCAD: Civil 3D is not loaded.",
        }, GatewayProtocol.WireJsonOptions));

    /// <inheritdoc/>
    public Task<string> ListSurfacesAsync() => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> GetElevationAsync(string? surfaceName, double x, double y) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> ListAlignmentsAsync() => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> CreateAlignmentAsync(CreateAlignmentRequest request) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> CreateSurfaceAsync(CreateSurfaceRequest request) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> GetProfileAsync(string? alignmentName, string? surfaceName, double interval) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> GetSurfaceStatsAsync(string? surfaceName) => throw Unavailable();

    /// <inheritdoc/>
    public Task<string> GetCrossSectionAsync(
        string? alignmentName,
        string? surfaceName,
        double station,
        double leftWidth,
        double rightWidth,
        double step) => throw Unavailable();
}
