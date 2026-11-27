using System;
using System.Collections.Generic;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Pure lookup policy mapping user-facing DWG version strings to canonical AutoCAD DWG format identifiers.
/// </summary>
public static class DwgVersionPolicy
{
    private static readonly Dictionary<string, string> Versions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["current"] = "current",
            ["2018"] = "AC1032",
            ["ac1032"] = "AC1032",
            ["2013"] = "AC1027",
            ["ac1027"] = "AC1027",
            ["2010"] = "AC1024",
            ["ac1024"] = "AC1024",
            ["2007"] = "AC1021",
            ["ac1021"] = "AC1021",
            ["2004"] = "AC1018",
            ["ac1018"] = "AC1018",
            ["2000"] = "AC1015",
            ["ac1015"] = "AC1015",
        };

    /// <summary>Normalizes an optional DWG version selector into its canonical AutoCAD format token.</summary>
    public static bool TryParseDwgVersion(
        string? raw,
        out string canonicalName,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            canonicalName = "current";
            error = null;
            return true;
        }

        string trimmed = raw.Trim();
        if (Versions.TryGetValue(trimmed, out string? matched))
        {
            canonicalName = matched;
            error = null;
            return true;
        }

        canonicalName = "current";
        error = $"Unsupported dwg_version '{raw}'. Expected: current, 2018 (AC1032), 2013 (AC1027), 2010 (AC1024), 2007 (AC1021), 2004 (AC1018), or 2000 (AC1015).";
        return false;
    }
}
