namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    internal static global::OpenTap.ITestStep PreviewRawStep(RawStepNode raw) => LoadRawStep(raw);

    // Project only Enabled so unrelated incomplete numeric draft fields remain per-node issues.
    internal static bool PreviewEnabled(MetricDraft metric)
    {
        var (id, settings) = metric.Source switch
        {
            MeasureSource measure => (measure.FunctionId, measure.Settings),
            AlgorithmSource algorithm => (algorithm.AlgorithmId, algorithm.Settings),
            _ => (string.Empty, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>())
        };
        if (id.Length == 0) return true;
        var step = AuthoringFunctionCatalog.CreateStep(id);
        ApplySettings(step, settings.Where(pair => pair.Key.Equals(nameof(global::OpenTap.ITestStep.Enabled), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        return step.Enabled;
    }

    internal static void RequirePreviewEnabled(MetricDraft metric)
    {
        if (!PreviewEnabled(metric)) throw new AuthoringWorkspaceException("Step is disabled in the source plan.");
    }

    internal static global::OpenTap.ITestStep PreviewStep(MetricDraft metric)
    {
        var source = ResolveSource(metric);
        var id = source switch { MeasureSource m => m.FunctionId, AlgorithmSource a => a.AlgorithmId, _ => string.Empty };
        var step = AuthoringFunctionCatalog.CreateStep(id);
        ApplySettings(step, source switch { MeasureSource m => m.Settings, AlgorithmSource a => a.Settings, _ => new Dictionary<string, string>() });
        step.Name = metric.Name;
        step.GetType().GetProperty("Channel")?.SetValue(step, metric.ChannelKey);
        if (step is HardwareTest.OpenTap.Plugins.Basic.ChannelAverageStep average && source is AlgorithmSource algorithm && algorithm.InputChannelKeys.Count == 1)
            average.InputChannel = algorithm.InputChannelKeys[0];
        PresentationAttach.Apply(step, metric);
        return step;
    }

    // Reuse the compiler's closed setting projection instead of introducing a property grid.
    internal static IReadOnlyDictionary<string, string> FunctionSettings(string functionId)
        => ReadSettings(AuthoringFunctionCatalog.CreateStep(functionId), includeEmpty: true);
}
