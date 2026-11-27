using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using AutoCAD.McpServer.Server;
using AutoCAD.McpServer.Services;
using AutoCAD.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static AutoCAD.Gateway.Tests.TestAssert;

namespace AutoCAD.Gateway.Tests;

internal static class OutputProtocolTests
{
    public static async Task RunAllAsync(
        Action<string, Action> run,
        Func<string, Func<Task>, Task> runAsync)
    {
        run("OutputFormatParser parses empty, case-insensitive formats, env vars, and rejects invalid values", TestOutputFormatParser);
        await runAsync("ToolAnnotationPolicy and McpResultAdapter enforce isError, ImageContent, annotations, and compact token reduction", TestOutputProtocolAndAnnotationsAsync);
    }

    private static void TestOutputFormatParser()
    {
        AssertEqual(OutputFormat.Compact, OutputFormatParser.Parse(null), "null defaults to Compact");
        AssertEqual(OutputFormat.Compact, OutputFormatParser.Parse(string.Empty), "empty defaults to Compact");
        AssertEqual(OutputFormat.Compact, OutputFormatParser.Parse("   "), "whitespace defaults to Compact");
        AssertEqual(OutputFormat.Compact, OutputFormatParser.Parse("COMPACT"), "case-insensitive compact");
        AssertEqual(OutputFormat.Json, OutputFormatParser.Parse("  jSoN "), "trimmed case-insensitive json");
        AssertEqual("compact", OutputFormat.Compact.ToWireName(), "compact wire name");
        AssertEqual("json", OutputFormat.Json.ToWireName(), "json wire name");

        var invalidEx = AssertThrows<ArgumentException>(
            () => OutputFormatParser.Parse("yaml"),
            "invalid output format must throw ArgumentException");
        AssertTrue(invalidEx.Message.Contains(OutputFormatParser.EnvVarName, StringComparison.Ordinal), "error message cites AUTOCAD_MCP_OUTPUT_FORMAT");

        string? original = Environment.GetEnvironmentVariable(OutputFormatParser.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OutputFormatParser.EnvVarName, "json");
            AssertEqual(OutputFormat.Json, OutputFormatParser.FromEnvironment(), "FromEnvironment reads json");

            Environment.SetEnvironmentVariable(OutputFormatParser.EnvVarName, null);
            AssertEqual(OutputFormat.Compact, OutputFormatParser.FromEnvironment(), "unset env var defaults to Compact");
        }
        finally
        {
            Environment.SetEnvironmentVariable(OutputFormatParser.EnvVarName, original);
        }
    }

    private static async Task TestOutputProtocolAndAnnotationsAsync()
    {
        var catalog = new AutoCADToolCatalog();
        foreach (string classified in ToolAnnotationPolicy.EnumerateClassifiedTools())
        {
            AssertTrue(
                catalog.TryGetAction(classified, out var found) && found is not null && !found.IsAlias,
                $"ToolAnnotationPolicy classified tool '{classified}' must exist as a canonical tool in AutoCADToolCatalog");
        }

        foreach (ToolActionDescriptor action in catalog.AllActions)
        {
            ToolAnnotationSpec spec = ToolAnnotationPolicy.ResolveForTool(action.AliasOf ?? action.Name);
            AssertTrue(!(spec.ReadOnly && spec.Destructive), $"tool '{action.Name}' cannot be both ReadOnly and Destructive");
        }

        AssertThrows<ArgumentException>(
            () => ToolAnnotationPolicy.ResolveForTool("  "),
            "empty tool name rejects annotation resolution");

        var readLayersSpec = ToolAnnotationPolicy.ResolveForTool("get_layers");
        AssertTrue(readLayersSpec.ReadOnly && !readLayersSpec.Destructive && readLayersSpec.Idempotent, "get_layers is read-only");

        var deleteSpec = ToolAnnotationPolicy.ResolveForTool("delete_entities");
        AssertTrue(!deleteSpec.ReadOnly && deleteSpec.Destructive, "delete_entities is destructive");

        var batchSpec = ToolAnnotationPolicy.ResolveForTool("execute_native_batch");
        AssertTrue(!batchSpec.ReadOnly && batchSpec.Destructive, "execute_native_batch is destructive");

        var saveSpec = ToolAnnotationPolicy.ResolveForTool("save_drawing");
        AssertTrue(saveSpec.Destructive && saveSpec.OpenWorld, "save_drawing is destructive and open-world");

        var newDrawingSpec = ToolAnnotationPolicy.ResolveForTool("new_drawing");
        AssertTrue(!newDrawingSpec.Destructive && newDrawingSpec.OpenWorld, "new_drawing is open-world");

        string[] contextTools = catalog.AllActions
            .Where(a => a.Domain == "context")
            .Select(a => a.AliasOf ?? a.Name)
            .ToArray();
        var ctxCompound = ToolAnnotationPolicy.ResolveForCompoundDomain(contextTools);
        AssertTrue(ctxCompound.ReadOnly && !ctxCompound.Destructive && ctxCompound.Idempotent, "autocad_context compound router is read-only");

        string[] measurementTools = catalog.AllActions
            .Where(a => a.Domain == "measurement")
            .Select(a => a.AliasOf ?? a.Name)
            .ToArray();
        var measCompound = ToolAnnotationPolicy.ResolveForCompoundDomain(measurementTools);
        AssertTrue(!measCompound.ReadOnly && measCompound.Destructive, "autocad_measurement aggregates destructive purge_and_audit_drawing");

        string errJson = CadFormat.Error("Edit Guard blocked save.", code: "edit_guard", hint: "Call allow_edit_document.");
        CallToolResult errCompact = McpResultAdapter.Adapt("save_drawing", errJson, OutputFormat.Compact);
        AssertEqual(true, errCompact.IsError, "compact error sets IsError=true");
        AssertTrue(errCompact.StructuredContent is null, "error result omits StructuredContent");
        string errText = ((TextContentBlock)errCompact.Content[0]).Text!;
        AssertTrue(errText.Contains("[edit_guard]", StringComparison.Ordinal) && errText.Contains("Hint: Call allow_edit_document.", StringComparison.Ordinal), "compact error formats code and hint");

        CallToolResult errJsonMode = McpResultAdapter.Adapt("save_drawing", errJson, OutputFormat.Json);
        AssertEqual(true, errJsonMode.IsError, "json mode error sets IsError=true");
        AssertEqual(errJson, ((TextContentBlock)errJsonMode.Content[0]).Text, "json mode preserves raw error JSON text");

        byte[] onePxPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        string shotJson = JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            file_path = "C:/shots/test.png",
            width = 1920,
            height = 1080,
            mime_type = "image/png",
            image_base64 = Convert.ToBase64String(onePxPng),
            duration_ms = 12.5,
        });
        CallToolResult shotResult = McpResultAdapter.Adapt("capture_screenshot", shotJson, OutputFormat.Compact);
        AssertEqual(false, shotResult.IsError, "screenshot succeeds with IsError=false");
        AssertEqual(2, shotResult.Content.Count, "screenshot returns TextContentBlock + ImageContentBlock");
        string shotText = ((TextContentBlock)shotResult.Content[0]).Text!;
        AssertTrue(!shotText.Contains("iVBOR", StringComparison.Ordinal), "screenshot text omits raw base64");
        AssertTrue(shotResult.Content[1] is ImageContentBlock imgBlock && imgBlock.MimeType == "image/png", "screenshot second block is ImageContentBlock image/png");
        AssertTrue(shotResult.StructuredContent.HasValue && !shotResult.StructuredContent.Value.TryGetProperty("image_base64", out _), "screenshot structuredContent strips image_base64");
        AssertTrue(shotResult.StructuredContent!.Value.GetProperty("inline_image_attached").GetBoolean(), "screenshot structuredContent sets inline_image_attached");

        string badShotJson = JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            file_path = "C:/shots/bad.png",
            mime_type = "image/png",
            image_base64 = "not-valid-base64!!!",
        });
        CallToolResult badShotResult = McpResultAdapter.Adapt("capture_screenshot", badShotJson, OutputFormat.Compact);
        AssertEqual(false, badShotResult.IsError, "invalid base64 screenshot omits image gracefully without IsError");
        AssertEqual(1, badShotResult.Content.Count, "invalid base64 screenshot emits only text block");
        AssertEqual(false, badShotResult.StructuredContent!.Value.GetProperty("inline_image_attached").GetBoolean(), "invalid base64 sets inline_image_attached=false");
        AssertTrue(badShotResult.StructuredContent!.Value.TryGetProperty("inline_omitted_reason", out _), "invalid base64 explains inline_omitted_reason");

        var realLayersPayload = Enumerable.Range(1, 50).Select(i => new
        {
            name = $"LAYER-{i:00}",
            is_locked = i == 3,
            is_frozen = i == 2,
            is_off = i == 4,
            color = (i % 7) + 1,
            linetype = "Continuous",
            lineweight = "LineWeight025",
            is_plottable = i != 5,
            description = string.Empty,
        }).ToArray();
        string rawLayers = JsonSerializer.Serialize(new { status = "success", ok = true, data = realLayersPayload });
        CallToolResult compactLayers = McpResultAdapter.Adapt("get_layers", rawLayers, OutputFormat.Compact);
        string compactText = ((TextContentBlock)compactLayers.Content[0]).Text!;
        AssertTrue(compactText.Contains("aci:2", StringComparison.Ordinal) && compactText.Contains("| off", StringComparison.Ordinal) && compactText.Contains("no-plot", StringComparison.Ordinal), "compact layers formats real LayerInfo color/is_off/is_plottable fields");
        AssertTrue(compactText.Length < rawLayers.Length * 0.65, $"compact layers text saves >35% chars ({compactText.Length} vs {rawLayers.Length})");
        AssertEqual(50, compactLayers.StructuredContent!.Value.GetProperty("data").GetArrayLength(), "structuredContent preserves all 50 LayerInfo items");

        string rawTexts = JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            data = new[]
            {
                new { type = "MText", text = "BEAM W12x26", x = 10.5, y = 20.25, layer = "ANNO" },
            },
        });
        CallToolResult compactTexts = McpResultAdapter.Adapt("get_all_texts", rawTexts, OutputFormat.Compact);
        string compactTextsOut = ((TextContentBlock)compactTexts.Content[0]).Text!;
        AssertTrue(compactTextsOut.Contains("Texts (1)", StringComparison.Ordinal) && compactTextsOut.Contains("BEAM W12x26", StringComparison.Ordinal), "compact texts formats real TextEntityInfo data array");

        string rawBlocks = JsonSerializer.Serialize(new { status = "success", ok = true, data = new[] { "DOOR_90", "WINDOW_120" } });
        CallToolResult compactBlocks = McpResultAdapter.Adapt("list_blocks", rawBlocks, OutputFormat.Compact);
        AssertTrue(((TextContentBlock)compactBlocks.Content[0]).Text!.Contains("DOOR_90", StringComparison.Ordinal), "compact list_blocks formats string array data");

        string rawEntities = JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            total_matched = 12,
            offset = 0,
            count = 1,
            has_more = true,
            next_cursor = "1",
            entities = new object[]
            {
                new
                {
                    handle = "1F4",
                    type = "Line",
                    layer = "WALLS",
                    space = "model",
                    color_index = 1,
                    geometry = new { length = 4.5 },
                },
            },
        });
        CallToolResult compactEntities = McpResultAdapter.Adapt("query_entities", rawEntities, OutputFormat.Compact);
        string entitiesOut = ((TextContentBlock)compactEntities.Content[0]).Text!;
        AssertTrue(entitiesOut.Contains("Entities (1/12)", StringComparison.Ordinal) && entitiesOut.Contains("len:4.5", StringComparison.Ordinal), "compact entities reads total_matched and nested geometry.length");

        string malformedHelp = JsonSerializer.Serialize(new
        {
            status = "success",
            ok = true,
            domain = "context",
            actions = new { broken = "not_an_object", get_layers = new { required = new string[0], optional = new string[0], description = "Read layers" } },
        });
        CallToolResult helpResult = McpResultAdapter.Adapt("autocad_context", malformedHelp, OutputFormat.Compact);
        AssertEqual(false, helpResult.IsError, "malformed action metadata in compound help skips safely");

        string partialBatchJson = JsonSerializer.Serialize(new
        {
            status = "partial_error",
            ok = false,
            rolled_back = false,
            succeeded = 2,
            failed = 1,
            handles = new[] { "1A1", "1A2" },
            results = new object[]
            {
                new { index = 0, ok = true, handle = "1A1" },
                new { index = 1, ok = true, handle = "1A2" },
                new { index = 2, ok = false, error_code = "not_found", error = "Layer 'MISSING' not found." },
            },
            error = new { code = "batch_partial_error", message = "1 of 3 batch operations failed." },
        });
        CallToolResult partialRes = McpResultAdapter.Adapt("execute_native_batch", partialBatchJson, OutputFormat.Compact);
        AssertEqual(true, partialRes.IsError, "partial_error sets IsError=true");
        string partialOut = ((TextContentBlock)partialRes.Content[0]).Text!;
        AssertTrue(partialOut.Contains("succeeded=2, failed=1", StringComparison.Ordinal) &&
                   partialOut.Contains("1A1, 1A2", StringComparison.Ordinal) &&
                   partialOut.Contains("item #2 [not_found]", StringComparison.Ordinal),
            "partial_error retains handles, counts, and per-item failures");

        string hugeErrJson = CadFormat.Error("Bad Gateway", code: "http_error", detail: new string('X', 5000));
        CallToolResult clampedErr = McpResultAdapter.Adapt("get_layers", hugeErrJson, OutputFormat.Compact);
        AssertTrue(((TextContentBlock)clampedErr.Content[0]).Text!.Length <= 4000, "compact errors are clamped to MaxContentChars");

        string emptyLayersJson = JsonSerializer.Serialize(new { status = "success", ok = true, data = Array.Empty<object>() });
        CallToolResult emptyLayersRes = McpResultAdapter.Adapt("get_layers", emptyLayersJson, OutputFormat.Compact);
        AssertTrue(((TextContentBlock)emptyLayersRes.Content[0]).Text!.StartsWith("Layers (0)", StringComparison.Ordinal), "empty get_layers renders Layers (0)");

        var services = new ServiceCollection();
        services.AddSingleton<IAutoCADGatewayClient>(new RecordingGatewayClient());
        services.AddMcpServer().WithConfiguredAutoCADTools(ToolMode.Core, OutputFormat.Compact);
        using var provider = services.BuildServiceProvider();
        var registered = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!;
        var getLayersTool = registered.First(t => t.ProtocolTool.Name == "get_layers");
        AssertEqual(true, getLayersTool.ProtocolTool.Annotations?.ReadOnlyHint, "DI get_layers exposes ReadOnlyHint=true");
        var delTool = registered.First(t => t.ProtocolTool.Name == "delete_entities");
        AssertEqual(true, delTool.ProtocolTool.Annotations?.DestructiveHint, "DI delete_entities exposes DestructiveHint=true");
        await Task.CompletedTask;
    }
}
