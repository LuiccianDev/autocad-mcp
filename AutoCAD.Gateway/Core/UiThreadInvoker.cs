using System;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Executes delegates on the active document in the AutoCAD main UI thread (Application.Idle event).
/// </summary>
public static class UiThreadInvoker
{
    /// <summary>
    /// Executes a function on the active document on the UI thread and returns its serialized result.
    /// </summary>
    /// <param name="query">Function receiving active document and returning a JSON string.</param>
    /// <returns>Task with result; fails if the function throws.</returns>
    public static Task<string> RunOnIdleAsync(Func<Document, string> query)
    {
        var tcs = new TaskCompletionSource<string>();
        EventHandler? handler = null;
        handler = (s, e) =>
        {
            if (handler != null)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle -= handler;
            }
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    tcs.SetResult("{\"status\": \"error\", \"message\": \"No active drawing document open in AutoCAD.\"}");
                    return;
                }

                tcs.SetResult(query(doc));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };

        Autodesk.AutoCAD.ApplicationServices.Application.Idle += handler;
        return tcs.Task;
    }
}
