using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Pure lookup policy mapping AutoCAD layer lineweight inputs (millimeters, names, or enum integers)
/// to legal <c>Autodesk.AutoCAD.DatabaseServices.LineWeight</c> integer codes.
/// </summary>
public static class LineWeightPolicy
{
    public const int ByLayerValue = -1;
    public const int ByBlockValue = -2;
    public const int DefaultValue = -3;

    private static readonly int[] LegalMetricHundredths =
    [
        0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50,
        53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211,
    ];

    private static readonly Dictionary<int, string> DisplayByEnumValue = BuildDisplayMap();
    private static readonly Dictionary<string, int> EnumByToken = BuildTokenMap();

    private static Dictionary<int, string> BuildDisplayMap()
    {
        var map = new Dictionary<int, string>(LegalMetricHundredths.Length + 1)
        {
            [DefaultValue] = "default",
        };
        foreach (int hundredths in LegalMetricHundredths)
        {
            double mm = hundredths / 100.0;
            map[hundredths] = mm.ToString("0.00", CultureInfo.InvariantCulture);
        }

        return map;
    }

    private static Dictionary<string, int> BuildTokenMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = DefaultValue,
            ["bylineweightdefault"] = DefaultValue,
            ["bydefault"] = DefaultValue,
            ["-3"] = DefaultValue,
        };

        foreach (int hundredths in LegalMetricHundredths)
        {
            double mm = hundredths / 100.0;
            string fixed2 = mm.ToString("0.00", CultureInfo.InvariantCulture);
            string compact = mm.ToString("0.##", CultureInfo.InvariantCulture);
            map[fixed2] = hundredths;
            map[fixed2 + "mm"] = hundredths;
            map[compact] = hundredths;
            map[compact + "mm"] = hundredths;
            map[$"lineweight{hundredths:000}"] = hundredths;
        }

        return map;
    }

    /// <summary>Parses a layer lineweight token or numeric millimeter value.</summary>
    public static bool TryParseLineWeight(
        string? raw,
        out int enumValue,
        out string canonicalDisplay,
        out string? error)
    {
        enumValue = DefaultValue;
        canonicalDisplay = "default";
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Lineweight cannot be empty.";
            return false;
        }

        string normalized = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.Equals(normalized, "bylayer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "-1", StringComparison.Ordinal))
        {
            error = "Layer lineweight cannot be 'ByLayer'; specify 'default' or an ISO millimeter weight (for example 0.25).";
            return false;
        }

        if (string.Equals(normalized, "byblock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "-2", StringComparison.Ordinal))
        {
            error = "Layer lineweight cannot be 'ByBlock'; specify 'default' or an ISO millimeter weight (for example 0.25).";
            return false;
        }

        if (EnumByToken.TryGetValue(normalized, out int matched))
        {
            enumValue = matched;
            canonicalDisplay = DisplayByEnumValue[matched];
            return true;
        }

        string strippedMm = normalized.EndsWith("mm", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^2]
            : normalized;

        if (double.TryParse(strippedMm, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric) &&
            double.IsFinite(numeric))
        {
            return TryFromNumeric(numeric, out enumValue, out canonicalDisplay, out error);
        }

        error = $"Unsupported lineweight '{raw}'. Expected 'default' or one of the 24 ISO millimeter weights (0.00 to 2.11).";
        return false;
    }

    /// <summary>Parses a numeric millimeter value or exact hundredths-of-mm enum integer.</summary>
    public static bool TryFromNumeric(
        double value,
        out int enumValue,
        out string canonicalDisplay,
        out string? error)
    {
        enumValue = DefaultValue;
        canonicalDisplay = "default";
        error = null;

        if (!double.IsFinite(value))
        {
            error = "Lineweight must be a finite number.";
            return false;
        }

        int roundedInt = (int)Math.Round(value);
        if (Math.Abs(value - roundedInt) < 1e-6)
        {
            if (roundedInt == DefaultValue)
            {
                return true;
            }

            if (roundedInt is ByLayerValue or ByBlockValue)
            {
                error = "Layer lineweight cannot be ByLayer (-1) or ByBlock (-2).";
                return false;
            }

            if (roundedInt > 2 && DisplayByEnumValue.TryGetValue(roundedInt, out string? byEnum))
            {
                enumValue = roundedInt;
                canonicalDisplay = byEnum;
                return true;
            }
        }

        int hundredths = (int)Math.Round(value * 100.0);
        if (Math.Abs(value * 100.0 - hundredths) < 1e-4 &&
            DisplayByEnumValue.TryGetValue(hundredths, out string? byMm))
        {
            enumValue = hundredths;
            canonicalDisplay = byMm;
            return true;
        }

        error = $"Unsupported numeric lineweight '{value.ToString(CultureInfo.InvariantCulture)}'. Expected 'default' (-3) or an ISO millimeter weight (for example 0.05, 0.13, 0.25, 0.35, 0.50, 1.00, 2.11).";
        return false;
    }

    /// <summary>Formats an AutoCAD <c>LineWeight</c> integer enum value as millimeters or <c>"default"</c>.</summary>
    public static string FormatMillimeters(int enumValue)
    {
        if (DisplayByEnumValue.TryGetValue(enumValue, out string? display))
        {
            return display;
        }

        return enumValue < 0 ? "default" : (enumValue / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
