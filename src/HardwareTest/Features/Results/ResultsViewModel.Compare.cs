using System.Collections.ObjectModel;
using System.Globalization;
using HardwareTest.Core.Runs;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

public sealed class RunComparisonRow
{
    public required string MetricKey { get; init; }
    public required string CurrentText { get; init; }
    public required string PreviousText { get; init; }
    public required string DeltaText { get; init; }
    public required string Note { get; init; }
}

/// Compare-with-previous same DUT+plan (in-panel Results).
public partial class ResultsViewModel
{
    public ObservableCollection<RunComparisonRow> ComparisonMetrics { get; }

    [Reactive] private string _comparisonSummary = string.Empty;
    [Reactive] private bool _hasComparison;

    private void ApplyComparison(RunComparisonReport report)
    {
        ComparisonMetrics.Clear();
        ComparisonSummary = report.OperatorSummary;
        foreach (var metric in report.Metrics)
        {
            ComparisonMetrics.Add(new RunComparisonRow
            {
                MetricKey = metric.MetricKey,
                CurrentText = FormatMean(metric.CurrentMean, metric.Unit),
                PreviousText = FormatMean(metric.PreviousMean, metric.Unit),
                DeltaText = metric.PercentDelta is { } d
                    ? d.ToString("0.#", CultureInfo.InvariantCulture) + "%"
                    : "—",
                Note = metric.Unavailable ? metric.UnavailableReason : string.Empty,
            });
        }

        HasComparison = ComparisonMetrics.Count > 0 || !string.IsNullOrWhiteSpace(ComparisonSummary);
    }

    private void ClearComparison()
    {
        ComparisonMetrics.Clear();
        ComparisonSummary = string.Empty;
        HasComparison = false;
    }

    private static string FormatMean(double? value, string? unit)
    {
        if (value is null)
        {
            return "—";
        }

        var text = value.Value.ToString("G6", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? text : $"{text} {unit}";
    }
}
