namespace HardwareTest.Core.Runs;

/// Read-time QA triage derived from a persisted run (no schema bump).
public sealed class RunTriageSummary
{
    public StepResultRecord? FirstFail { get; init; }
    public int PathPassCount { get; init; }
    public int PathFailCount { get; init; }
    public int TotalAttempts { get; init; }
    public bool IsLegacyTriage { get; init; }
    public string OperatorSummary { get; init; } = string.Empty;
    public IReadOnlyList<StepAttemptSummary> Ledgers { get; init; } = [];

    /// Builds triage from <see cref="TestRunRecord.StepAttempts"/> chronology; falls back to <see cref="TestRunRecord.Steps"/>.
    public static RunTriageSummary FromRecord(TestRunRecord run)
    {
        var isLegacy = run.StepAttempts.Count == 0;
        var ledgers = isLegacy ? BuildLegacyLedgers(run.Steps) : run.StepAttempts;
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
        var pathFail = latest.Count(a => !a.Passed);
        var pathPass = latest.Count(a => a.Passed);
        var totalAttempts = ledgers.Sum(l => l.AttemptCount > 0 ? l.AttemptCount : l.Attempts.Count);

        return new RunTriageSummary
        {
            FirstFail = firstFail,
            PathPassCount = pathPass,
            PathFailCount = pathFail,
            TotalAttempts = totalAttempts,
            IsLegacyTriage = isLegacy && run.Steps.Count > 0,
            Ledgers = ledgers,
            OperatorSummary = FormatSummary(firstFail, pathFail, pathPass, isLegacy && run.Steps.Count > 0),
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

    private static IReadOnlyList<StepAttemptSummary> BuildLegacyLedgers(IReadOnlyList<StepResultRecord> steps)
    {
        if (steps.Count == 0)
        {
            return [];
        }

        return steps
            .GroupBy(
                s => string.IsNullOrWhiteSpace(s.StepPath) ? s.StepId : s.StepPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ordered = g.OrderBy(ChronologyInstant).ThenBy(a => a.AttemptNumber).ToList();
                var last = ordered[^1];
                return new StepAttemptSummary
                {
                    StepPath = string.IsNullOrWhiteSpace(last.StepPath) ? last.StepId : last.StepPath,
                    StepName = string.IsNullOrWhiteSpace(last.StepId) ? last.StepPath : last.StepId,
                    AttemptCount = ordered.Count,
                    PassedCount = ordered.Count(a => a.Passed),
                    FailedCount = ordered.Count(a => !a.Passed),
                    LatestPassed = last.Passed,
                    LatestMessage = last.Message,
                    Attempts = ordered,
                };
            })
            .ToList();
    }

    private static string FormatSummary(StepResultRecord? firstFail, int pathFail, int pathPass, bool legacy)
    {
        if (firstFail is null)
        {
            return pathFail == 0
                ? $"No failed steps ({pathPass} passed path(s))."
                : $"{pathFail} failed path(s).";
        }

        var path = string.IsNullOrWhiteSpace(firstFail.StepPath) ? firstFail.StepId : firstFail.StepPath;
        var legacyMark = legacy ? " (legacy steps)" : string.Empty;
        return $"First fail: {path} — {firstFail.Message} ({pathFail} failed path(s)){legacyMark}";
    }
}
