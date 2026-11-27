using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Shared PaperSpace plotting constants and pure validation helpers reused across Gateway and MCP tools.
/// </summary>
public static class PlottingPolicy
{
    /// <summary>Standard ISO architectural sheet sizes supported for full-bleed PaperSpace page setups.</summary>
    public static readonly HashSet<string> ValidPaperSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "A0", "A1", "A2", "A3", "A4",
    };

    /// <summary>Valid PaperSpace page orientations.</summary>
    public static readonly HashSet<string> ValidOrientations = new(StringComparer.OrdinalIgnoreCase)
    {
        "landscape", "portrait",
    };

    /// <summary>Supported native parametric engineering title block templates.</summary>
    public static readonly HashSet<string> ValidTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "iso_standard", "compact", "detailed",
    };

    /// <summary>Supported PaperSpace viewport shade plot modes.</summary>
    public static readonly HashSet<string> ValidShadePlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "as_displayed", "wireframe", "hidden", "rendered",
    };

    /// <summary>Named 3D DCS view direction presets mapped to unit/direction components.</summary>
    public static readonly Dictionary<string, (double X, double Y, double Z)> ViewDirectionPresets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["top"] = (0, 0, 1),
            ["bottom"] = (0, 0, -1),
            ["front"] = (0, -1, 0),
            ["back"] = (0, 1, 0),
            ["left"] = (-1, 0, 0),
            ["right"] = (1, 0, 0),
            ["sw_iso"] = (-1, -1, 1),
            ["se_iso"] = (1, -1, 1),
            ["ne_iso"] = (1, 1, 1),
            ["nw_iso"] = (-1, 1, 1),
        };

    /// <summary>Validates that a layout name is a safe PaperSpace layout name (never <c>Model</c>).</summary>
    public static bool TryValidatePaperLayoutName(
        string? rawLayout,
        out string cleanName,
        out string? error)
    {
        cleanName = string.Empty;
        if (!SecurityPolicy.IsSafeLayoutName(rawLayout, allowSpaces: true))
        {
            error = "Layout name must be a valid AutoCAD symbol free of control characters or '~'/'?'.";
            return false;
        }

        string trimmed = rawLayout!.Trim();
        if (string.Equals(trimmed, "Model", StringComparison.OrdinalIgnoreCase))
        {
            error = "'layout_name' must be a valid PaperSpace layout name (not 'Model').";
            return false;
        }

        cleanName = trimmed;
        error = null;
        return true;
    }

    /// <summary>
    /// Parses a MCP string argument for <c>view_direction</c> (preset name or JSON array <c>[dx, dy, dz]</c>).
    /// </summary>
    public static bool TryParseViewDirectionInput(
        string? raw,
        out object? wireValue,
        out string? error)
    {
        wireValue = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        string trimmed = raw.Trim();
        if (!trimmed.StartsWith('['))
        {
            if (ViewDirectionPresets.ContainsKey(trimmed))
            {
                wireValue = trimmed.ToLowerInvariant();
                return true;
            }

            error = $"Unknown 'view_direction' preset '{trimmed}'. Expected: top, bottom, front, back, left, right, sw_iso, se_iso, ne_iso, nw_iso, or a [dx, dy, dz] array.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (!TryParseViewDirectionArray(doc.RootElement, out double dx, out double dy, out double dz, out error))
            {
                return false;
            }

            wireValue = new[] { dx, dy, dz };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON in 'view_direction': {ex.Message}";
            return false;
        }
    }

    /// <summary>Parses a Gateway JSON element for <c>view_direction</c> (null, preset string, or 3-element array).</summary>
    public static bool TryParseViewDirectionElement(
        JsonElement el,
        out double dx,
        out double dy,
        out double dz,
        out string? error)
    {
        error = null;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            dx = 0;
            dy = 0;
            dz = 1;
            return true;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            string preset = (el.GetString() ?? string.Empty).Trim();
            if (ViewDirectionPresets.TryGetValue(preset, out var dir))
            {
                (dx, dy, dz) = dir;
                return true;
            }

            dx = dy = dz = 0;
            error = $"Unknown 'view_direction' preset '{preset}'. Expected one of: top, bottom, front, back, left, right, sw_iso, se_iso, ne_iso, nw_iso, or a [dx, dy, dz] array.";
            return false;
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            return TryParseViewDirectionArray(el, out dx, out dy, out dz, out error);
        }

        dx = dy = dz = 0;
        error = "'view_direction' must be a direction preset string or a 3-element [dx, dy, dz] array.";
        return false;
    }

    private static bool TryParseViewDirectionArray(
        JsonElement arrayEl,
        out double dx,
        out double dy,
        out double dz,
        out string? error)
    {
        dx = dy = dz = 0;
        if (arrayEl.ValueKind != JsonValueKind.Array || arrayEl.GetArrayLength() != 3)
        {
            error = "'view_direction' array must contain exactly 3 finite numbers [dx, dy, dz].";
            return false;
        }

        Span<double> vals = stackalloc double[3];
        int idx = 0;
        foreach (JsonElement item in arrayEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double d) || !double.IsFinite(d))
            {
                error = "'view_direction' array must contain 3 finite numbers.";
                return false;
            }

            vals[idx++] = d;
        }

        if (vals[0] * vals[0] + vals[1] * vals[1] + vals[2] * vals[2] <= 1e-18)
        {
            error = "'view_direction' vector cannot have zero length.";
            return false;
        }

        dx = vals[0];
        dy = vals[1];
        dz = vals[2];
        error = null;
        return true;
    }
}
