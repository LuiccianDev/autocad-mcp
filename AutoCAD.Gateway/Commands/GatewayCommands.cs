using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.Gateway.Server;

namespace AutoCAD.Gateway.Commands;

/// <summary>
/// Registers and manages AutoCAD console commands controlling the lifecycle of AutoCAD AI Gateway.
/// </summary>
public sealed class GatewayCommands
{
    private const string DefaultServerUrl = "http://localhost:5055/";

    /// <summary>
    /// AutoCAD console command 'AI_START_SERVER' initializes the HTTP server on configured port (5055 by default).
    /// </summary>
    [CommandMethod("AI_START_SERVER")]
    public void StartServerCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        try
        {
            string? envPort = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT");
            string serverUrl = !string.IsNullOrWhiteSpace(envPort)
                ? $"http://localhost:{envPort.Trim()}/"
                : (Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? DefaultServerUrl);

            if (!serverUrl.EndsWith("/"))
            {
                serverUrl += "/";
            }

            if (HttpGatewayServer.Instance.IsRunning)
            {
                ed.WriteMessage($"\n>>> [NOTICE] AI Gateway Server is already running at {serverUrl}\n");
                return;
            }

            HttpGatewayServer.Instance.Start(serverUrl);
            ed.WriteMessage($"\n>>> [ON] AI Gateway Server successfully listening at {serverUrl}\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n>>> [ERROR] Could not start Gateway server: {ex.Message}\n");
        }
    }

    /// <summary>
    /// AutoCAD console command 'AI_STOP_SERVER' shuts down the HTTP server.
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
    /// AutoCAD console command 'AI_SHOW_CONFIG' displays server network status and active security token.
    /// </summary>
    [CommandMethod("AI_SHOW_CONFIG")]
    public void ShowConfigCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        string token = HttpGatewayServer.ActiveToken;
        string maskedToken = token.Length > 8
            ? $"{token[..4]}****{token[^4..]}"
            : "****";

        string port = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_PORT") ?? "5055";
        string serverUrl = Environment.GetEnvironmentVariable("AUTOCAD_GATEWAY_URL") ?? $"http://localhost:{port}/";
        string status = HttpGatewayServer.Instance.IsRunning ? $"[ACTIVE on port {port}]" : $"[STOPPED (port {port})]";

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage("\n>>> [AUTOCAD AI GATEWAY CONFIGURATION]");
        ed.WriteMessage($"\n>>> Server Status:   {status}");
        ed.WriteMessage($"\n>>> Listening URL:   {serverUrl}");
        ed.WriteMessage($"\n>>> Active Token (X-Gateway-Token): {maskedToken}");
        ed.WriteMessage("\n>>> Useful commands: AI_START_SERVER, AI_STOP_SERVER, AI_SET_TOKEN, AI_GENERATE_TOKEN");
        ed.WriteMessage("\n=======================================================\n");
    }

    /// <summary>
    /// AutoCAD console command 'AI_SET_TOKEN' allows dynamically changing the security token in memory.
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

        string newToken = result.StringResult.Trim();
        HttpGatewayServer.ActiveToken = newToken;

        try
        {
            Environment.SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", newToken, EnvironmentVariableTarget.User);
        }
        catch
        {
            // Ignore if user lacks permissions to modify Windows registry variables
        }

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage("\n>>> [SUCCESS] Security token updated in memory in real time.");
        ed.WriteMessage($"\n>>> New Token: {newToken}");
        ed.WriteMessage("\n>>> Remember to update your MCP client configuration (claude_desktop_config / mcp_config).");
        ed.WriteMessage("\n=======================================================\n");
    }

    /// <summary>
    /// AutoCAD console command 'AI_GENERATE_TOKEN' generates a new cryptographic token and applies it in memory.
    /// </summary>
    [CommandMethod("AI_GENERATE_TOKEN")]
    public void GenerateTokenCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        string newToken = "luiccian-" + Guid.NewGuid().ToString("N");
        HttpGatewayServer.ActiveToken = newToken;

        try
        {
            Environment.SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", newToken, EnvironmentVariableTarget.User);
        }
        catch
        {
            // Ignore if user lacks permissions
        }

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage("\n>>> [NEW SECURE TOKEN GENERATED AND APPLIED]");
        ed.WriteMessage($"\n>>> Token: {newToken}");
        ed.WriteMessage("\n>>> Saved to Windows user environment variable 'AUTOCAD_GATEWAY_TOKEN'.");
        ed.WriteMessage("\n>>> Copy this token into your MCP client config (claude_desktop_config.json / mcp_config.json).");
        ed.WriteMessage("\n=======================================================\n");
    }
}
