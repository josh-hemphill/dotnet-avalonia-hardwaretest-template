namespace HardwareTest.OpenTap.Plugins.Basic;

/// Direct Form II transposed discrete filter. No Core, Math.NET, or Avalonia.
public static class TransferFunctionFilter
{
    /// Filters x with b/a. a[0] must be non-zero; coefficients are normalized by a[0].
    public static double[] Filter(IReadOnlyList<double> b, IReadOnlyList<double> a, IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(x);
        var (bn, an) = Normalize(b, a);
        return FilterNormalized(bn, an, x);
    }

    /// Forward-backward zero-phase filter (odd-reflection pad, DF-II transposed each pass).
    public static double[] FiltFilt(IReadOnlyList<double> b, IReadOnlyList<double> a, IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(x);
        var (bn, an) = Normalize(b, a);
        var input = x as double[] ?? x.ToArray();
        var n = input.Length;
        if (n == 0)
        {
            return [];
        }

        var edge = 3 * (Math.Max(bn.Length, an.Length) - 1);
        if (edge < 0)
        {
            edge = 0;
        }

        if (edge >= n)
        {
            edge = Math.Max(0, n - 1);
        }

        var padded = OddPad(input, edge);
        var forward = FilterNormalized(bn, an, padded);
        Array.Reverse(forward);
        var backward = FilterNormalized(bn, an, forward);
        Array.Reverse(backward);
        if (edge == 0)
        {
            return backward;
        }

        var result = new double[n];
        Array.Copy(backward, edge, result, 0, n);
        return result;
    }

    /// Divides b/a by a[0]. Keeps original lengths; Filter pads internally.
    public static (double[] B, double[] A) Normalize(IReadOnlyList<double> b, IReadOnlyList<double> a)
    {
        if (b.Count == 0)
        {
            throw new InvalidOperationException("TF_DEN_LEADING_ZERO: numerator is empty.");
        }

        if (a.Count == 0 || a[0] == 0 || double.IsNaN(a[0]) || double.IsInfinity(a[0]))
        {
            throw new InvalidOperationException("TF_DEN_LEADING_ZERO: denominator leading coefficient is 0.");
        }

        var a0 = a[0];
        var bn = new double[b.Count];
        var an = new double[a.Count];
        for (var i = 0; i < b.Count; i++)
        {
            bn[i] = b[i] / a0;
        }

        for (var i = 0; i < a.Count; i++)
        {
            an[i] = a[i] / a0;
        }

        return (bn, an);
    }

    private static double[] FilterNormalized(double[] b, double[] a, IReadOnlyList<double> x)
    {
        var n = x.Count;
        var y = new double[n];
        var width = Math.Max(b.Length, a.Length);
        if (width == 0)
        {
            return y;
        }

        var bn = Pad(b, width);
        var an = Pad(a, width);
        var order = width - 1;
        var z = order == 0 ? [] : new double[order];
        for (var i = 0; i < n; i++)
        {
            var xn = x[i];
            y[i] = bn[0] * xn + (z.Length == 0 ? 0 : z[0]);
            for (var j = 0; j < z.Length - 1; j++)
            {
                z[j] = bn[j + 1] * xn - an[j + 1] * y[i] + z[j + 1];
            }

            if (z.Length > 0)
            {
                z[^1] = bn[^1] * xn - an[^1] * y[i];
            }
        }

        return y;
    }

    private static double[] Pad(double[] values, int width)
    {
        if (values.Length == width)
        {
            return values;
        }

        var padded = new double[width];
        Array.Copy(values, padded, values.Length);
        return padded;
    }

    private static double[] OddPad(double[] x, int edge)
    {
        if (edge <= 0)
        {
            return x.ToArray();
        }

        var n = x.Length;
        var padded = new double[n + 2 * edge];
        var x0 = x[0];
        for (var i = 0; i < edge; i++)
        {
            var src = Math.Min(n - 1, i + 1);
            padded[edge - 1 - i] = 2 * x0 - x[src];
        }

        Array.Copy(x, 0, padded, edge, n);
        var xn = x[^1];
        for (var i = 0; i < edge; i++)
        {
            var src = Math.Max(0, n - 2 - i);
            padded[edge + n + i] = 2 * xn - x[src];
        }

        return padded;
    }
}
