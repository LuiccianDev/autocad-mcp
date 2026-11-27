using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Evaluates arbitrary AutoLISP synchronously by exposing managed [LispFunction] callbacks
/// invoked from AutoCAD's command loop and converting ResultBuffer return values to JSON.
/// </summary>
public static class LispBridge
{
    private const int CommandStateWaitMs = 1000;

    private sealed class PendingRequest(string source)
    {
        internal readonly string Source = source;
        internal readonly TaskCompletionSource<object?> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly ConcurrentDictionary<string, PendingRequest> Requests = new();
    private static readonly SemaphoreSlim EvaluationGate = new(1, 1);

    /// <summary>Supplies wrapped AutoLISP source text to the active AutoCAD evaluation wrapper.</summary>
    [LispFunction("acad-mcp-source")]
    public static object? GetSource(ResultBuffer? args)
    {
        string token = args?.AsArray().FirstOrDefault().Value?.ToString() ?? string.Empty;
        return Requests.TryGetValue(token, out PendingRequest? pending) ? pending.Source : "nil";
    }

    /// <summary>Captures the successful return value of an AutoLISP evaluation.</summary>
    [LispFunction("acad-mcp-ok")]
    public static object? CaptureOk(ResultBuffer? args)
    {
        TypedValue[] values = args?.AsArray() ?? [];
        if (values.Length == 0)
        {
            return null;
        }

        string token = values[0].Value?.ToString() ?? string.Empty;
        if (Requests.TryGetValue(token, out PendingRequest? pending))
        {
            try
            {
                pending.Completion.TrySetResult(ConvertResult(values.Skip(1).ToArray()));
            }
            catch (System.Exception ex)
            {
                pending.Completion.TrySetException(ex);
            }
        }

        return null;
    }

    /// <summary>Captures an AutoLISP runtime or reader error message.</summary>
    [LispFunction("acad-mcp-err")]
    public static object? CaptureErr(ResultBuffer? args)
    {
        TypedValue[] values = args?.AsArray() ?? [];
        if (values.Length == 0)
        {
            return null;
        }

        string token = values[0].Value?.ToString() ?? string.Empty;
        string message = values.Length > 1
            ? values[1].Value?.ToString() ?? "Unknown AutoLISP error."
            : "Unknown AutoLISP error.";

        if (Requests.TryGetValue(token, out PendingRequest? pending))
        {
            pending.Completion.TrySetException(new AcadGatewayException("lisp_error", message));
        }

        return null;
    }

    /// <summary>Structured result from a synchronous AutoLISP evaluation or file load.</summary>
    public sealed record LispEvalResult(
        object? Value,
        string Document,
        CommandLineState? CommandLine,
        double DurationMs,
        string? FilePath = null);

    /// <summary>
    /// Validates and loads an external <c>.lsp</c> file synchronously with syntax pre-check and CMDECHO suppression.
    /// </summary>
    public static async Task<LispEvalResult> LoadFileAsync(
        string filePath,
        int timeoutMs = GatewayProtocol.DefaultLispLoadTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityPolicy.ValidateLocalFilePath(filePath, out string normPath, out string? pathErr, ".lsp"))
        {
            throw new AcadGatewayException("invalid_path", pathErr ?? "Invalid AutoLISP file path.");
        }

        DatabaseHelpers.EnsureNoReparsePoints(normPath);
        if (!File.Exists(normPath))
        {
            throw new AcadGatewayException("not_found", $"AutoLISP file not found: '{normPath}'.");
        }

        long fileLength = new FileInfo(normPath).Length;
        if (fileLength > GatewayProtocol.MaxBodyBytes)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"AutoLISP file '{normPath}' ({fileLength} bytes) exceeds maximum size ({GatewayProtocol.MaxBodyBytes} bytes).");
        }

        string source = await File.ReadAllTextAsync(normPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        if (source.Length > 0 && source[0] == '\uFEFF')
        {
            source = source[1..];
        }

        string? syntaxError = LispSyntax.Validate(source);
        if (syntaxError is not null)
        {
            throw new AcadGatewayException("bad_argument", $"Syntax error in '{normPath}': {syntaxError}");
        }

        string escaped = EscapeLispString(normPath.Replace('\\', '/'));
        LispEvalResult res = await EvalAsync($"(load {escaped})", timeoutMs, cancellationToken).ConfigureAwait(false);
        return res with { FilePath = normPath };
    }

    /// <summary>
    /// Evaluates AutoLISP code inside the active drawing and returns its converted JSON value
    /// together with post-execution command-line state and measured duration.
    /// </summary>
    /// <param name="code">AutoLISP code snippet to evaluate inside a lexical <c>(progn ...)</c>.</param>
    /// <param name="timeoutMs">Requested timeout in milliseconds (clamped to safe bounds).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static async Task<LispEvalResult> EvalAsync(
        string code,
        int timeoutMs = GatewayProtocol.DefaultLispTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        long startTick = Stopwatch.GetTimestamp();
        string? syntaxError = LispSyntax.Validate(code);
        if (syntaxError is not null)
        {
            throw new AcadGatewayException("bad_argument", syntaxError);
        }

        int clampedTimeout = GatewayProtocol.ClampLispTimeoutMs(timeoutMs);
        // Wrap in (progn ...) with trailing newline so line comments cannot swallow the closing paren.
        string wrappedSource = $"(progn\n{code}\n)";

        bool acquired = await EvaluationGate.WaitAsync(TimeSpan.FromMilliseconds(clampedTimeout), cancellationToken)
            .ConfigureAwait(false);
        if (!acquired)
        {
            throw new AcadGatewayException(
                "busy",
                $"Another AutoLISP evaluation did not finish within {clampedTimeout} ms.");
        }

        string token = "amcp" + Guid.NewGuid().ToString("N");
        var pending = new PendingRequest(wrappedSource);
        Requests[token] = pending;
        string documentName = string.Empty;
        long deadlineTick = Environment.TickCount64 + clampedTimeout;

        try
        {
            for (int attempt = 0; ; attempt++)
            {
                int remainingDispatchMs = Math.Max(250, (int)Math.Min(clampedTimeout, deadlineTick - Environment.TickCount64));
                try
                {
                    await MainThread.InvokeAsync(() =>
                    {
                        Document doc = Acad.ActiveDocument;
                        documentName = doc.Name;
                        DocumentSession session = DocumentSessionManager.GetOrCreate(doc);
                        Acad.ValidateWritable(doc, session, requireIdle: true);

                        // Safe to prepend ^C^C only after quiescence is verified to clear unsubmitted prompt text.
                        doc.SendStringToExecute("\x03\x03" + BuildDispatchExpression(token), true, false, false);
                    }, remainingDispatchMs, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (AcadGatewayException ex) when (
                    ex.Code == "busy" &&
                    string.Equals(ex.Detail, "SPACEMOUSEACTION", StringComparison.OrdinalIgnoreCase) &&
                    attempt < 10 &&
                    Environment.TickCount64 + 200 < deadlineTick)
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
            }

            int remainingEvalMs = Math.Max(250, (int)(deadlineTick - Environment.TickCount64));
            object? value;
            try
            {
                value = await pending.Completion.Task
                    .WaitAsync(TimeSpan.FromMilliseconds(remainingEvalMs), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                CommandLineState? leftover = await ReadCommandLineAsync().ConfigureAwait(false);
                await TryCancelActiveCommandAsync().ConfigureAwait(false);
                string hint = leftover?.CmdActive is int ca && (ca & 8) != 0
                    ? "A modal dialog (for example SECURELOAD trust prompt or file dialog) is blocking AutoCAD."
                    : (leftover?.Hint ?? "A modal dialog or interactive prompt may still be open in AutoCAD.");
                throw new AcadGatewayException(
                    "lisp_timeout",
                    $"AutoLISP evaluation did not return within {clampedTimeout} ms.",
                    hint);
            }
            catch (AcadGatewayException ex) when (ex.Code == "lisp_error")
            {
                CommandLineState? leftover = await ReadCommandLineAsync().ConfigureAwait(false);
                throw new AcadGatewayException(ex.Code, ex.Message, leftover?.Hint);
            }

            CommandLineState? commandLine = await ReadCommandLineAsync().ConfigureAwait(false);
            double durationMs = Stopwatch.GetElapsedTime(startTick).TotalMilliseconds;
            return new LispEvalResult(value, documentName, commandLine, Math.Round(durationMs, 2));
        }
        finally
        {
            Requests.TryRemove(token, out _);
            EvaluationGate.Release();
        }
    }

    private static string EscapeLispString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n':
                case '\r': sb.Append(' '); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static async Task TryCancelActiveCommandAsync()
    {
        try
        {
            await MainThread.InvokeAsync(() =>
            {
                Acad.ActiveDocument.SendStringToExecute("\x03\x03", true, false, false);
            }, 500).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Best-effort AutoLISP timeout cancellation failed: {0}", ex.Message);
        }
    }

    private static async Task<CommandLineState?> ReadCommandLineAsync()
    {
        try
        {
            var (active, names) = await MainThread.InvokeAsync(Acad.ReadCommandState, CommandStateWaitMs)
                .ConfigureAwait(false);
            return CommandLineState.FromSystemVariables(active, names);
        }
        catch (AcadGatewayException ex) when (ex.Code is "timeout" or "gateway_stopped")
        {
            return CommandLineState.NotRead(CommandStateWaitMs);
        }
    }

    private static string BuildDispatchExpression(string token) =>
        "(progn (vl-load-com)" +
        "((lambda (*error* _amcp_ce _amcp_v _amcp_ok)" +
        "(setq _amcp_ce (getvar \"CMDECHO\"))" +
        "(setvar \"CMDECHO\" 0)" +
        $"(setq *error* (function (lambda (msg) (setvar \"CMDECHO\" _amcp_ce) (acad-mcp-err \"{token}\" (if msg (vl-princ-to-string msg) \"AutoLISP error\")) (princ))))" +
        $"(setq _amcp_v (vl-catch-all-apply (function (lambda () (eval (read (acad-mcp-source \"{token}\")))))))" +
        "(setvar \"CMDECHO\" _amcp_ce)" +
        "(if (vl-catch-all-error-p _amcp_v)" +
        $"(acad-mcp-err \"{token}\" (vl-catch-all-error-message _amcp_v))" +
        "(progn " +
        $"(setq _amcp_ok (vl-catch-all-apply (function (lambda () (acad-mcp-ok \"{token}\" _amcp_v)))))" +
        "(if (vl-catch-all-error-p _amcp_ok)" +
        $"(acad-mcp-ok \"{token}\" (vl-princ-to-string _amcp_v))))))" +
        "*error* nil nil nil)(princ))\n";

    private static object? ConvertResult(TypedValue[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        int index = 0;
        return ConvertOne(values, ref index);
    }

    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    private static object? ConvertOne(TypedValue[] values, ref int index)
    {
        if (index >= values.Length)
        {
            return null;
        }

        TypedValue tv = values[index];
        switch ((LispDataType)tv.TypeCode)
        {
            case LispDataType.ListBegin:
                index++;
                var list = new List<object?>();
                while (index < values.Length && (LispDataType)values[index].TypeCode != LispDataType.ListEnd)
                {
                    if ((LispDataType)values[index].TypeCode == LispDataType.DottedPair)
                    {
                        index++;
                        continue;
                    }

                    list.Add(ConvertOne(values, ref index));
                }

                if (index < values.Length)
                {
                    index++;
                }

                return list;

            case LispDataType.Nil:
            case LispDataType.None:
            case LispDataType.Void:
            case LispDataType.ListEnd:
                index++;
                return null;

            case LispDataType.T_atom:
                index++;
                return true;

            case LispDataType.Text:
                index++;
                return tv.Value?.ToString();

            case LispDataType.Int16:
            case LispDataType.Int32:
                index++;
                return Convert.ToInt32(tv.Value, CultureInfo.InvariantCulture);

            case LispDataType.Double:
            case LispDataType.Angle:
            case LispDataType.Orientation:
                index++;
                return FiniteOrNull(Convert.ToDouble(tv.Value, CultureInfo.InvariantCulture));

            case LispDataType.Point2d:
                index++;
                return tv.Value is Point2d p2
                    ? new double?[] { FiniteOrNull(p2.X), FiniteOrNull(p2.Y) }
                    : null;

            case LispDataType.Point3d:
                index++;
                return tv.Value is Point3d p3
                    ? new double?[] { FiniteOrNull(p3.X), FiniteOrNull(p3.Y), FiniteOrNull(p3.Z) }
                    : null;

            case LispDataType.ObjectId:
                index++;
                try
                {
                    return tv.Value is ObjectId id && !id.IsNull ? id.Handle.ToString() : null;
                }
                catch
                {
                    return null;
                }

            default:
                index++;
                return tv.Value?.ToString();
        }
    }
}
