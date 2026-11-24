using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoCAD.McpServer.Tools;

namespace AutoCAD.McpServer.Server;

internal sealed class CompoundDispatcher
{
    private static readonly JsonSerializerOptions HelpJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AutoCADToolCatalog _catalog;
    private readonly Func<Type, object> _toolResolver;

    public CompoundDispatcher(AutoCADToolCatalog catalog, Func<Type, object> toolResolver)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _toolResolver = toolResolver ?? throw new ArgumentNullException(nameof(toolResolver));
    }

    public string BuildHelp(string domain, string? targetAction = null)
    {
        if (string.IsNullOrWhiteSpace(domain) || !_catalog.TryGetDomain(domain.Trim(), out ToolDomainDescriptor? domainDesc) || domainDesc is null)
        {
            return CadFormat.Error($"Unknown domain '{domain}'.");
        }

        if (string.IsNullOrWhiteSpace(targetAction))
        {
            var actions = new Dictionary<string, object>(domainDesc.CanonicalActionsByName.Count, StringComparer.Ordinal);
            foreach (ToolActionDescriptor action in domainDesc.Actions)
            {
                if (action.IsAlias)
                {
                    continue;
                }

                actions[action.Name] = new
                {
                    description = action.Description,
                    required = action.RequiredCanonicalNames,
                    optional = action.OptionalCanonicalNames,
                };
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                domain = domainDesc.Domain,
                tool = domainDesc.CompoundToolName,
                actions,
                aliases = domainDesc.Aliases,
            });
        }

        if (!TryResolveDomainAction(domainDesc, targetAction, out ToolActionDescriptor? resolved, out string? resolvedFrom, out string? errorJson))
        {
            return errorJson!;
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            domain = domainDesc.Domain,
            action = resolved!.Name,
            resolved_from = resolvedFrom,
            description = resolved.Description,
            input_schema = resolved.CompoundInputSchema,
        }, HelpJsonOptions);
    }

    public async Task<string> DispatchAsync(
        string domain,
        string? action,
        JsonElement? rawParams,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain) || !_catalog.TryGetDomain(domain.Trim(), out ToolDomainDescriptor? domainDesc) || domainDesc is null)
        {
            return CadFormat.Error($"Unknown domain '{domain}'.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return CadFormat.Error($"'action' cannot be empty in domain '{domainDesc.Domain}'. Use action='help' to list available actions.");
        }

        string trimmedAction = action.Trim();
        if (!TryParseParamsObject(rawParams, out Dictionary<string, JsonElement> suppliedParams, out string? paramsError))
        {
            return CadFormat.Error(paramsError!);
        }

        if (string.Equals(trimmedAction, "help", StringComparison.OrdinalIgnoreCase))
        {
            return DispatchHelp(domainDesc.Domain, suppliedParams);
        }

        if (!TryResolveDomainAction(domainDesc, trimmedAction, out ToolActionDescriptor? targetTool, out _, out string? resolveError))
        {
            return resolveError!;
        }

        if (!TryBindMethodArguments(targetTool!, suppliedParams, cancellationToken, out object?[]? args, out string? bindError))
        {
            return CadFormat.Error(bindError!);
        }

        object instance = _toolResolver(targetTool!.DeclaringType);
        try
        {
            object? rawResult = targetTool.Method.Invoke(instance, args);
            return rawResult switch
            {
                Task<string> taskStr => await taskStr.ConfigureAwait(false),
                ValueTask<string> valueTaskStr => await valueTaskStr.ConfigureAwait(false),
                string str => str,
                _ => CadFormat.Error($"Tool '{targetTool.Name}' returned an unsupported result type."),
            };
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            if (tie.InnerException is OperationCanceledException oce && cancellationToken.IsCancellationRequested)
            {
                ExceptionDispatchInfo.Capture(oce).Throw();
            }

            return CadFormat.Error($"Tool '{targetTool.Name}' failed: {tie.InnerException.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CadFormat.Error($"Tool '{targetTool.Name}' failed: {ex.Message}");
        }
    }

    private string DispatchHelp(string domain, Dictionary<string, JsonElement> suppliedParams)
    {
        string? targetAction = null;
        foreach (KeyValuePair<string, JsonElement> kv in suppliedParams)
        {
            if (!string.Equals(kv.Key, "action", StringComparison.Ordinal))
            {
                return CadFormat.Error($"Unexpected parameter '{kv.Key}' for action='help'. Only optional 'action' is accepted.");
            }

            if (kv.Value.ValueKind == JsonValueKind.Null)
            {
                targetAction = null;
            }
            else if (kv.Value.ValueKind == JsonValueKind.String)
            {
                string? rawTarget = kv.Value.GetString();
                if (string.IsNullOrWhiteSpace(rawTarget))
                {
                    return CadFormat.Error("'params.action' must be a non-empty action name string.");
                }

                targetAction = rawTarget;
            }
            else
            {
                return CadFormat.Error($"Parameter 'action' for help must be a string, got {kv.Value.ValueKind}.");
            }
        }

        return BuildHelp(domain, targetAction);
    }

    private bool TryResolveDomainAction(
        ToolDomainDescriptor domainDesc,
        string rawActionName,
        out ToolActionDescriptor? resolved,
        out string? resolvedFrom,
        out string? errorJson)
    {
        string key = rawActionName.Trim();
        if (domainDesc.CanonicalActionsByName.TryGetValue(key, out resolved))
        {
            resolvedFrom = null;
            errorJson = null;
            return true;
        }

        if (domainDesc.Aliases.TryGetValue(key, out string? canonicalName)
            && domainDesc.CanonicalActionsByName.TryGetValue(canonicalName, out resolved))
        {
            resolvedFrom = key;
            errorJson = null;
            return true;
        }

        resolved = null;
        resolvedFrom = null;

        if (_catalog.TryGetAction(key, out ToolActionDescriptor? otherDomainAction) && otherDomainAction is not null)
        {
            errorJson = CadFormat.Error(
                $"Action '{key}' belongs to domain '{otherDomainAction.Domain}' (tool '{otherDomainAction.CompoundToolName}'), not '{domainDesc.Domain}'.");
            return false;
        }

        errorJson = CadFormat.Error(
            $"Unknown action '{key}' in domain '{domainDesc.Domain}'. Call action='help' to list available actions.");
        return false;
    }

    private static bool TryParseParamsObject(
        JsonElement? rawParams,
        out Dictionary<string, JsonElement> parsed,
        out string? error)
    {
        parsed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        error = null;

        if (!rawParams.HasValue || rawParams.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        JsonElement value = rawParams.Value;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in value.EnumerateObject())
            {
                parsed[prop.Name] = prop.Value.Clone();
            }

            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string rawJson = value.GetString() ?? string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = $"'params' JSON string must decode to an object, got {doc.RootElement.ValueKind}.";
                    return false;
                }

                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    parsed[prop.Name] = prop.Value.Clone();
                }

                return true;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON in 'params': {ex.Message}";
                return false;
            }
        }

        error = $"'params' must be a JSON object (or JSON-encoded object string), got {value.ValueKind}.";
        return false;
    }

    private static bool TryBindMethodArguments(
        ToolActionDescriptor action,
        Dictionary<string, JsonElement> suppliedParams,
        CancellationToken cancellationToken,
        out object?[]? args,
        out string? error)
    {
        args = null;
        error = null;

        var boundByPosition = new JsonElement?[action.Parameters.Count];
        var unexpectedKeys = new List<string>();

        foreach (KeyValuePair<string, JsonElement> kv in suppliedParams)
        {
            if (!action.ParametersByAcceptedName.TryGetValue(kv.Key, out ToolParameterDescriptor? paramDesc))
            {
                unexpectedKeys.Add(kv.Key);
                continue;
            }

            if (boundByPosition[paramDesc.Position].HasValue)
            {
                error = $"Duplicate parameter binding for '{paramDesc.CanonicalName}' on '{action.Name}'.";
                return false;
            }

            boundByPosition[paramDesc.Position] = kv.Value;
        }

        if (unexpectedKeys.Count > 0)
        {
            error = $"Unexpected parameter(s) for '{action.Name}': {string.Join(", ", unexpectedKeys)}. Call action='help' with params={{\"action\":\"{action.Name}\"}} to inspect valid parameters.";
            return false;
        }

        var callArgs = new object?[action.ClrParameterCount];
        if (action.CancellationTokenPosition is int ctPos)
        {
            callArgs[ctPos] = cancellationToken;
        }

        var missingRequired = new List<string>();
        foreach (ToolParameterDescriptor paramDesc in action.Parameters)
        {
            JsonElement? rawVal = boundByPosition[paramDesc.Position];
            if (!rawVal.HasValue)
            {
                if (paramDesc.IsRequired)
                {
                    missingRequired.Add(paramDesc.CanonicalName);
                }
                else
                {
                    callArgs[paramDesc.ClrParameter.Position] = paramDesc.ClrParameter.DefaultValue;
                }

                continue;
            }

            if (!TryConvertParameterValue(rawVal.Value, paramDesc, action.Name, out object? converted, out string? convertError))
            {
                error = convertError;
                return false;
            }

            callArgs[paramDesc.ClrParameter.Position] = converted;
        }

        if (missingRequired.Count > 0)
        {
            error = $"Missing required parameter(s) for '{action.Name}': {string.Join(", ", missingRequired)}. Call action='help' with params={{\"action\":\"{action.Name}\"}} for schema.";
            return false;
        }

        args = callArgs;
        return true;
    }

    private static bool TryConvertParameterValue(
        JsonElement val,
        ToolParameterDescriptor paramDesc,
        string actionName,
        out object? value,
        out string? error)
    {
        value = null;
        error = null;

        Type targetType = paramDesc.ClrParameter.ParameterType;
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (val.ValueKind == JsonValueKind.Null)
        {
            if (paramDesc.AllowsNull)
            {
                value = null;
                return true;
            }

            if (paramDesc.IsRequired)
            {
                error = $"Missing required parameter '{paramDesc.CanonicalName}' for '{actionName}'.";
                return false;
            }

            error = $"Parameter '{paramDesc.CanonicalName}' for '{actionName}' cannot be null.";
            return false;
        }

        if (effectiveType == typeof(string))
        {
            if (val.ValueKind == JsonValueKind.String)
            {
                value = val.GetString();
                return true;
            }

            if (paramDesc.StructuredJson == StructuredJsonKind.Array && val.ValueKind == JsonValueKind.Array)
            {
                value = val.GetRawText();
                return true;
            }

            if (paramDesc.StructuredJson == StructuredJsonKind.Object && val.ValueKind == JsonValueKind.Object)
            {
                value = val.GetRawText();
                return true;
            }

            error = paramDesc.StructuredJson switch
            {
                StructuredJsonKind.Array => $"Parameter '{paramDesc.CanonicalName}' expects a string or JSON array, got {val.ValueKind}.",
                StructuredJsonKind.Object => $"Parameter '{paramDesc.CanonicalName}' expects a string or JSON object, got {val.ValueKind}.",
                _ => $"Parameter '{paramDesc.CanonicalName}' expects a string, got {val.ValueKind}.",
            };
            return false;
        }

        if (effectiveType == typeof(bool))
        {
            if (val.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = val.GetBoolean();
                return true;
            }

            error = $"Parameter '{paramDesc.CanonicalName}' expects a boolean, got {val.ValueKind}.";
            return false;
        }

        if (effectiveType == typeof(int))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int i))
            {
                value = i;
                return true;
            }

            error = $"Parameter '{paramDesc.CanonicalName}' expects a 32-bit integer, got {val.ValueKind}.";
            return false;
        }

        if (effectiveType == typeof(long))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out long l))
            {
                value = l;
                return true;
            }

            error = $"Parameter '{paramDesc.CanonicalName}' expects a 64-bit integer, got {val.ValueKind}.";
            return false;
        }

        if (effectiveType == typeof(double))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d) && double.IsFinite(d))
            {
                value = d;
                return true;
            }

            error = $"Parameter '{paramDesc.CanonicalName}' expects a finite number, got {val.ValueKind}.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize(val.GetRawText(), targetType);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid value for parameter '{paramDesc.CanonicalName}': {ex.Message}";
            return false;
        }
    }
}
