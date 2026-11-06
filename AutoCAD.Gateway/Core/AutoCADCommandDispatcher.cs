using System;
using System.Collections.Concurrent;
using Autodesk.AutoCAD.ApplicationServices;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Gestiona la cola concurrente y el despacho seguro de comandos en el hilo principal de AutoCAD mediante el evento Application.Idle.
/// </summary>
public sealed class AutoCADCommandDispatcher : IAutoCADCommandDispatcher
{
    private readonly ConcurrentQueue<string> _commandQueue = new();
    private readonly EventHandler _idleHandler;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="AutoCADCommandDispatcher"/>.
    /// </summary>
    public AutoCADCommandDispatcher()
    {
        _idleHandler = new EventHandler(ExecuteCommandsOnIdle);
    }

    /// <inheritdoc/>
    public void EnqueueCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        _commandQueue.Enqueue(command);
        Autodesk.AutoCAD.ApplicationServices.Application.Idle += _idleHandler;
    }

    /// <summary>
    /// Manejador del evento Idle de AutoCAD que procesa los comandos pendientes en el hilo principal.
    /// </summary>
    /// <param name="sender">El origen del evento.</param>
    /// <param name="e">Los argumentos del evento.</param>
    private void ExecuteCommandsOnIdle(object? sender, EventArgs e)
    {
        // Desvincular inmediatamente para evitar ejecuciones fantasmas o bucles continuos
        Autodesk.AutoCAD.ApplicationServices.Application.Idle -= _idleHandler;

        Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        while (_commandQueue.TryDequeue(out string? cmd))
        {
            if (string.IsNullOrEmpty(cmd)) continue;

            // DIRECTIVA DE SEGURIDAD:
            // \x03\x03 al inicio para cancelar cualquier comando previo que haya quedado activo o pegado.
            string safeCommand = "\x03\x03" + cmd;
            doc.SendStringToExecute(safeCommand, true, false, false);
        }
    }
}
