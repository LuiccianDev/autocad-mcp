using System;
using System.Security.Cryptography;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Server;

namespace AutoCAD.Gateway.Commands;

/// <summary>
/// Registers and manages AutoCAD console commands controlling the lifecycle of AutoCAD AI Gateway.
/// </summary>
public sealed class GatewayCommands
{
    /// <summary>
    /// AutoCAD console command 'AI_START_SERVER' starts the loopback Gateway server.
    /// </summary>
    [CommandMethod("AI_START_SERVER")]
    public void StartServerCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        DocumentSessionManager.ObserveActiveDocument(doc.Name);

        try
        {
            int requestedPort = GatewayProtocol.ResolveConfiguredPort();
            if (HttpGatewayServer.Instance.IsRunning)
            {
                ed.WriteMessage($"\n>>> [NOTICE] AI Gateway Server is already running on 127.0.0.1:{HttpGatewayServer.Instance.BoundPort}\n");
                return;
            }

            HttpGatewayServer.Instance.Start(requestedPort);
            ed.WriteMessage($"\n>>> [ON] AI Gateway Server listening on http://127.0.0.1:{HttpGatewayServer.Instance.BoundPort}/\n");
            if (!string.IsNullOrEmpty(HttpGatewayServer.Instance.InstanceFilePath))
            {
                ed.WriteMessage($">>> Instance descriptor: {HttpGatewayServer.Instance.InstanceFilePath}\n");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n>>> [ERROR] Could not start Gateway server: {ex.Message}\n");
        }
    }

    /// <summary>
    /// AutoCAD console command 'AI_STOP_SERVER' shuts down the loopback Gateway server.
    /// </summary>
    [CommandMethod("AI_STOP_SERVER")]
    public void StopServerCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        try
        {
            HttpGatewayServer.Instance.Stop();
            ed.WriteMessage("\n>>> [OFF] AI Gateway Server stopped successfully.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n>>> [ERROR] Error stopping Gateway server: {ex.Message}\n");
        }
    }

    /// <summary>
    /// AutoCAD console command 'AI_SHOW_CONFIG' displays server network status and masked token.
    /// </summary>
    [CommandMethod("AI_SHOW_CONFIG")]
    public void ShowConfigCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        string token = HttpGatewayServer.ActiveToken;
        string maskedToken = MaskToken(token);

        int port = HttpGatewayServer.Instance.IsRunning
            ? HttpGatewayServer.Instance.BoundPort
            : GatewayProtocol.ResolveConfiguredPort();
        string status = HttpGatewayServer.Instance.IsRunning
            ? $"[ACTIVE on 127.0.0.1:{port}]"
            : $"[STOPPED (configured port {port})]";

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage("\n>>> [AUTOCAD AI GATEWAY CONFIGURATION]");
        ed.WriteMessage($"\n>>> Server Status:   {status}");
        ed.WriteMessage($"\n>>> Listening URL:   http://127.0.0.1:{port}/");
        ed.WriteMessage($"\n>>> Active Token (X-Gateway-Token): {maskedToken}");
        ed.WriteMessage($"\n>>> Edit Guard:      {(DocumentSessionManager.EditGuardEnabled ? "ENABLED (requires new_drawing or allow_edit_document)" : "DISABLED")}");
        if (!string.IsNullOrEmpty(HttpGatewayServer.Instance.InstanceFilePath))
        {
            ed.WriteMessage($"\n>>> Instance File:   {HttpGatewayServer.Instance.InstanceFilePath}");
        }
        ed.WriteMessage("\n>>> Useful commands: AI_START_SERVER, AI_STOP_SERVER, AI_SET_TOKEN, AI_GENERATE_TOKEN, AI_EDIT_GUARD");
        ed.WriteMessage("\n=======================================================\n");
    }

    /// <summary>
    /// AutoCAD console command 'AI_EDIT_GUARD' toggles Edit Guard protection on pre-existing drawings.
    /// </summary>
    [CommandMethod("AI_EDIT_GUARD")]
    public void ToggleEditGuardCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        bool next = !DocumentSessionManager.EditGuardEnabled;
        DocumentSessionManager.EditGuardEnabled = next;
        ed.WriteMessage($"\n>>> [EDIT GUARD] Protection is now {(next ? "ENABLED" : "DISABLED")}.\n");
    }

    /// <summary>
    /// AutoCAD console command 'AI_SET_TOKEN' sets the session token in memory and updates the instance file.
    /// </summary>
    [CommandMethod("AI_SET_TOKEN")]
    public void SetTokenCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        PromptStringOptions options = new("\n>>> Enter new security token for AI Gateway: ")
        {
            AllowSpaces = false
        };

        PromptResult result = ed.GetString(options);
        if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult))
        {
            ed.WriteMessage("\n>>> [NOTICE] Operation cancelled or empty token.\n");
            return;
        }

        ApplySessionToken(ed, result.StringResult.Trim(), "SECURITY TOKEN UPDATED IN MEMORY");
    }

    /// <summary>
    /// AutoCAD console command 'AI_GENERATE_TOKEN' generates a new cryptographic session token.
    /// </summary>
    [CommandMethod("AI_GENERATE_TOKEN")]
    public void GenerateTokenCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        string newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ApplySessionToken(ed, newToken, "NEW SESSION TOKEN GENERATED AND APPLIED");
    }

    private static void ApplySessionToken(Editor ed, string newToken, string heading)
    {
        HttpGatewayServer.ActiveToken = newToken;
        HttpGatewayServer.Instance.RefreshInstanceDescriptor();

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage($"\n>>> [{heading}]");
        ed.WriteMessage($"\n>>> Masked Token: {MaskToken(newToken)}");
        if (!string.IsNullOrEmpty(HttpGatewayServer.Instance.InstanceFilePath))
        {
            ed.WriteMessage($"\n>>> Instance file refreshed: {HttpGatewayServer.Instance.InstanceFilePath}");
        }
        else
        {
            ed.WriteMessage("\n>>> Token active in memory for next AI_START_SERVER.");
        }
        ed.WriteMessage("\n=======================================================\n");
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "(minted on AI_START_SERVER)";
        }

        return token.Length > 8 ? $"{token[..4]}****{token[^4..]}" : "****";
    }
}
