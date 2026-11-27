using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoCAD.Gateway.Contracts;
using ModelContextProtocol.Protocol;

namespace AutoCAD.McpServer.Server;

/// <summary>
/// Adapts internal JSON string tool responses into native MCP <see cref="CallToolResult"/> envelopes
/// with <c>IsError</c>, multimodal <see cref="ImageContentBlock"/>, <c>StructuredContent</c>, and compact text.
/// </summary>
internal static class McpResultAdapter
{
    private const int MaxContentChars = 4_000;
    private const int MaxListRows = 100;
    private const int MaxLayerRows = 120;
    private const int MaxPartialBatchDetails = 10;

    public static CallToolResult Adapt(string toolName, string? rawJson, OutputFormat outputFormat)
    {
        string text = rawJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return TextOnlyResult(text, structuredContent: null, isError: false);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return TextOnlyResult(text, structuredContent: null, isError: false);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return TextOnlyResult(text, structuredContent: null, isError: false);
            }

            JsonElement root = doc.RootElement;
            if (IsErrorPayload(root))
            {
                string errText = outputFormat == OutputFormat.Json ? text : ClampOutput(FormatCompactError(root));
                return TextOnlyResult(errText, structuredContent: null, isError: true);
            }

            if (TryGetNonEmptyString(root, "image_base64", out string? b64))
            {
                return BuildScreenshotResult(root, b64!, outputFormat);
            }

            JsonElement cloned = root.Clone();
            string outText = outputFormat == OutputFormat.Json ? text : FormatCompactSuccess(toolName, root);
            return TextOnlyResult(outText, structuredContent: cloned, isError: false);
        }
    }

    private static CallToolResult TextOnlyResult(string text, JsonElement? structuredContent, bool isError) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = structuredContent,
            IsError = isError,
        };

    private static bool IsErrorPayload(JsonElement root)
    {
        if (root.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        return root.TryGetProperty("status", out JsonElement statusEl) &&
               statusEl.ValueKind == JsonValueKind.String &&
               (string.Equals(statusEl.GetString(), "error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(statusEl.GetString(), "partial_error", StringComparison.OrdinalIgnoreCase));
    }

    private static CallToolResult BuildScreenshotResult(JsonElement root, string b64, OutputFormat outputFormat)
    {
        string mime = TryGetNonEmptyString(root, "mime_type", out string? m) ? m! : "image/png";
        ImageContentBlock? imageBlock = null;
        string? decodeWarning = null;
        try
        {
            byte[] pngBytes = Convert.FromBase64String(b64);
            imageBlock = ImageContentBlock.FromBytes(pngBytes, mime);
        }
        catch (FormatException)
        {
            decodeWarning = "Invalid base64 image payload from Gateway.";
        }

        var sanitizedNode = JsonObject.Create(root) ?? new JsonObject();
        sanitizedNode.Remove("image_base64");
        sanitizedNode["inline_image_attached"] = imageBlock is not null;
        if (decodeWarning is not null)
        {
            sanitizedNode["inline_omitted_reason"] = decodeWarning;
        }

        JsonElement sanitizedEl = JsonSerializer.SerializeToElement(sanitizedNode, GatewayProtocol.WireJsonOptions);

        string textSummary;
        if (outputFormat == OutputFormat.Json)
        {
            textSummary = sanitizedEl.GetRawText();
        }
        else
        {
            var parts = new List<string>(5) { "Screenshot captured" };
            if (TryGetNonEmptyString(root, "file_path", out string? filePath))
            {
                parts.Add($"file: {filePath}");
            }

            if (TryGetScalarString(root, "width", out string? w) && TryGetScalarString(root, "height", out string? h))
            {
                parts.Add($"{w}x{h}");
            }

            parts.Add(imageBlock is not null ? $"inline: {mime}" : "inline omitted: invalid base64 payload");
            if (TryGetScalarString(root, "duration_ms", out string? dur))
            {
                parts.Add($"{dur} ms");
            }

            textSummary = string.Join(" | ", parts);
        }

        var blocks = new List<ContentBlock>(2) { new TextContentBlock { Text = textSummary } };
        if (imageBlock is not null)
        {
            blocks.Add(imageBlock);
        }

        return new CallToolResult
        {
            Content = blocks,
            StructuredContent = sanitizedEl,
            IsError = false,
        };
    }

    private static string FormatCompactError(JsonElement root)
    {
        string code = "error";
        string? message = null;
        string? detail = null;
        string? hint = null;

        if (root.TryGetProperty("error", out JsonElement errObj) && errObj.ValueKind == JsonValueKind.Object)
        {
            if (TryGetNonEmptyString(errObj, "code", out string? ec)) code = ec!;
            if (TryGetNonEmptyString(errObj, "message", out string? em)) message = em;
            if (TryGetNonEmptyString(errObj, "detail", out string? ed)) detail = ed;
            if (TryGetNonEmptyString(errObj, "hint", out string? eh)) hint = eh;
        }

        message ??= TryGetNonEmptyString(root, "message", out string? topMsg) ? topMsg! : "Operation failed.";
        detail ??= TryGetNonEmptyString(root, "detail", out string? topDetail) ? topDetail : null;
        hint ??= TryGetNonEmptyString(root, "hint", out string? topHint) ? topHint : null;

        var lines = new List<string>(8) { $"[{code}] {message}" };
        if (!string.IsNullOrWhiteSpace(detail)) lines.Add($"Detail: {detail}");
        if (!string.IsNullOrWhiteSpace(hint)) lines.Add($"Hint: {hint}");

        AppendPartialBatchSummary(lines, root);
        return string.Join('\n', lines);
    }

    private static void AppendPartialBatchSummary(List<string> lines, JsonElement root)
    {
        if (!TryGetScalarString(root, "succeeded", out string? succeeded) ||
            !TryGetScalarString(root, "failed", out string? failed))
        {
            return;
        }

        string rolledBack = GetBool(root, "rolled_back", true) ? "true" : "false";
        lines.Add($"Batch: succeeded={succeeded}, failed={failed}, rolled_back={rolledBack}");

        if (TryGetArray(root, "handles", out JsonElement handlesArr) && handlesArr.GetArrayLength() > 0)
        {
            int totalHandles = handlesArr.GetArrayLength();
            var preview = handlesArr.EnumerateArray()
                .Where(h => h.ValueKind == JsonValueKind.String)
                .Take(MaxPartialBatchDetails)
                .Select(h => h.GetString()!)
                .ToArray();
            if (preview.Length > 0)
            {
                string extra = totalHandles > preview.Length ? $", ... (+{totalHandles - preview.Length} more)" : string.Empty;
                lines.Add($"Committed handles: [{string.Join(", ", preview)}{extra}]");
            }
        }

        if (TryGetArray(root, "results", out JsonElement resultsArr))
        {
            int shownFails = 0;
            int totalFails = 0;
            foreach (JsonElement item in resultsArr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || GetBool(item, "ok", true))
                {
                    continue;
                }

                totalFails++;
                if (shownFails >= MaxPartialBatchDetails)
                {
                    continue;
                }

                string idxStr = TryGetScalarString(item, "index", out string? ix) ? ix! : "?";
                string errCode = TryGetNonEmptyString(item, "error_code", out string? ec) ? ec! : "error";
                string errText = TryGetNonEmptyString(item, "error", out string? et) ? TruncateInline(et!, 120) : "Operation failed.";
                lines.Add($"- item #{idxStr} [{errCode}]: {errText}");
                shownFails++;
            }

            if (totalFails > shownFails)
            {
                lines.Add($"... ({totalFails - shownFails} more batch failures omitted)");
            }
        }
    }

    private static string FormatCompactSuccess(string toolName, JsonElement root)
    {
        if (TryGetArray(root, "layers", out JsonElement layers))
        {
            return ClampOutput(FormatLayers(root, layers));
        }

        if (TryGetArray(root, "entities", out JsonElement entities))
        {
            return ClampOutput(FormatEntities(root, entities));
        }

        if (TryGetArray(root, "texts", out JsonElement texts))
        {
            return ClampOutput(FormatTexts(root, texts));
        }

        if (TryGetArray(root, "documents", out JsonElement docs))
        {
            return ClampOutput(FormatDocuments(root, docs));
        }

        if (TryGetArray(root, "data", out JsonElement dataArr))
        {
            int len = dataArr.GetArrayLength();
            if (LooksLikeLayerArray(dataArr) || (len == 0 && IsLayerTool(toolName)))
            {
                return ClampOutput(FormatLayers(root, dataArr));
            }

            if (LooksLikeTextArray(dataArr) || (len == 0 && IsTextTool(toolName)))
            {
                return ClampOutput(FormatTexts(root, dataArr));
            }

            if ((len > 0 && dataArr.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)) ||
                (len == 0 && IsStringListTool(toolName)))
            {
                string header = AppendHeader($"{toolName} ({len})", root, "document", "duration_ms");
                return ClampOutput(FormatBoundedSection(header, dataArr, MaxListRows, "items", JsonValueKind.String, item => $"- {item.GetString()}"));
            }
        }

        if (root.TryGetProperty("actions", out JsonElement actions) &&
            actions.ValueKind == JsonValueKind.Object &&
            TryGetNonEmptyString(root, "domain", out string? domain))
        {
            return ClampOutput(FormatCompoundHelpOverview(root, domain!, actions));
        }

        return ClampOutput(FormatGenericKeyValues(toolName, root));
    }

    private static bool IsLayerTool(string toolName) =>
        toolName is "get_layers" or "read_layers";

    private static bool IsTextTool(string toolName) =>
        toolName is "get_texts" or "read_texts" or "get_all_texts";

    private static bool IsStringListTool(string toolName) =>
        toolName is "list_blocks" or "list_available_blocks";

    private static bool LooksLikeLayerArray(JsonElement arr)
    {
        if (arr.GetArrayLength() == 0 || arr[0].ValueKind != JsonValueKind.Object) return false;
        JsonElement first = arr[0];
        return first.TryGetProperty("is_off", out _) ||
               first.TryGetProperty("is_frozen", out _) ||
               first.TryGetProperty("is_locked", out _) ||
               first.TryGetProperty("is_on", out _);
    }

    private static bool LooksLikeTextArray(JsonElement arr)
    {
        if (arr.GetArrayLength() == 0 || arr[0].ValueKind != JsonValueKind.Object) return false;
        JsonElement first = arr[0];
        return first.TryGetProperty("text", out _) &&
               first.TryGetProperty("x", out _) &&
               first.TryGetProperty("y", out _);
    }

    private static string FormatLayers(JsonElement root, JsonElement layers)
    {
        int count = TryGetScalarString(root, "count", out string? c) &&
            int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : layers.GetArrayLength();
        string header = AppendHeader($"Layers ({count})", root, "document", "revision", "duration_ms");

        return FormatBoundedSection(header, layers, MaxLayerRows, "layers", JsonValueKind.Object, item =>
        {
            string name = TryGetNonEmptyString(item, "name", out string? ln) ? ln! : "?";
            string aci = TryGetScalarString(item, "color", out string? realColor)
                ? realColor!
                : (TryGetScalarString(item, "color_index", out string? legacyColor) ? legacyColor! : "-");
            string lt = TryGetNonEmptyString(item, "linetype", out string? lts) ? lts! : "Continuous";
            string lw = TryGetScalarString(item, "lineweight", out string? lws) ? lws! : "default";

            bool isOn = item.TryGetProperty("is_off", out JsonElement offEl) && offEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? !offEl.GetBoolean()
                : GetBool(item, "is_on", true);

            var flags = new List<string>(5) { isOn ? "on" : "off" };
            if (GetBool(item, "is_frozen", false)) flags.Add("frozen");
            if (GetBool(item, "is_locked", false)) flags.Add("locked");
            if (item.TryGetProperty("is_plottable", out JsonElement plEl) && plEl.ValueKind == JsonValueKind.False) flags.Add("no-plot");
            if (GetBool(item, "is_current", false)) flags.Add("current");

            return $"- {name} | aci:{aci} | lt:{lt} | lw:{lw} | {string.Join(", ", flags)}";
        });
    }

    private static string FormatEntities(JsonElement root, JsonElement entities)
    {
        int returned = entities.GetArrayLength();
        string countLabel = TryGetScalarString(root, "total_matched", out string? matched)
            ? $"{returned}/{matched}"
            : (TryGetScalarString(root, "total_scanned", out string? scanned)
                ? $"{returned}/{scanned}"
                : returned.ToString(CultureInfo.InvariantCulture));

        string header = AppendHeader(
            $"Entities ({countLabel})",
            root,
            "document", "has_more", "next_cursor", "revision", "duration_ms");

        return FormatBoundedSection(header, entities, MaxListRows, "entities", JsonValueKind.Object, ent =>
        {
            string handle = TryGetNonEmptyString(ent, "handle", out string? h) ? h! : "?";
            string type = TryGetNonEmptyString(ent, "type", out string? t) ? t! : "Entity";
            string layer = TryGetNonEmptyString(ent, "layer", out string? l) ? l! : "0";
            var row = new StringBuilder($"- [{handle}] {type} | layer:{layer}");

            if (TryGetNonEmptyString(ent, "space", out string? sp) && !string.Equals(sp, "model", StringComparison.OrdinalIgnoreCase))
            {
                row.Append($" | space:{sp}");
            }

            JsonElement geom = ent.TryGetProperty("geometry", out JsonElement gEl) && gEl.ValueKind == JsonValueKind.Object
                ? gEl
                : ent;

            if (TryGetNonEmptyString(geom, "text", out string? txt))
            {
                row.Append($" | text:\"{TruncateInline(txt!, 60)}\"");
            }
            else if (TryGetNonEmptyString(geom, "block_name", out string? blk))
            {
                row.Append($" | block:{blk}");
            }
            else if (TryGetScalarString(geom, "length", out string? len))
            {
                row.Append($" | len:{len}");
            }
            else if (TryGetScalarString(geom, "radius", out string? rad))
            {
                row.Append($" | r:{rad}");
            }
            else if (TryGetScalarString(geom, "area", out string? area))
            {
                row.Append($" | area:{area}");
            }

            return row.ToString();
        });
    }

    private static string FormatTexts(JsonElement root, JsonElement texts)
    {
        int total = texts.GetArrayLength();
        string header = AppendHeader($"Texts ({total})", root, "document", "duration_ms");

        return FormatBoundedSection(header, texts, MaxListRows, "texts", JsonValueKind.Object, item =>
        {
            string tag = TryGetNonEmptyString(item, "handle", out string? h)
                ? h!
                : (TryGetNonEmptyString(item, "type", out string? tp) ? tp! : "Text");
            string layer = TryGetNonEmptyString(item, "layer", out string? l) ? l! : "0";
            string x = TryGetScalarString(item, "x", out string? xv) ? xv! : "0";
            string y = TryGetScalarString(item, "y", out string? yv) ? yv! : "0";
            string textVal = TryGetNonEmptyString(item, "text", out string? tv)
                ? tv!
                : (TryGetNonEmptyString(item, "content", out string? cv) ? cv! : string.Empty);

            return $"- [{tag}] ({x}, {y}) | layer:{layer} | {TruncateInline(textVal, 80)}";
        });
    }

    private static string FormatDocuments(JsonElement root, JsonElement docs)
    {
        int total = docs.GetArrayLength();
        string header = AppendHeader($"Documents ({total})", root, "active_document", "duration_ms");

        return FormatBoundedSection(header, docs, MaxListRows, "documents", JsonValueKind.Object, d =>
        {
            string name = TryGetNonEmptyString(d, "name", out string? dn) ? dn! : "?";
            string sid = TryGetNonEmptyString(d, "session_id", out string? id) ? id! : "-";
            string rev = TryGetScalarString(d, "revision", out string? r) ? r! : "0";
            string active = GetBool(d, "is_active", false) ? "true" : "false";
            string managed = GetBool(d, "is_managed", false) ? "true" : "false";
            return $"- {name} | id:{sid} | rev:{rev} | active:{active} | managed:{managed}";
        });
    }

    private static string FormatBoundedSection(
        string header,
        JsonElement arrayEl,
        int maxRows,
        string noun,
        JsonValueKind expectedKind,
        Func<JsonElement, string> rowFormatter)
    {
        var sb = new StringBuilder(header);
        int eligible = 0;
        int shown = 0;
        foreach (JsonElement item in arrayEl.EnumerateArray())
        {
            if (item.ValueKind != expectedKind) continue;
            eligible++;
            if (shown >= maxRows) continue;
            sb.Append('\n').Append(rowFormatter(item));
            shown++;
        }

        if (eligible > shown)
        {
            sb.Append($"\n... ({eligible - shown} more {noun} omitted; inspect structuredContent)");
        }

        return sb.ToString();
    }

    private static string FormatCompoundHelpOverview(JsonElement root, string domain, JsonElement actions)
    {
        string compoundTool = TryGetNonEmptyString(root, "tool", out string? t) ? t! : $"autocad_{domain}";
        int count = actions.EnumerateObject().Count();
        var sb = new StringBuilder($"Domain '{domain}' ({compoundTool}) | {count} actions");

        foreach (JsonProperty actionProp in actions.EnumerateObject())
        {
            JsonElement info = actionProp.Value;
            if (info.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string req = info.TryGetProperty("required", out JsonElement rEl) ? FormatStringListInline(rEl) : string.Empty;
            string opt = info.TryGetProperty("optional", out JsonElement oEl) ? FormatStringListInline(oEl) : string.Empty;
            string desc = TryGetNonEmptyString(info, "description", out string? d) ? TruncateInline(d!, 100) : string.Empty;
            sb.Append($"\n- {actionProp.Name} | req:[{req}] | opt:[{opt}] | {desc}");
        }

        if (root.TryGetProperty("aliases", out JsonElement aliases) && aliases.ValueKind == JsonValueKind.Object)
        {
            var aliasPairs = aliases.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .Select(p => $"{p.Name}->{p.Value.GetString()}")
                .ToArray();
            if (aliasPairs.Length > 0)
            {
                sb.Append("\nAliases: ").Append(string.Join(", ", aliasPairs));
            }
        }

        return sb.ToString();
    }

    private static string FormatGenericKeyValues(string toolName, JsonElement root)
    {
        var lines = new List<string> { $"OK ({toolName})" };
        AppendObjectProperties(lines, root, prefix: string.Empty, expandChildObjects: true);
        return string.Join('\n', lines);
    }

    private static void AppendObjectProperties(List<string> lines, JsonElement obj, string prefix, bool expandChildObjects)
    {
        foreach (JsonProperty prop in obj.EnumerateObject())
        {
            if (prop.Name is "status" or "ok" || prop.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            string keyName = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                lines.Add($"{keyName}: {ScalarToString(prop.Value)}");
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                int len = prop.Value.GetArrayLength();
                if (len <= 6 && prop.Value.EnumerateArray().All(e => e.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                {
                    lines.Add($"{keyName}: [{string.Join(", ", prop.Value.EnumerateArray().Select(ScalarToString))}]");
                }
                else
                {
                    lines.Add($"{keyName}: [{len} items; inspect structuredContent]");
                }

                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                string rawObj = prop.Value.GetRawText();
                if (rawObj.Length <= 220)
                {
                    lines.Add($"{keyName}: {rawObj}");
                }
                else if (expandChildObjects && prop.Name is "data" or "geometry" or "bounds" or "summary")
                {
                    AppendObjectProperties(lines, prop.Value, prop.Name, expandChildObjects: false);
                }
                else
                {
                    lines.Add($"{keyName}: [object ({prop.Value.EnumerateObject().Count()} keys); inspect structuredContent]");
                }
            }
        }
    }

    private static string AppendHeader(string prefix, JsonElement root, params string[] keys)
    {
        var sb = new StringBuilder(prefix);
        foreach (string key in keys)
        {
            if (TryGetScalarString(root, key, out string? val) && !string.IsNullOrWhiteSpace(val))
            {
                string label = key switch
                {
                    "document" => "doc",
                    "revision" => "rev",
                    "duration_ms" => "ms",
                    _ => key,
                };
                sb.Append($" | {label}: {val}");
            }
        }

        return sb.ToString();
    }

    private static string ClampOutput(string text)
    {
        if (text.Length <= MaxContentChars)
        {
            return text;
        }

        int cut = MaxContentChars - 64;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return string.Concat(
            text.AsSpan(0, cut),
            "\n... [truncated; full payload available in structuredContent]");
    }

    private static bool TryGetArray(JsonElement root, string name, out JsonElement arrayEl)
    {
        if (root.TryGetProperty(name, out arrayEl) && arrayEl.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        arrayEl = default;
        return false;
    }

    private static bool TryGetNonEmptyString(JsonElement obj, string name, out string? value)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            string? s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                value = s;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetScalarString(JsonElement obj, string name, out string? value)
    {
        if (obj.TryGetProperty(name, out JsonElement el) &&
            el.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
        {
            value = ScalarToString(el);
            return true;
        }

        value = null;
        return false;
    }

    private static bool GetBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : defaultValue;

    private static string ScalarToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => el.GetRawText(),
        _ => el.GetRawText(),
    };

    private static string FormatStringListInline(JsonElement arrayEl) =>
        arrayEl.ValueKind != JsonValueKind.Array
            ? string.Empty
            : string.Join(", ", arrayEl.EnumerateArray().Select(ScalarToString));

    private static string TruncateInline(string s, int maxLen)
    {
        string singleLine = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLen ? singleLine : string.Concat(singleLine.AsSpan(0, maxLen - 3), "...");
    }
}
