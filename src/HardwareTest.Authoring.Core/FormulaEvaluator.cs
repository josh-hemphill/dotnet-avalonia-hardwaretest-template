using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Preview/CI evaluator for the same AST the lowerer saw. No Avalonia, no MATLAB.
public static class FormulaEvaluator
{
    /// Evaluates a formula over named series. Vector results collapse to the last value.
    public static double Evaluate(FormulaAst ast, IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(series);
        var values = Eval(ast.Root, series);
        if (values.Count == 0)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaParse}: formula produced an empty series.");
        }

        return values[^1];
    }

    private static IReadOnlyList<double> Eval(
        FormulaExpr expr,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
        => expr switch
        {
            NumberExpr number => [number.Value],
            IdentExpr ident => SeriesValues(ident.Name, series),
            UnaryExpr unary => MapUnary(unary.Op, Eval(unary.Operand, series)),
            BinaryExpr binary => MapBinary(binary.Op, Eval(binary.Left, series), Eval(binary.Right, series)),
            CallExpr call => EvalCall(call, series),
            _ => throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaParse}: unsupported expression '{expr.GetType().Name}'."),
        };

    private static IReadOnlyList<double> SeriesValues(
        string name,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        if (!TryGetSeries(series, name, out var samples) || samples.Count == 0)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaParse}: missing series '{name}'.");
        }

        return samples.Select(s => s.Value).ToArray();
    }

    private static bool TryGetSeries(
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series,
        string name,
        out IReadOnlyList<StoredSample> samples)
    {
        if (series.TryGetValue(name, out samples!))
        {
            return true;
        }

        foreach (var (key, value) in series)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                samples = value;
                return true;
            }
        }

        samples = [];
        return false;
    }

    private static IReadOnlyList<double> MapUnary(string op, IReadOnlyList<double> values)
        => op == "-" ? values.Select(v => -v).ToArray() : values.ToArray();

    private static IReadOnlyList<double> MapBinary(string op, IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var n = Math.Max(left.Count, right.Count);
        var result = new double[n];
        for (var i = 0; i < n; i++)
        {
            var a = left.Count == 1 ? left[0] : left[i];
            var b = right.Count == 1 ? right[0] : right[i];
            result[i] = op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" or ".*" => a * b,
                "/" or "./" => a / b,
                "^" or ".^" => Math.Pow(a, b),
                _ => throw new AuthoringWorkspaceException(
                    $"{AuthoringCompileCodes.FormulaParse}: unknown operator '{op}'."),
            };
        }

        return result;
    }

    private static IReadOnlyList<double> EvalCall(
        CallExpr call,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        var args = call.Args.Select(arg => Eval(arg, series)).ToArray();
        return call.Name switch
        {
            "abs" => args[0].Select(Math.Abs).ToArray(),
            "sqrt" => args[0].Select(Math.Sqrt).ToArray(),
            "min" => [args.SelectMany(a => a).Min()],
            "max" => [args.SelectMany(a => a).Max()],
            "mean" => [args[0].Average()],
            "sum" => [args[0].Sum()],
            "std" => [Std(args[0])],
            "diff" => Diff(args[0]),
            "length" => [args[0].Count],
            "median" => [Median(args[0])],
            "rise_time" => [RiseTime(args[0], Scalar(args, 1), Scalar(args, 2))],
            "inband_pct" => [InBandPct(args[0], Scalar(args, 1), Scalar(args, 2))],
            _ => throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaParse}: unknown function '{call.Name}'."),
        };
    }

    private static double Scalar(IReadOnlyList<IReadOnlyList<double>> args, int index)
        => args.Count > index && args[index].Count > 0 ? args[index][0] : 0;

    private static double Std(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = values.Average();
        var sum = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sum / (values.Count - 1));
    }

    private static IReadOnlyList<double> Diff(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return [];
        }

        var result = new double[values.Count - 1];
        for (var i = 1; i < values.Count; i++)
        {
            result[i - 1] = values[i] - values[i - 1];
        }

        return result;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var ordered = values.OrderBy(v => v).ToArray();
        var mid = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2 : ordered[mid];
    }

    private static double RiseTime(IReadOnlyList<double> values, double lo, double hi)
    {
        var start = IndexOfCrossing(values, lo);
        var end = IndexOfCrossing(values, hi);
        if (start < 0 || end < 0 || end < start)
        {
            return double.NaN;
        }

        return end - start;
    }

    private static int IndexOfCrossing(IReadOnlyList<double> values, double threshold)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] >= threshold)
            {
                return i;
            }
        }

        return -1;
    }

    private static double InBandPct(IReadOnlyList<double> values, double lo, double hi)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var inside = values.Count(v => v >= lo && v <= hi);
        return 100.0 * inside / values.Count;
    }
}
