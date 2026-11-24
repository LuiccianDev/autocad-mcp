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
        IEnumerable<Type>? toolTypes = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var catalog = new AutoCADToolCatalog(toolTypes);

        IReadOnlyList<McpServerTool> tools = mode switch
        {
            ToolMode.Granular => RegisterDirectServicesAndBuildTools(builder.Services, catalog.AllActions),
            ToolMode.Core => RegisterDirectServicesAndBuildTools(builder.Services, catalog.CoreActions),
            ToolMode.Compound => RegisterCompoundServicesAndBuildTools(builder.Services, catalog),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ToolMode value."),
        };

        return builder.WithTools(tools);
    }

    private static IReadOnlyList<McpServerTool> RegisterDirectServicesAndBuildTools(
        IServiceCollection services,
        IReadOnlyList<ToolActionDescriptor> actions)
    {
        foreach (ToolActionDescriptor action in actions)
        {
            services.TryAddTransient(action.DeclaringType);
        }

        return BuildDirectTools(actions);
    }

    private static IReadOnlyList<McpServerTool> RegisterCompoundServicesAndBuildTools(
        IServiceCollection services,
        AutoCADToolCatalog catalog)
    {
        services.TryAddSingleton(catalog);

        foreach (ToolDomainDescriptor domain in catalog.Domains)
        {
            services.TryAddTransient(domain.ToolType);
        }

        services.TryAddSingleton(sp =>
            new CompoundDispatcher(catalog, t => sp.GetRequiredService(t)));

        return BuildCompoundTools(catalog.Domains);
    }

    private static IReadOnlyList<McpServerTool> BuildDirectTools(IReadOnlyList<ToolActionDescriptor> actions)
    {
        var tools = new List<McpServerTool>(actions.Count);
        foreach (ToolActionDescriptor action in actions)
        {
            Type declaringType = action.DeclaringType;
            var options = new McpServerToolCreateOptions
            {
                Name = action.Name,
                Description = action.Description,
            };

            tools.Add(McpServerTool.Create(
                action.Method,
                req => req.Services!.GetRequiredService(declaringType),
                options));
        }

        return tools;
    }

    private static IReadOnlyList<McpServerTool> BuildCompoundTools(IReadOnlyList<ToolDomainDescriptor> domains)
    {
        var tools = new List<McpServerTool>(domains.Count);
        foreach (ToolDomainDescriptor domain in domains)
        {
            string domainName = domain.Domain;
            string actionsCsv = string.Join(", ", domain.CanonicalActionsByName.Keys);
            var options = new McpServerToolCreateOptions
            {
                Name = domain.CompoundToolName,
                Description =
                    $"{domain.Summary} " +
                    $"Available actions: {actionsCsv}. " +
                    "Call with action='help' (default, optional params={\"action\":\"<name>\"}) to inspect parameter schemas.",
            };

            tools.Add(McpServerTool.Create(
                CompoundInvokeMethod,
                req => new CompoundDomainHandler(req.Services!.GetRequiredService<CompoundDispatcher>(), domainName),
                options));
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
