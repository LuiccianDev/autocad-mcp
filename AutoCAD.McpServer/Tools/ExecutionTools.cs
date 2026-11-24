using System;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for command execution, AutoLISP evaluation, viewport navigation, and environment setup in AutoCAD.
/// </summary>
[McpServerToolType]
[ToolDomain("execution", "autocad_execution", "Ping Gateway, run AutoCAD commands or batches, evaluate or load AutoLISP, zoom, save, and configure metric units.")]
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
    [ToolExposure(Core = true)]
    [Description("Ping the AutoCAD Gateway to verify connectivity.")]
    public async Task<string> PingGateway() => await _gatewayClient.PingGatewayAsync();

    /// <summary>
    /// Executes a sequential list of AutoCAD command strings or macros in a single transactional batch payload.
    /// </summary>
    /// <param name="commandsJson">JSON array of command strings to execute sequentially.</param>
    /// <returns>Asynchronous task returning result of batch injection.</returns>
    [McpServerTool(Name = "execute_batch_commands")]
    [Description("Execute a sequential list of AutoCAD commands or macros in a single batch delivery. 'commandsJson' is a JSON array of command strings.")]
    public async Task<string> ExecuteBatchCommands(
        [CompoundParam("commands", StructuredJson = StructuredJsonKind.Array)] string commandsJson)
    {
        if (string.IsNullOrWhiteSpace(commandsJson))
        {
            return CadFormat.Error("The 'commandsJson' parameter cannot be empty.");
        }

        var cmdSb = new StringBuilder();
        string trimmed = commandsJson.Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.String)
                    {
                        return CadFormat.Error("All entries in 'commandsJson' array must be strings.");
                    }

                    string s = el.GetString()?.TrimEnd('\r', '\n') ?? "";
                    if (s.Contains('\0'))
                    {
                        return CadFormat.Error("Commands cannot contain NUL characters.");
                    }

                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        cmdSb.Append(s).Append('\n');
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                string single = doc.RootElement.GetString()?.TrimEnd('\r', '\n') ?? "";
                if (single.Contains('\0'))
                {
                    return CadFormat.Error("Commands cannot contain NUL characters.");
                }

                cmdSb.Append(single).Append('\n');
            }
            else
            {
                return CadFormat.Error("'commandsJson' must be a JSON array of command strings.");
            }
        }
        catch (JsonException)
        {
            if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
            {
                return CadFormat.Error("Malformed JSON in 'commandsJson'. Expected a valid JSON array of command strings.");
            }

            if (commandsJson.Contains('\0'))
            {
                return CadFormat.Error("Commands cannot contain NUL characters.");
            }

            cmdSb.Append(commandsJson.TrimEnd('\r', '\n')).Append('\n');
        }

        if (cmdSb.Length == 0 || string.IsNullOrWhiteSpace(cmdSb.ToString()))
        {
            return CadFormat.Error("'commandsJson' must contain at least one non-empty command.");
        }

        return await _gatewayClient.ExecuteCommandAsync(cmdSb.ToString());
    }

    /// <summary>
    /// Enqueues native non-modal AutoCAD commands, AutoLISP macros, or YQArch commands directly into the main UI thread.
    /// </summary>
    /// <param name="command">Command strings to send (must include \\n where Enter is required).</param>
    /// <returns>Asynchronous task returning result of command injection.</returns>
    [McpServerTool(Name = "execute_command")]
    [ToolExposure(Core = true)]
    [Description("Enqueue native non-modal AutoCAD commands, AutoLISP macros, or YQArch commands (e.g. WW, JJ, AD, WD, ZXW) directly to AutoCAD through the Gateway. Include newlines (\\n) where Enter is required.")]
    public async Task<string> ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Contains('\0'))
        {
            return CadFormat.Error("'command' cannot be empty or contain NUL characters.");
        }

        return await _gatewayClient.ExecuteCommandAsync(command);
    }

    /// <summary>
    /// Evaluates AutoLISP code synchronously inside AutoCAD and returns its converted JSON value.
    /// </summary>
    /// <param name="code">AutoLISP code to evaluate (e.g. '(entmake ...)', '(setq ...)' or full routines).</param>
    /// <param name="timeoutMs">Evaluation timeout in milliseconds (250 to 120000, default 15000).</param>
    /// <returns>JSON containing evaluated value, document name, and command-line state.</returns>
    [McpServerTool(Name = "evaluate_lisp")]
    [ToolExposure(Core = true)]
    [Description("Evaluate AutoLISP code synchronously inside AutoCAD and return its JSON result and post-execution command-line state.")]
    public async Task<string> EvaluateLisp(string code, int timeoutMs = 15_000)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CadFormat.Error("AutoLISP code cannot be empty.");
        }

        int clampedTimeout = GatewayProtocol.ClampLispTimeoutMs(timeoutMs);
        string payload = JsonSerializer.Serialize(new
        {
            code = code.Trim(),
            timeout_ms = clampedTimeout,
        });
        return await _gatewayClient.PostJsonAsync("lisp/eval", payload, timeoutMs: clampedTimeout);
    }

    /// <summary>
    /// Loads and executes an external AutoLISP script file (.lsp) from disk inside AutoCAD via (load ...).
    /// </summary>
    /// <param name="filePath">Full file path of the .lsp script (e.g. 'C:/scripts/routine.lsp').</param>
    /// <returns>Asynchronous task returning file load result.</returns>
    [McpServerTool(Name = "load_lisp_file")]
    [Description("Load and evaluate an external AutoLISP script file (.lsp) from disk inside AutoCAD via synchronous LISP evaluation.")]
    public async Task<string> LoadLispFile(string filePath)
    {
        if (!SecurityPolicy.ValidateLocalFilePath(filePath, out string normPath, out string? pathErr, ".lsp"))
        {
            return CadFormat.Error(pathErr ?? "Invalid AutoLISP script path.");
        }

        string fwd = normPath.Replace('\\', '/');
        return await EvaluateLisp($"(load {CadFormat.LispString(fwd)})");
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
    [ToolExposure(Core = true)]
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

    /// <summary>Alias for ping_gateway.</summary>
    [McpServerTool(Name = "ping_autocad_gateway")]
    [ToolExposure(AliasOf = "ping_gateway")]
    [Description("Alias for ping_gateway: Ping the AutoCAD Gateway to verify connectivity.")]
    public async Task<string> PingAutoCADGateway() => await PingGateway();

    /// <summary>Alias for evaluate_lisp.</summary>
    [McpServerTool(Name = "execute_lisp")]
    [ToolExposure(AliasOf = "evaluate_lisp")]
    [Description("Alias for evaluate_lisp: Evaluate AutoLISP code synchronously inside AutoCAD.")]
    public async Task<string> ExecuteLisp(string code, int timeoutMs = 15_000) => await EvaluateLisp(code, timeoutMs);

    /// <summary>Alias for configure_drawing_units.</summary>
    [McpServerTool(Name = "configure_drawing_environment")]
    [ToolExposure(AliasOf = "configure_drawing_units")]
    [Description("Alias for configure_drawing_units: Configure AutoCAD drawing environment to METRIC METERS.")]
    public async Task<string> ConfigureDrawingEnvironment() => await ConfigureDrawingUnits();
}
