using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

/// Preview/CI TF apply: TimeBase → RequireUniform → Filter/FiltFilt.
public static class TransferFunctionEval
{
    public static IReadOnlyList<StoredSample> Apply(
        TransferFunctionAlgorithm tf,
        IReadOnlyList<StoredSample> input,
        string outputChannelKey)
    {
        ArgumentNullException.ThrowIfNull(tf);
        ArgumentNullException.ThrowIfNull(input);
        var elapsed = TransferFunctionTimeBase.ElapsedMs(input);
        var values = TransferFunctionTimeBase.Values(input);
        try
        {
            TransferFunctionGrid.RequireUniform(elapsed, tf.TsSeconds);
            var y = string.Equals(tf.Method, "filtfilt", StringComparison.Ordinal)
                ? TransferFunctionFilter.FiltFilt(tf.Numerator, tf.Denominator, values)
                : TransferFunctionFilter.Filter(tf.Numerator, tf.Denominator, values);
            var samples = new StoredSample[y.Length];
            for (var i = 0; i < y.Length; i++)
            {
                samples[i] = new StoredSample
                {
                    Channel = outputChannelKey,
                    MetricKey = outputChannelKey,
                    Value = y[i],
                    ElapsedMs = elapsed[i],
                };
            }

            return samples;
        }
        catch (InvalidOperationException ex)
        {
            throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.TfGrid}: {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<StoredSample> ApplyWithSynthesizedClock(
        TransferFunctionAlgorithm tf,
        IReadOnlyList<double> values,
        string outputChannelKey)
    {
        ArgumentNullException.ThrowIfNull(tf);
        ArgumentNullException.ThrowIfNull(values);
        var samples = values
            .Select((value, i) => new StoredSample
            {
                Channel = tf.InputChannelKey,
                MetricKey = tf.InputChannelKey,
                Value = value,
                ElapsedMs = i * tf.TsSeconds * 1000.0,
            })
            .ToArray();
        return Apply(tf, samples, outputChannelKey);
    }
}
