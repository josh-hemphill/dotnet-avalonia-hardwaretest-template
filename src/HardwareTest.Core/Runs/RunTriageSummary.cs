namespace HardwareTest.Core.Runs;

/// Read-time QA triage derived from a persisted run (no schema bump).
public sealed class RunTriageSummary
{
    public StepResultRecord? FirstFail { get; init; }
    public int PathPassCount { get; init; }
    public int PathFailCount { get; init; }
    public int TotalAttempts { get; init; }
    public string OperatorSummary { get; init; } = string.Empty;
    public IReadOnlyList<StepAttemptSummary> Ledgers { get; init; } = [];

    /// Builds triage from the attempt ledger or the original steps when no ledger was stored.
    public static RunTriageSummary FromRecord(TestRunRecord run)
    {
        var ledgers = run.StepAttempts.Count > 0
            ? run.StepAttempts
            : [.. run.Steps.GroupBy(step => string.IsNullOrWhiteSpace(step.StepPath) ? step.StepId : step.StepPath,
                StringComparer.OrdinalIgnoreCase)
                .Select(group => new StepAttemptSummary
                {
                    StepPath = group.Key,
                    StepName = group.First().StepType,
                    Attempts = [.. group],
                })];
        var chronology = ledgers
            .SelectMany(l => l.Attempts)
            .OrderBy(ChronologyInstant)
            .ThenBy(a => a.AttemptNumber)
            .ThenBy(a => a.StepPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var firstFail = chronology.FirstOrDefault(a => !a.Passed);
        var latest = ledgers
            .Select(LatestAttempt)
            .Where(a => a is not null)
            .Cast<StepResultRecord>()
            .ToList();
        var pathFail = latest.Count(a => a.OutcomeLabel == "Fail");
        var pathError = latest.Count(a => a.OutcomeLabel == "Error");
        var pathCancelled = latest.Count(a => a.OutcomeLabel == "Cancelled");
        var pathPass = latest.Count(a => a.Passed);
        var totalAttempts = ledgers.Sum(l => l.AttemptCount > 0 ? l.AttemptCount : l.Attempts.Count);

        return new RunTriageSummary
        {
            FirstFail = firstFail,
            PathPassCount = pathPass,
            PathFailCount = pathFail,
            TotalAttempts = totalAttempts,
            Ledgers = ledgers,
            OperatorSummary = FormatSummary(firstFail, pathFail, pathPass, pathError, pathCancelled),
        };
    }

    internal static DateTimeOffset ChronologyInstant(StepResultRecord attempt)
        => attempt.CompletedAt != default ? attempt.CompletedAt : attempt.StartedAt;

    private static StepResultRecord? LatestAttempt(StepAttemptSummary ledger)
    {
        if (ledger.Attempts.Count > 0)
        {
            return ledger.Attempts
                .OrderBy(ChronologyInstant)
                .ThenBy(a => a.AttemptNumber)
                .Last();
        }

        if (ledger.LatestPassed is null && string.IsNullOrWhiteSpace(ledger.LatestMessage))
        {
            return null;
        }

        return new StepResultRecord
        {
            StepPath = ledger.StepPath,
            StepId = ledger.StepName,
            Passed = ledger.LatestPassed == true,
            Message = ledger.LatestMessage,
        };
    }

    private static string FormatSummary(StepResultRecord? firstFail, int pathFail, int pathPass, int pathError, int pathCancelled)
    {
        if (firstFail is null)
        {
            if (pathError + pathCancelled > 0)
                return $"{pathPass} passed / {pathFail} failed / {pathError} error / {pathCancelled} cancelled path(s).";
            return pathFail == 0
                ? $"No failed steps ({pathPass} passed path(s))."
                : $"{pathFail} failed path(s).";
        }

        var path = string.IsNullOrWhiteSpace(firstFail.StepPath) ? firstFail.StepId : firstFail.StepPath;
        if (pathFail + pathError + pathCancelled == 0 && firstFail.OutcomeLabel == "Fail")
            return $"Earlier attempt failed: {path} — {firstFail.Message}. Latest results passed on {pathPass} path(s).";
        var counts = pathError + pathCancelled > 0 ? $"{pathFail} failed / {pathError} error / {pathCancelled} cancelled path(s)" : $"{pathFail} failed path(s)";
        return $"First nonpassing attempt in session: {path} — {firstFail.Message} ({counts})";
    }
}
