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
    double? Threshold,
    string? Note = null);

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
    public static MetricPreview From(
        MetricDraft? metric,
        IReadOnlyList<MetricDraft>? siblings = null,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded = null)
    {
        if (metric is null)
        {
            return Empty;
        }

        var kind = PresentationRoles.TryMapRole(metric.DisplayRole);
        if (metric.Source is TransferFunctionAlgorithm tf)
        {
            return PreviewTransferFunction(metric, tf, kind, siblings, recorded);
        }

        if (metric.Source is ExpressionAlgorithm expr
            && TryPreviewFilterFormula(expr, metric, kind, siblings, recorded, out var filterPreview))
        {
            return filterPreview;
        }

        var samples = Synthesize(metric, kind, siblings, recorded);
        var last = samples.Count == 0 ? 0 : samples[^1];
        var note = recorded is null ? null : "Recording samples (not Execute).";
        return new MetricPreview(
            metric.ChannelKey,
            metric.DisplayRole,
            kind,
            metric.YUnit,
            last,
            samples,
            metric.Limits?.Low,
            metric.Limits?.High,
            metric.Limits?.Threshold,
            note);
    }

    private static IReadOnlyList<double> Synthesize(
        MetricDraft metric,
        PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded)
    {
        if (metric.Source is ExpressionAlgorithm expr)
        {
            return EvaluateFormula(expr, metric, siblings, recorded);
        }

        if (recorded is not null
            && TryGetSeries(recorded, metric.ChannelKey, out var recordedSamples)
            && recordedSamples.Count > 0)
        {
            return recordedSamples.Select(sample => sample.Value).ToArray();
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
        IReadOnlyList<MetricDraft>? siblings,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded)
    {
        try
        {
            var ast = FormulaParser.Parse(expr.Source);
            if (recorded is not null)
            {
                return [FormulaEvaluator.Evaluate(ast, recorded)];
            }

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
                    values = Synthesize(sibling, PresentationRoles.TryMapRole(sibling.DisplayRole), null, null);
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

    private static bool TryGetSeries(
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series,
        string key,
        out IReadOnlyList<StoredSample> samples)
    {
        if (series.TryGetValue(key, out samples!))
        {
            return true;
        }

        foreach (var (name, value) in series)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                samples = value;
                return true;
            }
        }

        samples = [];
        return false;
    }

    private static bool TryPreviewFilterFormula(
        ExpressionAlgorithm expr,
        MetricDraft metric,
        PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded,
        out MetricPreview preview)
    {
        preview = Empty;
        try
        {
            var ast = FormulaParser.Parse(expr.Source);
            if (ast.Root is not FilterCallExpr)
            {
                return false;
            }

            if (FormulaLowerer.Lower(expr, metric.Limits) is not TransferFunctionAlgorithm tf)
            {
                return false;
            }

            preview = PreviewTransferFunction(metric, tf, kind, siblings, recorded);
            return true;
        }
        catch (AuthoringWorkspaceException)
        {
            return false;
        }
    }

    private static MetricPreview PreviewTransferFunction(
        MetricDraft metric,
        TransferFunctionAlgorithm tf,
        PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded)
    {
        try
        {
            IReadOnlyList<StoredSample> applied;
            if (recorded is not null
                && TryGetSeries(recorded, tf.InputChannelKey, out var input)
                && input.Count > 0)
            {
                applied = TransferFunctionEval.Apply(tf, input, metric.ChannelKey);
            }
            else
            {
                var sibling = siblings?.FirstOrDefault(s =>
                    string.Equals(s.ChannelKey, tf.InputChannelKey, StringComparison.OrdinalIgnoreCase));
                var canned = sibling is null
                    ? SynthesizeCanned(metric.Limits, kind)
                    : Synthesize(sibling, PresentationRoles.TryMapRole(sibling.DisplayRole), null, null);
                applied = TransferFunctionEval.ApplyWithSynthesizedClock(tf, canned, metric.ChannelKey);
            }

            var values = applied.Select(s => s.Value).ToArray();
            var last = values.Length == 0 ? 0 : values[^1];
            return new MetricPreview(
                metric.ChannelKey,
                metric.DisplayRole,
                kind,
                metric.YUnit,
                last,
                values,
                metric.Limits?.Low,
                metric.Limits?.High,
                metric.Limits?.Threshold,
                recorded is null ? null : "Recording samples (not Execute).");
        }
        catch (AuthoringWorkspaceException ex)
        {
            return new MetricPreview(
                metric.ChannelKey,
                metric.DisplayRole,
                kind,
                metric.YUnit,
                0,
                [],
                metric.Limits?.Low,
                metric.Limits?.High,
                metric.Limits?.Threshold,
                ex.Message);
        }
    }

    private static IReadOnlyList<double> SynthesizeCanned(LimitSpec? limits, PresentationTileKind? kind)
    {
        var nominal = Nominal(limits);
        if (kind is PresentationTileKind.Timeseries or PresentationTileKind.Timing)
        {
            return [nominal * 0.95, nominal, nominal * 1.02, nominal];
        }

        return [nominal * 0.95, nominal, nominal * 1.02, nominal];
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
