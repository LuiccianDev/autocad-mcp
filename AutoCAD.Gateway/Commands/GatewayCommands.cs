using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.Gateway.Server;

namespace AutoCAD.Gateway.Commands;

/// <summary>
/// Registra y gestiona los comandos de consola de AutoCAD para controlar el ciclo de vida del AI Gateway.
/// </summary>
public sealed class GatewayCommands
{
    private const string DefaultServerUrl = "http://localhost:5055/";

    /// <summary>
    /// Comando de consola de AutoCAD 'AI_START_SERVER' que inicializa el servidor HTTP en el puerto configurado (5055 por defecto).
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
                ed.WriteMessage($"\n>>> [AVISO] AI Gateway Server ya se encuentra en ejecucion en {serverUrl}\n");
                return;
            }

            HttpGatewayServer.Instance.Start(serverUrl);
            ed.WriteMessage($"\n>>> [ON] AI Gateway Server escuchando exitosamente en {serverUrl}\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n>>> [ERROR] No se pudo iniciar el servidor Gateway: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Comando de consola de AutoCAD 'AI_STOP_SERVER' que apaga el servidor HTTP.
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
            ed.WriteMessage("\n>>> [OFF] AI Gateway Server detenido correctamente.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n>>> [ERROR] Error al detener el servidor Gateway: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Comando de consola 'AI_SHOW_CONFIG' que muestra el estado de red y credenciales activas del Gateway.
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
        string estado = HttpGatewayServer.Instance.IsRunning ? $"[ACTIVO en puerto {port}]" : $"[DETENIDO (puerto {port})]";

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage($"\n>>> [CONFIGURACION ACTUAL DE AUTOCAD AI GATEWAY]");
        ed.WriteMessage($"\n>>> Estado del Servidor: {estado}");
        ed.WriteMessage($"\n>>> Puerto de Escucha:   {port} ({serverUrl})");
        ed.WriteMessage($"\n>>> Token Activo (X-Gateway-Token): {maskedToken}");
        ed.WriteMessage($"\n>>> Comandos utiles: AI_START_SERVER, AI_STOP_SERVER, AI_SET_TOKEN, AI_GENERATE_TOKEN");
        ed.WriteMessage("\n=======================================================\n");
    }

    /// <summary>
    /// Comando de consola 'AI_SET_TOKEN' que permite cambiar el token del Gateway en caliente en memoria.
    /// </summary>
    [CommandMethod("AI_SET_TOKEN")]
    public void SetTokenCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        PromptStringOptions options = new("\n>>> Introduce el nuevo token para el AI Gateway: ")
        {
            AllowSpaces = false
        };

        PromptResult result = ed.GetString(options);
        if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult))
        {
            ed.WriteMessage("\n>>> [AVISO] Operacion cancelada o token vacio.\n");
            return;
        }

        string nuevoToken = result.StringResult.Trim();
        HttpGatewayServer.ActiveToken = nuevoToken;

        try
        {
            Environment.SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", nuevoToken, EnvironmentVariableTarget.User);
        }
        catch
        {
            // Ignorar si el usuario carece de permisos para modificar el registro de variables
        }

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage("\n>>> [EXITO] Token actualizado en memoria en tiempo real.");
        ed.WriteMessage($"\n>>> Nuevo Token: {nuevoToken}");
        ed.WriteMessage("\n>>> Recuerda actualizar tu configuracion de MCP (claude_desktop_config / mcp_config).");
        ed.WriteMessage("\n=======================================================\n");
    }

    /// <summary>
    /// Comando de consola 'AI_GENERATE_TOKEN' que genera un nuevo token criptográfico seguro y lo activa en memoria.
    /// </summary>
    [CommandMethod("AI_GENERATE_TOKEN")]
    public void GenerateTokenCommand()
    {
        Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        string nuevoToken = "luiccian-" + Guid.NewGuid().ToString("N");
        HttpGatewayServer.ActiveToken = nuevoToken;

        try
        {
            Environment.SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", nuevoToken, EnvironmentVariableTarget.User);
        }
        catch
        {
            // Ignorar si el usuario carece de permisos para modificar el registro de variables
        }

        ed.WriteMessage("\n=======================================================");
        ed.WriteMessage("\n>>> [NUEVO TOKEN SEGURO GENERADO Y APLICADO]");
        ed.WriteMessage($"\n>>> Token: {nuevoToken}");
        ed.WriteMessage("\n>>> Ha sido guardado en la variable de usuario Windows 'AUTOCAD_GATEWAY_TOKEN'.");
        ed.WriteMessage("\n>>> Copia este token en tu cliente MCP (claude_desktop_config.json / mcp_config.json).");
        ed.WriteMessage("\n=======================================================\n");
    }
}
