using System.Threading.Tasks;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Contract for Autodesk Civil 3D access (surfaces and alignments). All responses are serialized JSON.
/// </summary>
public interface ICivil3DService
{
    /// <summary>Indicates whether Civil 3D API is available in the current session.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns availability status of Civil 3D and active document.</summary>
    Task<string> GetStatusAsync();

    /// <summary>Lists TIN surfaces in the drawing with minimum and maximum elevations.</summary>
    Task<string> ListSurfacesAsync();

    /// <summary>Queries ground elevation at point (x, y).</summary>
    /// <param name="surfaceName">Surface name; null uses the first available.</param>
    /// <param name="x">East coordinate.</param>
    /// <param name="y">North coordinate.</param>
    Task<string> GetElevationAsync(string? surfaceName, double x, double y);

    /// <summary>Lists alignments with start and end stations.</summary>
    Task<string> ListAlignmentsAsync();

    /// <summary>Calculates station and offset of point (x, y) relative to an alignment.</summary>
    /// <param name="alignmentName">Alignment name; null uses the first available.</param>
    /// <param name="x">East coordinate.</param>
    /// <param name="y">North coordinate.</param>
    Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y);

    /// <summary>Calculates (x, y) coordinate of a given station and offset.</summary>
    /// <param name="alignmentName">Alignment name; null uses the first available.</param>
    /// <param name="station">Station in meters.</param>
    /// <param name="offset">Lateral offset in meters (+ right).</param>
    Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset);

    /// <summary>Creates a horizontal alignment from PIs with optional circular curves.</summary>
    /// <param name="request">Validated creation request.</param>
    Task<string> CreateAlignmentAsync(CrearAlineamientoRequest request);

    /// <summary>Creates a TIN surface from survey points.</summary>
    /// <param name="request">Validated creation request.</param>
    Task<string> CreateSurfaceAsync(CrearSuperficieRequest request);
}
