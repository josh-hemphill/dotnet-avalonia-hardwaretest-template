namespace HardwareTest.OpenTap.Plugins.Basic;

/// Uniform elapsed-ms grid checks shared by preview and ApplyTransferFunctionStep.
public static class TransferFunctionGrid
{
    public const double DefaultTsRelativeEpsilon = 1e-6;
    public const double DefaultTsEpsilonFloorSeconds = 1e-9;

    /// Fail closed: length &lt; 2, NaN elapsed, irregular dt, or Ts mismatch. Never drop rows.
    public static void RequireUniform(
        IReadOnlyList<double> elapsedMs,
        double tsSeconds,
        double? epsilon = null)
    {
        if (tsSeconds <= 0 || double.IsNaN(tsSeconds) || double.IsInfinity(tsSeconds))
        {
            throw new InvalidOperationException("TF_GRID: TsSeconds must be > 0.");
        }

        var medianDt = MedianDtMs(elapsedMs);
        var tsAbs = epsilon ?? Math.Max(DefaultTsEpsilonFloorSeconds, DefaultTsRelativeEpsilon * tsSeconds);
        var medianSeconds = medianDt / 1000.0;
        if (Math.Abs(medianSeconds - tsSeconds) > tsAbs)
        {
            throw new InvalidOperationException(
                $"TF_GRID: median dt {medianSeconds} s does not match TsSeconds {tsSeconds}.");
        }
    }

    /// Median sample period in seconds. Fail closed on length &lt; 2, NaN, or irregular dt.
    public static double MedianTsSeconds(IReadOnlyList<double> elapsedMs)
        => MedianDtMs(elapsedMs) / 1000.0;

    private static double MedianDtMs(IReadOnlyList<double> elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(elapsedMs);
        if (elapsedMs.Count < 2)
        {
            throw new InvalidOperationException("TF_GRID: elapsed series length is < 2.");
        }

        var dt = new double[elapsedMs.Count - 1];
        for (var i = 0; i < dt.Length; i++)
        {
            var t0 = elapsedMs[i];
            var t1 = elapsedMs[i + 1];
            if (double.IsNaN(t0) || double.IsNaN(t1) || double.IsInfinity(t0) || double.IsInfinity(t1))
            {
                throw new InvalidOperationException("TF_GRID: elapsedMs contains NaN or infinity.");
            }

            dt[i] = t1 - t0;
            if (dt[i] <= 0)
            {
                throw new InvalidOperationException("TF_GRID: elapsedMs is not strictly increasing.");
            }
        }

        var ordered = dt.ToArray();
        Array.Sort(ordered);
        var medianDt = ordered.Length % 2 == 0
            ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2
            : ordered[ordered.Length / 2];
        if (medianDt <= 0)
        {
            throw new InvalidOperationException("TF_GRID: median dt is not positive.");
        }

        foreach (var step in dt)
        {
            if (Math.Abs(step - medianDt) / medianDt > DefaultTsRelativeEpsilon)
            {
                throw new InvalidOperationException("TF_GRID: elapsedMs dt is irregular.");
            }
        }

        return medianDt;
    }
}
