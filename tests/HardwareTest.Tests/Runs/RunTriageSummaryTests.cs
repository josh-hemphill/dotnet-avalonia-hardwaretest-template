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
        Assert.False(triage.IsLegacyTriage);
        Assert.Contains("Zzz", triage.OperatorSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy", triage.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromRecord_legacy_steps_fallback_uses_completion_time()
    {
        var t1 = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 2, 1, 8, 0, 2, TimeSpan.Zero);
        var run = new TestRunRecord
        {
            RunId = "legacy",
            Result = RunResult.Failed,
            Steps =
            [
                new StepResultRecord { StepId = "Later", StepPath = "Later", Passed = false, CompletedAt = t2 },
                new StepResultRecord { StepId = "Earlier", StepPath = "Earlier", Passed = false, CompletedAt = t1 },
            ],
        };

        var triage = RunTriageSummary.FromRecord(run);

        Assert.True(triage.IsLegacyTriage);
        Assert.Equal("Earlier", triage.FirstFail?.StepPath);
        Assert.Equal(2, triage.PathFailCount);
        Assert.Contains("legacy", triage.OperatorSummary, StringComparison.OrdinalIgnoreCase);
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
}
