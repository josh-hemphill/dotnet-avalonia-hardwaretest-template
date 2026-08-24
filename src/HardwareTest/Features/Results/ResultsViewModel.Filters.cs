using System.Collections.ObjectModel;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Time;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

/// List filters (result/plan/DUT/operator/date) and yield counts.
public partial class ResultsViewModel
{
    public const string DateAll = "All dates";
    public const string DateToday = "Today";
    public const string DateLast7Days = "Last 7 days";

    public ObservableCollection<string> OperatorFilterOptions { get; } = [AllFilter];
    public ObservableCollection<string> DateFilterOptions { get; } = [DateAll, DateToday, DateLast7Days];

    [Reactive] private string _operatorFilter = AllFilter;
    [Reactive] private string _dateFilter = DateAll;
    [Reactive] private string _yieldSummary = string.Empty;

    private void RebuildFilterOptions()
    {
        ReplaceOptions(
            PlanFilterOptions,
            _allRuns
                .Select(r => string.IsNullOrWhiteSpace(r.PlanName) ? r.PlanId : r.PlanName)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        if (!PlanFilterOptions.Contains(PlanFilter))
        {
            PlanFilter = AllFilter;
        }

        ReplaceOptions(
            DutFilterOptions,
            _allRuns
                .Select(r => string.IsNullOrWhiteSpace(r.DutSerial) ? NoneDutFilter : r.DutSerial!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase));
        if (!DutFilterOptions.Contains(DutFilter))
        {
            DutFilter = AllFilter;
        }

        ReplaceOptions(
            OperatorFilterOptions,
            _allRuns
                .Select(r => string.IsNullOrWhiteSpace(r.OperatorName) ? NoneDutFilter : r.OperatorName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase));
        if (!OperatorFilterOptions.Contains(OperatorFilter))
        {
            OperatorFilter = AllFilter;
        }
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(AllFilter);
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void ApplyFilters()
    {
        var selectedId = SelectedRun?.RunId;
        Runs.Clear();
        foreach (var run in _allRuns.Where(MatchesFilters))
        {
            Runs.Add(run);
        }

        var slice = _allRuns.Where(MatchesNonResultFilters).ToList();
        var passed = slice.Count(r => r.Result == RunResult.Passed);
        var failed = slice.Count(r => r.Result == RunResult.Failed);
        var other = slice.Count - passed - failed;
        YieldSummary = slice.Count == 0
            ? string.Empty
            : $"Passed {passed} · Failed {failed} · Other {other}";

        if (_allRuns.Count == 0)
        {
            FilterStatus = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(YieldSummary))
        {
            FilterStatus = $"Showing {Runs.Count} of {_allRuns.Count}";
        }
        else
        {
            FilterStatus = $"Showing {Runs.Count} of {_allRuns.Count} · {YieldSummary}";
        }

        this.RaisePropertyChanged(nameof(HasRuns));

        if (selectedId is not null)
        {
            SelectedRun = Runs.FirstOrDefault(r =>
                string.Equals(r.RunId, selectedId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool MatchesFilters(TestRunSummary run)
        => MatchesNonResultFilters(run) && MatchesResultFilter(run);

    private bool MatchesNonResultFilters(TestRunSummary run)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            var haystack = string.Join(
                ' ',
                run.PlanName,
                run.PlanId,
                run.DutSerial,
                run.DutPartNumber,
                run.OperatorName,
                run.RunId,
                run.SessionId);
            if (haystack.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        if (!string.Equals(PlanFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            var planLabel = string.IsNullOrWhiteSpace(run.PlanName) ? run.PlanId : run.PlanName;
            if (!string.Equals(planLabel, PlanFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(run.PlanId, PlanFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.Equals(DutFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            var dutLabel = string.IsNullOrWhiteSpace(run.DutSerial) ? NoneDutFilter : run.DutSerial!;
            if (!string.Equals(dutLabel, DutFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.Equals(OperatorFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            var opLabel = string.IsNullOrWhiteSpace(run.OperatorName) ? NoneDutFilter : run.OperatorName!;
            if (!string.Equals(opLabel, OperatorFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return MatchesDateFilter(run);
    }

    private bool MatchesResultFilter(TestRunSummary run)
    {
        if (string.Equals(ResultFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(ResultFilter, "Other", StringComparison.OrdinalIgnoreCase))
        {
            return run.Result is not (RunResult.Passed or RunResult.Failed);
        }

        return string.Equals(run.Result.ToString(), ResultFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesDateFilter(TestRunSummary run)
    {
        if (string.Equals(DateFilter, DateAll, StringComparison.OrdinalIgnoreCase)
            || string.Equals(DateFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var now = _clock.UtcNow;
        if (string.Equals(DateFilter, DateToday, StringComparison.OrdinalIgnoreCase))
        {
            return run.StartedAt.UtcDateTime.Date == now.UtcDateTime.Date;
        }

        if (string.Equals(DateFilter, DateLast7Days, StringComparison.OrdinalIgnoreCase))
        {
            return run.StartedAt >= now - TimeSpan.FromDays(7);
        }

        return true;
    }

    private void WidenFiltersForHiddenRun(bool keepFailedResultFilter)
    {
        ResultFilter = keepFailedResultFilter ? nameof(RunResult.Failed) : AllFilter;
        PlanFilter = AllFilter;
        DutFilter = AllFilter;
        OperatorFilter = AllFilter;
        DateFilter = DateAll;
        SearchText = string.Empty;
        ApplyFilters();
    }
}
