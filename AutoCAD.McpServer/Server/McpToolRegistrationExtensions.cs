using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.Gateway.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Server;

internal static class McpToolRegistrationExtensions
{
    private static readonly MethodInfo CompoundInvokeMethod =
        typeof(CompoundDomainHandler).GetMethod(nameof(CompoundDomainHandler.InvokeAsync), BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException("CompoundDomainHandler.InvokeAsync could not be reflected.");

    public static IMcpServerBuilder WithConfiguredAutoCADTools(
        this IMcpServerBuilder builder,
        ToolMode mode,
        OutputFormat outputFormat = OutputFormat.Compact,
        IEnumerable<Type>? toolTypes = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var catalog = new AutoCADToolCatalog(toolTypes);

        IReadOnlyList<McpServerTool> tools = mode switch
        {
            ToolMode.Granular => RegisterDirectServicesAndBuildTools(builder.Services, catalog.AllActions, outputFormat),
            ToolMode.Core => RegisterDirectServicesAndBuildTools(builder.Services, catalog.CoreActions, outputFormat),
            ToolMode.Compound => RegisterCompoundServicesAndBuildTools(builder.Services, catalog, outputFormat),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ToolMode value."),
        };

        return builder.WithTools(tools);
    }

    private static IReadOnlyList<McpServerTool> RegisterDirectServicesAndBuildTools(
        IServiceCollection services,
        IReadOnlyList<ToolActionDescriptor> actions,
        OutputFormat outputFormat)
    {
        foreach (ToolActionDescriptor action in actions)
        {
            services.TryAddTransient(action.DeclaringType);
        }

        return BuildDirectTools(actions, outputFormat);
    }

    private static IReadOnlyList<McpServerTool> RegisterCompoundServicesAndBuildTools(
        IServiceCollection services,
        AutoCADToolCatalog catalog,
        OutputFormat outputFormat)
    {
        services.TryAddSingleton(catalog);

        foreach (ToolDomainDescriptor domain in catalog.Domains)
        {
            services.TryAddTransient(domain.ToolType);
        }

        services.TryAddSingleton(sp =>
            new CompoundDispatcher(catalog, t => sp.GetRequiredService(t)));

        return BuildCompoundTools(catalog.Domains, outputFormat);
    }

    private static IReadOnlyList<McpServerTool> BuildDirectTools(
        IReadOnlyList<ToolActionDescriptor> actions,
        OutputFormat outputFormat)
    {
        var tools = new List<McpServerTool>(actions.Count);
        foreach (ToolActionDescriptor action in actions)
        {
            Type declaringType = action.DeclaringType;
            string canonicalName = action.AliasOf ?? action.Name;
            ToolAnnotationSpec hints = ToolAnnotationPolicy.ResolveForTool(canonicalName);
            var options = new McpServerToolCreateOptions
            {
                Name = action.Name,
                Description = action.Description,
                ReadOnly = hints.ReadOnly,
                Destructive = hints.Destructive,
                Idempotent = hints.Idempotent,
                OpenWorld = hints.OpenWorld,
                UseStructuredContent = false,
            };

            McpServerTool inner = McpServerTool.Create(
                action.Method,
                req => req.Services!.GetRequiredService(declaringType),
                options);
            tools.Add(new AdaptingMcpServerTool(inner, outputFormat));
        }

        return tools;
    }

    private static IReadOnlyList<McpServerTool> BuildCompoundTools(
        IReadOnlyList<ToolDomainDescriptor> domains,
        OutputFormat outputFormat)
    {
        var tools = new List<McpServerTool>(domains.Count);
        foreach (ToolDomainDescriptor domain in domains)
        {
            string domainName = domain.Domain;
            string actionsCsv = string.Join(", ", domain.CanonicalActionsByName.Keys);
            ToolAnnotationSpec hints = ToolAnnotationPolicy.ResolveForCompoundDomain(domain.CanonicalActionsByName.Keys);
            var options = new McpServerToolCreateOptions
            {
                Name = domain.CompoundToolName,
                Description =
                    $"{domain.Summary} " +
                    $"Available actions: {actionsCsv}. " +
                    "Call with action='help' (default, optional params={\"action\":\"<name>\"}) to inspect parameter schemas.",
                ReadOnly = hints.ReadOnly,
                Destructive = hints.Destructive,
                Idempotent = hints.Idempotent,
                OpenWorld = hints.OpenWorld,
                UseStructuredContent = false,
            };

            McpServerTool inner = McpServerTool.Create(
                CompoundInvokeMethod,
                req => new CompoundDomainHandler(req.Services!.GetRequiredService<CompoundDispatcher>(), domainName),
                options);
            tools.Add(new AdaptingMcpServerTool(inner, outputFormat));
        }

        return tools;
    }

    private sealed class CompoundDomainHandler
    {
        private readonly CompoundDispatcher _dispatcher;
        private readonly string _domain;

        public CompoundDomainHandler(CompoundDispatcher dispatcher, string domain)
        {
            _dispatcher = dispatcher;
            _domain = domain;
        }

        public Task<string> InvokeAsync(
            [Description("Action name within this domain, or 'help' (default) to inspect available actions and schemas.")]
            string action = "help",
            [Description("Action parameters as a JSON object (or JSON-encoded object string). For action='help', optionally pass {\"action\": \"<name>\"} for full parameter schema.")]
            JsonElement? @params = null,
            CancellationToken cancellationToken = default) =>
            _dispatcher.DispatchAsync(_domain, action, @params, cancellationToken);
    }
}
