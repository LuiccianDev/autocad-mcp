using System.Threading.Tasks;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Define las operaciones de consulta e inspección sobre la base de datos de AutoCAD en el hilo principal.
/// </summary>
public interface IAutoCADContextReader
{
    /// <summary>
    /// Obtiene la cantidad de entidades actualmente seleccionadas en el dibujo activo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con la cantidad seleccionada.</returns>
    Task<string> ReadSelectionCountAsync();

    /// <summary>
    /// Obtiene el conteo de entidades agrupadas por tipo en el espacio modelo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con el resumen de entidades.</returns>
    Task<string> ReadEntityCountsAsync();

    /// <summary>
    /// Obtiene la información general y metadatos del dibujo activo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con los metadatos del dibujo.</returns>
    Task<string> ReadDrawingInfoAsync();

    /// <summary>
    /// Obtiene el listado completo de capas y sus propiedades en el dibujo activo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con el listado de capas.</returns>
    Task<string> ReadLayersAsync();

    /// <summary>
    /// Obtiene las coordenadas extremas mínimas y máximas del dibujo activo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con los límites espaciales.</returns>
    Task<string> ReadExtentsAsync();

    /// <summary>
    /// Obtiene las entidades de texto (DBText y MText) existentes en el espacio modelo del dibujo activo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con los textos extraídos.</returns>
    Task<string> ReadTextsAsync();

    /// <summary>
    /// Obtiene los nombres de las definiciones de bloques no anónimas y no espacio papel del dibujo activo.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con las definiciones de bloques.</returns>
    Task<string> ReadBlocksAsync();

    /// <summary>
    /// Captura una imagen de la ventana gráfica o interfaz activa de AutoCAD y la almacena en un archivo PNG.
    /// </summary>
    /// <returns>Una tarea asíncrona cuyo resultado es la respuesta en formato JSON con la ruta del archivo PNG y sus dimensiones.</returns>
    Task<string> CaptureScreenshotAsync();
}
