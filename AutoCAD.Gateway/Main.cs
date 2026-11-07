using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.Gateway.Server;

[assembly: ExtensionApplication(typeof(AutoCAD.Gateway.Main))]
[assembly: CommandClass(typeof(AutoCAD.Gateway.Commands.GatewayCommands))]

namespace AutoCAD.Gateway;

/// <summary>
/// Manages the AutoCAD extension application lifecycle (initialization and termination).
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
            ed.WriteMessage(">>> Created and Developed by Luiccian (@LuiccianDev)\n");
            ed.WriteMessage(">>> Available commands:\n");
            ed.WriteMessage("    - AI_START_SERVER   : Starts the HTTP Gateway server.\n");
            ed.WriteMessage("    - AI_STOP_SERVER    : Stops the HTTP Gateway server.\n");
            ed.WriteMessage("    - AI_SHOW_CONFIG    : Displays server status and active token.\n");
            ed.WriteMessage("    - AI_SET_TOKEN      : Sets security token dynamically in memory.\n");
            ed.WriteMessage("    - AI_GENERATE_TOKEN : Generates a new secure cryptographic token.\n");
            ed.WriteMessage("=======================================================\n");
        }
    }

    /// <inheritdoc/>
    public void Terminate()
    {
        // Ensure the HTTP server stops gracefully upon AutoCAD exit
        HttpGatewayServer.Instance.Stop();
    }
}
