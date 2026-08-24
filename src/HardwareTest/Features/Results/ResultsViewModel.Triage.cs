using HardwareTest.Core.Runs;
using HardwareTest.Core.Text;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

/// Failed-run auto-filter, failed-steps toggle, and attempt rollup on the detail pane.
public partial class ResultsViewModel
{
    [Reactive] private bool _showFailedStepsOnly;
    [Reactive] private string _firstFailSummary = string.Empty;
    [Reactive] private bool _hasFirstFail;
    [Reactive] private string _triageSummary = string.Empty;

    public bool ShowFailedStepsBanner => ShowFailedStepsOnly && OpenedRun is not null;

    private void RebuildStepDetails()
    {
        StepDetails.Clear();
        FirstFailSummary = string.Empty;
        HasFirstFail = false;
        TriageSummary = string.Empty;
        this.RaisePropertyChanged(nameof(ShowFailedStepsBanner));
        if (OpenedRun is null)
        {
            return;
        }

        var triage = RunTriageSummary.FromRecord(OpenedRun);
        TriageSummary = triage.OperatorSummary;
        if (triage.FirstFail is not null)
        {
            HasFirstFail = true;
            var path = string.IsNullOrWhiteSpace(triage.FirstFail.StepPath)
                ? triage.FirstFail.StepId
                : triage.FirstFail.StepPath;
            FirstFailSummary = $"First fail: {path} — {triage.FirstFail.Message}";
        }

        var rows = triage.Ledgers
            .Select(ledger => (Latest: LatestOf(ledger), Ledger: ledger))
            .Where(row => row.Latest is not null)
            .Select(row => (Latest: row.Latest!, row.Ledger));
        if (ShowFailedStepsOnly)
        {
            rows = rows.Where(row => !row.Latest.Passed);
        }

        var materialized = rows.ToList();
        var remaining = SidebarDetailCap;
        var pathsShown = 0;
        foreach (var (latest, ledger) in materialized)
        {
            if (remaining <= 0)
            {
                break;
            }

            var marker = IsSameAttempt(latest, triage.FirstFail) ? "First fail · " : string.Empty;
            var rollup = ledger.AttemptCount > 0 ? $" · {ledger.Display}" : string.Empty;
            StepDetails.Add(
                $"{marker}{ShortId.Display(latest.StepId)} [{latest.StepType}] {(latest.Passed ? "PASS" : "FAIL")} — {latest.Message}{rollup}");
            remaining--;
            pathsShown++;

            if (IsSameAttempt(latest, triage.FirstFail) && ledger.Attempts.Count > 1)
            {
                foreach (var attempt in ledger.Attempts)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    StepDetails.Add(
                        $"  #{attempt.AttemptNumber} {(attempt.Passed ? "PASS" : "FAIL")} — {attempt.Message} @ {attempt.CompletedAt:u}");
                    remaining--;
                }
            }
        }

        if (pathsShown < materialized.Count)
        {
            StepDetails.Add($"…and {materialized.Count - pathsShown} more steps (see run.json / report).");
        }
    }

    private static StepResultRecord? LatestOf(StepAttemptSummary ledger)
    {
        if (ledger.Attempts.Count > 0)
        {
            return ledger.Attempts
                .OrderBy(a => a.CompletedAt != default ? a.CompletedAt : a.StartedAt)
                .ThenBy(a => a.AttemptNumber)
                .Last();
        }

        return null;
    }

    private static bool IsSameAttempt(StepResultRecord latest, StepResultRecord? firstFail)
    {
        if (firstFail is null)
        {
            return false;
        }

        var latestPath = string.IsNullOrWhiteSpace(latest.StepPath) ? latest.StepId : latest.StepPath;
        var failPath = string.IsNullOrWhiteSpace(firstFail.StepPath) ? firstFail.StepId : firstFail.StepPath;
        return string.Equals(latestPath, failPath, StringComparison.OrdinalIgnoreCase)
               && latest.AttemptNumber == firstFail.AttemptNumber;
    }
}
