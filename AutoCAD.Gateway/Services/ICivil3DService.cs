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
    /// <returns>Serialized JSON status summary.</returns>
    Task<string> GetStatusAsync();

    /// <summary>Lists TIN surfaces in the drawing with minimum and maximum elevations.</summary>
    /// <returns>Serialized JSON surface catalog.</returns>
    Task<string> ListSurfacesAsync();

    /// <summary>Queries ground elevation at point (x, y).</summary>
    /// <param name="surfaceName">Surface name; null uses the first available.</param>
    /// <param name="x">East coordinate.</param>
    /// <param name="y">North coordinate.</param>
    /// <returns>Serialized JSON elevation reading.</returns>
    Task<string> GetElevationAsync(string? surfaceName, double x, double y);

    /// <summary>Lists alignments with start and end stations.</summary>
    /// <returns>Serialized JSON alignment catalog.</returns>
    Task<string> ListAlignmentsAsync();

    /// <summary>Calculates station and offset of point (x, y) relative to an alignment.</summary>
    /// <param name="alignmentName">Alignment name; null uses the first available.</param>
    /// <param name="x">East coordinate.</param>
    /// <param name="y">North coordinate.</param>
    /// <returns>Serialized JSON station and lateral offset.</returns>
    Task<string> GetStationOffsetAsync(string? alignmentName, double x, double y);

    /// <summary>Calculates (x, y) coordinate of a given station and offset.</summary>
    /// <param name="alignmentName">Alignment name; null uses the first available.</param>
    /// <param name="station">Station in meters.</param>
    /// <param name="offset">Lateral offset in meters (+ right).</param>
    /// <returns>Serialized JSON world coordinates.</returns>
    Task<string> GetPointAtStationAsync(string? alignmentName, double station, double offset);

    /// <summary>Creates a horizontal alignment from PIs with optional circular curves.</summary>
    /// <param name="request">Validated creation request.</param>
    /// <returns>Serialized JSON confirmation including alignment length and station range.</returns>
    Task<string> CreateAlignmentAsync(CreateAlignmentRequest request);

    /// <summary>Creates a TIN surface from survey points.</summary>
    /// <param name="request">Validated creation request.</param>
    /// <returns>Serialized JSON confirmation including point count.</returns>
    Task<string> CreateSurfaceAsync(CreateSurfaceRequest request);

    /// <summary>Samples longitudinal ground profile along an alignment in a single atomic transaction.</summary>
    /// <param name="alignmentName">Alignment name; null uses the first available.</param>
    /// <param name="surfaceName">Surface name; null uses the first available.</param>
    /// <param name="interval">Sampling station interval in meters.</param>
    /// <returns>Serialized JSON longitudinal profile samples.</returns>
    Task<string> GetProfileAsync(string? alignmentName, string? surfaceName, double interval);

    /// <summary>Gets elevation statistics, 2D/3D surface areas, point count, and triangle count for a surface.</summary>
    /// <param name="surfaceName">Surface name; null uses the first available.</param>
    /// <returns>Serialized JSON surface statistics.</returns>
    Task<string> GetSurfaceStatsAsync(string? surfaceName);

    /// <summary>Samples a transverse ground cross-section perpendicular to an alignment at <paramref name="station"/>.</summary>
    /// <param name="alignmentName">Alignment name; null uses the first available.</param>
    /// <param name="surfaceName">Surface name; null uses the first available.</param>
    /// <param name="station">Chainage station along the alignment in meters.</param>
    /// <param name="leftWidth">Sampling distance to the left (negative offset) in meters.</param>
    /// <param name="rightWidth">Sampling distance to the right (positive offset) in meters.</param>
    /// <param name="step">Transverse sampling interval in meters.</param>
    /// <returns>Serialized JSON cross-section offsets, coordinates, and elevations.</returns>
    Task<string> GetCrossSectionAsync(
        string? alignmentName,
        string? surfaceName,
        double station,
        double leftWidth,
        double rightWidth,
        double step);
}
