using System.Collections.ObjectModel;
using System.Globalization;
using HardwareTest.Core.Runs;
using HardwareTest.Widgets.Presentation;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel
{
    private const int EvaluatedPreviewCount = 12;

    public ObservableCollection<EvaluatedResultRow> EvaluatedRows { get; } = [];

    [Reactive] private string _evaluatedSearch = string.Empty;
    [Reactive] private bool _showFailedMetricsOnly;
    [Reactive] private bool _hasEvaluatedRows;
    [Reactive] private string _evaluatedSummary = string.Empty;

    private void ResetEvaluatedRows()
    {
        EvaluatedSearch = string.Empty;
        ShowFailedMetricsOnly = OpenedRun?.Samples.Any(IsFailedMeasurement) == true;
        RebuildEvaluatedRows();
    }

    private void RebuildEvaluatedRows()
    {
        var run = OpenedRun;
        var evaluated = run?.Samples.Where(sample => !string.IsNullOrWhiteSpace(sample.RowVerdict)).ToArray() ?? [];
        HasEvaluatedRows = evaluated.Length > 0;
        EvaluatedRows.Clear();
        var query = EvaluatedSearch.Trim();
        var matches = evaluated
            .Where(sample => !ShowFailedMetricsOnly || IsFailedMeasurement(sample))
            .Where(sample => query.Length == 0
                || sample.EffectiveMetricKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                || sample.MetricDisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || sample.StepRunId?.ToString("D").Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || sample.StepPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(IsFailedMeasurement)
            .ToArray();
        var executionSamples = run?.Samples.Where(sample => sample.StepRunId.HasValue).ToLookup(sample => sample.StepRunId);
        var executionEvents = run?.Events.Where(mark => mark.StepRunId.HasValue).ToLookup(mark => mark.StepRunId);
        var attempts = evaluated.Where(sample => sample.StepRunId.HasValue).GroupBy(sample => sample.ProducerStepId)
            .SelectMany(group => group.OrderBy(sample => sample.Timestamp).Select(sample => sample.StepRunId).Distinct()
                .Select((execution, index) => (Id: execution!.Value, Attempt: index + 1)))
            .GroupBy(pair => pair.Id).ToDictionary(group => group.Key, group => group.First().Attempt);
        var source = run?.Events.Where(mark => mark.Name is "procedure.source" or "tables").Select(mark => mark.Label).ToArray() ?? [];
        foreach (var sample in matches.Take(EvaluatedPreviewCount))
        {
            var execution = sample.StepRunId;
            var events = executionEvents?[execution].ToArray() ?? [];
            var route = events.FirstOrDefault(mark => mark.Name == "case.route")?.Label;
            var configuration = events.FirstOrDefault(mark => mark.Name == "case.configuration")?.Label;
            var captures = executionSamples?[execution].Where(point => point.SampleIndex.HasValue).ToArray() ?? [];
            var keys = captures.Select(point => point.EffectiveMetricKey).Distinct().ToArray();
            EvaluatedRows.Add(new EvaluatedResultRow(sample,
                execution is { } id ? attempts.GetValueOrDefault(id, 1) : null,
                configuration, route, captures.Length, keys, source));
        }

        EvaluatedSummary = $"Showing {EvaluatedRows.Count} of {matches.Length} matches. "
            + $"{evaluated.Count(IsFailedMeasurement)} failed of {evaluated.Length} evaluated.";
        if (matches.Length > EvaluatedPreviewCount)
        {
            EvaluatedSummary += " Narrow the search to see other rows.";
        }
    }

    private static bool IsFailedMeasurement(StoredSample sample)
        => string.Equals(sample.RowVerdict, "Fail", StringComparison.OrdinalIgnoreCase);

    public sealed class EvaluatedResultRow(StoredSample sample, int? attempt, string? configuration, string? input,
        int captureCount, IReadOnlyList<string> captureChannels, IReadOnlyList<string?> revisions)
    {
        public string Name => string.IsNullOrWhiteSpace(sample.MetricDisplayName) ? sample.EffectiveMetricKey : sample.MetricDisplayName;
        public string Verdict => sample.RowVerdict ?? string.Empty;
        public string Value => WithUnit(Format(sample.Value));
        public LimitReadout? Reading { get; } = LimitReadout.Create(sample.Value, sample.LimitLow, sample.LimitHigh, sample.Unit);
        public bool HasLimits => Reading is not null;
        public string Timestamp => sample.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        public string StepPath => sample.StepPath;
        public bool HasTrace => sample.StepRunId is not null;
        public string AttemptSummary => $"Attempt {attempt}" + (configuration is null ? string.Empty : $" · {configuration}");
        public string Trace => string.Join(Environment.NewLine, new[]
        {
            $"Timestamp {Timestamp}", $"Metric {sample.EffectiveMetricKey}", StepPath, $"Execution {sample.StepRunId:D}", input,
            captureCount > 0 ? $"{captureCount} capture points · {string.Join(", ", captureChannels)}" : null
        }.Concat(revisions).Where(line => !string.IsNullOrWhiteSpace(line)));

        private string WithUnit(string number)
            => string.IsNullOrWhiteSpace(sample.Unit) || number == "—" ? number : $"{number} {sample.Unit}";

        private static string Format(double? number)
            => number?.ToString("G10", CultureInfo.InvariantCulture) ?? "—";
    }
}
