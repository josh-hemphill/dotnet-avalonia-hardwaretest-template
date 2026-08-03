using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Features.Presentation;

/// Maps DisplayRole strings to tile kinds (no Avalonia types).
public static class PresentationRoleMap
{
    public const string Timeseries = "timeseries";
    public const string Scalar = "scalar";
    public const string Passband = "passband";

    public const int MaxResultsTimeseriesCharts = 6;
    public const int MaxRunGaugeTiles = 4;

    /// Resolves a known role to a tile kind; unknown roles return null (text-only degradation).
    public static PresentationTileKind? TryMapRole(string? displayRole)
    {
        if (string.IsNullOrWhiteSpace(displayRole))
        {
            return null;
        }

        if (string.Equals(displayRole, Timeseries, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Timeseries;
        }

        if (string.Equals(displayRole, Scalar, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Scalar;
        }

        if (string.Equals(displayRole, Passband, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Passband;
        }

        return null;
    }

    /// True when the live sample should append to the chronological Run plot.
    public static bool IsTimeseriesPlotSample(MeasurementSampleEvent sample)
    {
        var kind = TryMapRole(sample.DisplayRole);
        if (kind == PresentationTileKind.Timeseries)
        {
            return true;
        }

        // Backward-compatible Sample publishes with no DisplayRole still drive the live plot.
        return string.IsNullOrWhiteSpace(sample.DisplayRole);
    }

    /// True when the live sample should upsert a Run gauge tile (scalar/passband only).
    public static bool IsRunGaugeSample(MeasurementSampleEvent sample)
        => TryMapRole(sample.DisplayRole) is PresentationTileKind.Scalar or PresentationTileKind.Passband;

    /// Creates or updates a Run gauge tile from a scalar/passband sample. Returns null when not a gauge role.
    public static PresentationTileViewModel? UpsertRunGauge(
        IList<PresentationTileViewModel> tiles,
        MeasurementSampleEvent sample,
        string? stepPath)
    {
        if (!IsRunGaugeSample(sample))
        {
            return null;
        }

        var kind = TryMapRole(sample.DisplayRole)!.Value;
        var key = sample.EffectiveMetricKey;
        var path = stepPath ?? string.Empty;
        var existing = tiles.FirstOrDefault(t =>
            string.Equals(t.MetricKey, key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.StepPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new PresentationTileViewModel(key, kind, sample.DisplayRole, sample.Unit, path);
            tiles.Add(existing);
        }
        else
        {
            existing.StepPath = path;
        }

        existing.Apply(sample.Value, sample.LimitLow, sample.LimitHigh);
        return existing;
    }

    /// Builds Results tiles from stored samples grouped by EffectiveMetricKey.
    public static IReadOnlyList<PresentationTileViewModel> BuildFromStoredSamples(
        IEnumerable<StoredSample> samples)
    {
        var groups = samples
            .GroupBy(s => s.EffectiveMetricKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tiles = new List<PresentationTileViewModel>();
        var timeseriesCount = 0;
        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(s => s.IterationIndex ?? int.MaxValue)
                .ThenBy(s => s.Timestamp)
                .ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var last = ordered[^1];
            var kind = TryMapRole(last.DisplayRole);
            if (kind is null)
            {
                // No role: treat multi-point as timeseries, single as text skip for widgets.
                kind = ordered.Length > 1 ? PresentationTileKind.Timeseries : null;
            }

            if (kind is null)
            {
                continue;
            }

            if (kind == PresentationTileKind.Timeseries)
            {
                if (timeseriesCount >= MaxResultsTimeseriesCharts)
                {
                    continue;
                }

                timeseriesCount++;
            }

            var tile = new PresentationTileViewModel(
                group.Key,
                kind.Value,
                last.DisplayRole,
                last.Unit,
                last.StepPath);
            tile.Apply(last.Value, last.LimitLow, last.LimitHigh);
            if (kind == PresentationTileKind.Timeseries)
            {
                tile.SetSeries(ordered.Select(s => s.Value).ToArray());
            }

            tiles.Add(tile);
        }

        return tiles
            .OrderByDescending(t => t.IsGauge)
            .ThenBy(t => t.MetricKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
