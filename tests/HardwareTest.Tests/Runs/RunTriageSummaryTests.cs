using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class RunTriageSummaryTests
{
    [Fact]
    public void Legacy_nonpassing_attempts_roundtrip_without_turning_cancellation_and_errors_into_measurement_failures()
    {
        const string json = """
        {"runId":"legacy-outcomes","stepAttempts":[
          {"stepPath":"waiting","attemptCount":1,"failedCount":1,"latestPassed":false,"latestMessage":"Aborted","attempts":[{"stepPath":"waiting","passed":false,"message":"Aborted"}]},
          {"stepPath":"protocol","attemptCount":1,"failedCount":1,"latestPassed":false,"latestMessage":"Error","attempts":[{"stepPath":"protocol","passed":false,"message":"Error"}]},
          {"stepPath":"measurement","attemptCount":1,"failedCount":1,"latestPassed":false,"latestMessage":"Fail","attempts":[{"stepPath":"measurement","passed":false,"message":"Fail"}]}
        ]}
        """;
        var run = System.Text.Json.JsonSerializer.Deserialize(json, HardwareTest.Core.Serialization.AppJsonContext.Default.TestRunRecord)!;
        var triage = RunTriageSummary.FromRecord(run);
        Assert.Equal(1, triage.PathFailCount);
        Assert.Contains("1 failed / 1 error / 1 cancelled", triage.OperatorSummary);
        Assert.Equal(0, run.StepAttempts[0].MeasurementFailureCount);
        Assert.Equal(1, run.StepAttempts[0].CancelledCount);
        Assert.DoesNotContain("1F", run.StepAttempts[0].Display);
        Assert.Equal("Cancelled", run.StepAttempts[0].LatestOutcome);
        Assert.Equal("Error", run.StepAttempts[1].LatestOutcome);
        Assert.Equal("Fail", run.StepAttempts[2].LatestOutcome);
        var saved = System.Text.Json.JsonSerializer.Serialize(run, HardwareTest.Core.Serialization.AppJsonContext.Default.TestRunRecord);
        Assert.DoesNotContain("outcomeLabel", saved);
        var restored = System.Text.Json.JsonSerializer.Deserialize(saved, HardwareTest.Core.Serialization.AppJsonContext.Default.TestRunRecord)!;
        Assert.Equal("Cancelled", restored.StepAttempts[0].Attempts[0].OutcomeLabel);
        Assert.Equal(1, RunTriageSummary.FromRecord(restored).PathFailCount);
    }

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
    public void FromRecord_empty_attempt_ledger_uses_the_saved_steps()
    {
        var run = new TestRunRecord
        {
            Steps = [new StepResultRecord { StepPath = "failed-step", Passed = false }],
        };

        var triage = RunTriageSummary.FromRecord(run);
        Assert.Equal("failed-step", triage.FirstFail?.StepPath);
        _ = Assert.Single(triage.Ledgers);
        Assert.Equal(1, triage.TotalAttempts);
        Assert.Equal(1, triage.PathFailCount);
        Assert.Equal(0, triage.PathPassCount);
    }

    [Fact]
    public void FromRecord_labels_a_failed_attempt_as_history_after_a_passing_retry()
    {
        var run = new TestRunRecord
        {
            StepAttempts = [new StepAttemptSummary
            {
                StepPath = "Curve",
                Attempts =
                [
                    new() { StepPath = "Curve", Passed = false, Message = "Fail", AttemptNumber = 1 },
                    new() { StepPath = "Curve", Passed = true, Message = "Pass", AttemptNumber = 2 },
                ],
            }],
        };

        var triage = RunTriageSummary.FromRecord(run);
        Assert.Equal(0, triage.PathFailCount);
        Assert.Contains("Earlier attempt failed: Curve", triage.OperatorSummary, StringComparison.Ordinal);
        Assert.Contains("Latest results passed", triage.OperatorSummary, StringComparison.Ordinal);
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
