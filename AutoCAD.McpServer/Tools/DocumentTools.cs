using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Provides MCP tools for AutoCAD multi-document management, Edit Guard authorization,
/// command cancellation, and system variable inspection/mutation.
/// </summary>
[McpServerToolType]
[ToolDomain("documents", "autocad_documents", "List, open, create, activate, close, and authorize MDI drawings, cancel commands, and inspect or set system variables.")]
public sealed class DocumentTools
{
    private readonly IAutoCADGatewayClient _gatewayClient;

    /// <summary>Initializes a new instance of the <see cref="DocumentTools"/> class.</summary>
    /// <param name="gatewayClient">Client for communicating with AutoCAD Gateway.</param>
    public DocumentTools(IAutoCADGatewayClient gatewayClient)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
    }

    /// <summary>Lists all open drawing documents with session IDs, revisions, and Edit Guard ownership.</summary>
    [McpServerTool(Name = "list_documents")]
    [ToolExposure(Core = true)]
    [Description("List all open AutoCAD drawing documents with session IDs, active status, read-only flags, and Edit Guard authorization.")]
    public Task<string> ListDocuments() => _gatewayClient.GetContextAsync("documents");

    /// <summary>Cancels any unfinished AutoCAD command-line prompt (Ctrl+C escape) and reports state.</summary>
    [McpServerTool(Name = "cancel_command")]
    [Description("Send Ctrl+C cancellation to clear an unfinished AutoCAD command-line prompt and return the updated command state.")]
    public Task<string> CancelCommand() => _gatewayClient.PostJsonAsync("session/cancel", "{}");

    /// <summary>Marks an open drawing as managed so writes succeed when Edit Guard is enabled.</summary>
    [McpServerTool(Name = "allow_edit_document")]
    [ToolExposure(Core = true)]
    [Description("Authorize (or revoke) edits on an existing open drawing when Edit Guard is active.")]
    public Task<string> AllowEditDocument(string? document = null, bool allow = true)
    {
        string payload = JsonSerializer.Serialize(new { document, allow });
        return _gatewayClient.PostJsonAsync("session/allow-edit", payload);
    }

    /// <summary>Creates a new drawing document and marks it Gateway-managed.</summary>
    [McpServerTool(Name = "new_drawing")]
    [Description("Create a new AutoCAD drawing document (optionally from a validated .dwt template) and authorize edits on it.")]
    public async Task<string> NewDrawing(string? template = null)
    {
        string? validatedTemplate = null;
        if (!string.IsNullOrWhiteSpace(template))
        {
            if (!SecurityPolicy.ValidateLocalFilePath(template, out string norm, out string? err, ".dwt"))
            {
                return CadFormat.Error(err ?? "Invalid template path.");
            }

            validatedTemplate = norm;
        }

        string payload = JsonSerializer.Serialize(new { template = validatedTemplate });
        return await _gatewayClient.PostJsonAsync("documents/new", payload);
    }

    /// <summary>Opens an existing local .dwg drawing file.</summary>
    [McpServerTool(Name = "open_drawing")]
    [Description("Open an existing local .dwg drawing file in AutoCAD without bypassing Edit Guard.")]
    public async Task<string> OpenDrawing(string filePath, bool readOnly = false)
    {
        if (!SecurityPolicy.ValidateLocalFilePath(filePath, out string norm, out string? err, ".dwg"))
        {
            return CadFormat.Error(err ?? "Invalid drawing path.");
        }

        string payload = JsonSerializer.Serialize(new { file_path = norm, read_only = readOnly });
        return await _gatewayClient.PostJsonAsync("documents/open", payload);
    }

    /// <summary>Activates an open drawing document by full path, unique filename, or session ID.</summary>
    [McpServerTool(Name = "activate_document")]
    [ToolExposure(Core = true)]
    [Description("Switch AutoCAD's active MDI drawing by full path, unique filename, or session ID.")]
    public async Task<string> ActivateDocument(string document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return CadFormat.Error("'document' cannot be empty.");
        }

        string payload = JsonSerializer.Serialize(new { document = document.Trim() });
        return await _gatewayClient.PostJsonAsync("documents/activate", payload);
    }

    /// <summary>Closes an open drawing with explicit save or discard confirmation.</summary>
    [McpServerTool(Name = "close_document")]
    [Description("Close an open drawing with explicit save=true, saveAs=.dwg path, or discardChanges=true confirmation.")]
    public async Task<string> CloseDocument(
        string? document = null,
        bool save = false,
        bool discardChanges = false,
        string? saveAs = null)
    {
        string? validatedSaveAs = null;
        if (!string.IsNullOrWhiteSpace(saveAs))
        {
            if (!SecurityPolicy.ValidateLocalFilePath(saveAs, out string norm, out string? err, ".dwg"))
            {
                return CadFormat.Error(err ?? "Invalid 'saveAs' path.");
            }

            validatedSaveAs = norm;
        }

        string payload = JsonSerializer.Serialize(new
        {
            document,
            save,
            discard_changes = discardChanges,
            save_as = validatedSaveAs,
        });
        return await _gatewayClient.PostJsonAsync("documents/close", payload);
    }

    /// <summary>Reads an AutoCAD system variable while blocking security-sensitive variables.</summary>
    [McpServerTool(Name = "get_system_variable")]
    [Description("Read an AutoCAD system variable safely (blocks security-sensitive variables such as TRUSTEDPATHS and SECURELOAD).")]
    public Task<string> GetSystemVariable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(CadFormat.Error("'name' cannot be empty."));
        }

        if (SecurityPolicy.IsBlockedSystemVariable(name))
        {
            return Task.FromResult(CadFormat.Error($"Access to security-sensitive system variable '{name.Trim()}' is blocked."));
        }

        return _gatewayClient.GetContextAsync($"sysvar?name={Uri.EscapeDataString(name.Trim())}");
    }

    /// <summary>
    /// Sets a non-security system variable on the currently active MDI drawing under Edit Guard protection.
    /// </summary>
    [McpServerTool(Name = "set_system_variable")]
    [Description("Set a non-security AutoCAD system variable on the active drawing (blocks TRUSTEDPATHS, SECURELOAD, SAFEMODE, etc.).")]
    public Task<string> SetSystemVariable(
        string name,
        [CompoundParam(AcceptScalarAsString = true)] string value,
        string? document = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(CadFormat.Error("'name' cannot be empty."));
        }

        if (SecurityPolicy.IsBlockedSystemVariable(name))
        {
            return Task.FromResult(CadFormat.Error($"Modifying security-sensitive system variable '{name.Trim()}' is blocked."));
        }

        string payload = JsonSerializer.Serialize(new { name = name.Trim(), value = value ?? string.Empty, document });
        return _gatewayClient.PostJsonAsync("sysvar", payload);
    }
}
