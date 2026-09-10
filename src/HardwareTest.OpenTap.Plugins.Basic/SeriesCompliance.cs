namespace HardwareTest.OpenTap.Plugins.Basic;

/// Series-wide in-band modes (Phase M). Not a DisplayRole.
public static class SeriesComplianceModes
{
    public const string None = "none";
    public const string AllSamples = "allSamples";
    public const string Dwell = "dwell";

    public static IEnumerable<string> Choices { get; } = [None, AllSamples, Dwell];

    /// True when the mode evaluates samples against limits.
    public static bool IsEnabled(string? mode)
        => string.Equals(mode, AllSamples, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, Dwell, StringComparison.OrdinalIgnoreCase);

    /// Inclusive band check; missing bound is ignored.
    public static bool IsOutOfBand(double value, double? limitLow, double? limitHigh)
        => (limitLow is { } lo && value < lo) || (limitHigh is { } hi && value > hi);

    /// Updates contiguous out-of-band dwell. True when this sample should Fail the step.
    public static bool ShouldFailSample(
        string? mode,
        bool failWhenOutOfBand,
        double value,
        double? limitLow,
        double? limitHigh,
        double? dwellLimitMs,
        int intervalMs,
        ref double dwellMs)
    {
        var outOfBand = IsOutOfBand(value, limitLow, limitHigh);
        if (outOfBand)
        {
            dwellMs += Math.Max(0, intervalMs);
        }
        else
        {
            dwellMs = 0;
        }

        if (!failWhenOutOfBand || !IsEnabled(mode))
        {
            return false;
        }

        if (string.Equals(mode, AllSamples, StringComparison.OrdinalIgnoreCase) && outOfBand)
        {
            return true;
        }

        return string.Equals(mode, Dwell, StringComparison.OrdinalIgnoreCase)
               && dwellLimitMs is { } dwellLimit
               && dwellMs > dwellLimit;
    }

    /// Parses comma-separated scripted voltages; empty/invalid tokens are skipped.
    public static IReadOnlyList<double> ParseScriptedValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var values = new List<double>();
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                values.Add(v);
            }
        }

        return values;
    }

    /// Worst distance outside the band (0 when in band or no limits).
    public static double MaxExcursion(IReadOnlyList<double> values, double? limitLow, double? limitHigh)
    {
        var max = 0.0;
        foreach (var value in values)
        {
            if (limitLow is { } lo && value < lo)
            {
                max = Math.Max(max, lo - value);
            }

            if (limitHigh is { } hi && value > hi)
            {
                max = Math.Max(max, value - hi);
            }
        }

        return max;
    }

    /// Percent of samples inside the band (100 when empty or no limits).
    public static double InBandPercent(IReadOnlyList<double> values, double? limitLow, double? limitHigh)
    {
        if (values.Count == 0 || (limitLow is null && limitHigh is null))
        {
            return 100;
        }

        var inBand = values.Count(v => !IsOutOfBand(v, limitLow, limitHigh));
        return 100.0 * inBand / values.Count;
    }

    /// Longest contiguous out-of-band run in milliseconds (interval * count).
    public static double MaxOutOfBandMs(IReadOnlyList<double> values, double? limitLow, double? limitHigh, int intervalMs)
    {
        var span = Math.Max(0, intervalMs);
        var best = 0;
        var run = 0;
        foreach (var value in values)
        {
            if (IsOutOfBand(value, limitLow, limitHigh))
            {
                run++;
                best = Math.Max(best, run);
            }
            else
            {
                run = 0;
            }
        }

        return best * span;
    }
}
