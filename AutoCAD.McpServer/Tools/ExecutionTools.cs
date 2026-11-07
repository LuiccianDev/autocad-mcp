using System;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using AutoCAD.McpServer.Services;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for command execution, AutoLISP evaluation, viewport navigation, and environment setup in AutoCAD.
/// </summary>
[McpServerToolType]
public sealed class ExecutionTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionTools"/> class.
    /// </summary>
    /// <param name="gatewayClient">The client for communicating with the AutoCAD Gateway.</param>
    public ExecutionTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>
    /// Sends a ping to the AutoCAD Gateway to verify active HTTP connectivity.
    /// </summary>
    /// <returns>Asynchronous task returning connection status.</returns>
    [McpServerTool(Name = "ping_gateway")]
    [Description("Ping the AutoCAD Gateway to verify connectivity.")]
    public async Task<string> PingGateway() => await _gatewayClient.PingGatewayAsync();

    /// <summary>
    /// Executes a sequential list of AutoCAD command strings or macros in a single transactional batch payload.
    /// </summary>
    /// <param name="commandsJson">JSON array of command strings to execute sequentially.</param>
    /// <returns>Asynchronous task returning result of batch injection.</returns>
    [McpServerTool(Name = "execute_batch_commands")]
    [Description("Execute a sequential list of AutoCAD commands or macros in a single batch delivery. 'commandsJson' is a JSON array of command strings.")]
    public async Task<string> ExecuteBatchCommands(string commandsJson)
    {
        if (string.IsNullOrWhiteSpace(commandsJson))
        {
            return "{\"status\": \"error\", \"message\": \"The 'commandsJson' parameter cannot be empty.\"}";
        }

        var cmdSb = new StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(commandsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string s = el.GetString()?.TrimEnd('\r', '\n') ?? "";
                    if (!string.IsNullOrEmpty(s))
                    {
                        cmdSb.Append(s).Append('\n');
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                cmdSb.Append(doc.RootElement.GetString()?.TrimEnd('\r', '\n')).Append('\n');
            }
            else
            {
                return "{\"status\": \"error\", \"message\": \"'commandsJson' must be a JSON array of command strings.\"}";
            }
        }
        catch (JsonException)
        {
            cmdSb.Append(commandsJson.TrimEnd('\r', '\n')).Append('\n');
        }

        return await _gatewayClient.ExecuteCommandAsync(cmdSb.ToString());
    }

    /// <summary>
    /// Enqueues native non-modal AutoCAD commands, AutoLISP macros, or YQArch commands directly into the main UI thread.
    /// </summary>
    /// <param name="command">Command strings to send (must include \\n where Enter is required).</param>
    /// <returns>Asynchronous task returning result of command injection.</returns>
    [McpServerTool(Name = "execute_command")]
    [Description("Enqueue native non-modal AutoCAD commands, AutoLISP macros, or YQArch commands (e.g. WW, JJ, AD, WD, ZXW) directly to AutoCAD through the Gateway. Include newlines (\\n) where Enter is required.")]
    public async Task<string> ExecuteCommand(string command) => await _gatewayClient.ExecuteCommandAsync(command);

    /// <summary>
    /// Evaluates and injects dynamic AutoLISP expressions into AutoCAD.
    /// </summary>
    /// <param name="code">AutoLISP code to evaluate (e.g. '(entmake ...)', '(setq ...)' or full routines).</param>
    /// <returns>Asynchronous task returning confirmation of LISP injection.</returns>
    [McpServerTool(Name = "evaluate_lisp")]
    [Description("Evaluate and inject dynamic AutoLISP expressions directly into AutoCAD (e.g. fast entity creation with entmake, variable or function definitions).")]
    public async Task<string> EvaluateLisp(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "{\"status\": \"error\", \"message\": \"AutoLISP code cannot be empty.\"}";
        }

        string lispCommand = code.Trim();
        if (!lispCommand.EndsWith('\n'))
        {
            lispCommand += "\n";
        }

        return await _gatewayClient.ExecuteCommandAsync(lispCommand);
    }

    /// <summary>
    /// Loads and executes an external AutoLISP script file (.lsp) from disk inside AutoCAD via (load ...).
    /// </summary>
    /// <param name="filePath">Full file path of the .lsp script (e.g. 'C:/scripts/routine.lsp').</param>
    /// <returns>Asynchronous task returning file load result.</returns>
    [McpServerTool(Name = "load_lisp_file")]
    [Description("Load and evaluate an external AutoLISP script file (.lsp) from disk inside AutoCAD via the (load ...) function.")]
    public async Task<string> LoadLispFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "{\"status\": \"error\", \"message\": \"File path cannot be empty.\"}";
        }

        string normalizedPath = filePath.Replace('\\', '/');
        string command = $"(load \"{normalizedPath}\")\n";
        return await _gatewayClient.ExecuteCommandAsync(command);
    }

    /// <summary>
    /// Executes Zoom Extents ('_.ZOOM E') in AutoCAD to center and fit all entities in the viewport.
    /// </summary>
    /// <returns>Asynchronous task returning confirmation.</returns>
    [McpServerTool(Name = "zoom_extents")]
    [Description("Execute Zoom Extents ('_.ZOOM E') in AutoCAD to center and fit all elements in the viewport.")]
    public async Task<string> ZoomExtents() => await _gatewayClient.ExecuteCommandAsync("_.ZOOM\nE\n");

    /// <summary>
    /// Saves the active AutoCAD drawing file ('_.QSAVE').
    /// </summary>
    /// <returns>Asynchronous task returning confirmation.</returns>
    [McpServerTool(Name = "save_drawing")]
    [Description("Save the active AutoCAD drawing file ('_.QSAVE').")]
    public async Task<string> SaveDrawing() => await _gatewayClient.ExecuteCommandAsync("_.QSAVE\n");

    /// <summary>
    /// Configures AutoCAD drawing environment to METRIC METERS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) and calibrates dimensions.
    /// </summary>
    /// <returns>Asynchronous task returning configuration result.</returns>
    [McpServerTool(Name = "configure_drawing_units")]
    [Description("Configure AutoCAD drawing environment to METRIC METERS (INSUNITS=4, MEASUREMENT=1, LUNITS=2, LUPREC=3) and calibrate dimension scales.")]
    public async Task<string> ConfigureDrawingUnits()
    {
        const string script =
            "_.-SETVAR\nINSUNITS\n4\n" +
            "_.-SETVAR\nMEASUREMENT\n1\n" +
            "_.-SETVAR\nLUNITS\n2\n" +
            "_.-SETVAR\nLUPREC\n3\n" +
            "_.-SETVAR\nAUNITS\n0\n" +
            "_.-SETVAR\nANGBASE\n0.0\n" +
            "_.-SETVAR\nANGDIR\n0\n" +
            "_.-SETVAR\nDIMTXT\n0.22\n" +
            "_.-SETVAR\nDIMASZ\n0.18\n" +
            "_.-SETVAR\nDIMEXE\n0.11\n" +
            "_.-SETVAR\nDIMEXO\n0.06\n" +
            "_.-SETVAR\nDIMGAP\n0.08\n" +
            "_.-SETVAR\nDIMDEC\n2\n" +
            "_.-SETVAR\nDIMTAD\n1\n" +
            "_.UCS\nW\n";

        return await _gatewayClient.ExecuteCommandAsync(script);
    }
}
