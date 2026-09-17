using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Groups a run's published samples by EffectiveMetricKey.
public static class RunDatasetBinder
{
    /// Extra MetricKeys in the run are kept; missing InputChannelKeys fail at eval.
    public static IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> SeriesByMetric(TestRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Samples
            .Where(sample => !string.IsNullOrWhiteSpace(sample.EffectiveMetricKey))
            .GroupBy(sample => sample.EffectiveMetricKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StoredSample>)group
                    .OrderBy(sample => sample.ElapsedMs ?? double.PositiveInfinity)
                    .ThenBy(sample => sample.Timestamp)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }
}
