using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Server;

/// <summary>
/// Wraps an SDK-generated <see cref="McpServerTool"/> so every invocation adapts its JSON payload
/// into native <see cref="CallToolResult"/> (<c>IsError</c>, <see cref="ImageContentBlock"/>,
/// <c>StructuredContent</c>, and compact text).
/// </summary>
internal sealed class AdaptingMcpServerTool : McpServerTool
{
    private readonly McpServerTool _inner;
    private readonly OutputFormat _outputFormat;

    public AdaptingMcpServerTool(McpServerTool inner, OutputFormat outputFormat)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _outputFormat = outputFormat;
    }

    public override Tool ProtocolTool => _inner.ProtocolTool;

    public override IReadOnlyList<object> Metadata => _inner.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        CallToolResult raw = await _inner.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        if (raw.IsError == true)
        {
            return raw;
        }

        string? rawText = null;
        if (raw.Content is not null)
        {
            foreach (ContentBlock block in raw.Content)
            {
                if (block is TextContentBlock textBlock)
                {
                    rawText = textBlock.Text;
                    break;
                }
            }
        }

        return rawText is null
            ? raw
            : McpResultAdapter.Adapt(_inner.ProtocolTool.Name, rawText, _outputFormat);
    }
}
