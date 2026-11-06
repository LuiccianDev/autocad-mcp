using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.Gateway.Server;

[assembly: ExtensionApplication(typeof(AutoCAD.Gateway.Main))]
[assembly: CommandClass(typeof(AutoCAD.Gateway.Commands.GatewayCommands))]

namespace AutoCAD.Gateway;

/// <summary>
/// Gestiona el ciclo de vida del ensamblado de extensión de AutoCAD (inicialización y terminación).
/// </summary>
public sealed class Main : IExtensionApplication
{
    /// <inheritdoc/>
    public void Initialize()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc != null)
        {
            Editor ed = doc.Editor;
            ed.WriteMessage("\n=======================================================\n");
            ed.WriteMessage(">>> AutoCAD AI Gateway Plugin v2.4.0\n");
            ed.WriteMessage(">>> Creado y Desarrollado por Luiccian (@LuiccianDev)\n");
            ed.WriteMessage(">>> Comandos disponibles:\n");
            ed.WriteMessage("    - AI_START_SERVER   : Inicia el servidor HTTP Gateway.\n");
            ed.WriteMessage("    - AI_STOP_SERVER    : Detiene el servidor HTTP Gateway.\n");
            ed.WriteMessage("    - AI_SHOW_CONFIG    : Muestra el estado y token activo.\n");
            ed.WriteMessage("    - AI_SET_TOKEN      : Cambia el token en memoria en caliente.\n");
            ed.WriteMessage("    - AI_GENERATE_TOKEN : Genera un nuevo token criptografico seguro.\n");
            ed.WriteMessage("=======================================================\n");
        }
    }

    /// <inheritdoc/>
    public void Terminate()
    {
        // Garantizar que el servidor HTTP se detenga ordenadamente al cerrar AutoCAD
        HttpGatewayServer.Instance.Stop();
    }
}
