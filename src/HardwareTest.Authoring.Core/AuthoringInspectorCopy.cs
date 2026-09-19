using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

/// One catalog function shown in the inspector (title + persisted id).
public sealed record AuthoringFunctionDisplay(string Id, string Title, string Summary);

/// Inspector labels and help for metric fields. Avalonia-free.
public static class AuthoringInspectorCopy
{
    public const string OperatorYUnitLabel = "Operator Y unit";
    public const string OperatorYUnitTooltip =
        "Unit label on the operator gauge and chart for this metric (e.g. V, ms, %). Presentation only — not an OpenTAP step property id.";

    public const string MeasureRecipeLabel = "Measure recipe";
    public const string MeasureRecipeTooltip =
        "Which closed measure or analyze recipe this step runs. Pick from the workspace catalog — do not type OpenTAP type names.";

    public const string MeasureSettingsPurpose =
        "OpenTAP step properties for this recipe. Values are saved as-is on Save plan.";

    public const string HistoryEnabledLabel = "Include in operator history";
    public const string HistoryEnabledTooltip =
        "When enabled, this metric participates in DUT history drift checks on the operator board (Presentation mixin).";

    public const string HistoryWatchLabel = "History watch % (caution)";
    public const string HistoryWatchTooltip =
        "Optional. Watch when |delta| vs the prior-run mean reaches this percent. Typical: 5. Leave empty for the shell default (5).";
    public const string HistoryWatchPlaceholder = "5";

    public const string HistoryAlertLabel = "History alert % (fail)";
    public const string HistoryAlertTooltip =
        "Optional. Alert when |delta| vs the prior-run mean reaches this percent. Typical: 10. Leave empty for the shell default (10).";
    public const string HistoryAlertPlaceholder = "10";

    public const string SeriesComplianceValueTooltip =
        "How out-of-band samples fail the acquire (stored values): none — off, limits may still display; allSamples — fail if any sample is outside Limit low/high; dwell — fail only after staying out of band longer than Dwell limit ms.";

    public static AuthoringFunctionDisplay DescribeFunction(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new AuthoringFunctionDisplay(string.Empty, string.Empty, string.Empty);
        }

        if (Titles.TryGetValue(id, out var mapped))
        {
            return mapped;
        }

        if (AuthoringFunctionCatalog.TryGet(id, out var spec))
        {
            return new AuthoringFunctionDisplay(id, SpacedLabel(StripStepSuffix(spec.TypeName)), spec.Pack);
        }

        return new AuthoringFunctionDisplay(
            id,
            id,
            "Not in the workspace catalog — pick a listed recipe or re-open after Save plan.");
    }

    public static IReadOnlyList<AuthoringFunctionDisplay> DescribeFunctions(IEnumerable<string> ids)
        => ids.Select(DescribeFunction).ToArray();

    public static SettingPresentation PresentSetting(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new SettingPresentation(key ?? string.Empty, key ?? string.Empty);
        }

        return SettingCopy.TryGetValue(key, out var presented)
            ? presented
            : new SettingPresentation(key, SpacedLabel(key));
    }

    public static string? SeriesComplianceModeCaption(string stored)
    {
        if (string.Equals(stored, SeriesComplianceModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return "Off (none)";
        }

        if (string.Equals(stored, SeriesComplianceModes.AllSamples, StringComparison.OrdinalIgnoreCase))
        {
            return "All samples (allSamples)";
        }

        if (string.Equals(stored, SeriesComplianceModes.Dwell, StringComparison.OrdinalIgnoreCase))
        {
            return "Dwell (dwell)";
        }

        return null;
    }

    public sealed record SettingPresentation(
        string Key,
        string Label,
        string? ValueTooltip = null,
        string? ValuePlaceholder = null);

    private static readonly Dictionary<string, SettingPresentation> SettingCopy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SeriesCompliance"] = new(
            "SeriesCompliance",
            "Series compliance",
            SeriesComplianceValueTooltip,
            SeriesComplianceModes.None),
        ["FailWhenOutOfBand"] = new(
            "FailWhenOutOfBand",
            "Fail when out of band",
            "true/false — when true, Series compliance can fail the acquire step.",
            "false"),
        ["DwellLimitMs"] = new(
            "DwellLimitMs",
            "Dwell limit (ms)",
            "Used only when Series compliance is dwell.",
            "5"),
    };

    private static readonly Dictionary<string, AuthoringFunctionDisplay> Titles = new(StringComparer.Ordinal)
    {
        [AuthoringFunctionIds.BasicAcquireVoltage] = new(
            AuthoringFunctionIds.BasicAcquireVoltage,
            "Acquire voltage",
            "Waveform timeseries on the bound instrument slot."),
        [AuthoringFunctionIds.BasicMeanGte] = new(
            AuthoringFunctionIds.BasicMeanGte,
            "Mean GTE",
            "Scalar mean vs threshold."),
        [AuthoringFunctionIds.BasicPublishBandScalar] = new(
            AuthoringFunctionIds.BasicPublishBandScalar,
            "Publish band scalar",
            "Passband with Limit low / Limit high."),
        [AuthoringFunctionIds.BasicBitSweepAcquire] = new(
            AuthoringFunctionIds.BasicBitSweepAcquire,
            "Bit sweep acquire",
            "Walk config bits while publishing Sample + Event."),
        [AuthoringFunctionIds.BasicPublishTimedSample] = new(
            AuthoringFunctionIds.BasicPublishTimedSample,
            "Publish timed sample",
            "One timed sample on the timing strip."),
        [AuthoringFunctionIds.BasicPublishSeriesCompliance] = new(
            AuthoringFunctionIds.BasicPublishSeriesCompliance,
            "Publish series compliance",
            "In-band percent passband from scripted values."),
        [AuthoringFunctionIds.BasicApplyTransferFunction] = new(
            AuthoringFunctionIds.BasicApplyTransferFunction,
            "Apply transfer function",
            "Discrete SISO IIR from numerator/denominator/Ts."),
        [AuthoringFunctionIds.BasicIdentityCheck] = new(
            AuthoringFunctionIds.BasicIdentityCheck,
            "Identity check",
            "Confirm DUT serial against the instrument."),
        [AuthoringFunctionIds.BasicReportStationHealth] = new(
            AuthoringFunctionIds.BasicReportStationHealth,
            "Report station health",
            "cal.dc.offset scalar with limits."),
        [AuthoringFunctionIds.IcIdentityQuery] = new(
            AuthoringFunctionIds.IcIdentityQuery,
            "Identity query",
            "InstrumentComponents identity step."),
        [AuthoringFunctionIds.IcSafeShutdown] = new(
            AuthoringFunctionIds.IcSafeShutdown,
            "Safe shutdown",
            "InstrumentComponents cleanup."),
    };

    private static string StripStepSuffix(string typeName)
        => typeName.EndsWith("Step", StringComparison.Ordinal) ? typeName[..^4] : typeName;

    internal static string SpacedLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var chars = new List<char> { char.ToUpperInvariant(key[0]) };
        for (var i = 1; i < key.Length; i++)
        {
            var current = key[i];
            if (current == '_')
            {
                chars.Add(' ');
                continue;
            }

            if (char.IsUpper(current) && !char.IsUpper(key[i - 1]))
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(current));
                continue;
            }

            chars.Add(chars[^1] == ' ' ? char.ToLowerInvariant(current) : current);
        }

        return new string(chars.ToArray());
    }
}
