using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Offline ExpressionAlgorithm eval over a bound run.json. No Execute / MATLAB.
public static class FormulaDatasetEval
{
    public const string TransferFunctionPendingNote = "needs Area 11 filter";

    /// Evaluates each ExpressionAlgorithm. TransferFunctionAlgorithm nodes are skipped.
    public static IReadOnlyList<StoredSample> EvaluateProgram(ProgramDraft draft, TestRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(run);
        var series = RunDatasetBinder.SeriesByMetric(run);
        var results = new List<StoredSample>();
        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure))
        {
            if (metric.Source is not ExpressionAlgorithm expr)
            {
                continue;
            }

            var ast = FormulaParser.Parse(expr.Source);
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
        }

        return results;
    }

    /// True when the draft has a TF metric that this area must not treat as identity.
    public static bool HasPendingTransferFunction(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure)
            .Any(metric => metric.Source is TransferFunctionAlgorithm);
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
}
