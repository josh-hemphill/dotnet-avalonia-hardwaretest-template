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
            throw FailEval("formula produced an empty series");
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
            FilterCallExpr => throw FailEval("filter/filtfilt is evaluated by TransferFunctionFilter"),
            _ => throw FailEval($"unsupported expression '{expr.GetType().Name}'"),
        };

    private static IReadOnlyList<double> SeriesValues(
        string name,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        if (!TryGetSeries(series, name, out var samples) || samples.Count == 0)
        {
            throw FailEval($"missing series '{name}'");
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
        if (left.Count == 0 || right.Count == 0)
        {
            throw FailEval($"operator '{op}' received an empty operand");
        }

        var n = Math.Max(left.Count, right.Count);
        if ((left.Count != 1 && left.Count != n) || (right.Count != 1 && right.Count != n))
        {
            throw FailEval($"operator '{op}' length mismatch ({left.Count} vs {right.Count})");
        }

        var result = new double[n];
        for (var i = 0; i < n; i++)
        {
            var a = At(left, i);
            var b = At(right, i);
            result[i] = op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" or ".*" => a * b,
                "/" or "./" => a / b,
                "^" or ".^" => Math.Pow(a, b),
                _ => throw FailEval($"unknown operator '{op}'"),
            };
        }

        return result;
    }

    private static double At(IReadOnlyList<double> values, int index)
        => values.Count == 1 ? values[0] : values[index];

    private static IReadOnlyList<double> EvalCall(
        CallExpr call,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series)
    {
        var args = call.Args.Select(arg => Eval(arg, series)).ToArray();
        if (args.Length == 0 || args[0].Count == 0)
        {
            throw FailEval($"function '{call.Name}' requires a non-empty first argument");
        }

        return call.Name switch
        {
            "abs" => args[0].Select(Math.Abs).ToArray(),
            "sqrt" => args[0].Select(Math.Sqrt).ToArray(),
            "min" => [Flatten(args).Min()],
            "max" => [Flatten(args).Max()],
            "mean" => [args[0].Average()],
            "sum" => [args[0].Sum()],
            "std" => [Std(args[0])],
            "diff" => Diff(args[0]),
            "length" => [args[0].Count],
            "median" => [Median(args[0])],
            "rise_time" => [RiseTime(args[0], RequireScalar(args, 1, call.Name), RequireScalar(args, 2, call.Name))],
            "inband_pct" => [InBandPct(args[0], RequireScalar(args, 1, call.Name), RequireScalar(args, 2, call.Name))],
            _ => throw FailEval($"unknown function '{call.Name}'"),
        };
    }

    private static IReadOnlyList<double> Flatten(IReadOnlyList<IReadOnlyList<double>> args)
    {
        var values = args.SelectMany(a => a).ToArray();
        if (values.Length == 0)
        {
            throw FailEval("min/max received an empty series");
        }

        return values;
    }

    private static double RequireScalar(IReadOnlyList<IReadOnlyList<double>> args, int index, string name)
    {
        if (args.Count <= index || args[index].Count == 0)
        {
            throw FailEval($"function '{name}' is missing argument {index + 1}");
        }

        return args[index][0];
    }

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
        if (ordered.Length == 0)
        {
            throw FailEval("median received an empty series");
        }

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

    private static AuthoringWorkspaceException FailEval(string message)
        => new($"{AuthoringCompileCodes.FormulaEval}: {message}.");
}
