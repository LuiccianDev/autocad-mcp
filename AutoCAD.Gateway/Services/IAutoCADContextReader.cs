using System.Threading.Tasks;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Defines query and inspection operations executed safely on AutoCAD's main thread.
/// </summary>
public interface IAutoCADContextReader
{
    /// <summary>Gets the count of entities currently selected in the active drawing.</summary>
    /// <returns>Asynchronous task returning serialized JSON selection count.</returns>
    Task<string> ReadSelectionCountAsync();

    /// <summary>Gets entity counts grouped by DXF/object type in ModelSpace.</summary>
    /// <returns>Asynchronous task returning serialized JSON entity counts.</returns>
    Task<string> ReadEntityCountsAsync();

    /// <summary>Gets general metadata and unit configuration for the active drawing.</summary>
    /// <returns>Asynchronous task returning serialized JSON drawing metadata.</returns>
    Task<string> ReadDrawingInfoAsync();

    /// <summary>Gets the complete layer table and properties for the active drawing.</summary>
    /// <returns>Asynchronous task returning serialized JSON layer list.</returns>
    Task<string> ReadLayersAsync();

    /// <summary>Gets minimum and maximum world extents of the active drawing.</summary>
    /// <returns>Asynchronous task returning serialized JSON extents.</returns>
    Task<string> ReadExtentsAsync();

    /// <summary>Gets all DBText and MText entities present in ModelSpace.</summary>
    /// <returns>Asynchronous task returning serialized JSON text entities.</returns>
    Task<string> ReadTextsAsync();

    /// <summary>Gets non-anonymous, non-layout block definition names in the active drawing.</summary>
    /// <returns>Asynchronous task returning serialized JSON block definitions.</returns>
    Task<string> ReadBlocksAsync();

    /// <summary>Captures a PNG screenshot of AutoCAD's active viewport or main window.</summary>
    /// <param name="inline">When true, includes base64-encoded PNG bytes if within the size cap.</param>
    /// <returns>Asynchronous task returning serialized JSON path, dimensions, and optional base64 payload.</returns>
    Task<string> CaptureScreenshotAsync(bool inline = false);
}
