using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using static AutoCAD.Gateway.Tests.TestAssert;

namespace AutoCAD.Gateway.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main()
    {
        Run("HttpRequestGuard.Authorize allows valid loopback GET and POST", TestAuthorizeValid);
        Run("HttpRequestGuard.Authorize rejects bad/empty tokens, DNS rebinding hosts, Origin/Referer, and non-JSON POST", TestAuthorizeRejections);
        Run("HttpRequestGuard.Admit checks auth before body limits, maps 405/415 codes, and enforces framing rules", TestAdmitFraming);
        Run("HttpRequestGuard.TryParseHead rejects bare LF, pre-colon whitespace, duplicate critical headers, and invalid HTTP versions", TestParseHeadHardening);
        Run("HttpRequestGuard.TrySplitTarget separates path, decodes query parameters, and rejects bad % escapes", TestSplitTarget);
        Run("GatewayProtocol resolves ports and normalizes HTTP URLs safely", TestGatewayProtocolHelpers);
        Run("QueryCursor validates signature, document, fingerprint, bounds, TTL, and revision", TestQueryCursor);
        Run("InstanceDiscovery publishes atomically, prunes dead PIDs, and matches port", TestInstanceDiscovery);
        Run("CommandLineState classifies idle, active prompt, dialog bit, and timeout", TestCommandLineState);
        Run("LispSyntax and GatewayProtocol validate delimiters, NUL bytes, timeouts, and error status mapping", TestLispSyntaxAndProtocol);
        Run("SecurityPolicy validates blocked sysvars, handles, bounding boxes, and local file paths", TestSecurityPolicy);
        Run("GatewayToolCatalog exposes unique endpoints, valid undo kinds, and agent guide", TestGatewayToolCatalog);
        Run("Security audit regressions: Fetch Metadata, loopback URLs, device names, sysvar prefixes, oversized discovery files", TestSecurityAuditRegressions);
        await RunAsync("HttpWireCodec reads buffered heads, catches truncated/invalid UTF-8, detects 431, and drains refusals", TestHttpWireCodecAsync);
        await ToolModeTests.RunAllAsync(Run, RunAsync);

        Console.WriteLine($"Summary: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"[FAIL] {name}: {ex}");
        }
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"[FAIL] {name}: {ex}");
        }
    }

    private static void TestAuthorizeValid()
    {
        const string token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        var getHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
        };
        AssertEqual<int?>(null, HttpRequestGuard.Authorize("GET", getHeaders, token, 5055), "127.0.0.1 GET");

        var postHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "localhost:5055",
            ["Content-Type"] = "application/json; charset=utf-8",
        };
        AssertEqual<int?>(null, HttpRequestGuard.Authorize("POST", postHeaders, token, 5055), "localhost POST");
    }

    private static void TestAuthorizeRejections()
    {
        const string token = "secret-session-token-1234567890";
        Dictionary<string, string> BaseHeaders() => new(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "application/json",
        };

        var missingToken = BaseHeaders();
        missingToken.Remove(GatewayProtocol.TokenHeader);
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", missingToken, token, 5055), "Missing token");

        var wrongToken = BaseHeaders();
        wrongToken[GatewayProtocol.TokenHeader] = "wrong-token";
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", wrongToken, token, 5055), "Wrong token");
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", BaseHeaders(), string.Empty, 5055), "Empty server token");

        var rebindHost = BaseHeaders();
        rebindHost["Host"] = "attacker.example.com:5055";
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", rebindHost, token, 5055), "DNS rebinding host");

        var wrongPort = BaseHeaders();
        wrongPort["Host"] = "127.0.0.1:9999";
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", wrongPort, token, 5055), "Wrong port");

        var withOrigin = BaseHeaders();
        withOrigin["Origin"] = "http://localhost:3000";
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", withOrigin, token, 5055), "Origin header");

        var withReferer = BaseHeaders();
        withReferer["Referer"] = "http://evil.local/";
        AssertEqual<int?>(403, HttpRequestGuard.Authorize("POST", withReferer, token, 5055), "Referer header");

        var textPlain = BaseHeaders();
        textPlain["Content-Type"] = "text/plain";
        AssertEqual<int?>(415, HttpRequestGuard.Authorize("POST", textPlain, token, 5055), "text/plain POST");

        var options = BaseHeaders();
        AssertEqual<int?>(405, HttpRequestGuard.Authorize("OPTIONS", options, token, 5055), "OPTIONS disallowed");
    }

    private static void TestAdmitFraming()
    {
        const string token = "valid-token-value";
        var unauthHuge = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = "invalid",
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "application/json",
            ["Content-Length"] = "999999999",
        };
        var denied = HttpRequestGuard.Admit("POST", unauthHuge, token, 5055, 1024);
        AssertTrue(!denied.Admitted, "Unauthorized huge request must be refused");
        AssertEqual(403, denied.Status, "Auth refusal must precede size error");
        AssertEqual("forbidden", denied.Refusal!.Error!.Code, "403 error code");

        var badMethod = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
        };
        var methodRefusal = HttpRequestGuard.Admit("DELETE", badMethod, token, 5055, 1024);
        AssertEqual(405, methodRefusal.Status, "Disallowed method");
        AssertEqual("method_not_allowed", methodRefusal.Refusal!.Error!.Code, "405 error code");

        var badMedia = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "text/plain",
        };
        var mediaRefusal = HttpRequestGuard.Admit("POST", badMedia, token, 5055, 1024);
        AssertEqual(415, mediaRefusal.Status, "Unsupported media type");
        AssertEqual("unsupported_media_type", mediaRefusal.Refusal!.Error!.Code, "415 error code");

        var badLen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "application/json",
            ["Content-Length"] = "-5",
        };
        AssertEqual(400, HttpRequestGuard.Admit("POST", badLen, token, 5055, 1024).Status, "Negative Content-Length");

        var chunked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "application/json",
            ["Transfer-Encoding"] = "chunked",
        };
        AssertEqual(400, HttpRequestGuard.Admit("POST", chunked, token, 5055, 1024).Status, "Chunked rejected");

        var getWithBody = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Length"] = "10",
        };
        AssertEqual(400, HttpRequestGuard.Admit("GET", getWithBody, token, 5055, 1024).Status, "GET body rejected");

        var tooBig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "application/json",
            ["Content-Length"] = "2048",
        };
        AssertEqual(413, HttpRequestGuard.Admit("POST", tooBig, token, 5055, 1024).Status, "Oversized body");

        AssertThrows<ArgumentOutOfRangeException>(
            () => HttpRequestGuard.Admit("GET", badMethod, token, 5055, -1),
            "Negative maxBodyBytes throws");

        var valid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayProtocol.TokenHeader] = token,
            ["Host"] = "127.0.0.1:5055",
            ["Content-Type"] = "application/json",
            ["Content-Length"] = "42",
        };
        var ok = HttpRequestGuard.Admit("POST", valid, token, 5055, 1024);
        AssertTrue(ok.Admitted, "Valid request must be admitted");
        AssertEqual(42, ok.ContentLength, "Admitted ContentLength");
    }

    private static void TestParseHeadHardening()
    {
        string validHead = "POST /execute HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nX-Gateway-Token: abc\r\nContent-Length: 2\r\n\r\n";
        AssertTrue(HttpRequestGuard.TryParseHead(validHead, out string method, out string target, out var headers), "Valid head");
        AssertEqual("POST", method, "Parsed method");
        AssertEqual("/execute", target, "Parsed target");
        AssertEqual("2", headers["content-length"], "Case-insensitive header lookup");

        string dupHost = "GET /_ping HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nHost: evil.com:5055\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(dupHost, out _, out _, out _), "Duplicate Host rejected");

        string dupContentType = "POST /execute HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nContent-Type: application/json\r\nContent-Type: text/plain\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(dupContentType, out _, out _, out _), "Duplicate Content-Type rejected");

        string dupToken = "GET /_ping HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nX-Gateway-Token: a\r\nX-Gateway-Token: b\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(dupToken, out _, out _, out _), "Duplicate Token rejected");

        string preColonSpace = "GET /_ping HTTP/1.1\r\nHost : 127.0.0.1:5055\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(preColonSpace, out _, out _, out _), "Space before colon rejected");

        string bareLf = "GET /_ping HTTP/1.1\r\nHost: 127.0.0.1:5055\nSmuggled: 1\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(bareLf, out _, out _, out _), "Bare LF smuggling rejected");

        string badVersion = "GET /_ping HTTP/1.99\r\nHost: 127.0.0.1:5055\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(badVersion, out _, out _, out _), "Bogus HTTP version rejected");

        string extraSpace = "GET  /_ping HTTP/1.1\r\nHost: 127.0.0.1:5055\r\n\r\n";
        AssertTrue(!HttpRequestGuard.TryParseHead(extraSpace, out _, out _, out _), "Double-spaced request line rejected");
    }

    private static void TestSplitTarget()
    {
        AssertTrue(
            HttpRequestGuard.TrySplitTarget("/civil/surface/elevation?x=12.5&y=34.5&surface=EG%20Topo%262", out string path, out var q),
            "Split target");
        AssertEqual("/civil/surface/elevation", path, "Unescaped path");
        AssertEqual("12.5", q["x"], "Query x");
        AssertEqual("EG Topo&2", q["surface"], "Query unescaped safely without breaking &");

        AssertTrue(!HttpRequestGuard.TrySplitTarget("/bad%ZZpath", out _, out _), "Malformed percent escape rejected");
    }

    private static void TestGatewayProtocolHelpers()
    {
        AssertEqual(5055, GatewayProtocol.ResolveConfiguredPort("", ""), "Default port");
        AssertEqual(5123, GatewayProtocol.ResolveConfiguredPort("5123", ""), "Explicit env port");
        AssertEqual(6000, GatewayProtocol.ResolveConfiguredPort("invalid", "http://127.0.0.1:6000"), "Fallback URL port");
        AssertEqual(5055, GatewayProtocol.ResolveConfiguredPort("99999", "ftp://127.0.0.1:9999"), "Rejects invalid port and non-HTTP URL");

        AssertTrue(GatewayProtocol.TryNormalizeHttpUrl("http://localhost:5077", out Uri? normalized), "Valid HTTP URL");
        AssertEqual("http://localhost:5077/", normalized!.AbsoluteUri, "Normalized trailing slash");
        AssertTrue(!GatewayProtocol.TryNormalizeHttpUrl("file:///C:/temp", out _), "Non-HTTP URL rejected");
    }

    private static void TestQueryCursor()
    {
        long now = 1_700_000_000;
        var cursor = new QueryCursor(ttlSeconds: 60, nowUnixSeconds: () => now);
        string tok = cursor.Create("doc-1", revision: 7, offset: 500, fingerprint: "LINE|WALLS");

        AssertEqual(500, cursor.Read(tok, "doc-1", revision: 7, fingerprint: "LINE|WALLS"), "Valid cursor offset");

        AssertThrows<ArgumentException>(
            () => cursor.Create("doc-1", revision: 7, offset: 0, fingerprint: new string('A', 2000)),
            "Oversized fingerprint rejected in Create");

        var stale = AssertThrows<CursorValidationException>(
            () => cursor.Read(tok, "doc-1", revision: 8, fingerprint: "LINE|WALLS"),
            "Changed revision");
        AssertEqual("CURSOR_STALE", stale.Code, "Stale error code");

        var wrongDoc = AssertThrows<CursorValidationException>(
            () => cursor.Read(tok, "doc-2", revision: 7, fingerprint: "LINE|WALLS"),
            "Wrong doc");
        AssertEqual("CURSOR_INVALID", wrongDoc.Code, "Wrong doc code");

        var wrongFp = AssertThrows<CursorValidationException>(
            () => cursor.Read(tok, "doc-1", revision: 7, fingerprint: "CIRCLE|WALLS"),
            "Wrong fingerprint");
        AssertEqual("CURSOR_INVALID", wrongFp.Code, "Wrong fingerprint code");

        now += 61;
        var expired = AssertThrows<CursorValidationException>(
            () => cursor.Read(tok, "doc-1", revision: 7, fingerprint: "LINE|WALLS"),
            "Expired cursor");
        AssertEqual("CURSOR_EXPIRED", expired.Code, "Expired error code");
    }

    private static void TestInstanceDiscovery()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "acad-gw-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "99999.json"), "{\"pid\":99999,\"port\":5055,\"token\":\"old\"}");
            File.WriteAllText(Path.Combine(tempDir, "100.json"), "not-json");

            Func<int, bool> alive = pid => pid is 100 or 200 or 300;
            InstanceDiscovery.Publish(new InstanceDescriptor
            {
                Pid = 200,
                Port = 5100,
                Token = "tok-200",
                StartedUtc = "2026-11-21T10:00:00Z",
                Product = "AutoCAD 2027",
            }, tempDir, alive);

            AssertTrue(!File.Exists(Path.Combine(tempDir, "99999.json")), "Dead PID pruned");

            InstanceDiscovery.Publish(new InstanceDescriptor
            {
                Pid = 300,
                Port = 5200,
                Token = "tok-300",
                StartedUtc = "2026-11-21T11:00:00Z",
                Product = "Civil 3D 2027",
            }, tempDir, alive);

            var newest = InstanceDiscovery.FindNewestLive(tempDir, alive);
            AssertTrue(newest is not null, "Newest live instance found");
            AssertEqual(300, newest!.Pid, "Newest PID");

            var byPort = InstanceDiscovery.FindLiveByPort(5100, tempDir, alive);
            AssertTrue(byPort is not null, "Port-matched instance found");
            AssertEqual(200, byPort!.Pid, "Matched port PID");
            AssertEqual("tok-200", byPort.Token, "Matched port token");

            InstanceDiscovery.Remove(300, tempDir);
            var fallback = InstanceDiscovery.FindNewestLive(tempDir, alive);
            AssertEqual(200, fallback!.Pid, "Fallback to remaining live PID");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static void TestCommandLineState()
    {
        AssertTrue(CommandLineState.FromSystemVariables(0, "") is null, "Idle CMDACTIVE 0 returns null");

        var prompt = CommandLineState.FromSystemVariables(1, "-INSERT");
        AssertTrue(prompt is not null, "Active prompt produces state");
        AssertEqual(CommandLineState.Pending, prompt!.State, "Pending state");
        AssertEqual("-INSERT", prompt.PendingCommand, "Command name preserved");
        AssertEqual(1, prompt.CmdActive, "CmdActive preserved");
        AssertTrue(prompt.Hint.Contains("cancel_command", StringComparison.Ordinal), "Prompt hint mentions cancel_command");

        var dialog = CommandLineState.FromSystemVariables(CommandLineState.DialogBit | 1, "PLOT");
        AssertTrue(dialog is not null && dialog.Hint.Contains("dialog", StringComparison.OrdinalIgnoreCase), "Dialog bit classified");

        var unknown = CommandLineState.NotRead(1000);
        AssertEqual(CommandLineState.Unknown, unknown.State, "NotRead uses Unknown state");
    }

    private static void TestLispSyntaxAndProtocol()
    {
        AssertTrue(LispSyntax.Validate("(+ 1 2)") is null, "Simple form valid");
        AssertTrue(LispSyntax.Validate("(defun f () (princ \";)\\\"\") (princ))") is null, "Escaped quote and comment char in string valid");
        AssertTrue(LispSyntax.Validate("; unmatched ) in comment\n(+ 1 2) ; trailing comment") is null, "Line comments valid");
        AssertTrue(LispSyntax.Validate(";| block ) ( comment |; (+ 1 2)") is null, "Block comment valid");

        AssertTrue(LispSyntax.Validate("") is not null, "Empty rejected");
        AssertTrue(LispSyntax.Validate("(+ 1\0 2)") is not null, "NUL rejected");
        AssertTrue(LispSyntax.Validate("(+ 1 2))") is not null, "Extra close paren rejected");
        AssertTrue(LispSyntax.Validate("(+ 1 2") is not null, "Unclosed paren rejected");
        AssertTrue(LispSyntax.Validate("(princ \"abc)") is not null, "Unclosed string rejected");
        AssertTrue(LispSyntax.Validate(";| open comment (+ 1 2)") is not null, "Unclosed block comment rejected");

        AssertEqual(GatewayProtocol.MinLispTimeoutMs, GatewayProtocol.ClampLispTimeoutMs(10), "Timeout floor clamped");
        AssertEqual(GatewayProtocol.MaxLispTimeoutMs, GatewayProtocol.ClampLispTimeoutMs(9_999_999_999L), "Large long timeout clamped");
        AssertEqual(422, GatewayProtocol.MapErrorCodeToHttpStatus("lisp_error"), "lisp_error maps to 422");
        AssertEqual(400, GatewayProtocol.MapErrorCodeToHttpStatus("bad_query"), "bad_query maps to 400");
        AssertEqual(409, GatewayProtocol.MapErrorCodeToHttpStatus("layer_in_use"), "layer_in_use maps to 409");
        AssertEqual(409, GatewayProtocol.MapErrorCodeToHttpStatus("unsaved_changes"), "unsaved_changes maps to 409");
        AssertEqual(409, GatewayProtocol.MapErrorCodeToHttpStatus("ambiguous_document"), "ambiguous_document maps to 409");
        AssertEqual(409, GatewayProtocol.MapErrorCodeToHttpStatus("already_exists"), "already_exists maps to 409");
        AssertEqual(503, GatewayProtocol.MapErrorCodeToHttpStatus("gateway_stopped"), "gateway_stopped maps to 503");
        AssertEqual(504, GatewayProtocol.MapErrorCodeToHttpStatus("lisp_timeout"), "lisp_timeout maps to 504");
    }

    private static void TestSecurityPolicy()
    {
        AssertTrue(SecurityPolicy.IsBlockedSystemVariable("TRUSTEDPATHS"), "TRUSTEDPATHS blocked");
        AssertTrue(SecurityPolicy.IsBlockedSystemVariable("secureload"), "secureload blocked case-insensitively");
        AssertTrue(SecurityPolicy.IsBlockedSystemVariable("SAFEMODE"), "SAFEMODE blocked");
        AssertTrue(!SecurityPolicy.IsBlockedSystemVariable("INSUNITS"), "INSUNITS allowed");

        AssertTrue(SecurityPolicy.IsValidEntityHandle("1A3F"), "Valid hex handle accepted");
        AssertTrue(!SecurityPolicy.IsValidEntityHandle("0"), "Zero handle rejected");
        AssertTrue(!SecurityPolicy.IsValidEntityHandle("XYZ1"), "Non-hex handle rejected");
        AssertTrue(!SecurityPolicy.IsValidEntityHandle("1234567890ABCDEF0"), "Overlong handle rejected");

        AssertTrue(SecurityPolicy.IntersectsBbox(0, 0, 10, 10, 5, 5, 15, 15), "Overlapping bboxes intersect");
        AssertTrue(!SecurityPolicy.IntersectsBbox(0, 0, 10, 10, 11, 11, 20, 20), "Disjoint bboxes do not intersect");

        AssertTrue(SecurityPolicy.ValidateLocalFilePath(@"C:\drawings\plan.dwg", out _, out _, ".dwg"), "Valid local .dwg accepted");
        AssertTrue(SecurityPolicy.IsValidSymbolName("ARCH-WALLS"), "Valid layer name accepted");
        AssertTrue(!SecurityPolicy.IsValidSymbolName("BAD\nLAYER"), "Control char in symbol name rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"C:\drawings\..\secret.dwg", out _, out _, ".dwg"), "Parent traversal rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"\\server\share\plan.dwg", out _, out _, ".dwg"), "UNC share rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"C:\drawings\plan.dwg:stream", out _, out _, ".dwg"), "Alternate data stream rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"C:\drawings\script.exe", out _, out _, ".dwg"), "Disallowed extension rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath("relative\\plan.dwg", out _, out _, ".dwg"), "Relative path rejected");

        AssertEqual(200, SecurityPolicy.MaxTableRows, "Table row cap enforced at 200");
        AssertEqual(50, SecurityPolicy.MaxTableCols, "Table column cap enforced at 50");
        AssertEqual(500, SecurityPolicy.MaxProfileSamples, "Profile sample cap enforced at 500");
        AssertEqual(200, SecurityPolicy.MaxCrossSectionSamples, "Cross-section sample cap enforced at 200");
        AssertEqual(8 * 1024 * 1024, SecurityPolicy.MaxScreenshotInlineBytes, "Inline screenshot cap enforced at 8 MiB");
    }

    private static void TestGatewayToolCatalog()
    {
        var all = GatewayToolCatalog.All;
        AssertEqual(55, all.Count, "Catalog contains all 55 Phase 5 Gateway routes");

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ToolDescriptor tool in all)
        {
            AssertTrue(!string.IsNullOrWhiteSpace(tool.Name), "Tool name must be non-empty");
            AssertTrue(!string.IsNullOrWhiteSpace(tool.Category), "Tool category must be non-empty");
            AssertTrue(!string.IsNullOrWhiteSpace(tool.Description), "Tool description must be non-empty");
            AssertTrue(tool.Method is "GET" or "POST", $"Tool {tool.Name} has invalid HTTP method {tool.Method}");
            AssertTrue(tool.Route.StartsWith('/'), $"Tool {tool.Name} route must start with '/'");
            AssertTrue(UndoKind.ValidKinds.Contains(tool.Undo), $"Tool {tool.Name} has invalid undo kind '{tool.Undo}'");
            AssertTrue(seenNames.Add(tool.Name), $"Duplicate tool name in catalog: {tool.Name}");
            AssertTrue(seenKeys.Add($"{tool.Method} {tool.Route}"), $"Duplicate route pair in catalog: {tool.Method} {tool.Route}");

            if (tool.Undo == UndoKind.None)
            {
                AssertTrue(!tool.Mutating && !tool.RequiresEditGuard, $"Read-only tool {tool.Name} must not mutate or require Edit Guard");
            }
            else
            {
                AssertTrue(tool.Mutating, $"Mutating/side-effect undo kind '{tool.Undo}' requires Mutating=true on {tool.Name}");
            }
        }

        string sampleJson = System.Text.Json.JsonSerializer.Serialize(all[0], GatewayProtocol.WireJsonOptions);
        AssertTrue(sampleJson.Contains("\"requires_edit_guard\":false", StringComparison.Ordinal), "Descriptor serializes snake_case flags");

        string guide = GatewayToolCatalog.BuildAgentGuideText();
        AssertTrue(guide.Contains("Edit Guard", StringComparison.Ordinal), "Guide covers Edit Guard");
        AssertTrue(guide.Contains("revision_conflict", StringComparison.Ordinal), "Guide cites revision_conflict");
        AssertTrue(!guide.Contains("CURSOR_STALE", StringComparison.Ordinal), "Guide avoids internal-only CURSOR_STALE");
    }

    private static async Task TestHttpWireCodecAsync()
    {
        // 1. Pre-read body leftover + UTF-8 round-trip
        byte[] payload = Encoding.UTF8.GetBytes("{\"command\":\"_.ZOOM\\nE\\n\"}");
        byte[] raw = Encoding.ASCII.GetBytes(
            $"POST /execute HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\n\r\n")
            .ConcatBytes(payload);
        using (var ms = new MemoryStream(raw))
        {
            var head = await HttpWireCodec.ReadHeadAsync(ms, 4096);
            AssertTrue(!head.Closed && !head.HeaderTooLarge, "Head read cleanly");
            var body = await HttpWireCodec.ReadBodyAsync(ms, payload.Length, head.LeftoverBytes);
            AssertTrue(body.IsComplete && !body.InvalidUtf8, "Body complete and valid UTF-8");
            AssertEqual("{\"command\":\"_.ZOOM\\nE\\n\"}", body.Body, "Body matches");
        }

        // 2. Truncated invalid UTF-8 MUST fail completeness check (Content-Length 4, only 2 invalid bytes sent)
        using (var ms = new MemoryStream([0xFF, 0xFF]))
        {
            var body = await HttpWireCodec.ReadBodyAsync(ms, 4, null);
            AssertTrue(!body.IsComplete, "Truncated raw bytes must fail IsComplete regardless of UTF-8 replacement length");
        }

        // 3. Complete invalid UTF-8 MUST flag InvalidUtf8
        using (var ms = new MemoryStream([0xFF, 0xFE, 0xFD, 0xFC]))
        {
            var body = await HttpWireCodec.ReadBodyAsync(ms, 4, null);
            AssertTrue(body.IsComplete && body.InvalidUtf8, "Invalid UTF-8 flagged");
        }

        // 4. Oversized header triggers HeaderTooLarge (431)
        byte[] giantHead = Encoding.ASCII.GetBytes("GET /_ping HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nX-Pad: " + new string('x', 500) + "\r\n\r\n");
        using (var ms = new MemoryStream(giantHead))
        {
            var head = await HttpWireCodec.ReadHeadAsync(ms, 256);
            AssertTrue(head.HeaderTooLarge, "Header limit triggers HeaderTooLarge");
        }

        // 5. Real loopback TcpListener refusal drain completes without RST
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = Task.Run(async () =>
            {
                using TcpClient serverClient = await listener.AcceptTcpClientAsync();
                await using NetworkStream s = serverClient.GetStream();
                var h = await HttpWireCodec.ReadHeadAsync(s, 4096);
                AssertTrue(!h.Closed, "Server read head");
                byte[] bodyBytes = Encoding.UTF8.GetBytes("{\"status\":\"error\",\"ok\":false}");
                await HttpWireCodec.WriteResponseAsync(s, 403, bodyBytes);
                await HttpWireCodec.FinishRefusalAsync(serverClient.Client, s, 65536, TimeSpan.FromSeconds(2));
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using NetworkStream cs = client.GetStream();
            byte[] req = Encoding.ASCII.GetBytes("POST /execute HTTP/1.1\r\nHost: 127.0.0.1:5055\r\nContent-Length: 10\r\n\r\n{\"a\":\"bc\"}");
            await cs.WriteAsync(req);
            await cs.FlushAsync();
            client.Client.Shutdown(SocketShutdown.Send);

            using var reader = new StreamReader(cs, Encoding.UTF8);
            string resp = await reader.ReadToEndAsync();
            AssertTrue(resp.StartsWith("HTTP/1.1 403 Forbidden", StringComparison.Ordinal), "Client cleanly received 403 response");
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TestSecurityAuditRegressions()
    {
        const string token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        foreach (var (hdrName, hdrVal) in new[]
        {
            ("Sec-Fetch-Site", "cross-site"),
            ("Sec-Fetch-Mode", "cors"),
            ("Sec-Fetch-Dest", "empty"),
        })
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [GatewayProtocol.TokenHeader] = token,
                ["Host"] = "127.0.0.1:5055",
                [hdrName] = hdrVal,
            };
            AssertEqual<int?>(403, HttpRequestGuard.Authorize("GET", headers, token, 5055), $"{hdrName} rejected");
        }

        AssertTrue(!GatewayProtocol.TryNormalizeHttpUrl("http://evil.example:5055", out _), "Non-loopback host rejected");
        AssertTrue(GatewayProtocol.TryNormalizeHttpUrl("http://127.0.0.1:5055", out _), "127.0.0.1 loopback accepted");
        AssertTrue(GatewayProtocol.TryNormalizeHttpUrl("http://localhost:5077", out _), "localhost loopback accepted");

        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"C:\out\CON.pdf", out _, out _, ".pdf"), "Reserved CON device rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"C:\out\nul", out _, out _), "Reserved NUL device rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"C:\out\plan.dwg.", out _, out _, ".dwg"), "Trailing-dot segment rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"/\evil\share\test.lsp", out _, out _, ".lsp"), "Mixed /\\ UNC prefix rejected");
        AssertTrue(!SecurityPolicy.ValidateLocalFilePath(@"\/evil\share\test.lsp", out _, out _, ".lsp"), "Mixed \\/ UNC prefix rejected");

        AssertEqual(409, GatewayProtocol.MapErrorCodeToHttpStatus("autocad_error"), "autocad_error maps to 409");
        AssertEqual(409, GatewayProtocol.MapErrorCodeToHttpStatus("civil_unavailable"), "civil_unavailable maps to 409");

        AssertTrue(SecurityPolicy.IsBlockedSystemVariable("_.SECURELOAD"), "Prefix-normalized _.SECURELOAD blocked");
        AssertTrue(SecurityPolicy.IsBlockedSystemVariable("LOGFILENAME"), "LOGFILENAME blocked");
        AssertTrue(SecurityPolicy.IsValidEntityHandle("8000000000000001"), "Unsigned 64-bit high-bit handle accepted");
        AssertTrue(!SecurityPolicy.IsValidEntityHandle("0000"), "Zero handle rejected");
        AssertTrue(SecurityPolicy.IsSafePlotStyleName("monochrome.ctb"), "Standard CTB accepted");
        AssertTrue(!SecurityPolicy.IsSafePlotStyleName(@"\\attacker\share\evil.ctb"), "UNC plot_style rejected");
        AssertTrue(!SecurityPolicy.IsSafePlotStyleName(@"\"), "Backslash pause plot_style rejected");
        AssertTrue(!SecurityPolicy.IsSafePlotStyleName("Screen 100.ctb", allowSpaces: false), "Space-containing CLI plot_style rejected");
        AssertTrue(SecurityPolicy.IsSafeLayoutName("SHEET-01"), "Valid layout symbol accepted");
        AssertTrue(!SecurityPolicy.IsSafeLayoutName("SHEET 01", allowSpaces: false), "Space-containing CLI layout rejected");
        AssertTrue(!SecurityPolicy.IsSafeLayoutName("?"), "Prompt '?' layout rejected");

        byte[] boundaryHead = Encoding.ASCII.GetBytes("GET /_ping HTTP/1.1\r\nHost: 127.0.0.1:5055\r\n\r\nBODY_TAIL");
        using (var msBoundary = new MemoryStream(boundaryHead))
        {
            HttpHeadReadResult boundaryRes = HttpWireCodec.ReadHeadAsync(msBoundary, maxHeaderBytes: 45).GetAwaiter().GetResult();
            AssertTrue(!boundaryRes.HeaderTooLarge, "Terminated header within maxHeaderBytes admitted");
            AssertEqual("BODY_TAIL", Encoding.ASCII.GetString(boundaryRes.LeftoverBytes), "Chunk bytes past maxHeaderBytes preserved after header terminator");
        }

        string dir = Path.Combine(Path.GetTempPath(), $"acad_sec_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string oversized = new string(' ', (int)InstanceDiscovery.MaxDescriptorBytes + 256);
            string payload = "{\"pid\":4242,\"port\":5055,\"token\":\"tok\",\"started_utc\":\"2026-11-22T00:00:00Z\",\"pad\":\"" + oversized + "\"}";
            File.WriteAllText(Path.Combine(dir, "4242.json"), payload, Encoding.UTF8);
            AssertTrue(InstanceDiscovery.FindNewestLive(dir, _ => true) is null, "Oversized discovery file ignored");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static byte[] ConcatBytes(this byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }
}
