namespace AutoCAD.Gateway.Core;

/// <summary>
/// Define el contrato para el encolado y despacho seguro de comandos en el hilo principal de AutoCAD.
/// </summary>
public interface IAutoCADCommandDispatcher
{
    /// <summary>
    /// Encola un comando de texto para ser despachado en el siguiente ciclo ocioso de AutoCAD.
    /// </summary>
    /// <param name="command">La cadena de comando o script a ejecutar.</param>
    void EnqueueCommand(string command);
}
