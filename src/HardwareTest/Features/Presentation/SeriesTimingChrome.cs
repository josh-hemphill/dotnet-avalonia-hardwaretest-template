using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Features.Presentation;

/// Elapsed-axis helpers for Chart markers and the timing strip.
public static class SeriesTimingChrome
{
    /// Contiguous out-of-band runs as elapsed-second spans.
    public static IReadOnlyList<(double T0, double T1)> OutOfBandSpans(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        int length,
        double? limitLow,
        double? limitHigh)
    {
        if (length <= 0 || (limitLow is null && limitHigh is null))
        {
            return [];
        }

        var n = Math.Min(length, Math.Min(xs.Count, ys.Count));
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

    /// Strip length in seconds: event elapsed, plus the last time-axis X when present.
    public static double StripDurationSec(
        IEnumerable<MeasurementEventMark> events,
        double? timeAxisEndSec)
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

        return Math.Max(eventMax, timeAxisEndSec ?? 0);
    }

    /// Latest event at or before elapsedMs; otherwise the last event.
    public static MeasurementEventMark? LatestEventAt(
        IReadOnlyList<MeasurementEventMark> events,
        double elapsedMs)
    {
        MeasurementEventMark? best = null;
        foreach (var mark in events)
        {
            if (mark.ElapsedMs <= elapsedMs
                && (best is null || mark.ElapsedMs > best.ElapsedMs))
            {
                best = mark;
            }
        }

        return best ?? (events.Count > 0 ? events[^1] : null);
    }

    /// Toolbar elapsed label (not a growing hero).
    public static string FormatElapsed(double? elapsedMs)
    {
        if (elapsedMs is not { } ms)
        {
            return string.Empty;
        }

        return ms >= 1000
            ? $"{(ms / 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} s"
            : $"{ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} ms";
    }

    /// Toolbar event label (`name:label` when a label exists).
    public static string FormatEventLabel(MeasurementEventMark? mark)
    {
        if (mark is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(mark.Label) ? mark.Name : $"{mark.Name}:{mark.Label}";
    }

    /// Converts stored events to live marks.
    public static IReadOnlyList<MeasurementEventMark> ToMarks(IEnumerable<StoredEvent> events)
        => events
            .Select(e => new MeasurementEventMark(e.Name, e.ElapsedMs, e.Label, e.Value, e.StepPath))
            .ToList();

    /// Chart tick positions (elapsed seconds) and toolbar-style labels.
    public static IReadOnlyList<(double ElapsedSec, string Label)> ToPlotTicks(
        IEnumerable<MeasurementEventMark> events)
        => events.Select(e => (e.ElapsedMs / 1000.0, FormatEventLabel(e))).ToList();
}
