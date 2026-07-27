using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class DutHistoryServiceTests
{
    [Fact]
    public async Task Analyze_flags_watch_when_channel_mean_shifts_over_5_percent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-dut-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            var prior = new TestRunRecord
            {
                RunId = "prior-1",
                PlanId = "sample",
                PlanName = "Sample Hardware Suite",
                DutSerial = "SN-HIST",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CompletedAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(5),
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample { Channel = "VDC", Value = 10.0, Timestamp = DateTimeOffset.UtcNow.AddHours(-1) },
                    new StoredSample { Channel = "VDC", Value = 10.0, Timestamp = DateTimeOffset.UtcNow.AddHours(-1).AddSeconds(1) },
                ],
            };
            await store.SaveAsync(prior);

            var current = new TestRunRecord
            {
                RunId = "current-1",
                PlanId = "sample",
                PlanName = "Sample Hardware Suite",
                DutSerial = "SN-HIST",
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "VDC",
                        MetricKey = "VDC",
                        Value = 9.2,
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                ],
            };

            var service = new DutHistoryService(store);
            var report = await service.AnalyzeAsync(current);
            Assert.Equal(1, report.PriorRunCount);
            Assert.Equal(DutHistorySeverity.Watch, report.OverallSeverity);
            Assert.Contains("Watch", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("VDC", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_returns_ok_when_within_threshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-dut-hist-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            await store.SaveAsync(new TestRunRecord
            {
                RunId = "p1",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-OK",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "raw",
                        MetricKey = "rail.3v3",
                        Value = 1.0,
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                ],
            });

            var report = await new DutHistoryService(store).AnalyzeAsync(new TestRunRecord
            {
                RunId = "c1",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-OK",
                StartedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "other",
                        MetricKey = "rail.3v3",
                        Value = 1.02,
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                ],
            });

            Assert.Equal(DutHistorySeverity.Normal, report.OverallSeverity);
            Assert.Contains(report.Metrics, m => m.Channel == "rail.3v3");
            Assert.Contains("OK", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_uses_per_metric_alert_threshold_from_sample()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-dut-hist-thr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            await store.SaveAsync(new TestRunRecord
            {
                RunId = "prior",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-THR",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Result = RunResult.Passed,
                Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
            });

            var current = new TestRunRecord
            {
                RunId = "cur",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-THR",
                StartedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "VDC",
                        MetricKey = "VDC",
                        Value = 9.2, // 8% — default watch, but watch raised to 20 so Normal
                        HistoryWatchPercent = 20,
                        HistoryAlertPercent = 30,
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                ],
            };

            var report = await new DutHistoryService(store).AnalyzeAsync(current);
            Assert.Equal(DutHistorySeverity.Normal, report.OverallSeverity);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_skips_metric_when_history_disabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-dut-hist-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileRunStore(dir);
            await store.SaveAsync(new TestRunRecord
            {
                RunId = "prior",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-OFF",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Result = RunResult.Passed,
                Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
            });

            var current = new TestRunRecord
            {
                RunId = "cur",
                PlanId = "sample",
                PlanName = "Sample",
                DutSerial = "SN-OFF",
                StartedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Passed,
                Samples =
                [
                    new StoredSample
                    {
                        Channel = "VDC",
                        MetricKey = "VDC",
                        Value = 1,
                        HistoryEnabled = false,
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                ],
            };

            var report = await new DutHistoryService(store).AnalyzeAsync(current);
            Assert.DoesNotContain(report.Metrics, m => m.Channel == "VDC");
            Assert.Contains("OK", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
