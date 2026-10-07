namespace HardwareTest.Authoring;

/// Recipe criteria are independent of Presentation. Bounds are inclusive and finite.
public sealed record CriterionRequirements(bool RequiresThreshold, bool RequiresBand, string Unit,
    bool Inclusive = true, bool RequiresFiniteNumbers = true);

public static class AuthoringCriteria
{
    public static CriterionRequirements? Requirements(MetricDraft metric)
    {
        var id = metric.Source switch
        {
            AlgorithmSource a => a.AlgorithmId,
            MeasureSource m => m.FunctionId,
            ExpressionAlgorithm e when FormulaParser.TryParse(e.Source, out var ast, out _)
                && ast!.Root is CallExpr { Name: "mean", Args: [IdentExpr] } => AuthoringFunctionIds.BasicChannelAverage,
            _ => string.Empty,
        };
        if (metric.Source is MeasureSource measure
            && (id is AuthoringFunctionIds.BasicAcquireVoltage or AuthoringFunctionIds.BasicBitSweepAcquire)
            && measure.Settings.TryGetValue("SeriesCompliance", out var mode)
            && HardwareTest.OpenTap.Plugins.Basic.SeriesComplianceModes.IsEnabled(mode))
            return new(false, true, metric.YUnit);
        return id switch
        {
            AuthoringFunctionIds.BasicMeanGte or AuthoringFunctionIds.BasicChannelAverage
                => new(true, false, metric.YUnit),
            AuthoringFunctionIds.BasicPublishBandScalar or AuthoringFunctionIds.BasicPublishSeriesCompliance or AuthoringFunctionIds.BasicReportStationHealth
                => new(false, true, metric.YUnit),
            _ => null,
        };
    }

    public static void Validate(MetricDraft metric)
    {
        var required = Requirements(metric);
        var limits = metric.Limits;
        if (required?.RequiresThreshold == true && limits?.Threshold is null
            || required?.RequiresBand == true && (limits?.Low is null || limits.High is null))
            throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.MissingLimits}: '{metric.ChannelKey}' requires {(required.RequiresThreshold ? "a threshold" : "low and high bounds")}.");
        if (limits is null) return;
        foreach (var value in new[] { limits.Low, limits.High, limits.Threshold })
            if (value is { } number && !double.IsFinite(number))
                throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.MissingLimits}: '{metric.ChannelKey}' criteria must be finite.");
        if (limits.Low > limits.High)
            throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.MissingLimits}: '{metric.ChannelKey}' low must not exceed high.");
    }

    public static bool IsRuntimeLimit(string key) => key.Equals("Threshold", StringComparison.OrdinalIgnoreCase)
        || key.Equals("LimitLow", StringComparison.OrdinalIgnoreCase) || key.Equals("LimitHigh", StringComparison.OrdinalIgnoreCase)
        || key.Equals("OffsetLimitLow", StringComparison.OrdinalIgnoreCase) || key.Equals("OffsetLimitHigh", StringComparison.OrdinalIgnoreCase);
}
