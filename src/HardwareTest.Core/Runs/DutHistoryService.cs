using System.Globalization;

namespace HardwareTest.Core.Runs;

public enum DutHistorySeverity
{
    Normal = 0,
    Watch = 1,
    Alert = 2,
}

public sealed class DutMetricDelta
{
    public required string Channel { get; init; }
    public double CurrentMean { get; init; }
    public double? PriorMean { get; init; }
    public double? PercentDelta { get; init; }
    public DutHistorySeverity Severity { get; init; }
}

public sealed class DutHistoryReport
{
    public int PriorRunCount { get; init; }
    public DutHistorySeverity OverallSeverity { get; init; }
    public string OperatorSummary { get; init; } = string.Empty;
    public IReadOnlyList<DutMetricDelta> Metrics { get; init; } = [];
}

public interface IDutHistoryService
{
    /// Compares channel means on <paramref name="current"/> to the last N prior runs
    /// with the same DUT serial and plan id (or plan name when ids differ in summaries).
    Task<DutHistoryReport> AnalyzeAsync(TestRunRecord current, CancellationToken cancellationToken = default);
}

/// Offline DUT wear / shift flags from local run.json history (no separate app).
public sealed class DutHistoryService : IDutHistoryService
{
    public const int DefaultPriorLimit = 10;
    public const double WatchPercentThreshold = 5.0;
    public const double AlertPercentThreshold = 10.0;

    private readonly IRunStore _runStore;
    private readonly int _priorLimit;

    public DutHistoryService(IRunStore runStore, int priorLimit = DefaultPriorLimit)
    {
        _runStore = runStore;
        _priorLimit = Math.Clamp(priorLimit, 1, 100);
    }

    public async Task<DutHistoryReport> AnalyzeAsync(
        TestRunRecord current,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(current.DutSerial))
        {
            return new DutHistoryReport
            {
                OperatorSummary = "DUT history needs a DUT serial.",
            };
        }

        if (string.IsNullOrWhiteSpace(current.PlanId) && string.IsNullOrWhiteSpace(current.PlanName))
        {
            return new DutHistoryReport
            {
                OperatorSummary = "DUT history needs a plan id or name.",
            };
        }

        var summaries = await _runStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidateIds = new List<string>();
        foreach (var summary in summaries.OrderByDescending(s => s.StartedAt))
        {
            if (candidateIds.Count >= _priorLimit)
            {
                break;
            }

            if (string.Equals(summary.RunId, current.RunId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(summary.DutSerial, current.DutSerial, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Summaries expose PlanName; match current PlanId or PlanName.
            if (!PlanMatches(summary, current))
            {
                continue;
            }

            candidateIds.Add(summary.RunId);
        }

        if (candidateIds.Count == 0)
        {
            return new DutHistoryReport
            {
                OperatorSummary = "No prior runs for this DUT and plan yet.",
            };
        }

        var priorMeans = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        var loadedPriors = 0;
        foreach (var runId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prior = await _runStore.LoadAsync(runId, cancellationToken).ConfigureAwait(false);
            if (prior is null || prior.Samples.Count == 0)
            {
                continue;
            }

            // Prefer PlanId when loading full records.
            if (!string.IsNullOrWhiteSpace(current.PlanId)
                && !string.IsNullOrWhiteSpace(prior.PlanId)
                && !string.Equals(prior.PlanId, current.PlanId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(prior.PlanName, current.PlanName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            loadedPriors++;
            foreach (var (channel, mean) in ChannelMeans(prior.Samples))
            {
                if (!priorMeans.TryGetValue(channel, out var list))
                {
                    list = [];
                    priorMeans[channel] = list;
                }

                list.Add(mean);
            }
        }

        var currentMeans = ChannelMeans(current.Samples);
        if (currentMeans.Count == 0)
        {
            return new DutHistoryReport
            {
                PriorRunCount = loadedPriors,
                OperatorSummary = $"Found {loadedPriors} prior run(s); current run has no samples to compare.",
            };
        }

        if (loadedPriors == 0)
        {
            return new DutHistoryReport
            {
                OperatorSummary = "No prior runs with samples for this DUT and plan yet.",
            };
        }

        var policyByKey = ResolveHistoryPolicies(current.Samples);
        var metrics = new List<DutMetricDelta>();
        var skippedUnknownPolicy = 0;
        foreach (var (channel, mean) in currentMeans.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!policyByKey.TryGetValue(channel, out var policy) || policy.Enabled is null)
            {
                // Unknown policy (legacy / absent HistoryEnabled) — do not invent defaults.
                skippedUnknownPolicy++;
                continue;
            }

            if (!policy.Enabled.Value)
            {
                continue;
            }

            var watch = policy.WatchPercent ?? WatchPercentThreshold;
            var alert = policy.AlertPercent ?? AlertPercentThreshold;
            if (alert < watch)
            {
                alert = watch;
            }

            double? priorMean = null;
            double? percentDelta = null;
            var severity = DutHistorySeverity.Normal;
            if (priorMeans.TryGetValue(channel, out var priors) && priors.Count > 0)
            {
                priorMean = priors.Average();
                if (Math.Abs(priorMean.Value) > 1e-12)
                {
                    percentDelta = (mean - priorMean.Value) / Math.Abs(priorMean.Value) * 100.0;
                    var abs = Math.Abs(percentDelta.Value);
                    if (abs >= alert)
                    {
                        severity = DutHistorySeverity.Alert;
                    }
                    else if (abs >= watch)
                    {
                        severity = DutHistorySeverity.Watch;
                    }
                }
            }

            metrics.Add(new DutMetricDelta
            {
                Channel = channel,
                CurrentMean = mean,
                PriorMean = priorMean,
                PercentDelta = percentDelta,
                Severity = severity,
            });
        }

        if (metrics.Count == 0 && skippedUnknownPolicy > 0)
        {
            return new DutHistoryReport
            {
                PriorRunCount = loadedPriors,
                OperatorSummary = current.IsLegacy
                    ? "No comparison available (legacy run record)."
                    : "No comparison available (history policy unknown for metrics).",
            };
        }

        var overall = metrics.Count == 0
            ? DutHistorySeverity.Normal
            : metrics.Max(m => m.Severity);
        return new DutHistoryReport
        {
            PriorRunCount = loadedPriors,
            OverallSeverity = overall,
            Metrics = metrics,
            OperatorSummary = BuildSummary(overall, metrics, loadedPriors),
        };
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

    private readonly record struct HistoryPolicy(bool? Enabled, double? WatchPercent, double? AlertPercent);

    /// Prefer the latest sample by Timestamp (then Channel) for each metric key's history stamps.
    private static Dictionary<string, HistoryPolicy> ResolveHistoryPolicies(IEnumerable<StoredSample> samples)
    {
        var map = new Dictionary<string, HistoryPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples
                     .Where(s => !string.IsNullOrWhiteSpace(s.EffectiveMetricKey))
                     .OrderBy(s => s.Timestamp)
                     .ThenBy(s => s.EffectiveMetricKey, StringComparer.OrdinalIgnoreCase))
        {
            map[sample.EffectiveMetricKey] = new HistoryPolicy(
                sample.HistoryEnabled,
                sample.HistoryWatchPercent,
                sample.HistoryAlertPercent);
        }

        return map;
    }

    private static string BuildSummary(
        DutHistorySeverity overall,
        IReadOnlyList<DutMetricDelta> metrics,
        int priorCount)
    {
        var flagged = metrics
            .Where(m => m.Severity != DutHistorySeverity.Normal && m.PercentDelta is not null)
            .OrderByDescending(m => Math.Abs(m.PercentDelta!.Value))
            .FirstOrDefault();

        if (flagged is null)
        {
            return $"DUT history OK vs last {priorCount} run(s).";
        }

        var direction = flagged.PercentDelta!.Value >= 0 ? "above" : "below";
        var pct = Math.Abs(flagged.PercentDelta.Value).ToString("0.#", CultureInfo.InvariantCulture);
        var label = overall == DutHistorySeverity.Alert ? "Alert" : "Watch";
        return $"{label}: {flagged.Channel} mean is {pct}% {direction} your last {priorCount} passes.";
    }
}
