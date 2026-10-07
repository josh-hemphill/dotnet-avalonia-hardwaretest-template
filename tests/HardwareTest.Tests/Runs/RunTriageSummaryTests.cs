using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class RunTriageSummaryTests
{
    [Fact]
    public void FromRecord_uses_attempt_chronology_not_path_order()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 1, 10, 0, 5, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 1, 10, 0, 9, TimeSpan.Zero);
        var run = new TestRunRecord
        {
            RunId = "triage-chrono",
            Result = RunResult.Failed,
            Steps =
            [
                new StepResultRecord { StepId = "Mmm", StepPath = "Mmm", Passed = false, CompletedAt = t3 },
                new StepResultRecord { StepId = "Aaa", StepPath = "Aaa", Passed = true, CompletedAt = t1 },
                new StepResultRecord { StepId = "Zzz", StepPath = "Zzz", Passed = false, CompletedAt = t2 },
            ],
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "Zzz",
                    StepName = "Zzz",
                    AttemptCount = 1,
                    FailedCount = 1,
                    LatestPassed = false,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Zzz", StepPath = "Zzz", Passed = false, CompletedAt = t2 },
                    ],
                },
                new StepAttemptSummary
                {
                    StepPath = "Aaa",
                    StepName = "Aaa",
                    AttemptCount = 1,
                    PassedCount = 1,
                    LatestPassed = true,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Aaa", StepPath = "Aaa", Passed = true, CompletedAt = t1 },
                    ],
                },
                new StepAttemptSummary
                {
                    StepPath = "Mmm",
                    StepName = "Mmm",
                    AttemptCount = 1,
                    FailedCount = 1,
                    LatestPassed = false,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Mmm", StepPath = "Mmm", Passed = false, CompletedAt = t3 },
                    ],
                },
            ],
        };

        var triage = RunTriageSummary.FromRecord(run);

        Assert.Equal("Zzz", triage.FirstFail?.StepPath);
        Assert.Equal(1, triage.PathPassCount);
        Assert.Equal(2, triage.PathFailCount);
        Assert.Equal(3, triage.TotalAttempts);
        Assert.Contains("Zzz", triage.OperatorSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy", triage.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromRecord_attempts_use_completion_time()
    {
        var t1 = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 2, 1, 8, 0, 2, TimeSpan.Zero);
        var run = new TestRunRecord
        {
            RunId = "completion",
            Result = RunResult.Failed,
            StepAttempts =
            [
                new StepAttemptSummary { StepPath = "Later", Attempts = [new StepResultRecord { StepId = "Later", StepPath = "Later", Passed = false, CompletedAt = t2 }] },
                new StepAttemptSummary { StepPath = "Earlier", Attempts = [new StepResultRecord { StepId = "Earlier", StepPath = "Earlier", Passed = false, CompletedAt = t1 }] },
            ],
        };

        var triage = RunTriageSummary.FromRecord(run);

        Assert.Equal("Earlier", triage.FirstFail?.StepPath);
        Assert.Equal(2, triage.PathFailCount);
        Assert.Equal(2, triage.TotalAttempts);
    }

    [Fact]
    public void FromRecord_all_pass_has_no_first_fail()
    {
        var run = new TestRunRecord
        {
            Result = RunResult.Passed,
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "A",
                    AttemptCount = 2,
                    PassedCount = 2,
                    LatestPassed = true,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "A", StepPath = "A", Passed = true, AttemptNumber = 1, CompletedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) },
                        new StepResultRecord { StepId = "A", StepPath = "A", Passed = true, AttemptNumber = 2, CompletedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 1, TimeSpan.Zero) },
                    ],
                },
            ],
        };

        var triage = RunTriageSummary.FromRecord(run);
        Assert.Null(triage.FirstFail);
        Assert.Equal(1, triage.PathPassCount);
        Assert.Equal(0, triage.PathFailCount);
        Assert.Equal(2, triage.TotalAttempts);
        Assert.Contains("No failed steps", triage.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void FromRecord_empty_attempt_ledger_does_not_infer_chronology_from_steps()
    {
        var run = new TestRunRecord
        {
            Steps = [new StepResultRecord { StepPath = "failed-step", Passed = false }],
        };

        var triage = RunTriageSummary.FromRecord(run);
        Assert.Null(triage.FirstFail);
        Assert.Empty(triage.Ledgers);
        Assert.Equal(0, triage.TotalAttempts);
        Assert.Equal(0, triage.PathFailCount);
        Assert.Equal(0, triage.PathPassCount);
    }

    [Fact]
    public void FromRecord_compact_and_incomplete_ledgers_preserve_latest_totals_without_inventing_attempts()
    {
        var failure = new StepResultRecord
        {
            StepPath = "retry",
            Passed = false,
            AttemptNumber = 1,
            StartedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var run = new TestRunRecord
        {
            StepAttempts =
            [
                new StepAttemptSummary { StepPath = "compact", AttemptCount = 4, LatestPassed = false, LatestMessage = "Failed" },
                new StepAttemptSummary { StepPath = "unknown", AttemptCount = 2 },
                new StepAttemptSummary
                {
                    StepPath = "retry", AttemptCount = 3,
                    Attempts = [failure, new StepResultRecord
                    {
                        StepPath = "retry", Passed = true, AttemptNumber = 3,
                        CompletedAt = failure.StartedAt.AddSeconds(2),
                    }],
                },
            ],
        };

        var triage = RunTriageSummary.FromRecord(run);
        Assert.Same(failure, triage.FirstFail);
        Assert.Equal(9, triage.TotalAttempts);
        Assert.Equal(1, triage.PathFailCount);
        Assert.Equal(1, triage.PathPassCount);
        Assert.Empty(run.StepAttempts[0].Attempts);
        Assert.Empty(run.StepAttempts[1].Attempts);
    }

}
