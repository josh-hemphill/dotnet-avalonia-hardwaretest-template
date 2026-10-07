namespace HardwareTest.OpenTap.Plugins.Basic;

public readonly record struct ChannelAverageResult(double Average, bool Passed);

/// Shared inclusive channel-average criterion for execution and recording preview.
public static class ChannelAverageEvaluator
{
    public static ChannelAverageResult Evaluate(IReadOnlyList<double> samples, double threshold)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(threshold))
        {
            throw new InvalidOperationException("CHANNEL_AVERAGE: threshold must be finite.");
        }

        if (samples.Count == 0 || samples.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidOperationException("CHANNEL_AVERAGE: requires nonempty finite samples.");
        }

        // Scale before summing so finite samples near double.MaxValue do not overflow.
        var scale = samples.Max(value => Math.Abs(value));
        var average = scale == 0 ? 0 : samples.Select(value => value / scale).Average() * scale;
        if (!double.IsFinite(average))
        {
            throw new InvalidOperationException("CHANNEL_AVERAGE: average must be finite.");
        }

        return new ChannelAverageResult(average, average >= threshold);
    }
}
