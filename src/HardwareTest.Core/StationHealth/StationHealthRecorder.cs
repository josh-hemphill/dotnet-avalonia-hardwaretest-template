using HardwareTest.Core.Runs;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.StationHealth;

/// Builds a station health record from a terminal stationHealth run.
public static class StationHealthRecorder
{
    public const string OffsetMetric = "cal.dc.offset";
    public const string AgeMetric = "cal.age.hours";

    /// Persist Pass/Fail stationHealth runs that published cal Scalars. DUT / empty runs never overwrite.
    public static bool ShouldPersist(string? programKind, RunResult result, IReadOnlyList<StoredSample>? samples = null)
        => ProgramKinds.IsStationHealth(programKind)
           && result is RunResult.Passed or RunResult.Failed
           && HasHealthMetrics(samples);

    public static bool HasHealthMetrics(IReadOnlyList<StoredSample>? samples)
        => samples is { Count: > 0 }
           && samples.Any(s =>
               string.Equals(s.EffectiveMetricKey, OffsetMetric, StringComparison.OrdinalIgnoreCase)
               || string.Equals(s.EffectiveMetricKey, AgeMetric, StringComparison.OrdinalIgnoreCase));

    public static StationHealthRecord Create(
        string profileId,
        string? programId,
        string? runId,
        RunResult result,
        IReadOnlyList<StoredSample> samples,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(clock);

        var metrics = samples
            .Where(s => !string.IsNullOrWhiteSpace(s.EffectiveMetricKey))
            .Select(s => new StationHealthMetric
            {
                Name = s.EffectiveMetricKey,
                Value = s.Value,
                Unit = string.IsNullOrWhiteSpace(s.Unit) ? null : s.Unit,
                LimitLow = s.LimitLow,
                LimitHigh = s.LimitHigh,
            })
            .ToList();

        var age = metrics.FirstOrDefault(m =>
            string.Equals(m.Name, AgeMetric, StringComparison.OrdinalIgnoreCase));

        return new StationHealthRecord
        {
            SchemaVersion = Serialization.SchemaVersions.StationHealthRecord,
            ProfileId = string.IsNullOrWhiteSpace(profileId)
                ? FileStationHealthStore.DefaultProfileId
                : profileId.Trim(),
            MeasuredAt = clock.UtcNow,
            Source = ResolveSource(samples),
            Verdict = result switch
            {
                RunResult.Passed => StationHealthVerdicts.Pass,
                RunResult.Failed => StationHealthVerdicts.Fail,
                _ => StationHealthVerdicts.Error,
            },
            ProgramId = programId,
            RunId = runId,
            Metrics = metrics,
            MaxAgeHours = age?.LimitHigh,
        };
    }

    public static async Task TryWriteAsync(
        IStationHealthStore store,
        IClock clock,
        string? programKind,
        string profileId,
        string? programId,
        string? runId,
        RunResult result,
        IReadOnlyList<StoredSample> samples,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldPersist(programKind, result, samples))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(store);
        var record = Create(profileId, programId, runId, result, samples, clock);
        await store.WriteAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveSource(IReadOnlyList<StoredSample> samples)
    {
        if (samples.Any(s =>
                string.Equals(s.ResultSource, SampleResultSources.Cached, StringComparison.OrdinalIgnoreCase)))
        {
            return StationHealthSources.Recalled;
        }

        return StationHealthSources.Queried;
    }
}
