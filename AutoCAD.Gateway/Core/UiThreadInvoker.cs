using System;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Ejecuta delegados sobre el documento activo en el hilo principal de AutoCAD (evento Idle).
/// </summary>
public static class UiThreadInvoker
{
    /// <summary>
    /// Ejecuta una función sobre el documento activo en el hilo UI y devuelve su resultado serializado.
    /// </summary>
    /// <param name="query">Función que recibe el documento activo y retorna una cadena JSON.</param>
    /// <returns>Tarea con el resultado; falla con la excepción original si la función lanza.</returns>
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
                    tcs.SetResult("{\"status\": \"error\", \"message\": \"No hay documento abierto\"}");
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
