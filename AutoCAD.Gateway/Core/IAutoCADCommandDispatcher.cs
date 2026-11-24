using Autodesk.AutoCAD.ApplicationServices;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Contract for thread-safe queuing and dispatching of AutoCAD commands on the main UI thread.
/// </summary>
public interface IAutoCADCommandDispatcher
{
    /// <summary>
    /// Enqueues a command string to execute on the next AutoCAD idle cycle against the active document.
    /// </summary>
    /// <param name="command">Command string or script to execute.</param>
    void EnqueueCommand(string command);

    /// <summary>
    /// Enqueues a command string bound to a specific validated <paramref name="expectedDocument"/>.
    /// </summary>
    /// <param name="expectedDocument">Document verified writable at enqueue time.</param>
    /// <param name="command">Command string or script to execute.</param>
    void EnqueueCommand(Document expectedDocument, string command);
}
