using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Server;

[assembly: ExtensionApplication(typeof(AutoCAD.Gateway.Main))]
[assembly: CommandClass(typeof(AutoCAD.Gateway.Commands.GatewayCommands))]
[assembly: CommandClass(typeof(AutoCAD.Gateway.Services.LispBridge))]

namespace AutoCAD.Gateway;

/// <summary>
/// Manages the AutoCAD extension application lifecycle (initialization and termination).
/// </summary>
public sealed class Main : IExtensionApplication
{
    /// <inheritdoc/>
    public void Initialize()
    {
        MainThread.Start();
        DocumentSessionManager.Initialize();

        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc != null)
        {
            string version = typeof(Main).Assembly.GetName().Version?.ToString(3) ?? "2.5.0";
            Editor ed = doc.Editor;
            ed.WriteMessage("\n=======================================================\n");
            ed.WriteMessage($">>> AutoCAD AI Gateway Plugin v{version}\n");
            ed.WriteMessage(">>> Created and Developed by Luiccian (@LuiccianDev)\n");
            ed.WriteMessage(">>> Available commands:\n");
            ed.WriteMessage("    - AI_START_SERVER   : Starts the loopback Gateway server.\n");
            ed.WriteMessage("    - AI_STOP_SERVER    : Stops the loopback Gateway server.\n");
            ed.WriteMessage("    - AI_SHOW_CONFIG    : Displays server status and masked token.\n");
            ed.WriteMessage("    - AI_SET_TOKEN      : Sets security token dynamically in memory.\n");
            ed.WriteMessage("    - AI_GENERATE_TOKEN : Generates a new cryptographic session token.\n");
            ed.WriteMessage("    - AI_EDIT_GUARD     : Toggles Edit Guard protection on existing DWGs.\n");
            ed.WriteMessage("=======================================================\n");
        }
    }

    /// <inheritdoc/>
    public void Terminate()
    {
        HttpGatewayServer.Instance.Stop();
        DocumentSessionManager.Shutdown();
        MainThread.Stop();
    }
}
