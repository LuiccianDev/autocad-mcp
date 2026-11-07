using System;
using System.Collections.Concurrent;
using Autodesk.AutoCAD.ApplicationServices;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Manages concurrent queue and safe command dispatching onto the AutoCAD main UI thread via Application.Idle.
/// </summary>
public sealed class AutoCADCommandDispatcher : IAutoCADCommandDispatcher
{
    private readonly ConcurrentQueue<string> _commandQueue = new();
    private readonly EventHandler _idleHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoCADCommandDispatcher"/> class.
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
    /// Event handler for AutoCAD Application.Idle processing pending queued commands on the main thread.
    /// </summary>
    private void ExecuteCommandsOnIdle(object? sender, EventArgs e)
    {
        // Unbind immediately to avoid repeated invocation or tight loops on UI thread
        Autodesk.AutoCAD.ApplicationServices.Application.Idle -= _idleHandler;

        Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        while (_commandQueue.TryDequeue(out string? cmd))
        {
            if (string.IsNullOrEmpty(cmd)) continue;

            // SAFETY DIRECTIVE:
            // \x03\x03 cancels any previous stuck command before injecting the new payload.
            string safeCommand = "\x03\x03" + cmd;
            doc.SendStringToExecute(safeCommand, true, false, false);
        }
    }
}
