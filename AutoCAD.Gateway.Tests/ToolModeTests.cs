using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using AutoCAD.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using static AutoCAD.Gateway.Tests.TestAssert;

namespace AutoCAD.Gateway.Tests;

internal static class ToolModeTests
{
    private static readonly string[] ExpectedCoreTools =
    [
        "get_drawing_info", "get_layers", "get_drawing_extents", "capture_screenshot",
        "query_entities", "get_entity", "modify_entity", "delete_entities", "execute_native_batch",
        "draw_batch_geometry", "create_layer", "set_layer_state",
        "list_documents", "allow_edit_document", "activate_document",
        "measure_distance", "purge_and_audit_drawing", "create_viewport", "export_pdf",
        "check_civil3d_status", "list_civil3d_surfaces", "list_civil3d_alignments", "get_surface_elevation", "sample_longitudinal_profile",
        "ping_gateway", "execute_command", "evaluate_lisp", "save_drawing",
    ];

    private static readonly string[] ExpectedCompoundTools =
    [
        "autocad_context", "autocad_entities", "autocad_drafting", "autocad_drawing",
        "autocad_layers", "autocad_documents", "autocad_measurement", "autocad_plotting",
        "autocad_civil3d", "autocad_execution",
    ];

    public static async Task RunAllAsync(
        Action<string, Action> run,
        Func<string, Func<Task>, Task> runAsync)
    {
        run("ToolModeParser parses empty, case-insensitive modes, env vars, and rejects invalid values", TestToolModeParser);
        run("AutoCADToolCatalog discovers 10 domains, 89 total, 75 canonical, 14 aliases, and exact 28 core tools", TestCatalogDiscoveryAndSchemas);
        run("CompoundDispatcher.BuildHelp returns overview, targeted schemas, alias resolution, and cross-domain hints", TestCompoundHelp);
        await runAsync("CompoundDispatcher.DispatchAsync validates inputs and dispatches sync, async, alias, and native JSON payloads", TestCompoundDispatchAsync);
        run("McpToolRegistrationExtensions registers 89, 28, and 10 tools by mode with valid compound schemas", TestMcpRegistrationModes);
        await runAsync("Thermos regressions: structured anyOf schemas, cross-domain aliases, async errors, DI scoping, and batch validation", TestThermosRegressionsAsync);
        await runAsync("Out-of-process STDIO MCP server respects AUTOCAD_MCP_TOOL_MODE and rejects invalid mode on stderr", TestStdioEndToEndAsync);
    }

    private static void TestToolModeParser()
    {
        AssertEqual(ToolMode.Granular, ToolModeParser.Parse(null), "null defaults to Granular");
        AssertEqual(ToolMode.Granular, ToolModeParser.Parse(string.Empty), "empty defaults to Granular");
        AssertEqual(ToolMode.Granular, ToolModeParser.Parse("   "), "whitespace defaults to Granular");
        AssertEqual(ToolMode.Granular, ToolModeParser.Parse("GRANULAR"), "case-insensitive granular");
        AssertEqual(ToolMode.Compound, ToolModeParser.Parse("  Compound  "), "trimmed case-insensitive compound");
        AssertEqual(ToolMode.Core, ToolModeParser.Parse("cOrE"), "case-insensitive core");
        AssertEqual("granular", ToolMode.Granular.ToWireName(), "granular wire name");
        AssertEqual("compound", ToolMode.Compound.ToWireName(), "compound wire name");
        AssertEqual("core", ToolMode.Core.ToWireName(), "core wire name");

        var invalidEx = AssertThrows<ArgumentException>(
            () => ToolModeParser.Parse("minimal"),
            "invalid mode must throw ArgumentException");
        AssertTrue(invalidEx.Message.Contains(ToolModeParser.EnvVarName, StringComparison.Ordinal), "error message cites env var name");

        string? original = Environment.GetEnvironmentVariable(ToolModeParser.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(ToolModeParser.EnvVarName, "compound");
            AssertEqual(ToolMode.Compound, ToolModeParser.FromEnvironment(), "FromEnvironment reads compound");

            Environment.SetEnvironmentVariable(ToolModeParser.EnvVarName, null);
            AssertEqual(ToolMode.Granular, ToolModeParser.FromEnvironment(), "unset env var defaults to Granular");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ToolModeParser.EnvVarName, original);
        }
    }

    private static void TestCatalogDiscoveryAndSchemas()
    {
        var catalog = new AutoCADToolCatalog();

        AssertEqual(10, catalog.Domains.Count, "10 tool domains");
        AssertEqual(89, catalog.AllActions.Count, "89 total tools");
        AssertEqual(75, catalog.CanonicalActions.Count, "75 canonical actions");
        AssertEqual(28, catalog.CoreActions.Count, "28 core tools");
        AssertEqual(14, catalog.AllActions.Count(a => a.IsAlias), "14 legacy aliases");
        AssertTrue(catalog.CoreActions.All(a => !a.IsAlias), "core mode excludes all aliases");

        var actualCore = catalog.CoreActions.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expectedCore = ExpectedCoreTools.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        AssertTrue(actualCore.SequenceEqual(expectedCore, StringComparer.Ordinal), "exact 28 core tool names match");

        var actualCompound = catalog.Domains.Select(d => d.CompoundToolName).ToArray();
        AssertTrue(actualCompound.SequenceEqual(ExpectedCompoundTools, StringComparer.Ordinal), "exact 10 compound domain tool names match");

        AssertTrue(catalog.TryGetAction("draw_batch_geometry", out var drawBatch), "draw_batch_geometry exists");
        AssertEqual("elements", drawBatch!.Parameters[0].CanonicalName, "elementsJson maps to canonical 'elements'");
        AssertTrue(drawBatch.ParametersByAcceptedName.ContainsKey("elements"), "accepts 'elements'");
        AssertTrue(drawBatch.ParametersByAcceptedName.ContainsKey("elements_json"), "accepts 'elements_json'");
        AssertTrue(drawBatch.ParametersByAcceptedName.ContainsKey("elementsJson"), "accepts 'elementsJson'");

        AssertTrue(catalog.TryGetAction("execute_native_batch", out var nativeBatch), "execute_native_batch exists");
        AssertEqual("operations", nativeBatch!.Parameters[0].CanonicalName, "operationsJson maps to canonical 'operations'");

        AssertTrue(catalog.TryGetAction("execute_batch_commands", out var batchCmds), "execute_batch_commands exists");
        AssertEqual("commands", batchCmds!.Parameters[0].CanonicalName, "commandsJson maps to canonical 'commands'");

        AssertTrue(catalog.TryGetAction("create_table", out var createTable), "create_table exists");
        AssertTrue(createTable!.ParametersByAcceptedName.ContainsKey("cells_json"), "create_table preserves 'cells_json'");
        AssertTrue(createTable.ParametersByAcceptedName.ContainsKey("headers_json"), "create_table preserves 'headers_json'");

        AssertTrue(catalog.TryGetAction("define_block", out var defineBlock), "define_block exists");
        AssertTrue(defineBlock!.ParametersByAcceptedName.ContainsKey("operations_json"), "define_block preserves 'operations_json'");
        AssertTrue(defineBlock.ParametersByAcceptedName.ContainsKey("attributes_json"), "define_block preserves 'attributes_json'");

        AssertTrue(catalog.TryGetAction("set_block_attributes", out var setBlockAttrs), "set_block_attributes exists");
        AssertTrue(setBlockAttrs!.ParametersByAcceptedName.ContainsKey("attributes_json"), "set_block_attributes preserves 'attributes_json'");
    }

    private static void TestCompoundHelp()
    {
        var catalog = new AutoCADToolCatalog();
        var dispatcher = new CompoundDispatcher(catalog, _ => throw new InvalidOperationException("Help must not instantiate tools."));

        using (var doc = JsonDocument.Parse(dispatcher.BuildHelp("context")))
        {
            var root = doc.RootElement;
            AssertEqual("success", root.GetProperty("status").GetString(), "context help status");
            AssertEqual("context", root.GetProperty("domain").GetString(), "context domain");
            AssertEqual("autocad_context", root.GetProperty("tool").GetString(), "context compound tool name");
            AssertEqual(10, root.GetProperty("actions").EnumerateObject().Count(), "context canonical action count");
            AssertEqual(6, root.GetProperty("aliases").EnumerateObject().Count(), "context alias count");
            AssertEqual("get_layers", root.GetProperty("aliases").GetProperty("read_layers").GetString(), "read_layers alias maps to get_layers");
        }

        using (var doc = JsonDocument.Parse(dispatcher.BuildHelp("drawing", "draw_batch_geometry")))
        {
            var root = doc.RootElement;
            AssertEqual("success", root.GetProperty("status").GetString(), "targeted drawing help status");
            AssertEqual("draw_batch_geometry", root.GetProperty("action").GetString(), "targeted action name");
            AssertTrue(!root.TryGetProperty("resolved_from", out _), "canonical action omits resolved_from");
            var schema = root.GetProperty("input_schema");
            AssertTrue(schema.GetProperty("properties").TryGetProperty("elements", out _), "schema uses canonical 'elements'");
            AssertTrue(schema.GetProperty("properties").TryGetProperty("default_layer", out _), "schema uses canonical 'default_layer'");
            AssertEqual("elements", schema.GetProperty("required")[0].GetString(), "elements is required");
        }

        using (var doc = JsonDocument.Parse(dispatcher.BuildHelp("context", "read_layers")))
        {
            var root = doc.RootElement;
            AssertEqual("success", root.GetProperty("status").GetString(), "alias help status");
            AssertEqual("get_layers", root.GetProperty("action").GetString(), "alias resolves to canonical action");
            AssertEqual("read_layers", root.GetProperty("resolved_from").GetString(), "alias sets resolved_from");
        }

        using (var doc = JsonDocument.Parse(dispatcher.BuildHelp("layers", "draw_line")))
        {
            var root = doc.RootElement;
            AssertEqual("error", root.GetProperty("status").GetString(), "cross-domain help returns error");
            string msg = root.GetProperty("message").GetString() ?? string.Empty;
            AssertTrue(msg.Contains("drawing", StringComparison.Ordinal) && msg.Contains("autocad_drawing", StringComparison.Ordinal), "cross-domain hint names target domain and compound tool");
        }
    }

    private static async Task TestCompoundDispatchAsync()
    {
        var mockGateway = new RecordingGatewayClient();
        var catalog = new AutoCADToolCatalog();
        var dispatcher = new CompoundDispatcher(
            catalog,
            type => Activator.CreateInstance(type, mockGateway)
                ?? throw new InvalidOperationException($"Cannot instantiate {type.Name}."));

        JsonElement ParseElement(string json)
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.Clone();
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "", null)))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "empty action rejected");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("\"{bad json\""))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "malformed params JSON string rejected");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("[1, 2, 3]"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "non-object JSON array rejected");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{\"name\":\"WALLS\",\"bogus\":123}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "unexpected key rejected");
            AssertTrue((doc.RootElement.GetProperty("message").GetString() ?? "").Contains("bogus", StringComparison.Ordinal), "error cites unexpected key");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "missing required param rejected");
            AssertTrue((doc.RootElement.GetProperty("message").GetString() ?? "").Contains("name", StringComparison.Ordinal), "error cites missing required param");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{\"name\":\"WALLS\",\"color_aci\":1,\"colorAci\":2}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "duplicate snake_case and camelCase binding rejected");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{\"name\":\"WALLS\",\"color_aci\":\"red\"}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "wrong integer type rejected");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{\"name\":[\"WALLS\"]}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "plain string parameter rejects JSON array");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("layers", "draw_line", ParseElement("{\"x1\":0,\"y1\":0,\"x2\":1,\"y2\":1}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "cross-domain dispatch returns error");
            string msg = doc.RootElement.GetProperty("message").GetString() ?? "";
            AssertTrue(msg.Contains("autocad_drawing", StringComparison.Ordinal), "cross-domain dispatch names target tool");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("context", "help", ParseElement("{\"action\":\"read_layers\"}"))))
        {
            AssertEqual("success", doc.RootElement.GetProperty("status").GetString(), "help via dispatch succeeds");
            AssertEqual("get_layers", doc.RootElement.GetProperty("action").GetString(), "help resolves alias");
            AssertEqual("read_layers", doc.RootElement.GetProperty("resolved_from").GetString(), "help records resolved_from");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("context", "help", ParseElement("{\"action\":\"   \"}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "blank params.action rejected in help");
        }

        string distJson = await dispatcher.DispatchAsync("measurement", "measure_distance", ParseElement("{\"x1\":0,\"y1\":0,\"x2\":3,\"y2\":4}"));
        using (var doc = JsonDocument.Parse(distJson))
        {
            AssertEqual("success", doc.RootElement.GetProperty("status").GetString(), "sync measure_distance succeeds");
            AssertEqual(5.0, doc.RootElement.GetProperty("distance_2d").GetDouble(), "3-4-5 triangle distance");
        }

        AssertEqual(0, mockGateway.Calls.Count, "measure_distance made zero HTTP calls");

        mockGateway.Calls.Clear();
        await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{\"name\":\"WALLS\",\"color_aci\":3}"));
        AssertEqual(1, mockGateway.Calls.Count, "create_layer posted once");
        AssertEqual("POST:layers/create", mockGateway.Calls[0].Route, "create_layer route");
        AssertTrue(mockGateway.Calls[0].Body.Contains("\"color_index\":3", StringComparison.Ordinal), "snake_case color_aci bound");

        mockGateway.Calls.Clear();
        await dispatcher.DispatchAsync("layers", "create_layer", ParseElement("{\"name\":\"DOORS\",\"colorAci\":4}"));
        AssertTrue(mockGateway.Calls[0].Body.Contains("\"color_index\":4", StringComparison.Ordinal), "camelCase colorAci bound");

        mockGateway.Calls.Clear();
        string batchResult = await dispatcher.DispatchAsync(
            "drawing",
            "draw_batch_geometry",
            ParseElement("{\"elements\":[{\"type\":\"line\",\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0}],\"default_layer\":\"WALLS\"}"));
        AssertTrue(batchResult.Contains("\"ok\":true", StringComparison.Ordinal), "draw_batch_geometry with native JSON array succeeds");
        AssertEqual("POST:batch", mockGateway.Calls[0].Route, "draw_batch_geometry posts to batch");

        mockGateway.Calls.Clear();
        string nativeBatchResult = await dispatcher.DispatchAsync(
            "entities",
            "execute_native_batch",
            ParseElement("{\"operations\":[{\"op\":\"create\",\"type\":\"circle\",\"center\":[0,0,0],\"radius\":2.5}]}"));
        AssertTrue(nativeBatchResult.Contains("\"ok\":true", StringComparison.Ordinal), "execute_native_batch with native JSON array succeeds");
        AssertEqual("POST:batch", mockGateway.Calls[0].Route, "execute_native_batch posts to batch");

        mockGateway.Calls.Clear();
        string tableResult = await dispatcher.DispatchAsync(
            "drafting",
            "create_table",
            ParseElement("{\"x\":0,\"y\":0,\"rows\":1,\"cols\":2,\"cells_json\":[[\"A\",\"B\"]],\"headers_json\":[\"C1\",\"C2\"]}"));
        AssertTrue(tableResult.Contains("\"ok\":true", StringComparison.Ordinal), "create_table with native JSON arrays succeeds");
        AssertEqual("POST:drafting/table", mockGateway.Calls[0].Route, "create_table posts to drafting/table");

        mockGateway.Calls.Clear();
        string blockAttrResult = await dispatcher.DispatchAsync(
            "drafting",
            "set_block_attributes",
            ParseElement("{\"handle\":\"1A\",\"attributes_json\":{\"TAG\":\"VAL\"}}"));
        AssertTrue(blockAttrResult.Contains("\"ok\":true", StringComparison.Ordinal), "set_block_attributes with native JSON object succeeds");
        AssertEqual("POST:blocks/attributes", mockGateway.Calls[0].Route, "set_block_attributes posts to blocks/attributes");

        mockGateway.Calls.Clear();
        string defineBlockResult = await dispatcher.DispatchAsync(
            "drafting",
            "define_block",
            ParseElement("{\"name\":\"VALVE\",\"operations_json\":[{\"op\":\"create\",\"type\":\"circle\",\"center\":[0,0,0],\"radius\":0.5}],\"attributes_json\":[{\"tag\":\"ID\",\"prompt\":\"Tag ID\",\"default\":\"V1\"}]}"));
        AssertTrue(defineBlockResult.Contains("\"ok\":true", StringComparison.Ordinal), "define_block with native JSON arrays succeeds");
        AssertEqual("POST:blocks/define", mockGateway.Calls[0].Route, "define_block posts to blocks/define");

        mockGateway.Calls.Clear();
        string execBatchResult = await dispatcher.DispatchAsync(
            "execution",
            "execute_batch_commands",
            ParseElement("{\"commands\":[\"_.REGEN\",\"_.ZOOM\\nE\"]}"));
        AssertTrue(execBatchResult.Contains("\"ok\":true", StringComparison.Ordinal), "execute_batch_commands with native JSON array succeeds");
        AssertEqual("POST:execute", mockGateway.Calls[0].Route, "execute_batch_commands posts to execute");

        mockGateway.Calls.Clear();
        await dispatcher.DispatchAsync("context", "read_layers", null);
        AssertEqual("GET:context/layers", mockGateway.Calls[0].Route, "read_layers alias dispatches to context/layers");
    }

    private static void TestMcpRegistrationModes()
    {
        foreach (var (mode, expectedCount) in new[]
        {
            (ToolMode.Granular, 89),
            (ToolMode.Core, 28),
            (ToolMode.Compound, 10),
        })
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAutoCADGatewayClient>(new RecordingGatewayClient());
            services.AddMcpServer().WithConfiguredAutoCADTools(mode);

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
            var tools = options.ToolCollection?.ToList() ?? [];
            AssertEqual(expectedCount, tools.Count, $"DI registration count for {mode}");

            if (mode == ToolMode.Compound)
            {
                var names = tools.Select(t => t.ProtocolTool.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                var expected = ExpectedCompoundTools.OrderBy(n => n, StringComparer.Ordinal).ToArray();
                AssertTrue(names.SequenceEqual(expected, StringComparer.Ordinal), "compound mode registers exact 10 domain tools");
                var schema = tools[0].ProtocolTool.InputSchema;
                AssertTrue(schema.GetProperty("properties").TryGetProperty("action", out _), "compound schema exposes 'action'");
                AssertTrue(schema.GetProperty("properties").TryGetProperty("params", out _), "compound schema exposes 'params'");
                AssertTrue(!schema.GetProperty("properties").TryGetProperty("cancellationToken", out _), "compound schema hides CancellationToken");
                AssertTrue(!schema.TryGetProperty("required", out var reqEl) || reqEl.GetArrayLength() == 0, "compound schema keeps action and params optional");
            }
            else if (mode == ToolMode.Core)
            {
                var names = tools.Select(t => t.ProtocolTool.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                var expected = ExpectedCoreTools.OrderBy(n => n, StringComparer.Ordinal).ToArray();
                AssertTrue(names.SequenceEqual(expected, StringComparer.Ordinal), "core mode registers exact 28 tool names");
            }
        }
    }

    private static async Task TestStdioEndToEndAsync()
    {
        string serverDll = ResolveMcpServerDllPath();

        await VerifyStdioModeAsync(serverDll, "compound", 10, ExpectedCompoundTools, callDistanceCheck: true);
        await VerifyStdioModeAsync(serverDll, "core", 28, ExpectedCoreTools, callDistanceCheck: false);
        await VerifyStdioModeAsync(serverDll, "granular", 89, null, callDistanceCheck: false);

        var psi = new ProcessStartInfo("dotnet", $"exec \"{serverDll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment[ToolModeParser.EnvVarName] = "invalid_mode_xyz";

        using var invalidProc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start AutoCAD.McpServer process.");
        invalidProc.StandardInput.Close();
        string stdout = await invalidProc.StandardOutput.ReadToEndAsync();
        string stderr = await invalidProc.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await invalidProc.WaitForExitAsync(cts.Token);

        AssertEqual(1, invalidProc.ExitCode, "invalid AUTOCAD_MCP_TOOL_MODE returns exit code 1");
        AssertEqual(string.Empty, stdout, "invalid mode writes nothing to stdout");
        AssertTrue(stderr.Contains(ToolModeParser.EnvVarName, StringComparison.Ordinal), "invalid mode logs error to stderr");
    }

    private static async Task VerifyStdioModeAsync(
        string serverDll,
        string modeValue,
        int expectedToolCount,
        string[]? expectedNames,
        bool callDistanceCheck)
    {
        var psi = new ProcessStartInfo("dotnet", $"exec \"{serverDll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment[ToolModeParser.EnvVarName] = modeValue;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start AutoCAD.McpServer process.");
        try
        {
            var stderrTask = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            async Task SendAsync(string json)
            {
                await proc.StandardInput.WriteLineAsync(json.AsMemory(), cts.Token);
                await proc.StandardInput.FlushAsync(cts.Token);
            }

            async Task<JsonDocument> ReadJsonRpcAsync()
            {
                while (true)
                {
                    string? line = await proc.StandardOutput.ReadLineAsync(cts.Token);
                    if (line is null)
                    {
                        string err = await stderrTask;
                        throw new InvalidOperationException($"STDIO server closed unexpectedly in mode '{modeValue}'. Stderr: {err}");
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    return JsonDocument.Parse(line);
                }
            }

            await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"tests\",\"version\":\"1.0\"}}}");
            using (var initResp = await ReadJsonRpcAsync())
            {
                AssertEqual(1, initResp.RootElement.GetProperty("id").GetInt32(), $"initialize id in {modeValue}");
                AssertTrue(initResp.RootElement.TryGetProperty("result", out _), $"initialize succeeded in {modeValue}");
            }

            await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}");
            await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");

            using (var listResp = await ReadJsonRpcAsync())
            {
                AssertEqual(2, listResp.RootElement.GetProperty("id").GetInt32(), $"tools/list id in {modeValue}");
                var tools = listResp.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
                AssertEqual(expectedToolCount, tools.Count, $"STDIO tools/list count for {modeValue}");

                if (expectedNames is not null)
                {
                    var actualSet = tools.Select(t => t.GetProperty("name").GetString()!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    var expectedSet = expectedNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    AssertTrue(actualSet.SequenceEqual(expectedSet, StringComparer.Ordinal), $"STDIO tool names match for {modeValue}");
                }
            }

            if (callDistanceCheck)
            {
                await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"autocad_measurement\",\"arguments\":{\"action\":\"measure_distance\",\"params\":{\"x1\":0,\"y1\":0,\"x2\":6,\"y2\":8}}}}");
                using var callResp = await ReadJsonRpcAsync();
                AssertEqual(3, callResp.RootElement.GetProperty("id").GetInt32(), "tools/call id");
                string text = callResp.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
                using var payload = JsonDocument.Parse(text);
                AssertEqual(10.0, payload.RootElement.GetProperty("distance_2d").GetDouble(), "STDIO compound tools/call returns 6-8-10 distance");

                await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"autocad_layers\",\"arguments\":{}}}");
                using var helpResp = await ReadJsonRpcAsync();
                string helpText = helpResp.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
                using var helpPayload = JsonDocument.Parse(helpText);
                AssertEqual("layers", helpPayload.RootElement.GetProperty("domain").GetString(), "STDIO compound call omitting args defaults to help");
            }

            proc.StandardInput.Close();
            await proc.WaitForExitAsync(cts.Token);
            string stderrOut = await stderrTask;
            AssertEqual(0, proc.ExitCode, $"clean STDIO shutdown exit code in {modeValue}. Stderr: {stderrOut}");
        }
        finally
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task TestThermosRegressionsAsync()
    {
        var catalog = new AutoCADToolCatalog();

        AssertTrue(catalog.TryGetAction("draw_batch_geometry", out ToolActionDescriptor? drawBatch), "draw_batch_geometry found");
        JsonElement elementsAnyOf = drawBatch!.CompoundInputSchema.GetProperty("properties").GetProperty("elements").GetProperty("anyOf");
        var elementsTypes = elementsAnyOf.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToHashSet(StringComparer.Ordinal);
        AssertTrue(elementsTypes.SetEquals(["array", "string"]), "draw_batch_geometry.elements advertises array | string");

        AssertTrue(catalog.TryGetAction("define_block", out ToolActionDescriptor? defineBlock), "define_block found");
        JsonElement attrAnyOf = defineBlock!.CompoundInputSchema.GetProperty("properties").GetProperty("attributes_json").GetProperty("anyOf");
        var attrTypes = attrAnyOf.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToHashSet(StringComparer.Ordinal);
        AssertTrue(attrTypes.SetEquals(["array", "string", "null"]), "nullable define_block.attributes_json advertises array | string | null");

        AssertTrue(catalog.TryGetAction("set_block_attributes", out ToolActionDescriptor? setBlockAttrs), "set_block_attributes found");
        JsonElement setAttrAnyOf = setBlockAttrs!.CompoundInputSchema.GetProperty("properties").GetProperty("attributes_json").GetProperty("anyOf");
        var setAttrTypes = setAttrAnyOf.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToHashSet(StringComparer.Ordinal);
        AssertTrue(setAttrTypes.SetEquals(["object", "string"]), "set_block_attributes.attributes_json advertises object | string");

        var mockGateway = new RecordingGatewayClient();
        var dispatcher = new CompoundDispatcher(
            catalog,
            type => Activator.CreateInstance(type, mockGateway)
                ?? throw new InvalidOperationException($"Cannot instantiate {type.Name}."));

        static JsonElement ParseJson(string json)
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.Clone();
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("drawing", "draw_batch_geometry", ParseJson("{\"elements\":{\"type\":\"line\"}}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "draw_batch_geometry rejects JSON object");
        }

        using (var doc = JsonDocument.Parse(await dispatcher.DispatchAsync("drafting", "set_block_attributes", ParseJson("{\"handle\":\"1A\",\"attributes_json\":[\"A\"]}"))))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "set_block_attributes rejects JSON array");
        }

        AssertThrows<InvalidOperationException>(
            () => new AutoCADToolCatalog([typeof(AlphaFakeTools), typeof(BetaCrossAliasTools)]),
            "cross-domain [ToolExposure(AliasOf)] throws");

        var fakeCatalog = new AutoCADToolCatalog([typeof(FaultyFakeTools)]);
        var fakeInstance = new FaultyFakeTools();
        var fakeDispatcher = new CompoundDispatcher(fakeCatalog, _ => fakeInstance);

        using (var doc = JsonDocument.Parse(await fakeDispatcher.DispatchAsync("faulty", "async_bomb", null)))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "async tool exception caught as error JSON");
            AssertTrue((doc.RootElement.GetProperty("message").GetString() ?? "").Contains("async failure exploded", StringComparison.Ordinal), "async exception message surfaced");
        }

        using (var doc = JsonDocument.Parse(await fakeDispatcher.DispatchAsync("faulty", "unrequested_cancel", null)))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "unrequested OperationCanceledException converted to error JSON");
        }

        using (var canceledCts = new CancellationTokenSource())
        {
            canceledCts.Cancel();
            bool threwCanceled = false;
            try
            {
                await fakeDispatcher.DispatchAsync("faulty", "honor_caller_cancel", null, canceledCts.Token);
            }
            catch (OperationCanceledException)
            {
                threwCanceled = true;
            }

            AssertTrue(threwCanceled, "caller-requested cancellation propagates OperationCanceledException");
        }

        var drawingTools = new DrawingTools(mockGateway);
        using (var doc = JsonDocument.Parse(await drawingTools.DrawBatchGeometry("[{\"type\":\"line\",\"x1\":\"oops\",\"y1\":0,\"x2\":1,\"y2\":1}]")))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "DrawBatchGeometry rejects non-numeric coordinate string");
        }

        using (var doc = JsonDocument.Parse(await drawingTools.DrawBatchGeometry("[{\"type\":\"polyline\",\"points\":[[0,0],[5,0]],\"closed\":\"maybe\"}]")))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "DrawBatchGeometry rejects invalid boolean string");
        }

        mockGateway.Calls.Clear();
        string polyOk = await drawingTools.DrawBatchGeometry("[{\"type\":\"polyline\",\"points\":[{\"x\":0,\"y\":0,\"bulge\":0.5},[10,0]],\"closed\":\"true\"}]");
        AssertTrue(polyOk.Contains("\"ok\":true", StringComparison.Ordinal), "DrawBatchGeometry accepts vertex objects and boolean string");
        AssertTrue(mockGateway.Calls[0].Body.Contains("\"closed\":true", StringComparison.Ordinal), "DrawBatchGeometry converts 'true' string to boolean true");
        AssertTrue(mockGateway.Calls[0].Body.Contains("[0,0,0.5]", StringComparison.Ordinal), "DrawBatchGeometry preserves vertex object bulge");

        mockGateway.Calls.Clear();
        var execTools = new ExecutionTools(mockGateway);
        await execTools.ExecuteBatchCommands("[\"_.REGEN\",\"   \",\"\\t\"]");
        AssertEqual(1, mockGateway.Calls.Count, "ExecuteBatchCommands posts once");
        AssertEqual("_.REGEN\n", mockGateway.Calls[0].Body, "ExecuteBatchCommands skips whitespace-only strings");

        foreach (ToolMode directMode in new[] { ToolMode.Granular, ToolMode.Core })
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAutoCADGatewayClient>(new RecordingGatewayClient());
            services.AddMcpServer().WithConfiguredAutoCADTools(directMode);
            using var provider = services.BuildServiceProvider();
            AssertTrue(provider.GetService<CompoundDispatcher>() is null, $"{directMode} mode does not register CompoundDispatcher");
            AssertTrue(provider.GetService<AutoCADToolCatalog>() is null, $"{directMode} mode does not register AutoCADToolCatalog");
        }

        var compoundServices = new ServiceCollection();
        compoundServices.AddSingleton<IAutoCADGatewayClient>(new RecordingGatewayClient());
        compoundServices.AddMcpServer().WithConfiguredAutoCADTools(ToolMode.Compound);
        using var compoundProvider = compoundServices.BuildServiceProvider();
        AssertTrue(compoundProvider.GetService<CompoundDispatcher>() is not null, "Compound mode registers CompoundDispatcher");
        AssertTrue(compoundProvider.GetService<AutoCADToolCatalog>() is not null, "Compound mode registers AutoCADToolCatalog");

        AssertEqual(
            AutoCADGatewayClient.FastReadTimeoutMs,
            AutoCADGatewayClient.ResolveTimeoutMs(HttpMethod.Get, "context/info"),
            "GET context/info uses fast read timeout");
        AssertEqual(
            AutoCADGatewayClient.FastReadTimeoutMs,
            AutoCADGatewayClient.ResolveTimeoutMs(HttpMethod.Get, "_ping"),
            "GET _ping uses fast read timeout");
        AssertEqual(
            AutoCADGatewayClient.DefaultWriteTimeoutMs,
            AutoCADGatewayClient.ResolveTimeoutMs(HttpMethod.Post, "entities/create"),
            "standard POST uses default write timeout");
        AssertEqual(
            AutoCADGatewayClient.HeavyOperationTimeoutMs,
            AutoCADGatewayClient.ResolveTimeoutMs(HttpMethod.Post, "batch"),
            "batch POST uses heavy operation timeout");
        AssertEqual(
            AutoCADGatewayClient.HeavyOperationTimeoutMs,
            AutoCADGatewayClient.ResolveTimeoutMs(HttpMethod.Post, "plotting/pdf"),
            "plotting/pdf POST uses heavy operation timeout");
        AssertEqual(
            45_000L,
            AutoCADGatewayClient.ResolveTimeoutMs(HttpMethod.Post, "batch", 35_000),
            "explicit timeout extends wait budget");

        var draftingTools = new DraftingTools(mockGateway);
        using (var doc = JsonDocument.Parse(await draftingTools.CreateDimension("aligned", 0, 0, 10, 0, textHeight: 0)))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "CreateDimension rejects non-positive textHeight");
        }

        using (var doc = JsonDocument.Parse(await draftingTools.CreateDimension("aligned", 0, 0, 10, 0, arrowSize: -0.2)))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "CreateDimension rejects negative arrowSize");
        }

        using (var doc = JsonDocument.Parse(await draftingTools.CreateDimension("aligned", 0, 0, 10, 0, dimScale: 0)))
        {
            AssertEqual("error", doc.RootElement.GetProperty("status").GetString(), "CreateDimension rejects zero dimScale");
        }

        mockGateway.Calls.Clear();
        string dimOk = await draftingTools.CreateDimension("aligned", 0, 0, 10, 0, textHeight: 0.28, arrowSize: 0.2, dimScale: 1.0);
        AssertTrue(dimOk.Contains("\"ok\":true", StringComparison.Ordinal), "CreateDimension succeeds with explicit metric sizing");
        AssertEqual("POST:drafting/dimension", mockGateway.Calls[0].Route, "CreateDimension posts to drafting/dimension");
        AssertTrue(mockGateway.Calls[0].Body.Contains("\"text_height\":0.28", StringComparison.Ordinal), "CreateDimension serializes text_height");
        AssertTrue(mockGateway.Calls[0].Body.Contains("\"arrow_size\":0.2", StringComparison.Ordinal), "CreateDimension serializes arrow_size");
        AssertTrue(mockGateway.Calls[0].Body.Contains("\"dim_scale\":1", StringComparison.Ordinal), "CreateDimension serializes dim_scale");
    }

    private static string ResolveMcpServerDllPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string candidateFromTestsBin = baseDir.Replace(
            $"{Path.DirectorySeparatorChar}AutoCAD.Gateway.Tests{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}AutoCAD.McpServer{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

        string candidateDll = Path.Combine(candidateFromTestsBin, "AutoCAD.McpServer.dll");
        if (File.Exists(candidateDll) && File.Exists(Path.Combine(candidateFromTestsBin, "AutoCAD.McpServer.runtimeconfig.json")))
        {
            return candidateDll;
        }

        string trimmedBase = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? activeConfig = Directory.GetParent(trimmedBase)?.Name;
        var configsToTry = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(activeConfig))
        {
            configsToTry.Add(activeConfig);
        }

        foreach (string fallbackConfig in new[] { "Release", "Debug" })
        {
            if (!configsToTry.Contains(fallbackConfig, StringComparer.OrdinalIgnoreCase))
            {
                configsToTry.Add(fallbackConfig);
            }
        }

        string? dir = baseDir;
        while (dir is not null)
        {
            foreach (string config in configsToTry)
            {
                string dllPath = Path.Combine(dir, "AutoCAD.McpServer", "bin", config, "net10.0", "AutoCAD.McpServer.dll");
                if (File.Exists(dllPath) && File.Exists(Path.Combine(Path.GetDirectoryName(dllPath)!, "AutoCAD.McpServer.runtimeconfig.json")))
                {
                    return dllPath;
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException("Could not locate AutoCAD.McpServer.dll with runtimeconfig.json.");
    }

    [ToolDomain("alpha", "autocad_alpha", "Fake alpha tools.")]
    private sealed class AlphaFakeTools
    {
        [McpServerTool(Name = "alpha_op")]
        [Description("Valid alpha op.")]
        public string Op() => "{\"status\":\"success\"}";
    }

    [ToolDomain("beta", "autocad_beta", "Fake beta tools.")]
    private sealed class BetaCrossAliasTools
    {
        [McpServerTool(Name = "beta_alias")]
        [ToolExposure(AliasOf = "alpha_op")]
        [Description("Invalid cross-domain alias.")]
        public string BadAlias() => "{\"status\":\"success\"}";
    }

    [ToolDomain("faulty", "autocad_faulty", "Fake faulty tools.")]
    private sealed class FaultyFakeTools
    {
        [McpServerTool(Name = "async_bomb")]
        [Description("Throws asynchronously.")]
        public async Task<string> AsyncBomb()
        {
            await Task.Yield();
            throw new InvalidOperationException("async failure exploded");
        }

        [McpServerTool(Name = "unrequested_cancel")]
        [Description("Throws unrequested cancellation.")]
        public async Task<string> UnrequestedCancel()
        {
            await Task.Yield();
            throw new OperationCanceledException("internal cancel");
        }

        [McpServerTool(Name = "honor_caller_cancel")]
        [Description("Propagates caller cancellation.")]
        public async Task<string> HonorCallerCancel(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return "{\"status\":\"success\"}";
        }
    }

    private sealed record RecordedGatewayCall(string Route, string Body);

    private sealed class RecordingGatewayClient : IAutoCADGatewayClient
    {
        public List<RecordedGatewayCall> Calls { get; } = [];

        public Task<string> GetContextAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            Calls.Add(new($"GET:{endpoint}", string.Empty));
            return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true, route = endpoint }));
        }

        public Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            Calls.Add(new("POST:execute", command));
            return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true }));
        }

        public Task<string> PingGatewayAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add(new("GET:_ping", string.Empty));
            return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true }));
        }

        public Task<string> PostJsonAsync(string endpoint, string json, int? timeoutMs = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new($"POST:{endpoint}", json));
            return Task.FromResult(JsonSerializer.Serialize(new { status = "success", ok = true, route = endpoint }));
        }
    }
}
