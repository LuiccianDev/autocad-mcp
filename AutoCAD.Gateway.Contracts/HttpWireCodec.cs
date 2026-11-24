using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Result of reading an HTTP/1.x request head from a stream.
/// Carries any body bytes that arrived in the same socket buffer read.
/// </summary>
public readonly record struct HttpHeadReadResult(
    bool Closed,
    bool HeaderTooLarge,
    string HeaderText,
    byte[] LeftoverBytes);

/// <summary>
/// Result of reading and decoding an HTTP request body.
/// </summary>
public readonly record struct HttpBodyReadResult(
    bool IsComplete,
    bool InvalidUtf8,
    string Body);

/// <summary>
/// Shared buffered HTTP/1.x socket framing helpers usable both by the Gateway server and offline wire tests.
/// </summary>
public static class HttpWireCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] HeaderTerminator = [(byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n'];

    /// <summary>
    /// Reads chunks up to <paramref name="maxHeaderBytes"/> until CRLFCRLF terminates the header block,
    /// returning any excess bytes already read from the stream as <see cref="HttpHeadReadResult.LeftoverBytes"/>.
    /// </summary>
    public static async Task<HttpHeadReadResult> ReadHeadAsync(
        Stream stream,
        int maxHeaderBytes = GatewayProtocol.MaxHeaderBytes,
        CancellationToken ct = default)
    {
        using var accumulator = new MemoryStream();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return new HttpHeadReadResult(Closed: true, HeaderTooLarge: false, string.Empty, []);
                }

                int prevLen = (int)accumulator.Length;
                if (prevLen + read > maxHeaderBytes)
                {
                    int allowed = Math.Max(0, maxHeaderBytes - prevLen);
                    if (allowed > 0)
                    {
                        accumulator.Write(chunk, 0, allowed);
                    }

                    byte[] snapshot = accumulator.ToArray();
                    int termCheck = snapshot.AsSpan().IndexOf(HeaderTerminator);
                    if (termCheck < 0)
                    {
                        return new HttpHeadReadResult(Closed: false, HeaderTooLarge: true, string.Empty, []);
                    }

                    int headEnd = termCheck + HeaderTerminator.Length;
                    string text = Encoding.ASCII.GetString(snapshot, 0, headEnd);
                    int snapshotTailLen = snapshot.Length - headEnd;
                    int unreadChunkTailLen = read - allowed;
                    byte[] tail = new byte[snapshotTailLen + unreadChunkTailLen];
                    if (snapshotTailLen > 0)
                    {
                        Buffer.BlockCopy(snapshot, headEnd, tail, 0, snapshotTailLen);
                    }
                    if (unreadChunkTailLen > 0)
                    {
                        Buffer.BlockCopy(chunk, allowed, tail, snapshotTailLen, unreadChunkTailLen);
                    }
                    return new HttpHeadReadResult(false, false, text, tail);
                }

                accumulator.Write(chunk, 0, read);
                byte[] buf = accumulator.GetBuffer();
                int totalLen = (int)accumulator.Length;
                int searchStart = Math.Max(0, prevLen - (HeaderTerminator.Length - 1));
                int rel = buf.AsSpan(searchStart, totalLen - searchStart).IndexOf(HeaderTerminator);
                if (rel >= 0)
                {
                    int headEnd = searchStart + rel + HeaderTerminator.Length;
                    string headerText = Encoding.ASCII.GetString(buf, 0, headEnd);
                    int leftoverLen = totalLen - headEnd;
                    byte[] leftover = leftoverLen > 0 ? buf.AsSpan(headEnd, leftoverLen).ToArray() : [];
                    return new HttpHeadReadResult(false, false, headerText, leftover);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    /// <summary>
    /// Incrementally reads <paramref name="contentLength"/> bytes starting with <paramref name="leftover"/>,
    /// avoiding eager full-size allocations until bytes arrive, and strictly validates UTF-8.
    /// </summary>
    public static async Task<HttpBodyReadResult> ReadBodyAsync(
        Stream stream,
        int contentLength,
        byte[]? leftover = null,
        CancellationToken ct = default)
    {
        if (contentLength <= 0)
        {
            return new HttpBodyReadResult(IsComplete: true, InvalidUtf8: false, string.Empty);
        }

        int initialCapacity = Math.Min(contentLength, 64 * 1024);
        using var bodyStream = new MemoryStream(initialCapacity);

        if (leftover is { Length: > 0 })
        {
            int copy = Math.Min(leftover.Length, contentLength);
            bodyStream.Write(leftover, 0, copy);
        }

        byte[] chunk = ArrayPool<byte>.Shared.Rent(Math.Min(contentLength, 16 * 1024));
        try
        {
            while (bodyStream.Length < contentLength)
            {
                int remaining = (int)(contentLength - bodyStream.Length);
                int toRead = Math.Min(chunk.Length, remaining);
                int read = await stream.ReadAsync(chunk.AsMemory(0, toRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return new HttpBodyReadResult(IsComplete: false, InvalidUtf8: false, string.Empty);
                }

                bodyStream.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        byte[] bytes = bodyStream.GetBuffer();
        int len = (int)bodyStream.Length;
        try
        {
            string decoded = StrictUtf8.GetString(bytes, 0, len);
            return new HttpBodyReadResult(IsComplete: true, InvalidUtf8: false, decoded);
        }
        catch (DecoderFallbackException)
        {
            return new HttpBodyReadResult(IsComplete: true, InvalidUtf8: true, string.Empty);
        }
    }

    /// <summary>
    /// Half-closes the write side and drains bounded unread request bytes so Windows does not
    /// issue TCP RST before the client reads a refusal or 4xx response.
    /// </summary>
    public static async Task FinishRefusalAsync(
        Socket socket,
        Stream stream,
        long maxDrainBytes,
        TimeSpan drainTimeout,
        CancellationToken ct = default)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(drainTimeout);

        byte[] scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long drained = 0;
        try
        {
            while (drained < maxDrainBytes)
            {
                int read = await stream.ReadAsync(scratch, deadline.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                drained += read;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>
    /// Writes an HTTP/1.1 JSON response with strict content headers and no CORS headers.
    /// </summary>
    public static async Task WriteResponseAsync(
        Stream stream,
        int status,
        byte[] payload,
        CancellationToken ct = default)
    {
        string reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            409 => "Conflict",
            413 => "Payload Too Large",
            415 => "Unsupported Media Type",
            422 => "Unprocessable Content",
            431 => "Request Header Fields Too Large",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => "Error",
        };

        byte[] head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
