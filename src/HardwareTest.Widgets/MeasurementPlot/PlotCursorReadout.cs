using System.Globalization;

namespace HardwareTest.Widgets.MeasurementPlot;

/// Nearest-sample readout for a vertical chart cursor. Avalonia-free so unit tests can cover it.
public static class PlotCursorReadout
{
    /// Pointer movement of this many pixels (inclusive) is a pan, not a tap.
    public const double TapSlopPx = 10;

    /// True when the press-to-release delta is strictly inside the tap slop.
    public static bool IsTap(double dx, double dy)
        => (dx * dx) + (dy * dy) < TapSlopPx * TapSlopPx;

    /// Finds the sample whose X (elapsed seconds, or index when <paramref name="xs"/> is short) is closest to <paramref name="x"/>.
    public static bool TryNearestSample(
        ReadOnlySpan<double> xs,
        ReadOnlySpan<double> ys,
        int length,
        double x,
        out int index,
        out double sampleX,
        out double sampleY)
    {
        index = -1;
        sampleX = 0;
        sampleY = 0;
        var n = Math.Min(length, ys.Length);
        if (n <= 0)
        {
            return false;
        }

        var timed = xs.Length >= n;
        var best = double.PositiveInfinity;
        for (var i = 0; i < n; i++)
        {
            var xi = timed ? xs[i] : i;
            var distance = Math.Abs(xi - x);
            if (distance >= best)
            {
                continue;
            }

            best = distance;
            index = i;
            sampleX = xi;
            sampleY = ys[i];
        }

        return index >= 0;
    }

    /// Formats a snapped Y with an optional unit, matching live toolbar style.
    public static string FormatValue(double y, string? unit)
    {
        var value = y.ToString("G6", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? value : $"{value} {unit}";
    }

    /// Formats cursor Y against the same limit band copy the latest-sample toolbar uses.
    public static string FormatBand(double y, double? limitLow, double? limitHigh, string? unit)
    {
        if (limitLow is null && limitHigh is null)
        {
            return "No limits";
        }

        var oob = (limitLow is { } lo && y < lo) || (limitHigh is { } hi && y > hi);
        var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        if (limitLow is { } low && limitHigh is { } high)
        {
            var range =
                $"{low.ToString("G6", CultureInfo.InvariantCulture)}–{high.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
            return oob ? $"Out of band {range}" : $"Within {range}";
        }

        if (limitLow is { } onlyLow)
        {
            var text = onlyLow.ToString("G6", CultureInfo.InvariantCulture) + suffix;
            return oob ? $"Below {text}" : $"≥ {text}";
        }

        var highText = limitHigh!.Value.ToString("G6", CultureInfo.InvariantCulture) + suffix;
        return oob ? $"Above {highText}" : $"≤ {highText}";
    }
}

/// Raised when the operator places, moves, or clears the plot readout cursor.
public sealed class PlotCursorChangedEventArgs : EventArgs
{
    public double? X { get; init; }
    public double? Y { get; init; }
    public int? SampleIndex { get; init; }
}
