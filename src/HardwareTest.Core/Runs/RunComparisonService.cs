using HardwareTest.Core.Text;

namespace HardwareTest.Core.Runs;

/// One metric row in a run-to-run comparison (aligned by EffectiveMetricKey).
public sealed class RunComparisonMetric
{
    public required string MetricKey { get; init; }
    public double? CurrentMean { get; init; }
    public double? PreviousMean { get; init; }
    public double? PercentDelta { get; init; }
    public string? Unit { get; init; }
    public bool Unavailable { get; init; }
    public string UnavailableReason { get; init; } = string.Empty;
}

/// Typed comparison of the opened run vs a previous same DUT+plan run.
public sealed class RunComparisonReport
{
    public string CurrentRunId { get; init; } = string.Empty;
    public string? PreviousRunId { get; init; }
    public DateTimeOffset? PreviousStartedAt { get; init; }
    public string OperatorSummary { get; init; } = string.Empty;
    public IReadOnlyList<RunComparisonMetric> Metrics { get; init; } = [];
}

public interface IRunComparisonService
{
    /// Compares <paramref name="current"/> to the latest earlier run with the same DUT serial and plan.
    Task<RunComparisonReport> CompareToPreviousAsync(
        TestRunRecord current,
        CancellationToken cancellationToken = default);

    Task<RunComparisonReport> CompareAsync(
        string currentRunId,
        string previousRunId,
        CancellationToken cancellationToken = default);
}

/// Local on-disk comparison; missing metrics are listed as unavailable (does not throw).
public sealed class RunComparisonService : IRunComparisonService
{
    private readonly IRunStore _runStore;

    public RunComparisonService(IRunStore runStore) => _runStore = runStore;

    public async Task<RunComparisonReport> CompareToPreviousAsync(
        TestRunRecord current,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(current.DutSerial))
        {
            return Empty(current.RunId, "Compare with previous needs a DUT serial.");
        }

        if (string.IsNullOrWhiteSpace(current.PlanId) && string.IsNullOrWhiteSpace(current.PlanName))
        {
            return Empty(current.RunId, "Compare with previous needs a plan id or name.");
        }

        var previousId = await FindPreviousRunIdAsync(current, cancellationToken).ConfigureAwait(false);
        if (previousId is null)
        {
            return Empty(current.RunId, "No previous run for this DUT and plan.");
        }

        return await CompareAsync(current.RunId, previousId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RunComparisonReport> CompareAsync(
        string currentRunId,
        string previousRunId,
        CancellationToken cancellationToken = default)
    {
        var current = await _runStore.LoadAsync(currentRunId, cancellationToken).ConfigureAwait(false);
        var previous = await _runStore.LoadAsync(previousRunId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return Empty(currentRunId, "Current run not found.");
        }

        if (previous is null)
        {
            return Empty(currentRunId, "Previous run not found.");
        }

        var currentMeans = ChannelMeans(current.Samples);
        var previousMeans = ChannelMeans(previous.Samples);
        var units = UnitsByKey(current.Samples);
        var keys = currentMeans.Keys
            .Union(previousMeans.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var metrics = new List<RunComparisonMetric>(keys.Count);
        foreach (var key in keys)
        {
            currentMeans.TryGetValue(key, out var cur);
            previousMeans.TryGetValue(key, out var prev);
            var hasCurrent = currentMeans.ContainsKey(key);
            var hasPrevious = previousMeans.ContainsKey(key);
            if (!hasCurrent || !hasPrevious)
            {
                metrics.Add(new RunComparisonMetric
                {
                    MetricKey = key,
                    CurrentMean = hasCurrent ? cur : null,
                    PreviousMean = hasPrevious ? prev : null,
                    Unit = units.GetValueOrDefault(key),
                    Unavailable = true,
                    UnavailableReason = hasCurrent
                        ? "Not in previous run"
                        : "Not in current run",
                });
                continue;
            }

            double? percent = null;
            if (Math.Abs(prev) > 1e-12)
            {
                percent = (cur - prev) / Math.Abs(prev) * 100.0;
            }

            metrics.Add(new RunComparisonMetric
            {
                MetricKey = key,
                CurrentMean = cur,
                PreviousMean = prev,
                PercentDelta = percent,
                Unit = units.GetValueOrDefault(key),
            });
        }

        var shortPrev = ShortId.Display(previous.RunId);
        return new RunComparisonReport
        {
            CurrentRunId = current.RunId,
            PreviousRunId = previous.RunId,
            PreviousStartedAt = previous.StartedAt,
            Metrics = metrics,
            OperatorSummary = metrics.Count == 0
                ? $"Previous run {shortPrev} has no overlapping metrics."
                : $"Compared with previous {shortPrev} ({previous.StartedAt:u}).",
        };
    }

    private async Task<string?> FindPreviousRunIdAsync(TestRunRecord current, CancellationToken cancellationToken)
    {
        var summaries = await _runStore.ListAsync(cancellationToken).ConfigureAwait(false);
        return summaries
            .Where(s =>
                !string.Equals(s.RunId, current.RunId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.DutSerial, current.DutSerial, StringComparison.OrdinalIgnoreCase)
                && PlanMatches(s, current)
                && s.StartedAt < current.StartedAt)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.RunId)
            .FirstOrDefault();
    }

    private static bool PlanMatches(TestRunSummary summary, TestRunRecord current)
        => string.Equals(summary.PlanName, current.PlanName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(summary.PlanName, current.PlanId, StringComparison.OrdinalIgnoreCase)
           || (!string.IsNullOrWhiteSpace(summary.PlanId)
               && (string.Equals(summary.PlanId, current.PlanId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(summary.PlanId, current.PlanName, StringComparison.OrdinalIgnoreCase)));

    private static Dictionary<string, double> ChannelMeans(IEnumerable<StoredSample> samples)
        => samples
            .Where(s => !string.IsNullOrWhiteSpace(s.EffectiveMetricKey))
            .GroupBy(s => s.EffectiveMetricKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Average(s => s.Value), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> UnitsByKey(IEnumerable<StoredSample> samples)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples.Where(s =>
                     !string.IsNullOrWhiteSpace(s.EffectiveMetricKey) && !string.IsNullOrWhiteSpace(s.Unit)))
        {
            map[sample.EffectiveMetricKey] = sample.Unit!;
        }

        return map;
    }

    private static RunComparisonReport Empty(string currentRunId, string summary)
        => new()
        {
            CurrentRunId = currentRunId,
            OperatorSummary = summary,
        };
}
