using System;
using System.Collections.Concurrent;
using Autodesk.AutoCAD.ApplicationServices;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Manages concurrent queue and safe command dispatching onto the AutoCAD main UI thread via Application.Idle.
/// </summary>
public sealed class AutoCADCommandDispatcher : IAutoCADCommandDispatcher
{
    private readonly record struct QueuedCommand(Document? ExpectedDocument, string Command);

    private readonly ConcurrentQueue<QueuedCommand> _commandQueue = new();
    private readonly EventHandler _idleHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoCADCommandDispatcher"/> class.
    /// </summary>
    public AutoCADCommandDispatcher()
    {
        _idleHandler = new EventHandler(ExecuteCommandsOnIdle);
    }

    /// <inheritdoc/>
    public void EnqueueCommand(string command) =>
        EnqueueInternal(expectedDocument: null, command);

    /// <inheritdoc/>
    public void EnqueueCommand(Document expectedDocument, string command) =>
        EnqueueInternal(expectedDocument, command);

    private void EnqueueInternal(Document? expectedDocument, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        _commandQueue.Enqueue(new QueuedCommand(expectedDocument, command));
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

        while (_commandQueue.TryDequeue(out QueuedCommand item))
        {
            if (string.IsNullOrEmpty(item.Command)) continue;

            if (item.ExpectedDocument is not null && !ReferenceEquals(doc, item.ExpectedDocument))
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Dropped queued /execute command because MdiActiveDocument switched before idle dispatch.");
                continue;
            }

            try
            {
                Acad.ValidateWritable(doc, DocumentSessionManager.GetOrCreate(doc), requireIdle: false);
            }
            catch (AcadGatewayException ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Dropped queued /execute command at idle time due to write guard ({0}): {1}", ex.Code, ex.Message);
                continue;
            }

            // \x03\x03 cancels any previous stuck command before injecting the payload.
            string safeCommand = "\x03\x03" + item.Command;
            doc.SendStringToExecute(safeCommand, true, false, false);
        }
    }
}
