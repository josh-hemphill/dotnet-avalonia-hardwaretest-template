using System.Globalization;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Maps MetricPreview (+ optional recording events) onto operator tile shapes without Avalonia.
public sealed record AuthoringPreviewChrome(
    bool IsGauge,
    bool IsChart,
    bool IsStrip,
    bool IsText,
    string MetricKey,
    string ValueText,
    string LimitsText,
    bool ShowBand,
    double Value,
    double? LimitLow,
    double? LimitHigh,
    string YUnit,
    IReadOnlyList<double> Xs,
    IReadOnlyList<double> Ys,
    bool UsesTimeAxis,
    IReadOnlyList<MeasurementEventMark> Events,
    IReadOnlyList<(double T0, double T1)> Spans,
    double DurationSec);

/// Builds AuthoringPreviewChrome from canned or recorded preview samples.
public static class AuthoringPreviewChromeBuilder
{
    public static AuthoringPreviewChrome Empty { get; } = new(
        false,
        false,
        false,
        true,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        0,
        null,
        null,
        string.Empty,
        [],
        [],
        false,
        [],
        [],
        0);

    /// Gauge / chart / timing chrome for the current metric preview.
    public static AuthoringPreviewChrome From(MetricPreview preview, IReadOnlyList<StoredEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var marks = ToMarks(events);
        var kind = preview.TileKind;
        var ys = preview.CannedSamples;
        var xs = IndexXs(ys.Count);
        var isGauge = kind is PresentationTileKind.Scalar or PresentationTileKind.Passband;
        var isChart = kind == PresentationTileKind.Timeseries;
        var isStrip = kind == PresentationTileKind.Timing;
        var spans = isChart || isStrip
            ? OutOfBandSpans(xs, ys, preview.LimitLow, preview.LimitHigh)
            : [];
        var duration = StripDurationSec(marks, xs.Count == 0 ? null : xs[^1]);
        return new AuthoringPreviewChrome(
            isGauge,
            isChart,
            isStrip,
            kind is null,
            preview.ChannelKey,
            FormatValue(preview.CannedValue, preview.YUnit),
            FormatLimits(preview.LimitLow, preview.LimitHigh, preview.YUnit),
            kind == PresentationTileKind.Passband && (preview.LimitLow is not null || preview.LimitHigh is not null),
            preview.CannedValue,
            preview.LimitLow,
            preview.LimitHigh,
            preview.YUnit,
            xs,
            ys,
            false,
            marks,
            spans,
            duration);
    }

    private static IReadOnlyList<double> IndexXs(int count)
    {
        var xs = new double[count];
        for (var i = 0; i < count; i++)
        {
            xs[i] = i;
        }

        return xs;
    }

    private static IReadOnlyList<MeasurementEventMark> ToMarks(IReadOnlyList<StoredEvent>? events)
        => events is null
            ? []
            : events.Select(e => new MeasurementEventMark(e.Name, e.ElapsedMs, e.Label, e.Value, e.StepPath)).ToArray();

    private static IReadOnlyList<(double T0, double T1)> OutOfBandSpans(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        double? limitLow,
        double? limitHigh)
    {
        if (ys.Count == 0 || (limitLow is null && limitHigh is null))
        {
            return [];
        }

        var n = Math.Min(xs.Count, ys.Count);
        var spans = new List<(double T0, double T1)>();
        var runStart = -1;
        for (var i = 0; i < n; i++)
        {
            var oob = (limitLow is { } lo && ys[i] < lo) || (limitHigh is { } hi && ys[i] > hi);
            if (oob && runStart < 0)
            {
                runStart = i;
            }
            else if (!oob && runStart >= 0)
            {
                spans.Add((xs[runStart], xs[i - 1]));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            spans.Add((xs[runStart], xs[n - 1]));
        }

        return spans;
    }

    private static double StripDurationSec(IReadOnlyList<MeasurementEventMark> events, double? axisEnd)
    {
        var eventMax = 0.0;
        foreach (var mark in events)
        {
            var sec = mark.ElapsedMs / 1000.0;
            if (sec > eventMax)
            {
                eventMax = sec;
            }
        }

        return Math.Max(eventMax, axisEnd ?? 0);
    }

    private static string FormatValue(double value, string? unit)
    {
        var v = value.ToString("G6", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? v : $"{v} {unit}";
    }

    private static string FormatLimits(double? low, double? high, string? unit)
    {
        if (low is null && high is null)
        {
            return string.Empty;
        }

        var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        if (low is not null && high is not null)
        {
            return $"[{low.Value.ToString("G6", CultureInfo.InvariantCulture)} … {high.Value.ToString("G6", CultureInfo.InvariantCulture)}]{suffix}";
        }

        if (low is not null)
        {
            return $"≥ {low.Value.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
        }

        return $"≤ {high!.Value.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
    }
}
