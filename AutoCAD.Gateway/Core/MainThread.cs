using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Persistent main-thread dispatcher draining queued delegates during AutoCAD <c>Application.Idle</c>
/// with a bounded per-tick budget and optional command-context hopping.
/// </summary>
public static class MainThread
{
    private const int DrainBudget = 8;

    private readonly record struct WorkItem(Action Execute, Action Cancel);

    private static readonly ConcurrentQueue<WorkItem> Queue = new();
    private static readonly Lock LifecycleLock = new();
    private static bool _hooked;
    private static bool _stopped;

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Starts or re-enables listening to <c>Application.Idle</c> if not already active.</summary>
    public static void Start()
    {
        lock (LifecycleLock)
        {
            _stopped = false;
            if (_hooked)
            {
                return;
            }

            AcApp.Idle += OnIdle;
            _hooked = true;
        }
    }

    /// <summary>Unhooks <c>Application.Idle</c> and cancels any queued pending items so callers never hang.</summary>
    public static void Stop()
    {
        lock (LifecycleLock)
        {
            _stopped = true;
            if (_hooked)
            {
                try
                {
                    AcApp.Idle -= OnIdle;
                }
                catch
                {
                    // Ignore unhook errors during AutoCAD teardown
                }

                _hooked = false;
            }
        }

        while (Queue.TryDequeue(out WorkItem orphan))
        {
            try
            {
                orphan.Cancel();
            }
            catch
            {
                // Work item owns its own TaskCompletionSource error handling
            }
        }
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        int budget = DrainBudget;
        while (budget-- > 0 && Queue.TryDequeue(out WorkItem item))
        {
            try
            {
                item.Execute();
            }
            catch
            {
                // Exceptions inside WorkItem are routed to its TaskCompletionSource
            }
        }

        if (!Queue.IsEmpty)
        {
            WakeMainWindowLoop();
        }
    }

    internal static void WakeMainWindowLoop()
    {
        try
        {
            nint hwnd = AcApp.MainWindow?.Handle ?? 0;
            if (hwnd != 0)
            {
                PostMessageW(hwnd, 0, 0, 0);
            }
        }
        catch
        {
            // Best-effort wakeup during message pump drain
        }
    }

    /// <summary>
    /// Enqueues <paramref name="func"/> onto AutoCAD's main thread and awaits completion with timeout.
    /// </summary>
    /// <param name="func">Delegate to execute on AutoCAD's UI thread.</param>
    /// <param name="timeoutMs">Positive timeout in milliseconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static Task<T> InvokeAsync<T>(
        Func<T> func,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return EnqueueCoreAsync<T>(
            tcs =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            $"AutoCAD main thread did not process the request within {timeoutMs} ms. " +
            "A modal dialog or interactive command may be blocking AutoCAD.",
            "AutoCAD Gateway main-thread dispatcher stopped before request ran.",
            timeoutMs,
            cancellationToken);
    }

    /// <summary>Enqueues an <see cref="Action"/> onto AutoCAD's main thread.</summary>
    /// <param name="action">Action to execute on AutoCAD's UI thread.</param>
    /// <param name="timeoutMs">Positive timeout in milliseconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static Task InvokeAsync(
        Action action,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InvokeAsync<object?>(() => { action(); return null; }, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="func"/> inside AutoCAD's document/command context
    /// (required for <c>Editor.Command</c> or command-loop operations).
    /// </summary>
    /// <param name="func">Delegate to run in document command context.</param>
    /// <param name="preflight">Optional main-thread check executed immediately before entering command context.</param>
    /// <param name="timeoutMs">Positive timeout in milliseconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static Task<T> InCommandContextAsync<T>(
        Func<T> func,
        Action? preflight = null,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return EnqueueCoreAsync<T>(
            tcs =>
            {
                try
                {
                    var docs = AcApp.DocumentManager;
                    preflight?.Invoke();

                    if (!docs.IsApplicationContext)
                    {
                        tcs.TrySetResult(func());
                        return;
                    }

                    _ = DispatchInCommandContextAsync(docs, tcs, func, cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            $"AutoCAD command context did not complete within {timeoutMs} ms.",
            "AutoCAD Gateway stopped before command context ran.",
            timeoutMs,
            cancellationToken);
    }

    private static async Task DispatchInCommandContextAsync<T>(
        Autodesk.AutoCAD.ApplicationServices.DocumentCollection docs,
        TaskCompletionSource<T> tcs,
        Func<T> func,
        CancellationToken cancellationToken)
    {
        try
        {
            await docs.ExecuteInCommandContextAsync(_ =>
            {
                if (tcs.Task.IsCompleted || cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return Task.CompletedTask;
                }

                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }

                return Task.CompletedTask;
            }, null);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    private static async Task<T> EnqueueCoreAsync<T>(
        Action<TaskCompletionSource<T>> executeBody,
        string timeoutMessage,
        string stoppedMessage,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeoutMs, 0);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(
            Execute: () =>
            {
                if (tcs.Task.IsCompleted || cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                executeBody(tcs);
            },
            Cancel: () => tcs.TrySetException(new AcadGatewayException("gateway_stopped", stoppedMessage)));

        lock (LifecycleLock)
        {
            if (_stopped)
            {
                throw new AcadGatewayException("gateway_stopped", stoppedMessage);
            }

            if (!_hooked)
            {
                AcApp.Idle += OnIdle;
                _hooked = true;
            }

            Queue.Enqueue(item);
        }

        WakeMainWindowLoop();

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Cancel the TCS so a stalled callback that later runs cannot execute ghost mutations.
            tcs.TrySetCanceled();
            throw new AcadGatewayException("timeout", timeoutMessage);
        }
    }
}
