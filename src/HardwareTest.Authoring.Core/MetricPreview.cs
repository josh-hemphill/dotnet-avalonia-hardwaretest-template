using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Canned operator-preview tile for one metric (no Execute / TapThread).
public sealed record MetricPreview(
    string ChannelKey,
    string DisplayRole,
    PresentationTileKind? TileKind,
    string YUnit,
    double CannedValue,
    IReadOnlyList<double> CannedSamples,
    double? LimitLow,
    double? LimitHigh,
    double? Threshold);

/// Builds preview samples from draft limits/role through PresentationRoles.TryMapRole.
public static class MetricPreviewBuilder
{
    public static MetricPreview Empty { get; } = new(
        string.Empty,
        string.Empty,
        null,
        string.Empty,
        0,
        [],
        null,
        null,
        null);

    /// Synthesizes canned values; maps DisplayRole to gauge vs chart vs timing.
    public static MetricPreview From(MetricDraft? metric)
    {
        if (metric is null)
        {
            return Empty;
        }

        var kind = PresentationRoles.TryMapRole(metric.DisplayRole);
        var samples = Synthesize(metric, kind);
        var last = samples.Count == 0 ? 0 : samples[^1];
        return new MetricPreview(
            metric.ChannelKey,
            metric.DisplayRole,
            kind,
            metric.YUnit,
            last,
            samples,
            metric.Limits?.Low,
            metric.Limits?.High,
            metric.Limits?.Threshold);
    }

    private static IReadOnlyList<double> Synthesize(MetricDraft metric, PresentationTileKind? kind)
    {
        var nominal = Nominal(metric.Limits);
        if (kind is PresentationTileKind.Timeseries or PresentationTileKind.Timing)
        {
            return [nominal * 0.95, nominal, nominal * 1.02, nominal];
        }

        return [nominal];
    }

    private static double Nominal(LimitSpec? limits)
    {
        if (limits is null)
        {
            return 1;
        }

        if (limits.Low is { } lo && limits.High is { } hi)
        {
            return (lo + hi) / 2;
        }

        if (limits.Threshold is { } threshold)
        {
            return threshold;
        }

        return limits.Low ?? limits.High ?? 1;
    }
}
