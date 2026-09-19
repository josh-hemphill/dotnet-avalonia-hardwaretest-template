using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public enum AuthoringSettingKind
{
    Text,
    Integer,
    Double,
    Boolean,
    Choice,
}

public enum AuthoringSettingChoiceSource
{
    None,
    Static,
    SeriesCompliance,
    ChannelKeys,
}

public sealed record AuthoringMetricSettingSpec(
    AuthoringSettingKind Kind,
    AuthoringSettingChoiceSource ChoiceSource = AuthoringSettingChoiceSource.None,
    IReadOnlyList<string>? StaticChoices = null,
    double? Minimum = null,
    bool ChoiceIsEditable = false);

/// Closed table of measure/algorithm setting kinds. Not an OpenTAP property grid.
public static class AuthoringMetricSettingCatalog
{
    private static readonly IReadOnlyList<string> SeriesComplianceChoices =
        SeriesComplianceModes.Choices as IReadOnlyList<string> ?? [.. SeriesComplianceModes.Choices];

    private static readonly (string FunctionId, string Key, AuthoringMetricSettingSpec Spec)[] Table =
    [
        ("*", "SampleCount", new(AuthoringSettingKind.Integer, Minimum: 1)),
        ("*", "IntervalMs", new(AuthoringSettingKind.Integer, Minimum: 0)),
        ("*", "BitCount", new(AuthoringSettingKind.Integer, Minimum: 1)),
        ("*", "Index", new(AuthoringSettingKind.Integer, Minimum: 0)),
        ("*", "Threshold", new(AuthoringSettingKind.Double)),
        ("*", "LimitLow", new(AuthoringSettingKind.Double)),
        ("*", "LimitHigh", new(AuthoringSettingKind.Double)),
        ("*", "OffsetLimitLow", new(AuthoringSettingKind.Double)),
        ("*", "OffsetLimitHigh", new(AuthoringSettingKind.Double)),
        ("*", "ElapsedMs", new(AuthoringSettingKind.Double, Minimum: 0)),
        ("*", "TsSeconds", new(AuthoringSettingKind.Double, Minimum: 0)),
        ("*", "FailWhenOutOfBand", new(AuthoringSettingKind.Boolean)),
        ("*", "PublishSummaries", new(AuthoringSettingKind.Boolean)),
        ("*", "DwellLimitMs", new(AuthoringSettingKind.Integer, Minimum: 0)),
        ("*", "SeriesCompliance", new(AuthoringSettingKind.Choice, AuthoringSettingChoiceSource.SeriesCompliance)),
        ("*", "Channel", new(AuthoringSettingKind.Choice, AuthoringSettingChoiceSource.ChannelKeys, ChoiceIsEditable: true)),
        ("*", "InputChannel", new(AuthoringSettingKind.Choice, AuthoringSettingChoiceSource.ChannelKeys, ChoiceIsEditable: true)),
    ];

    public static AuthoringMetricSettingSpec Resolve(string? functionId, string key)
    {
        AuthoringMetricSettingSpec? wildcard = null;
        foreach (var (id, settingKey, spec) in Table)
        {
            if (!string.Equals(settingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(functionId)
                && string.Equals(id, functionId, StringComparison.Ordinal))
            {
                return spec;
            }

            if (id == "*")
            {
                wildcard = spec;
            }
        }

        return wildcard ?? new AuthoringMetricSettingSpec(AuthoringSettingKind.Text);
    }

    public static AuthoringSettingRow CreateRow(
        string functionId,
        string key,
        string value,
        IReadOnlyList<string> channelKeys)
    {
        var spec = Resolve(functionId, key);
        var presented = AuthoringInspectorCopy.PresentSetting(key);
        var choices = spec.ChoiceSource switch
        {
            AuthoringSettingChoiceSource.SeriesCompliance => SeriesComplianceChoices,
            AuthoringSettingChoiceSource.ChannelKeys => AuthoringWorkspaceCatalog.Union(channelKeys, [value]),
            AuthoringSettingChoiceSource.Static => spec.StaticChoices ?? [],
            _ => null,
        };
        return new AuthoringSettingRow(
            key,
            value ?? string.Empty,
            presented.Label,
            spec.Kind,
            choices,
            presented.ValueTooltip,
            presented.ValuePlaceholder,
            spec.Minimum,
            spec.ChoiceIsEditable);
    }
}
