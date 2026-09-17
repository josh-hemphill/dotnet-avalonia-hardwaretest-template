using HardwareTest.Core.Runs;
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
    public static MetricPreview From(MetricDraft? metric, IReadOnlyList<MetricDraft>? siblings = null)
    {
        if (metric is null)
        {
            return Empty;
        }

        var kind = PresentationRoles.TryMapRole(metric.DisplayRole);
        var samples = Synthesize(metric, kind, siblings);
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

    private static IReadOnlyList<double> Synthesize(
        MetricDraft metric,
        PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings)
    {
        if (metric.Source is ExpressionAlgorithm expr)
        {
            return EvaluateFormula(expr, metric, siblings);
        }

        var nominal = Nominal(metric.Limits);
        if (kind is PresentationTileKind.Timeseries or PresentationTileKind.Timing)
        {
            return [nominal * 0.95, nominal, nominal * 1.02, nominal];
        }

        return [nominal];
    }

    private static IReadOnlyList<double> EvaluateFormula(
        ExpressionAlgorithm expr,
        MetricDraft metric,
        IReadOnlyList<MetricDraft>? siblings)
    {
        try
        {
            var ast = FormulaParser.Parse(expr.Source);
            var keys = FormulaExprWalk.Identifiers(ast.Root)
                .Concat(expr.InputChannelKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var series = new Dictionary<string, IReadOnlyList<StoredSample>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var sibling = siblings?.FirstOrDefault(s =>
                    string.Equals(s.ChannelKey, key, StringComparison.OrdinalIgnoreCase));
                IReadOnlyList<double> values;
                if (sibling is null || sibling.Source is ExpressionAlgorithm)
                {
                    var nominal = Nominal(metric.Limits);
                    values = [nominal * 0.95, nominal, nominal * 1.02, nominal];
                }
                else
                {
                    values = Synthesize(sibling, PresentationRoles.TryMapRole(sibling.DisplayRole), null);
                }

                series[key] = values
                    .Select((value, i) => new StoredSample { Channel = key, MetricKey = key, Value = value, ElapsedMs = i })
                    .ToArray();
            }

            return [FormulaEvaluator.Evaluate(ast, series)];
        }
        catch (AuthoringWorkspaceException)
        {
            return [];
        }
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
