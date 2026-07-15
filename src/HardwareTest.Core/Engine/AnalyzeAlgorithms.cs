using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;

namespace HardwareTest.Core.Engine;

public interface IAnalyzeAlgorithm
{
    string Id { get; }
    AnalyzeResult Execute(AnalyzeContext context);
}

public sealed class AnalyzeContext
{
    public required string Channel { get; init; }
    public required double Threshold { get; init; }
    public required IReadOnlyList<MeasurementSample> Samples { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public required TestRunRecord Record { get; init; }
}

public sealed class AnalyzeResult
{
    public required bool Passed { get; init; }
    public required string Message { get; init; }
    public double? Metric { get; init; }
}

public interface IAnalyzeAlgorithmResolver
{
    IAnalyzeAlgorithm Resolve(string algorithmId);
}

public sealed class AnalyzeAlgorithmResolver : IAnalyzeAlgorithmResolver
{
    private readonly Dictionary<string, IAnalyzeAlgorithm> _map;

    public AnalyzeAlgorithmResolver(IEnumerable<IAnalyzeAlgorithm> algorithms)
    {
        _map = algorithms.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IAnalyzeAlgorithm Resolve(string algorithmId)
    {
        if (_map.TryGetValue(algorithmId, out var algo))
        {
            return algo;
        }

        throw new InvalidOperationException(
            $"Unknown analyze algorithm '{algorithmId}'. Register an IAnalyzeAlgorithm or use a built-in id (e.g. mean-gte).");
    }
}

/// Built-in: mean of channel samples must be >= threshold (Value).
public sealed class MeanGteAnalyzeAlgorithm : IAnalyzeAlgorithm
{
    public string Id => "mean-gte";

    public AnalyzeResult Execute(AnalyzeContext context)
    {
        var samples = context.Samples
            .Where(s => string.Equals(s.Channel, context.Channel, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (samples.Length == 0)
        {
            return new AnalyzeResult
            {
                Passed = false,
                Message = $"mean-gte: no samples on channel '{context.Channel}'",
                Metric = double.NaN,
            };
        }

        var mean = samples.Average(s => s.Value);
        var passed = mean >= context.Threshold;
        return new AnalyzeResult
        {
            Passed = passed,
            Message =
                $"mean({context.Channel})={mean.ToString(System.Globalization.CultureInfo.InvariantCulture)} gte {context.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            Metric = mean,
        };
    }
}
