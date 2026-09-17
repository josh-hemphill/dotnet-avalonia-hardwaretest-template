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
            var y = Dispatch(tf.Method, tf.Numerator, tf.Denominator, values);
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
            throw new AuthoringWorkspaceException(WrapTfException(ex), ex);
        }
    }

    private static double[] Dispatch(
        string method,
        IReadOnlyList<double> numerator,
        IReadOnlyList<double> denominator,
        IReadOnlyList<double> values)
    {
        if (string.Equals(method, "filtfilt", StringComparison.Ordinal))
        {
            return TransferFunctionFilter.FiltFilt(numerator, denominator, values);
        }

        if (string.Equals(method, "filter", StringComparison.Ordinal))
        {
            return TransferFunctionFilter.Filter(numerator, denominator, values);
        }

        throw new InvalidOperationException(
            $"{AuthoringCompileCodes.TfMethod}: method must be lowercase filter or filtfilt.");
    }

    private static string WrapTfException(InvalidOperationException ex)
    {
        if (ex.Message.StartsWith(AuthoringCompileCodes.TfDenLeadingZero, StringComparison.Ordinal)
            || ex.Message.StartsWith(AuthoringCompileCodes.TfMethod, StringComparison.Ordinal)
            || ex.Message.StartsWith(AuthoringCompileCodes.TfGrid, StringComparison.Ordinal))
        {
            return ex.Message;
        }

        return $"{AuthoringCompileCodes.TfGrid}: {ex.Message}";
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
