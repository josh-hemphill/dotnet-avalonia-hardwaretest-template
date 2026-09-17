using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Maps StoredSample series to 1:1 elapsed/value vectors. Null ElapsedMs → NaN; never drop rows.
public static class TransferFunctionTimeBase
{
    public static IReadOnlyList<double> ElapsedMs(IReadOnlyList<StoredSample> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.Select(sample => sample.ElapsedMs is { } elapsed ? elapsed : double.NaN).ToArray();
    }

    public static IReadOnlyList<double> Values(IReadOnlyList<StoredSample> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.Select(sample => sample.Value).ToArray();
    }
}
