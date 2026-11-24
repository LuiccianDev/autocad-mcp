using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Core.Models;
using AutoCAD.Gateway.Services;

namespace AutoCAD.Gateway.Server;

/// <summary>
/// Routes admitted HTTP requests to the appropriate command dispatcher, context reader, LISP bridge,
/// native ObjectARX services, or Civil 3D service.
/// </summary>
public sealed class RequestRouter
{
    private readonly record struct RouteContext(
        string Path,
        IReadOnlyDictionary<string, string> Query,
        string Body,
        int BoundPort);

    private static readonly Dictionary<string, Dictionary<string, Func<RequestRouter, RouteContext, Task<(int StatusCode, string ResponseBody)>>>> Routes =
        BuildRoutes();

    /// <summary>Returns all registered <c>(Method, Route)</c> pairs exposed by the router.</summary>
    public static IReadOnlyCollection<(string Method, string Route)> RegisteredRoutes { get; } = BuildRegisteredRoutesList();

    private static IReadOnlyCollection<(string Method, string Route)> BuildRegisteredRoutesList()
    {
        var list = new List<(string, string)>();
        foreach (var kvp in Routes)
        {
            foreach (string method in kvp.Value.Keys)
            {
                list.Add((method, kvp.Key));
            }
        }

        return list;
    }

    private static Dictionary<string, Dictionary<string, Func<RequestRouter, RouteContext, Task<(int, string)>>>> BuildRoutes()
    {
        var map = new Dictionary<string, Dictionary<string, Func<RequestRouter, RouteContext, Task<(int, string)>>>>(
            StringComparer.OrdinalIgnoreCase);

        void Add(string method, string path, Func<RequestRouter, RouteContext, Task<(int, string)>> handler)
        {
            if (!map.TryGetValue(path, out var byMethod))
            {
                byMethod = new Dictionary<string, Func<RequestRouter, RouteContext, Task<(int, string)>>>(
                    StringComparer.OrdinalIgnoreCase);
                map[path] = byMethod;
            }

            byMethod[method] = handler;
        }

        RegisterSystemAndContextRoutes(Add);
        RegisterEntityAndDraftingRoutes(Add);
        RegisterDocumentAndLayerRoutes(Add);
        RegisterCivilRoutes(Add);
        return map;
    }

    private static void RegisterSystemAndContextRoutes(
        Action<string, string, Func<RequestRouter, RouteContext, Task<(int, string)>>> add)
    {
        add("GET", GatewayRoutes.Ping, static (_, ctx) => Task.FromResult((200, JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            alive = true,
            port = ctx.BoundPort,
            pid = Environment.ProcessId,
            edit_guard = DocumentSessionManager.EditGuardEnabled,
            restricted_mode = DocumentSessionManager.RestrictedExecutionEnabled,
            document = DocumentSessionManager.LastObservedDocumentName,
        }, GatewayProtocol.WireJsonOptions))));

        add("GET", GatewayRoutes.Tools, static (_, _) => Task.FromResult((200, JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            version = GetGatewayVersion(),
            count = GatewayToolCatalog.All.Count,
            tools = GatewayToolCatalog.All,
        }, GatewayProtocol.WireJsonOptions))));

        add("GET", GatewayRoutes.Guide, static (_, _) => Task.FromResult((200, JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            version = GetGatewayVersion(),
            guide = GatewayToolCatalog.BuildAgentGuideText(),
        }, GatewayProtocol.WireJsonOptions))));

        add("GET", GatewayRoutes.ContextSelection, static async (r, _) => (200, await r._contextReader.ReadSelectionCountAsync()));
        add("GET", GatewayRoutes.ContextEntities, static async (r, _) => (200, await r._contextReader.ReadEntityCountsAsync()));
        add("GET", GatewayRoutes.ContextInfo, static async (r, _) => (200, await r._contextReader.ReadDrawingInfoAsync()));
        add("GET", GatewayRoutes.ContextLayers, static async (r, _) => (200, await r._contextReader.ReadLayersAsync()));
        add("GET", GatewayRoutes.ContextExtents, static async (r, _) => (200, await r._contextReader.ReadExtentsAsync()));
        add("GET", GatewayRoutes.ContextTexts, static async (r, _) => (200, await r._contextReader.ReadTextsAsync()));
        add("GET", GatewayRoutes.ContextBlocks, static async (r, _) => (200, await r._contextReader.ReadBlocksAsync()));
        add("GET", GatewayRoutes.ContextScreenshot, static async (r, ctx) =>
        {
            bool inline = ctx.Query.TryGetValue("inline", out string? rawInline) &&
                string.Equals(rawInline.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            return (200, await r._contextReader.CaptureScreenshotAsync(inline));
        });

        add("POST", GatewayRoutes.Execute, static (r, ctx) => r.HandleExecuteAsync(ctx.Body));
        add("POST", GatewayRoutes.LispEval, static (_, ctx) => HandleLispEvalAsync(ctx.Body));
    }

    private static void RegisterEntityAndDraftingRoutes(
        Action<string, string, Func<RequestRouter, RouteContext, Task<(int, string)>>> add)
    {
        add("POST", GatewayRoutes.EntitiesQuery, static async (_, ctx) => (200, await EntityQueryService.QueryAsync(ctx.Body)));
        add("POST", GatewayRoutes.EntitiesGet, static async (_, ctx) => (200, await EntityQueryService.GetAsync(ctx.Body)));
        add("POST", GatewayRoutes.EntitiesCreate, static async (_, ctx) => (200, await EntityService.CreateAsync(ctx.Body)));
        add("POST", GatewayRoutes.EntitiesModify, static async (_, ctx) => (200, await EntityService.ModifyAsync(ctx.Body)));
        add("POST", GatewayRoutes.EntitiesTransform, static async (_, ctx) => (200, await EntityService.TransformAsync(ctx.Body)));
        add("POST", GatewayRoutes.EntitiesDelete, static async (_, ctx) => (200, await EntityService.DeleteAsync(ctx.Body)));
        add("POST", GatewayRoutes.EntitiesExplode, static async (_, ctx) => (200, await BlockService.ExplodeEntitiesAsync(ctx.Body)));
        add("POST", GatewayRoutes.Batch, static async (_, ctx) => (200, await EntityService.BatchAsync(ctx.Body)));

        add("POST", GatewayRoutes.DraftingDimension, static async (_, ctx) => (200, await DraftingService.CreateDimensionAsync(ctx.Body)));
        add("POST", GatewayRoutes.DraftingMLeader, static async (_, ctx) => (200, await DraftingService.CreateMLeaderAsync(ctx.Body)));
        add("POST", GatewayRoutes.DraftingHatch, static async (_, ctx) => (200, await DraftingService.CreateHatchAsync(ctx.Body)));
        add("POST", GatewayRoutes.DraftingTable, static async (_, ctx) => (200, await DraftingService.CreateTableAsync(ctx.Body)));

        add("POST", GatewayRoutes.BlocksDefine, static async (_, ctx) => (200, await BlockService.DefineBlockAsync(ctx.Body)));
        add("GET", GatewayRoutes.BlocksDefinition, static async (_, ctx) =>
        {
            ctx.Query.TryGetValue("name", out string? name);
            ctx.Query.TryGetValue("document", out string? document);
            return (200, await BlockService.GetBlockDefinitionAsync(name, document));
        });
        add("GET", GatewayRoutes.BlocksAttributes, static async (_, ctx) =>
        {
            ctx.Query.TryGetValue("handle", out string? handle);
            ctx.Query.TryGetValue("document", out string? document);
            return (200, await BlockService.GetBlockAttributesAsync(handle, document));
        });
        add("POST", GatewayRoutes.BlocksAttributes, static async (_, ctx) => (200, await BlockService.SetBlockAttributesAsync(ctx.Body)));
        add("GET", GatewayRoutes.BlocksXrefs, static async (_, ctx) =>
        {
            ctx.Query.TryGetValue("document", out string? document);
            return (200, await BlockService.ListXrefsAsync(document));
        });

        add("POST", GatewayRoutes.PlottingViewport, static async (_, ctx) => (200, await PlottingService.CreateViewportAsync(ctx.Body)));
        add("POST", GatewayRoutes.PlottingPdf, static async (_, ctx) => (200, await PlottingService.PlotPdfAsync(ctx.Body)));
    }

    private static void RegisterDocumentAndLayerRoutes(
        Action<string, string, Func<RequestRouter, RouteContext, Task<(int, string)>>> add)
    {
        add("POST", GatewayRoutes.LayersCreate, static async (_, ctx) => (200, await LayerService.CreateOrUpdateAsync(ctx.Body)));
        add("POST", GatewayRoutes.LayersState, static async (_, ctx) => (200, await LayerService.SetStateAsync(ctx.Body)));
        add("POST", GatewayRoutes.LayersDelete, static async (_, ctx) => (200, await LayerService.DeleteAsync(ctx.Body)));

        add("GET", GatewayRoutes.Documents, static async (_, _) => (200, await DocumentControlService.ListDocumentsAsync()));
        add("POST", GatewayRoutes.SessionCancel, static async (_, _) => (200, await DocumentControlService.CancelCommandAsync()));
        add("POST", GatewayRoutes.SessionAllowEdit, static async (_, ctx) => (200, await DocumentControlService.AllowEditDocumentAsync(ctx.Body)));
        add("POST", GatewayRoutes.DocumentsNew, static async (_, ctx) => (200, await DocumentControlService.NewDrawingAsync(ctx.Body)));
        add("POST", GatewayRoutes.DocumentsOpen, static async (_, ctx) => (200, await DocumentControlService.OpenDrawingAsync(ctx.Body)));
        add("POST", GatewayRoutes.DocumentsActivate, static async (_, ctx) => (200, await DocumentControlService.ActivateDocumentAsync(ctx.Body)));
        add("POST", GatewayRoutes.DocumentsClose, static async (_, ctx) => (200, await DocumentControlService.CloseDocumentAsync(ctx.Body)));
        add("GET", GatewayRoutes.SysVar, static async (_, ctx) =>
        {
            ctx.Query.TryGetValue("name", out string? name);
            return (200, await DocumentControlService.GetSystemVariableAsync(name));
        });
        add("POST", GatewayRoutes.SysVar, static async (_, ctx) => (200, await DocumentControlService.SetSystemVariableAsync(ctx.Body)));
    }

    private static void RegisterCivilRoutes(
        Action<string, string, Func<RequestRouter, RouteContext, Task<(int, string)>>> add)
    {
        void RegisterCivil(string method, string path, Func<ICivil3DService, RouteContext, Task<string>> civilHandler) =>
            add(method, path, async (_, ctx) => (200, await civilHandler(Civil3DServiceFactory.Resolve(), ctx)));

        RegisterCivil("GET", GatewayRoutes.CivilStatus, static (civil, _) => civil.GetStatusAsync());
        RegisterCivil("GET", GatewayRoutes.CivilSurfaces, static (civil, _) => civil.ListSurfacesAsync());
        RegisterCivil("GET", GatewayRoutes.CivilAlignments, static (civil, _) => civil.ListAlignmentsAsync());
        RegisterCivil("GET", GatewayRoutes.CivilSurfaceElevation, static (civil, ctx) =>
        {
            ctx.Query.TryGetValue("surface", out string? surface);
            if (!TryGetFinite(ctx.Query, "x", out double x) || !TryGetFinite(ctx.Query, "y", out double y))
            {
                throw new AcadGatewayException("bad_query", "Required finite numeric query parameters: x, y.");
            }

            return civil.GetElevationAsync(surface, x, y);
        });
        RegisterCivil("GET", GatewayRoutes.CivilSurfaceStats, static (civil, ctx) =>
        {
            ctx.Query.TryGetValue("surface", out string? surface);
            return civil.GetSurfaceStatsAsync(surface);
        });
        RegisterCivil("GET", GatewayRoutes.CivilStationOffset, static (civil, ctx) =>
        {
            ctx.Query.TryGetValue("alignment", out string? alignment);
            if (!TryGetFinite(ctx.Query, "x", out double x) || !TryGetFinite(ctx.Query, "y", out double y))
            {
                throw new AcadGatewayException("bad_query", "Required finite numeric query parameters: x, y.");
            }

            return civil.GetStationOffsetAsync(alignment, x, y);
        });
        RegisterCivil("GET", GatewayRoutes.CivilPointAtStation, static (civil, ctx) =>
        {
            ctx.Query.TryGetValue("alignment", out string? alignment);
            if (!TryGetFinite(ctx.Query, "station", out double station) || !TryGetFinite(ctx.Query, "offset", out double offset))
            {
                throw new AcadGatewayException("bad_query", "Required finite numeric query parameters: station, offset.");
            }

            return civil.GetPointAtStationAsync(alignment, station, offset);
        });
        RegisterCivil("GET", GatewayRoutes.CivilProfile, static (civil, ctx) =>
        {
            ctx.Query.TryGetValue("alignment", out string? alignment);
            ctx.Query.TryGetValue("surface", out string? surface);
            if (!TryGetFiniteOpt(ctx.Query, "interval", 20.0, out double interval) || interval <= 0)
            {
                throw new AcadGatewayException("bad_query", "Query parameter 'interval' must be a finite number > 0.");
            }

            return civil.GetProfileAsync(alignment, surface, interval);
        });
        RegisterCivil("GET", GatewayRoutes.CivilCrossSection, static (civil, ctx) =>
        {
            ctx.Query.TryGetValue("alignment", out string? alignment);
            ctx.Query.TryGetValue("surface", out string? surface);
            if (!TryGetFinite(ctx.Query, "station", out double station))
                throw new AcadGatewayException("bad_query", "Required finite numeric query parameter: station.");
            if (!TryGetFiniteOpt(ctx.Query, "left_width", 20.0, out double leftWidth))
                throw new AcadGatewayException("bad_query", "Query parameter 'left_width' must be a finite number.");
            if (!TryGetFiniteOpt(ctx.Query, "right_width", 20.0, out double rightWidth))
                throw new AcadGatewayException("bad_query", "Query parameter 'right_width' must be a finite number.");
            if (!TryGetFiniteOpt(ctx.Query, "step", 2.0, out double step))
                throw new AcadGatewayException("bad_query", "Query parameter 'step' must be a finite number.");

            return civil.GetCrossSectionAsync(alignment, surface, station, leftWidth, rightWidth, step);
        });
        RegisterCivil("POST", GatewayRoutes.CivilCreateAlignment, static (civil, ctx) =>
        {
            JsonElement bodyEl = DatabaseHelpers.ParseJsonObject(ctx.Body);
            var req = DeserializeOrThrow<CreateAlignmentRequest>(bodyEl);
            return civil.CreateAlignmentAsync(req);
        });
        RegisterCivil("POST", GatewayRoutes.CivilCreateSurface, static (civil, ctx) =>
        {
            JsonElement bodyEl = DatabaseHelpers.ParseJsonObject(ctx.Body);
            var req = DeserializeOrThrow<CreateSurfaceRequest>(bodyEl);
            return civil.CreateSurfaceAsync(req);
        });
    }

    private readonly IAutoCADCommandDispatcher _dispatcher;
    private readonly IAutoCADContextReader _contextReader;

    /// <summary>Initializes a new instance of the <see cref="RequestRouter"/> class.</summary>
    /// <param name="dispatcher">AutoCAD command dispatcher for legacy CLI automation.</param>
    /// <param name="contextReader">AutoCAD main-thread database context reader.</param>
    public RequestRouter(IAutoCADCommandDispatcher dispatcher, IAutoCADContextReader contextReader)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
    }

    /// <summary>Processes an admitted HTTP request and returns (statusCode, jsonBody).</summary>
    /// <param name="method">Validated HTTP request method (GET or POST).</param>
    /// <param name="path">Normalized URL path without query string.</param>
    /// <param name="query">Decoded query parameters.</param>
    /// <param name="body">UTF-8 decoded JSON request body.</param>
    /// <param name="boundPort">Loopback TCP port hosting this Gateway instance.</param>
    /// <returns>Tuple of HTTP status code and UTF-8 JSON response body.</returns>
    public async Task<(int StatusCode, string ResponseBody)> RouteRequestAsync(
        string method,
        string path,
        IReadOnlyDictionary<string, string> query,
        string body,
        int boundPort = GatewayProtocol.DefaultPort)
    {
        try
        {
            if (!Routes.TryGetValue(path, out var methods))
            {
                return ErrorResult(404, "not_found", "Route not found.");
            }

            if (!methods.TryGetValue(method, out var handler))
            {
                return ErrorResult(405, "method_not_allowed", "HTTP method not allowed.");
            }

            return await handler(this, new RouteContext(path, query, body, boundPort));
        }
        catch (AcadGatewayException ex)
        {
            return (ex.ToHttpStatusCode(), JsonSerializer.Serialize(
                ToolEnvelope.Failure(ex.Code, ex.Message, ex.Detail), GatewayProtocol.WireJsonOptions));
        }
        catch (JsonException)
        {
            return ErrorResult(400, "bad_json", "Invalid or malformed JSON field types.");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            return ErrorResult(409, "autocad_error", $"{ex.Message} ({ex.ErrorStatus})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("AutoCAD Gateway route error: {0}", ex);
            return ErrorResult(500, "internal_error", "An internal gateway error occurred while processing the request.");
        }
    }

    private async Task<(int StatusCode, string ResponseBody)> HandleExecuteAsync(string body)
    {
        if (DocumentSessionManager.RestrictedExecutionEnabled)
        {
            return ErrorResult(
                403,
                "forbidden",
                "Raw CLI execution (/execute) is disabled while AUTOCAD_GATEWAY_RESTRICTED_MODE=1 is active.");
        }

        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        // Preserve trailing newlines/spaces verbatim so AutoCAD SendStringToExecute receives Enter strokes.
        string? command = DatabaseHelpers.GetStringOpt(root, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return ErrorResult(400, "bad_argument", "'command' is required and cannot be empty.");
        }

        if (command.Contains('\0'))
        {
            return ErrorResult(400, "bad_argument", "'command' cannot contain NUL characters.");
        }

        await Acad.RunOnDocumentAsync((doc, session) =>
        {
            Acad.ValidateWritable(doc, session, requireIdle: false);
            _dispatcher.EnqueueCommand(doc, command);
            return true;
        }).ConfigureAwait(false);

        return (200, JsonSerializer.Serialize(ToolEnvelope.Success(
            message: "Command successfully enqueued."), GatewayProtocol.WireJsonOptions));
    }

    private static async Task<(int StatusCode, string ResponseBody)> HandleLispEvalAsync(string body)
    {
        if (DocumentSessionManager.RestrictedExecutionEnabled)
        {
            return ErrorResult(
                403,
                "forbidden",
                "Unrestricted AutoLISP evaluation (/lisp/eval) is disabled while AUTOCAD_GATEWAY_RESTRICTED_MODE=1 is active.");
        }

        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string? code = DatabaseHelpers.GetStringOpt(root, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = DatabaseHelpers.GetStringOpt(root, "expression");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return ErrorResult(400, "bad_argument", "'code' (or 'expression') cannot be empty.");
        }

        long? timeoutVal = DatabaseHelpers.GetLongOpt(root, "timeout_ms")
            ?? DatabaseHelpers.GetLongOpt(root, "timeoutMs");
        int timeoutMs = timeoutVal.HasValue
            ? GatewayProtocol.ClampLispTimeoutMs(timeoutVal.Value)
            : GatewayProtocol.DefaultLispTimeoutMs;

        var (value, document, cmdLine) = await LispBridge.EvalAsync(code, timeoutMs);
        return (200, SerializeLispResponse(
            value,
            document,
            cmdLine?.State,
            cmdLine?.PendingCommand,
            cmdLine?.CmdActive,
            cmdLine?.Hint));
    }

    private static string SerializeLispResponse(
        object? value,
        string document,
        string? commandState,
        string? pendingCommand,
        int? cmdActive,
        string? hint)
    {
        var envelope = new System.Text.Json.Nodes.JsonObject
        {
            ["status"] = "success",
            ["ok"] = true,
            ["value"] = value is null ? null : JsonSerializer.SerializeToNode(value, GatewayProtocol.WireJsonOptions),
            ["document"] = document,
        };
        if (commandState is not null) envelope["command_state"] = commandState;
        if (pendingCommand is not null) envelope["pending_command"] = pendingCommand;
        if (cmdActive.HasValue) envelope["cmd_active"] = cmdActive.Value;
        if (hint is not null) envelope["hint"] = hint;
        return envelope.ToJsonString();
    }

    private static T DeserializeOrThrow<T>(JsonElement bodyEl) where T : class
    {
        try
        {
            return bodyEl.Deserialize<T>()
                ?? throw new AcadGatewayException("bad_request", "Empty or invalid JSON body.");
        }
        catch (JsonException)
        {
            throw new AcadGatewayException("bad_json", "Invalid field types in JSON request body.");
        }
    }

    private static string GetGatewayVersion()
    {
        Assembly asm = typeof(RequestRouter).Assembly;
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plusIdx = info.IndexOf('+');
            return plusIdx > 0 ? info[..plusIdx] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "2.5.0";
    }

    private static bool TryGetFiniteOpt(
        IReadOnlyDictionary<string, string> q, string key, double defaultValue, out double value)
    {
        if (!q.TryGetValue(key, out string? raw))
        {
            value = defaultValue;
            return true;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value);
    }

    private static bool TryGetFinite(
        IReadOnlyDictionary<string, string> q, string key, out double value)
    {
        value = 0.0;
        return q.TryGetValue(key, out string? raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value);
    }

    private static (int StatusCode, string ResponseBody) ErrorResult(int statusCode, string code, string message) =>
        (statusCode, JsonSerializer.Serialize(ToolEnvelope.Failure(code, message), GatewayProtocol.WireJsonOptions));
}
