using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using AutoCAD.McpServer.Tools;
using ModelContextProtocol.Server;

namespace AutoCAD.McpServer.Server;

internal sealed class AutoCADToolCatalog
{
    private readonly Dictionary<string, ToolDomainDescriptor> _domainsByName;
    private readonly Dictionary<string, ToolActionDescriptor> _actionsByName;

    public static IReadOnlyList<Type> DefaultToolTypes { get; } =
    [
        typeof(ContextTools),
        typeof(EntityTools),
        typeof(DraftingTools),
        typeof(DrawingTools),
        typeof(LayerTools),
        typeof(DocumentTools),
        typeof(MeasurementTools),
        typeof(PlottingTools),
        typeof(Civil3DTools),
        typeof(ExecutionTools),
    ];

    public AutoCADToolCatalog(IEnumerable<Type>? toolTypes = null)
    {
        IReadOnlyList<Type> types = (toolTypes ?? DefaultToolTypes).ToArray();
        if (types.Count == 0)
        {
            throw new InvalidOperationException("At least one MCP tool type is required to build the catalog.");
        }

        _domainsByName = new Dictionary<string, ToolDomainDescriptor>(StringComparer.OrdinalIgnoreCase);
        _actionsByName = new Dictionary<string, ToolActionDescriptor>(StringComparer.Ordinal);

        var domainList = new List<ToolDomainDescriptor>(types.Count);
        var allActions = new List<ToolActionDescriptor>();
        var canonicalActions = new List<ToolActionDescriptor>();
        var coreActions = new List<ToolActionDescriptor>();
        var compoundToolNames = new HashSet<string>(StringComparer.Ordinal);

        var nullabilityContext = new NullabilityInfoContext();

        foreach (Type toolType in types)
        {
            ToolDomainAttribute? domainAttr = toolType.GetCustomAttribute<ToolDomainAttribute>();
            if (domainAttr is null)
            {
                throw new InvalidOperationException($"Tool type '{toolType.FullName}' is missing [ToolDomain].");
            }

            if (string.IsNullOrWhiteSpace(domainAttr.Domain) || string.IsNullOrWhiteSpace(domainAttr.CompoundToolName))
            {
                throw new InvalidOperationException($"Tool type '{toolType.FullName}' has an empty [ToolDomain] name.");
            }

            if (_domainsByName.ContainsKey(domainAttr.Domain))
            {
                throw new InvalidOperationException($"Duplicate [ToolDomain] '{domainAttr.Domain}'.");
            }

            if (!compoundToolNames.Add(domainAttr.CompoundToolName))
            {
                throw new InvalidOperationException($"Duplicate compound tool name '{domainAttr.CompoundToolName}'.");
            }

            var domainActions = new List<ToolActionDescriptor>();
            var domainCanonical = new Dictionary<string, ToolActionDescriptor>(StringComparer.Ordinal);
            var domainAliases = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (MethodInfo method in toolType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(m => m.MetadataToken))
            {
                McpServerToolAttribute? mcpAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (mcpAttr is null)
                {
                    continue;
                }

                string toolName = string.IsNullOrWhiteSpace(mcpAttr.Name)
                    ? throw new InvalidOperationException($"Tool method '{toolType.Name}.{method.Name}' has empty [McpServerTool(Name)].")
                    : mcpAttr.Name.Trim();

                if (_actionsByName.ContainsKey(toolName))
                {
                    throw new InvalidOperationException($"Duplicate MCP tool name '{toolName}'.");
                }

                string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                ToolExposureAttribute? exposure = method.GetCustomAttribute<ToolExposureAttribute>();
                bool isCore = exposure?.Core == true;
                string? aliasOf = string.IsNullOrWhiteSpace(exposure?.AliasOf) ? null : exposure.AliasOf.Trim();

                if (isCore && aliasOf is not null)
                {
                    throw new InvalidOperationException($"Alias tool '{toolName}' cannot be marked Core = true.");
                }

                ToolActionDescriptor descriptor = BuildActionDescriptor(
                    toolName,
                    domainAttr.Domain,
                    domainAttr.CompoundToolName,
                    description,
                    isCore,
                    aliasOf,
                    toolType,
                    method,
                    nullabilityContext);

                _actionsByName.Add(toolName, descriptor);
                allActions.Add(descriptor);
                domainActions.Add(descriptor);

                if (aliasOf is null)
                {
                    canonicalActions.Add(descriptor);
                    domainCanonical.Add(toolName, descriptor);
                }
                else
                {
                    domainAliases.Add(toolName, aliasOf);
                }

                if (isCore)
                {
                    coreActions.Add(descriptor);
                }
            }

            if (domainActions.Count == 0)
            {
                throw new InvalidOperationException($"Tool type '{toolType.FullName}' declared no [McpServerTool] methods.");
            }

            var domainDesc = new ToolDomainDescriptor(
                domainAttr.Domain,
                domainAttr.CompoundToolName,
                domainAttr.Summary,
                toolType,
                domainActions,
                domainCanonical,
                domainAliases);

            _domainsByName.Add(domainAttr.Domain, domainDesc);
            domainList.Add(domainDesc);
        }

        ValidateAliasTargets(allActions);

        Domains = domainList;
        AllActions = allActions;
        CanonicalActions = canonicalActions;
        CoreActions = coreActions;
    }

    public IReadOnlyList<ToolDomainDescriptor> Domains { get; }

    public IReadOnlyList<ToolActionDescriptor> AllActions { get; }

    public IReadOnlyList<ToolActionDescriptor> CanonicalActions { get; }

    public IReadOnlyList<ToolActionDescriptor> CoreActions { get; }

    public bool TryGetDomain(string domain, out ToolDomainDescriptor? descriptor) =>
        _domainsByName.TryGetValue(domain, out descriptor);

    public bool TryGetAction(string actionName, out ToolActionDescriptor? descriptor) =>
        _actionsByName.TryGetValue(actionName, out descriptor);

    internal static string ToSnakeCase(string clrName)
    {
        if (string.IsNullOrEmpty(clrName))
        {
            return clrName;
        }

        var sb = new StringBuilder(clrName.Length + 8);
        for (int i = 0; i < clrName.Length; i++)
        {
            char c = clrName[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(clrName[i - 1]) || char.IsDigit(clrName[i - 1])))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static ToolActionDescriptor BuildActionDescriptor(
        string toolName,
        string domain,
        string compoundToolName,
        string description,
        bool isCore,
        string? aliasOf,
        Type declaringType,
        MethodInfo method,
        NullabilityInfoContext nullabilityContext)
    {
        ParameterInfo[] clrParams = method.GetParameters();
        var parameters = new List<ToolParameterDescriptor>();
        var byAcceptedName = new Dictionary<string, ToolParameterDescriptor>(StringComparer.Ordinal);
        var requiredCanonical = new List<string>();
        var optionalCanonical = new List<string>();
        int? cancellationTokenPosition = null;

        int position = 0;
        foreach (ParameterInfo clrParam in clrParams)
        {
            if (clrParam.ParameterType == typeof(CancellationToken))
            {
                if (cancellationTokenPosition.HasValue)
                {
                    throw new InvalidOperationException($"Multiple CancellationToken parameters on '{toolName}'.");
                }

                cancellationTokenPosition = clrParam.Position;
                continue;
            }

            string clrName = clrParam.Name ?? throw new InvalidOperationException($"Unnamed parameter on '{toolName}'.");
            string rawSnake = ToSnakeCase(clrName);
            CompoundParamAttribute? paramMeta = clrParam.GetCustomAttribute<CompoundParamAttribute>();
            string canonicalName = string.IsNullOrWhiteSpace(paramMeta?.CanonicalName)
                ? rawSnake
                : paramMeta.CanonicalName.Trim();
            StructuredJsonKind structuredJson = paramMeta?.StructuredJson ?? StructuredJsonKind.None;

            bool isRequired = !clrParam.HasDefaultValue;
            bool allowsNull = Nullable.GetUnderlyingType(clrParam.ParameterType) is not null
                || (!clrParam.ParameterType.IsValueType && nullabilityContext.Create(clrParam).ReadState == NullabilityState.Nullable);

            var paramDesc = new ToolParameterDescriptor(
                position,
                clrParam,
                clrName,
                rawSnake,
                canonicalName,
                isRequired,
                allowsNull,
                structuredJson);

            parameters.Add(paramDesc);
            RegisterAcceptedName(byAcceptedName, canonicalName, paramDesc, toolName);
            RegisterAcceptedName(byAcceptedName, rawSnake, paramDesc, toolName);
            RegisterAcceptedName(byAcceptedName, clrName, paramDesc, toolName);

            if (isRequired)
            {
                requiredCanonical.Add(canonicalName);
            }
            else
            {
                optionalCanonical.Add(canonicalName);
            }

            position++;
        }

        var lazyCompoundSchema = new Lazy<JsonElement>(
            () => BuildCompoundInputSchema(toolName, description, method, parameters, requiredCanonical),
            LazyThreadSafetyMode.ExecutionAndPublication);

        return new ToolActionDescriptor(
            toolName,
            domain,
            compoundToolName,
            description,
            isCore,
            aliasOf,
            declaringType,
            method,
            clrParams.Length,
            cancellationTokenPosition,
            lazyCompoundSchema,
            parameters,
            byAcceptedName,
            requiredCanonical,
            optionalCanonical);
    }

    private static JsonElement BuildCompoundInputSchema(
        string toolName,
        string description,
        MethodInfo method,
        IReadOnlyList<ToolParameterDescriptor> parameters,
        IReadOnlyList<string> requiredCanonical)
    {
        // Pass a throwing factory because McpServerTool.Create rejects target: null on instance methods;
        // the probe is created lazily only when compound help inspects an action schema.
        McpServerTool probe = McpServerTool.Create(
            method,
            static _ => throw new InvalidOperationException("Catalog schema probe cannot be invoked."),
            new McpServerToolCreateOptions
            {
                Name = toolName,
                Description = description,
            });

        JsonElement rawSchema = probe.ProtocolTool.InputSchema;
        var rawProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (rawSchema.TryGetProperty("properties", out JsonElement propsElement) && propsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in propsElement.EnumerateObject())
            {
                rawProperties[prop.Name] = prop.Value;
            }
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            foreach (ToolParameterDescriptor paramDesc in parameters)
            {
                writer.WritePropertyName(paramDesc.CanonicalName);
                bool hasRaw = rawProperties.TryGetValue(paramDesc.ClrName, out JsonElement rawProp);
                if (paramDesc.StructuredJson == StructuredJsonKind.None)
                {
                    if (hasRaw)
                    {
                        rawProp.WriteTo(writer);
                    }
                    else
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "string");
                        writer.WriteEndObject();
                    }
                }
                else
                {
                    WriteStructuredPropertySchema(writer, hasRaw ? rawProp : null, paramDesc);
                }
            }

            writer.WriteEndObject();

            if (requiredCanonical.Count > 0)
            {
                writer.WritePropertyName("required");
                writer.WriteStartArray();
                foreach (string req in requiredCanonical)
                {
                    writer.WriteStringValue(req);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        return doc.RootElement.Clone();
    }

    private static void WriteStructuredPropertySchema(
        Utf8JsonWriter writer,
        JsonElement? rawProp,
        ToolParameterDescriptor paramDesc)
    {
        writer.WriteStartObject();

        if (rawProp is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (JsonProperty prop in obj.EnumerateObject())
            {
                if (prop.Name is "type" or "anyOf")
                {
                    continue;
                }

                prop.WriteTo(writer);
            }
        }

        string structuredType = paramDesc.StructuredJson switch
        {
            StructuredJsonKind.Array => "array",
            StructuredJsonKind.Object => "object",
            _ => throw new ArgumentOutOfRangeException(nameof(paramDesc), paramDesc.StructuredJson, "Expected structured JSON kind."),
        };

        writer.WritePropertyName("anyOf");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("type", structuredType);
        writer.WriteEndObject();
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteEndObject();
        if (paramDesc.AllowsNull)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "null");
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void RegisterAcceptedName(
        Dictionary<string, ToolParameterDescriptor> map,
        string key,
        ToolParameterDescriptor descriptor,
        string toolName)
    {
        if (map.TryGetValue(key, out ToolParameterDescriptor? existing) && !ReferenceEquals(existing, descriptor))
        {
            throw new InvalidOperationException($"Parameter name collision '{key}' on tool '{toolName}'.");
        }

        map[key] = descriptor;
    }

    private void ValidateAliasTargets(IReadOnlyList<ToolActionDescriptor> allActions)
    {
        foreach (ToolActionDescriptor action in allActions)
        {
            if (action.AliasOf is null)
            {
                continue;
            }

            if (!_actionsByName.TryGetValue(action.AliasOf, out ToolActionDescriptor? target))
            {
                throw new InvalidOperationException($"Alias '{action.Name}' points to unknown tool '{action.AliasOf}'.");
            }

            if (target.IsAlias)
            {
                throw new InvalidOperationException($"Alias '{action.Name}' cannot chain to another alias '{target.Name}'.");
            }

            if (!string.Equals(target.Domain, action.Domain, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Alias '{action.Name}' in domain '{action.Domain}' cannot target cross-domain tool '{target.Name}' in domain '{target.Domain}'.");
            }
        }
    }
}

internal sealed record ToolDomainDescriptor(
    string Domain,
    string CompoundToolName,
    string Summary,
    Type ToolType,
    IReadOnlyList<ToolActionDescriptor> Actions,
    IReadOnlyDictionary<string, ToolActionDescriptor> CanonicalActionsByName,
    IReadOnlyDictionary<string, string> Aliases);

internal sealed record ToolActionDescriptor(
    string Name,
    string Domain,
    string CompoundToolName,
    string Description,
    bool IsCore,
    string? AliasOf,
    Type DeclaringType,
    MethodInfo Method,
    int ClrParameterCount,
    int? CancellationTokenPosition,
    Lazy<JsonElement> LazyCompoundInputSchema,
    IReadOnlyList<ToolParameterDescriptor> Parameters,
    IReadOnlyDictionary<string, ToolParameterDescriptor> ParametersByAcceptedName,
    IReadOnlyList<string> RequiredCanonicalNames,
    IReadOnlyList<string> OptionalCanonicalNames)
{
    public bool IsAlias => AliasOf is not null;

    public JsonElement CompoundInputSchema => LazyCompoundInputSchema.Value;
}

internal sealed record ToolParameterDescriptor(
    int Position,
    ParameterInfo ClrParameter,
    string ClrName,
    string RawSnakeName,
    string CanonicalName,
    bool IsRequired,
    bool AllowsNull,
    StructuredJsonKind StructuredJson);
