using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Groups a run's published samples by EffectiveMetricKey, preserving publication order.
public static class RunDatasetBinder
{
    /// Only the requested producer contributes; executions and repeat iterations remain separate.
    public static IReadOnlyList<IReadOnlyList<StoredSample>> SeriesForProducer(TestRunRecord run, string key, Guid producer)
    {
        var candidates = run.Samples.Where(sample => string.Equals(sample.EffectiveMetricKey, key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Any(sample => sample.ProducerStepId is not null)) candidates = candidates.Where(sample => sample.ProducerStepId == producer).ToArray();
        return candidates.GroupBy(sample => (sample.ProducerStepId, sample.StepRunId, sample.StepPath, sample.LoopRunId, sample.LoopPath, sample.IterationIndex))
            .Select(group => (IReadOnlyList<StoredSample>)group.ToArray()).ToArray();
    }

    /// Extra MetricKeys in the run are kept; missing InputChannelKeys fail at eval.
    public static IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> SeriesByMetric(TestRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Samples
            .Where(sample => !string.IsNullOrWhiteSpace(sample.EffectiveMetricKey))
            .GroupBy(sample => sample.EffectiveMetricKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StoredSample>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }
}
