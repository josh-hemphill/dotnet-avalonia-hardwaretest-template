using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class RunComparisonServiceTests
{
    [Fact]
    public async Task CompareToPrevious_uses_latest_earlier_same_dut_and_plan()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-cmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            var t0 = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
            var t1 = t0.AddHours(1);
            var t2 = t0.AddHours(2);
            await store.SaveAsync(SampleRun("older", t0, "SN-1", "sample", ("VDC", 10.0)));
            await store.SaveAsync(SampleRun("previous", t1, "SN-1", "sample", ("VDC", 8.0)));
            await store.SaveAsync(SampleRun("other-dut", t1, "SN-2", "sample", ("VDC", 1.0)));
            var current = SampleRun("current", t2, "SN-1", "sample", ("VDC", 9.0));
            await store.SaveAsync(current);

            var report = await new RunComparisonService(store).CompareToPreviousAsync(current);

            Assert.Equal("current", report.CurrentRunId);
            Assert.Equal("previous", report.PreviousRunId);
            Assert.Contains("previous", report.OperatorSummary, StringComparison.Ordinal);
            var vdc = Assert.Single(report.Metrics);
            Assert.Equal("VDC", vdc.MetricKey);
            Assert.Equal(9.0, vdc.CurrentMean);
            Assert.Equal(8.0, vdc.PreviousMean);
            Assert.False(vdc.Unavailable);
            Assert.NotNull(vdc.PercentDelta);
            Assert.Equal(12.5, vdc.PercentDelta!.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CompareToPrevious_lists_missing_metrics_as_unavailable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-cmp-miss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            var t0 = new DateTimeOffset(2026, 8, 21, 8, 0, 0, TimeSpan.Zero);
            await store.SaveAsync(SampleRun("prev", t0, "SN-M", "sample", ("VDC", 5.0)));
            var current = SampleRun("cur", t0.AddMinutes(10), "SN-M", "sample", ("VDC", 5.0), ("IDC", 0.2));
            await store.SaveAsync(current);

            var report = await new RunComparisonService(store).CompareToPreviousAsync(current);

            Assert.Equal(2, report.Metrics.Count);
            var idc = Assert.Single(report.Metrics, m => m.MetricKey == "IDC");
            Assert.True(idc.Unavailable);
            Assert.Equal("Not in previous run", idc.UnavailableReason);
            Assert.Equal(0.2, idc.CurrentMean);
            Assert.Null(idc.PreviousMean);

            var vdc = Assert.Single(report.Metrics, m => m.MetricKey == "VDC");
            Assert.False(vdc.Unavailable);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CompareToPrevious_groups_by_effective_metric_key()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-cmp-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            var t0 = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);
            await store.SaveAsync(new TestRunRecord
            {
                RunId = "prev",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-K",
                StartedAt = t0,
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "ch-a",
                        MetricKey = "rail.vdc",
                        Value = 12.0,
                        Unit = "V",
                        Timestamp = t0,
                    },
                ],
            });
            var current = new TestRunRecord
            {
                RunId = "cur",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-K",
                StartedAt = t0.AddHours(1),
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "ch-b",
                        MetricKey = "rail.vdc",
                        Value = 11.0,
                        Unit = "V",
                        Timestamp = t0.AddHours(1),
                    },
                ],
            };
            await store.SaveAsync(current);

            var report = await new RunComparisonService(store).CompareToPreviousAsync(current);
            var row = Assert.Single(report.Metrics);
            Assert.Equal("rail.vdc", row.MetricKey);
            Assert.Equal(11.0, row.CurrentMean);
            Assert.Equal(12.0, row.PreviousMean);
            Assert.Equal("V", row.Unit);
            Assert.False(row.Unavailable);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CompareToPrevious_returns_empty_when_no_prior_run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-cmp-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            var current = SampleRun(
                "only",
                new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
                "SN-X",
                "sample",
                ("VDC", 1.0));
            await store.SaveAsync(current);

            var report = await new RunComparisonService(store).CompareToPreviousAsync(current);

            Assert.Null(report.PreviousRunId);
            Assert.Empty(report.Metrics);
            Assert.Contains("No previous run", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CompareToPrevious_ignores_different_plan()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-cmp-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            var t0 = new DateTimeOffset(2026, 8, 23, 14, 0, 0, TimeSpan.Zero);
            await store.SaveAsync(SampleRun("other-plan", t0, "SN-P", "board-demo", ("VDC", 3.0)));
            var current = SampleRun("cur", t0.AddHours(1), "SN-P", "sample", ("VDC", 3.0));
            await store.SaveAsync(current);

            var report = await new RunComparisonService(store).CompareToPreviousAsync(current);

            Assert.Null(report.PreviousRunId);
            Assert.Contains("No previous run", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static TestRunRecord SampleRun(
        string id,
        DateTimeOffset started,
        string dut,
        string plan,
        params (string Channel, double Value)[] samples)
        => new()
        {
            RunId = id,
            PlanId = plan,
            PlanName = plan,
            DutSerial = dut,
            StartedAt = started,
            Result = RunResult.Passed,
            Samples = samples
                .Select(s => new StoredSample
                {
                    Channel = s.Channel,
                    Value = s.Value,
                    Timestamp = started,
                })
                .ToList(),
        };
}
