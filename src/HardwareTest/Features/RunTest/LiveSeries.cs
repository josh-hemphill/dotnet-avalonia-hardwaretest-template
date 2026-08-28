using System.Globalization;

namespace HardwareTest.Features.RunTest;

/// Identifies one live timeseries by step path and metric key.
public readonly record struct LiveSeriesKey(string StepPath, string MetricKey)
{
    public string DisplayName
    {
        get
        {
            var step = StepPath;
            var slash = step.LastIndexOf('/');
            if (slash >= 0 && slash < step.Length - 1)
            {
                step = step[(slash + 1)..];
            }

            return string.IsNullOrWhiteSpace(step) ? MetricKey : $"{MetricKey} · {step}";
        }
    }
}

/// Bounded ring buffer for one live metric (values + elapsed seconds from first sample).
public sealed class LiveSeriesBuffer
{
    public const int Capacity = 2048;

    private readonly double[] _values = new double[Capacity];
    private readonly double[] _elapsed = new double[Capacity];
    private int _count;
    private int _write;
    private DateTimeOffset? _t0;

    public LiveSeriesBuffer(LiveSeriesKey key) => Key = key;

    public LiveSeriesKey Key { get; }
    public string? Unit { get; private set; }
    public double? LimitLow { get; private set; }
    public double? LimitHigh { get; private set; }
    public double LatestValue { get; private set; }
    public DateTimeOffset? LatestTimestamp { get; private set; }
    public int Count => _count;

    public bool IsOutOfBand
        => (LimitLow is { } lo && LatestValue < lo) || (LimitHigh is { } hi && LatestValue > hi);

    /// Appends one sample, dropping the oldest when the ring is full.
    public void Append(double value, DateTimeOffset timestamp, double? limitLow, double? limitHigh, string? unit)
    {
        _t0 ??= timestamp;
        var elapsed = Math.Max(0, (timestamp - _t0.Value).TotalSeconds);
        _values[_write] = value;
        _elapsed[_write] = elapsed;
        _write = (_write + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }

        LatestValue = value;
        LatestTimestamp = timestamp;
        LimitLow = limitLow ?? LimitLow;
        LimitHigh = limitHigh ?? LimitHigh;
        if (!string.IsNullOrWhiteSpace(unit))
        {
            Unit = unit;
        }
    }

    /// Copies the windowed series into a snapshot (oldest → newest).
    public LiveSeriesSnapshot Snapshot(TimeSpan? window)
    {
        if (_count == 0)
        {
            return LiveSeriesSnapshot.Empty(Key, Unit);
        }

        var start = _count == Capacity ? _write : 0;
        var lastElapsed = _elapsed[(start + _count - 1) % Capacity];
        var minElapsed = window is { } span ? lastElapsed - span.TotalSeconds : double.NegativeInfinity;

        var xs = new double[_count];
        var ys = new double[_count];
        var n = 0;
        for (var i = 0; i < _count; i++)
        {
            var idx = (start + i) % Capacity;
            if (_elapsed[idx] < minElapsed)
            {
                continue;
            }

            xs[n] = _elapsed[idx];
            ys[n] = _values[idx];
            n++;
        }

        return new LiveSeriesSnapshot(
            Key,
            xs,
            ys,
            n,
            Unit,
            LimitLow,
            LimitHigh,
            LatestValue,
            LatestTimestamp,
            IsOutOfBand);
    }
}

/// Immutable plot-ready slice of a live series.
public sealed record LiveSeriesSnapshot(
    LiveSeriesKey Key,
    double[] Xs,
    double[] Ys,
    int Length,
    string? Unit,
    double? LimitLow,
    double? LimitHigh,
    double LatestValue,
    DateTimeOffset? LatestTimestamp,
    bool IsOutOfBand)
{
    public static LiveSeriesSnapshot Empty(LiveSeriesKey key, string? unit)
        => new(key, [], [], 0, unit, null, null, 0, null, false);

    public string ValueText
    {
        get
        {
            if (Length == 0)
            {
                return "—";
            }

            var v = LatestValue.ToString("G6", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(Unit) ? v : $"{v} {Unit}";
        }
    }

    public string BandText
    {
        get
        {
            if (LimitLow is null && LimitHigh is null)
            {
                return "No limits";
            }

            var suffix = string.IsNullOrWhiteSpace(Unit) ? string.Empty : $" {Unit}";
            if (LimitLow is { } lo && LimitHigh is { } hi)
            {
                var range = $"{lo.ToString("G6", CultureInfo.InvariantCulture)}–{hi.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
                return IsOutOfBand ? $"Out of band {range}" : $"Within {range}";
            }

            if (LimitLow is { } onlyLow)
            {
                return IsOutOfBand
                    ? $"Below {onlyLow.ToString("G6", CultureInfo.InvariantCulture)}{suffix}"
                    : $"≥ {onlyLow.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
            }

            return IsOutOfBand
                ? $"Above {LimitHigh!.Value.ToString("G6", CultureInfo.InvariantCulture)}{suffix}"
                : $"≤ {LimitHigh!.Value.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
        }
    }
}

/// One selectable series in the Chart workspace toolbar.
public sealed class LiveSeriesItemViewModel
{
    public LiveSeriesItemViewModel(LiveSeriesKey key, string? unit, bool isOutOfBand)
    {
        Key = key;
        DisplayName = key.DisplayName;
        Unit = unit ?? string.Empty;
        IsOutOfBand = isOutOfBand;
    }

    public LiveSeriesKey Key { get; }
    public string DisplayName { get; }
    public string Unit { get; }
    public bool IsOutOfBand { get; }
}
