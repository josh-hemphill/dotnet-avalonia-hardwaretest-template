using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

/// Canned operator-preview tile for one metric (no Execute / TapThread).
public sealed record MetricPreview(
    string ChannelKey,
    string DisplayRole,
    PresentationTileKind? TileKind,
    string YUnit,
    double CannedValue,
    IReadOnlyList<double> CannedSamples,
    IReadOnlyList<double?> SampleElapsedMs,
    double? LimitLow,
    double? LimitHigh,
    double? Threshold,
    string? Note = null,
    bool? Passed = null);

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
        [],
        null,
        null,
        null);

    /// Synthesizes canned values; maps DisplayRole to gauge vs chart vs timing.
    public static MetricPreview From(
        MetricDraft? metric,
        IReadOnlyList<MetricDraft>? siblings = null,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded = null,
        ProgramDraft? sourceContext = null)
    {
        if (metric is null)
        {
            return Empty;
        }

        var kind = PresentationRoles.TryMapRole(metric.DisplayRole);
        if (TryPreviewAverage(metric, kind, siblings, recorded, sourceContext, out var averagePreview)) return averagePreview;
        if (metric.Source is TransferFunctionAlgorithm tf)
        {
            return PreviewTransferFunction(metric, tf, kind, siblings, recorded, sourceContext);
        }

        if (metric.Source is ExpressionAlgorithm expr
            && TryPreviewFilterFormula(expr, metric, kind, siblings, recorded, sourceContext, out var filterPreview))
        {
            return filterPreview;
        }

        var series = Synthesize(metric, kind, siblings, recorded);
        var last = series.Values.Count == 0 ? 0 : series.Values[^1];
        var note = recorded is null ? null : "Recording samples (not Execute).";
        return new MetricPreview(
            metric.ChannelKey,
            metric.DisplayRole,
            kind,
            metric.YUnit,
            last,
            series.Values,
            series.Elapsed,
            metric.Limits?.Low,
            metric.Limits?.High,
            metric.Limits?.Threshold,
            note);
    }

    private static bool TryPreviewAverage(MetricDraft metric, PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings, IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded,
        ProgramDraft? sourceContext,
        out MetricPreview preview)
    {
        preview = Empty;
        string? channel = metric.Source switch
        {
            AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicChannelAverage } a
                => a.InputChannelKeys.FirstOrDefault() ?? a.Settings.GetValueOrDefault("InputChannel"),
            ExpressionAlgorithm e when FormulaParser.TryParse(e.Source, out var ast, out _)
                && ast!.Root is CallExpr { Name: "mean", Args: [IdentExpr ident] } => ident.Name,
            _ => null,
        };
        if (channel is null) return false;
        try
        {
            AuthoringCriteria.Validate(metric);
            IReadOnlyList<double> values;
            string? note = recorded is null ? null : "Recording samples (not Execute).";
            if (recorded is not null)
            {
                if (!TryGetSeries(recorded, channel, out var samples))
                    throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.FormulaEval}: missing series '{channel}'.");
                values = samples.Select(s => s.Value).ToArray();
            }
            else
            {
                double? scalar = null;
                if (sourceContext is not null)
                {
                    if (metric.Source is ExpressionAlgorithm)
                        scalar = PlanCompiler.ScalarMeanPreviewExample(FormulaDeploymentClassifier.DeploymentContext(metric, sourceContext), metric.ChannelKey);
                    else ValidateSource(metric, siblings, sourceContext);
                }
                if (scalar is { } value)
                {
                    values = [value];
                    note = "Preview only: mathematical example from a known Scalar publisher. Deployment requires a preceding Sample publisher.";
                }
                else
                {
                    var sibling = siblings?.FirstOrDefault(m => string.Equals(m.ChannelKey, channel, StringComparison.OrdinalIgnoreCase));
                    values = sibling is null ? SynthesizeCanned(metric.Limits, kind)
                        : Synthesize(sibling, PresentationRoles.TryMapRole(sibling.DisplayRole), null, null).Values;
                }
            }
            var result = ChannelAverageEvaluator.Evaluate(values, metric.Limits!.Threshold!.Value);
            preview = new(metric.ChannelKey, metric.DisplayRole, kind, metric.YUnit, result.Average,
                [result.Average], [], metric.Limits.Low, metric.Limits.High, metric.Limits.Threshold,
                note, result.Passed);
        }
        catch (Exception ex) when (ex is AuthoringWorkspaceException or InvalidOperationException)
        {
            preview = new(metric.ChannelKey, metric.DisplayRole, kind, metric.YUnit, 0, [], [],
                metric.Limits?.Low, metric.Limits?.High, metric.Limits?.Threshold, ex.Message);
        }
        return true;
    }

    private readonly record struct PreviewSeries(IReadOnlyList<double> Values, IReadOnlyList<double?> Elapsed);

    private static PreviewSeries Synthesize(
        MetricDraft metric,
        PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded)
    {
        if (metric.Source is ExpressionAlgorithm expr)
        {
            var values = EvaluateFormula(expr, metric, siblings, recorded);
            return new PreviewSeries(values, []);
        }

        if (recorded is not null
            && TryGetSeries(recorded, metric.ChannelKey, out var recordedSamples)
            && recordedSamples.Count > 0)
        {
            return FromStored(recordedSamples);
        }

        var nominal = Nominal(metric.Limits);
        if (kind is PresentationTileKind.Timeseries or PresentationTileKind.Timing)
        {
            return new PreviewSeries([nominal * 0.95, nominal, nominal * 1.02, nominal], []);
        }

        return new PreviewSeries([nominal], []);
    }

    private static PreviewSeries FromStored(IReadOnlyList<StoredSample> samples)
        => new(samples.Select(sample => sample.Value).ToArray(), samples.Select(sample => sample.ElapsedMs).ToArray());

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
                    values = Synthesize(sibling, PresentationRoles.TryMapRole(sibling.DisplayRole), null, null).Values;
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
        ProgramDraft? sourceContext,
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

            if (FormulaLowerer.Lower(expr, metric.Limits, recorded) is not TransferFunctionAlgorithm tf)
            {
                return false;
            }

            preview = PreviewTransferFunction(metric, tf, kind, siblings, recorded, sourceContext);
            return true;
        }
        catch (AuthoringWorkspaceException ex)
        {
            if (recorded is null)
            {
                return false;
            }

            preview = new MetricPreview(
                metric.ChannelKey,
                metric.DisplayRole,
                kind,
                metric.YUnit,
                0,
                [],
                [],
                metric.Limits?.Low,
                metric.Limits?.High,
                metric.Limits?.Threshold,
                ex.Message);
            return true;
        }
    }

    private static MetricPreview PreviewTransferFunction(
        MetricDraft metric,
        TransferFunctionAlgorithm tf,
        PresentationTileKind? kind,
        IReadOnlyList<MetricDraft>? siblings,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? recorded,
        ProgramDraft? sourceContext)
    {
        try
        {
            IReadOnlyList<StoredSample> applied;
            string? note;
            if (recorded is not null)
            {
                if (!TryGetSeries(recorded, tf.InputChannelKey, out var input) || input.Count == 0)
                {
                    throw new AuthoringWorkspaceException(
                        $"{AuthoringCompileCodes.FormulaEval}: missing series '{tf.InputChannelKey}'.");
                }

                applied = TransferFunctionEval.Apply(tf, input, metric.ChannelKey);
                note = "Recording samples (not Execute).";
            }
            else
            {
                ValidateSource(metric, siblings, sourceContext);
                var sibling = siblings?.FirstOrDefault(s =>
                    string.Equals(s.ChannelKey, tf.InputChannelKey, StringComparison.OrdinalIgnoreCase));
                var canned = sibling is null
                    ? SynthesizeCanned(metric.Limits, kind)
                    : Synthesize(sibling, PresentationRoles.TryMapRole(sibling.DisplayRole), null, null).Values;
                applied = TransferFunctionEval.ApplyWithSynthesizedClock(tf, canned, metric.ChannelKey);
                note = null;
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
                applied.Select(s => s.ElapsedMs).ToArray(),
                metric.Limits?.Low,
                metric.Limits?.High,
                metric.Limits?.Threshold,
                note);
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
                [],
                metric.Limits?.Low,
                metric.Limits?.High,
                metric.Limits?.Threshold,
                ex.Message);
        }
    }

    private static void ValidateSource(MetricDraft metric, IReadOnlyList<MetricDraft>? siblings, ProgramDraft? sourceContext)
    {
        if (sourceContext is not null)
        {
            var deployment = FormulaDeploymentClassifier.DeploymentContext(metric, sourceContext);
            PlanCompiler.ValidateFormulaInput(deployment.Measure, metric.ChannelKey, deployment.AuthoringState);
        }
        else if (siblings is not null)
            PlanCompiler.ValidateFormulaInput(siblings.Select(value => (MeasureNode)new MetricNode(value)).ToArray(), metric.ChannelKey);
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
