using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Offline ExpressionAlgorithm and TransferFunctionAlgorithm eval over a bound run.json.
public static class FormulaDatasetEval
{
    /// Evaluates each ExpressionAlgorithm and TransferFunctionAlgorithm over the recording.
    public static IReadOnlyList<StoredSample> EvaluateProgram(ProgramDraft draft, TestRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(run);
        var series = RunDatasetBinder.SeriesByMetric(run);
        var results = new List<StoredSample>();
        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure))
        {
            switch (metric.Source)
            {
                case ExpressionAlgorithm expr:
                    {
                        var ast = FormulaParser.Parse(expr.Source);
                        if (ast.Root is FilterCallExpr)
                        {
                            var lowered = FormulaLowerer.Lower(expr, metric.Limits);
                            if (lowered is TransferFunctionAlgorithm loweredTf)
                            {
                                results.AddRange(EvalTransferFunction(loweredTf, metric.ChannelKey, series));
                                break;
                            }
                        }

                        EnsureSeriesPresent(expr, ast, series);
                        EnsureMeanThreshold(ast, metric);
                        var value = FormulaEvaluator.Evaluate(ast, series);
                        EnsureWithinLimits(metric, value, series, expr);
                        results.Add(new StoredSample
                        {
                            Channel = metric.ChannelKey,
                            MetricKey = metric.ChannelKey,
                            Value = value,
                            Unit = metric.YUnit,
                            DisplayRole = metric.DisplayRole,
                        });
                        break;
                    }
                case TransferFunctionAlgorithm tf:
                    results.AddRange(EvalTransferFunction(tf, metric.ChannelKey, series));
                    break;
            }
        }

        return results;
    }

    private static void EnsureSeriesPresent(
        ExpressionAlgorithm expr,
        FormulaAst ast,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        var needed = expr.InputChannelKeys
            .Concat(FormulaExprWalk.Identifiers(ast.Root))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var key in needed)
        {
            if (!TryGetSeries(series, key, out var samples) || samples.Count == 0)
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringCompileCodes.FormulaEval}: missing series '{key}'.");
            }
        }
    }

    private static void EnsureMeanThreshold(FormulaAst ast, MetricDraft metric)
    {
        if (ast.Root is CallExpr { Name: "mean", Args: [IdentExpr] } && metric.Limits?.Threshold is null)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.MissingLimits}: mean() requires a scalar LimitSpec threshold.");
        }
    }

    private static void EnsureWithinLimits(
        MetricDraft metric,
        double value,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series,
        ExpressionAlgorithm expr)
    {
        var limits = metric.Limits ?? RecordedLimits(series, expr.InputChannelKeys);
        if (limits is null)
        {
            return;
        }

        if (limits.Threshold is { } threshold && value < threshold)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaEval}: '{metric.ChannelKey}' {value} is below threshold {threshold}.");
        }

        if (limits.Low is { } low && value < low)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaEval}: '{metric.ChannelKey}' {value} is below limit {low}.");
        }

        if (limits.High is { } high && value > high)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaEval}: '{metric.ChannelKey}' {value} is above limit {high}.");
        }
    }

    private static LimitSpec? RecordedLimits(
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series,
        IReadOnlyList<string> inputKeys)
    {
        foreach (var key in inputKeys)
        {
            if (!TryGetSeries(series, key, out var samples) || samples.Count == 0)
            {
                continue;
            }

            var sample = samples[0];
            if (sample.LimitLow is null && sample.LimitHigh is null)
            {
                continue;
            }

            return new LimitSpec(sample.LimitLow, sample.LimitHigh, null);
        }

        return null;
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

    private static IReadOnlyList<StoredSample> EvalTransferFunction(
        TransferFunctionAlgorithm tf,
        string outputChannelKey,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        if (!TryGetSeries(series, tf.InputChannelKey, out var input) || input.Count == 0)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaEval}: missing series '{tf.InputChannelKey}'.");
        }

        return TransferFunctionEval.Apply(tf, input, outputChannelKey);
    }
}
