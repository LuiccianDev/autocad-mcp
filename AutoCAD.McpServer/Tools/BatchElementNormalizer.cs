using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using AutoCAD.Gateway.Contracts;
using static AutoCAD.McpServer.Tools.CadFormat;

namespace AutoCAD.McpServer.Tools;

/// <summary>
/// Normalizes batch geometry elements from English/Spanish aliases into canonical ObjectARX Gateway operations.
/// </summary>
internal static class BatchElementNormalizer
{
    public static bool TryNormalizeElement(
        JsonElement item,
        string defaultLayer,
        out Dictionary<string, object?>? op,
        out string error)
    {
        op = null;
        error = string.Empty;

        string rawType = GetStringOpt(item, "type") ?? GetStringOpt(item, "tipo") ?? string.Empty;
        string kind = rawType.Trim().ToLowerInvariant();
        if (kind.Length == 0)
        {
            error = "Missing 'type' field.";
            return false;
        }

        string layer = defaultLayer.Trim();
        string? rawLayer = GetStringOpt(item, "layer") ?? GetStringOpt(item, "capa");
        if (!string.IsNullOrWhiteSpace(rawLayer))
        {
            if (!SecurityPolicy.IsValidSymbolName(rawLayer))
            {
                error = $"Invalid layer name '{rawLayer}'.";
                return false;
            }

            layer = rawLayer.Trim();
        }

        switch (kind)
        {
            case "line" or "linea":
            {
                if (!TryGetDouble(item, "x1", [], 0.0, out double x1, out error) ||
                    !TryGetDouble(item, "y1", [], 0.0, out double y1, out error) ||
                    !TryGetDouble(item, "z1", [], 0.0, out double z1, out error) ||
                    !TryGetDouble(item, "x2", [], 0.0, out double x2, out error) ||
                    !TryGetDouble(item, "y2", [], 0.0, out double y2, out error) ||
                    !TryGetDouble(item, "z2", [], 0.0, out double z2, out error))
                {
                    return false;
                }

                if (Math.Abs(x2 - x1) < 1e-12 && Math.Abs(y2 - y1) < 1e-12 && Math.Abs(z2 - z1) < 1e-12)
                {
                    error = "Line element cannot have zero length.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "line",
                    ["start"] = new[] { x1, y1, z1 },
                    ["end"] = new[] { x2, y2, z2 },
                    ["layer"] = layer,
                };
                return true;
            }

            case "rectangle" or "rectangulo" or "rect":
            {
                if (!TryGetDouble(item, "x1", [], 0.0, out double x1, out error) ||
                    !TryGetDouble(item, "y1", [], 0.0, out double y1, out error) ||
                    !TryGetDouble(item, "x2", [], 0.0, out double x2, out error) ||
                    !TryGetDouble(item, "y2", [], 0.0, out double y2, out error))
                {
                    return false;
                }

                if (Math.Abs(x2 - x1) < 1e-12 || Math.Abs(y2 - y1) < 1e-12)
                {
                    error = "Rectangle element cannot have zero width or zero height.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "rectangle",
                    ["min"] = new[] { x1, y1 },
                    ["max"] = new[] { x2, y2 },
                    ["layer"] = layer,
                };
                return true;
            }

            case "circle" or "circulo":
            {
                if (!TryGetDouble(item, "center_x", ["centerX", "centroX", "centro_x", "x"], 0.0, out double cx, out error) ||
                    !TryGetDouble(item, "center_y", ["centerY", "centroY", "centro_y", "y"], 0.0, out double cy, out error) ||
                    !TryGetDouble(item, "center_z", ["centerZ", "centroZ", "centro_z", "z"], 0.0, out double cz, out error) ||
                    !TryGetDouble(item, "radius", ["radio", "r"], 0.0, out double r, out error))
                {
                    return false;
                }

                if (r <= 0)
                {
                    error = "Invalid radius in circle element.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "circle",
                    ["center"] = new[] { cx, cy, cz },
                    ["radius"] = r,
                    ["layer"] = layer,
                };
                return true;
            }

            case "arc" or "arco":
            {
                if (!TryGetDouble(item, "center_x", ["centerX", "centroX", "centro_x", "x"], 0.0, out double cx, out error) ||
                    !TryGetDouble(item, "center_y", ["centerY", "centroY", "centro_y", "y"], 0.0, out double cy, out error) ||
                    !TryGetDouble(item, "center_z", ["centerZ", "centroZ", "centro_z", "z"], 0.0, out double cz, out error) ||
                    !TryGetDouble(item, "radius", ["radio", "r"], 0.0, out double r, out error) ||
                    !TryGetDouble(item, "start_angle", ["startAngle", "anguloInicio", "angulo_inicio"], 0.0, out double sa, out error) ||
                    !TryGetDouble(item, "end_angle", ["endAngle", "anguloFin", "angulo_fin"], 0.0, out double ea, out error))
                {
                    return false;
                }

                if (r <= 0)
                {
                    error = "Invalid radius in arc element.";
                    return false;
                }

                if (Math.Abs(ea - sa) < 1e-9)
                {
                    error = "Arc element cannot have zero sweep angle.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "arc",
                    ["center"] = new[] { cx, cy, cz },
                    ["radius"] = r,
                    ["start_angle"] = sa,
                    ["end_angle"] = ea,
                    ["layer"] = layer,
                };
                return true;
            }

            case "polyline" or "polilinea" or "lwpolyline":
            {
                if (!ExtractBatchPoints(item, out double[][]? pts, out error))
                {
                    return false;
                }

                if (!TryGetOptionalBool(item, out bool? closedVal, out error, "closed", "cerrada"))
                {
                    return false;
                }

                if (pts == null || pts.Length < 2)
                {
                    error = "Polyline requires at least 2 valid vertices.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "polyline",
                    ["points"] = pts,
                    ["closed"] = closedVal ?? false,
                    ["layer"] = layer,
                };
                return true;
            }

            case "text" or "texto":
            {
                string txt = GetStringOpt(item, "text") ?? GetStringOpt(item, "texto") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(txt))
                {
                    error = "Text element requires non-empty 'text'.";
                    return false;
                }

                if (!TryGetDouble(item, "x", [], 0.0, out double tx, out error) ||
                    !TryGetDouble(item, "y", [], 0.0, out double ty, out error) ||
                    !TryGetDouble(item, "z", [], 0.0, out double tz, out error) ||
                    !TryGetDouble(item, "height", ["altura"], 0.25, out double h, out error) ||
                    !TryGetDouble(item, "rotation", ["rotacion"], 0.0, out double rot, out error))
                {
                    return false;
                }

                if (h <= 0)
                {
                    error = "Invalid height in text element.";
                    return false;
                }

                op = new Dictionary<string, object?>
                {
                    ["op"] = "create",
                    ["type"] = "text",
                    ["text"] = txt,
                    ["position"] = new[] { tx, ty, tz },
                    ["height"] = h,
                    ["rotation"] = rot,
                    ["layer"] = layer,
                };
                return true;
            }

            default:
                error = $"Unsupported element type '{rawType}'. Use: line, polyline, rectangle, circle, arc, text.";
                return false;
        }
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonProperty matched)
    {
        foreach (JsonProperty prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                matched = prop;
                return true;
            }
        }

        matched = default;
        return false;
    }

    private static bool TryParseFiniteNumber(JsonElement el, out double value)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value) && double.IsFinite(value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value))
        {
            return true;
        }

        value = 0.0;
        return false;
    }

    private static bool ExtractBatchPoints(JsonElement item, out double[][]? points, out string error)
    {
        points = null;
        error = "Polyline requires at least 2 valid vertices.";

        if (!TryGetPropertyCaseInsensitive(item, "points", out JsonProperty prop)
            && !TryGetPropertyCaseInsensitive(item, "puntos", out prop))
        {
            return false;
        }

        if (prop.Value.ValueKind == JsonValueKind.String)
        {
            points = ParsePoints(prop.Value.GetString(), 2);
            return points != null;
        }

        if (prop.Value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<double[]>();
        foreach (JsonElement pt in prop.Value.EnumerateArray())
        {
            if (pt.ValueKind == JsonValueKind.Array)
            {
                int len = pt.GetArrayLength();
                if (len is < 2 or > 3)
                {
                    return false;
                }

                var coords = new double[len];
                int k = 0;
                foreach (JsonElement numEl in pt.EnumerateArray())
                {
                    if (!TryParseFiniteNumber(numEl, out coords[k]))
                    {
                        return false;
                    }

                    k++;
                }

                list.Add(coords);
            }
            else if (pt.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetPropertyCaseInsensitive(pt, "x", out JsonProperty xProp)
                    || !TryGetPropertyCaseInsensitive(pt, "y", out JsonProperty yProp)
                    || !TryParseFiniteNumber(xProp.Value, out double vx)
                    || !TryParseFiniteNumber(yProp.Value, out double vy))
                {
                    return false;
                }

                if (TryGetPropertyCaseInsensitive(pt, "bulge", out JsonProperty bProp)
                    && bProp.Value.ValueKind != JsonValueKind.Null)
                {
                    if (!TryParseFiniteNumber(bProp.Value, out double vb))
                    {
                        return false;
                    }

                    list.Add([vx, vy, vb]);
                }
                else
                {
                    list.Add([vx, vy]);
                }
            }
            else
            {
                return false;
            }
        }

        if (list.Count < 2)
        {
            return false;
        }

        points = list.ToArray();
        return true;
    }

    private static string? GetStringOpt(JsonElement obj, string name) =>
        TryGetPropertyCaseInsensitive(obj, name, out JsonProperty prop) && prop.Value.ValueKind == JsonValueKind.String
            ? prop.Value.GetString()
            : null;

    private static bool TryGetOptionalBool(JsonElement obj, out bool? value, out string error, params string[] names)
    {
        value = null;
        error = string.Empty;
        foreach (string name in names)
        {
            if (!TryGetPropertyCaseInsensitive(obj, name, out JsonProperty prop))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Null)
            {
                value = null;
                return true;
            }

            if (prop.Value.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (prop.Value.ValueKind == JsonValueKind.False)
            {
                value = false;
                return true;
            }

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                string raw = (prop.Value.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                if (raw is "true" or "1")
                {
                    value = true;
                    return true;
                }

                if (raw is "false" or "0")
                {
                    value = false;
                    return true;
                }
            }

            error = $"Field '{prop.Name}' must be a boolean.";
            return false;
        }

        return true;
    }

    private static bool TryGetDouble(
        JsonElement el,
        string primary,
        string[] aliases,
        double fallback,
        out double value,
        out string error)
    {
        error = string.Empty;
        JsonProperty matched;
        if (!TryGetPropertyCaseInsensitive(el, primary, out matched))
        {
            bool found = false;
            foreach (string alias in aliases)
            {
                if (TryGetPropertyCaseInsensitive(el, alias, out matched))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                value = fallback;
                return true;
            }
        }

        if (matched.Value.ValueKind == JsonValueKind.Null)
        {
            value = fallback;
            return true;
        }

        if (TryParseFiniteNumber(matched.Value, out value))
        {
            return true;
        }

        error = $"Field '{matched.Name}' must be a finite number.";
        return false;
    }
}
