using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using HardwareTest.ViewModels.Tests.Time;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsViewModelTests
{
    [Fact]
    public async Task Open_shows_steps_and_dut_columns()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "r2",
            PlanName = "Sample",
            DutSerial = "SN-R",
            DutPartNumber = "PN-R",
            SessionId = "sess123456",
            OperatorName = "Op",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "Identity",
                    StepType = "IdentityCheckStep",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
            Samples =
            [
                new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.1 },
            ],
        });
        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal("SN-R", vm.Runs[0].DutSerial);
        Assert.Equal("PN-R", vm.Runs[0].DutPartNumber);
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.ShowDetail);
        Assert.NotEmpty(vm.StepDetails);
        Assert.NotEmpty(vm.SampleDetails);
    }

    [Fact]
    public async Task Open_caps_step_and_sample_sidebar_rows()
    {
        var store = new FakeRunStore();
        var steps = Enumerable.Range(0, 250)
            .Select(i => new StepResultRecord
            {
                StepId = $"step-{i}",
                StepType = "Acquire",
                Passed = true,
                Message = "ok",
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            })
            .ToList();
        var samples = Enumerable.Range(0, 250)
            .Select(i => new StoredSample
            {
                Channel = "VDC",
                Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(i),
                Value = i,
            })
            .ToList();
        store.Seed(new TestRunRecord
        {
            RunId = "big",
            PlanName = "Sample",
            DutSerial = "SN-BIG",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Steps = steps,
            Samples = samples,
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        Assert.Equal(201, vm.StepDetails.Count);
        Assert.Contains(vm.StepDetails, line => line.Contains("more steps", StringComparison.Ordinal));
        Assert.Equal(201, vm.SampleDetails.Count);
        Assert.Contains(vm.SampleDetails, line => line.Contains("more samples", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Open_shows_dut_history_when_priors_exist()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-H",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
        });
        store.Seed(new TestRunRecord
        {
            RunId = "current",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-H",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 8.5, Timestamp = DateTimeOffset.UtcNow, HistoryEnabled = true }],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var history = new DutHistoryService(store);
        var vm = new ResultsViewModel(store, new FakeReportService(), history);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs.First(r => r.RunId == "current");
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasHistory);
        Assert.Contains("Alert", vm.HistorySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(DutHistorySeverity.Alert), vm.HistorySeverity);
    }

    [Fact]
    public async Task Open_sample_details_include_metric_key_and_role()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "pres-1",
            PlanName = "Sample",
            DutSerial = "SN-P",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Value = 1.25,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            ],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.Contains(vm.SampleDetails, line =>
            line.Contains("VDC", StringComparison.OrdinalIgnoreCase)
            && line.Contains("timeseries", StringComparison.OrdinalIgnoreCase)
            && line.Contains("V", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Open_builds_presentation_chart_and_gauge_tiles()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "pres-tiles",
            PlanName = "Sample",
            DutSerial = "SN-T",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Value = 1.0,
                    Timestamp = DateTimeOffset.UtcNow,
                },
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Value = 1.2,
                    Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(5),
                },
                new StoredSample
                {
                    Channel = "Mean",
                    MetricKey = "VDC.mean",
                    DisplayRole = "scalar",
                    Unit = "V",
                    Value = 1.1,
                    LimitLow = 0,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            ],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasPresentationTiles);
        Assert.Contains(vm.PresentationTiles, t => t.IsChart && t.YsLength == 2);
        Assert.Contains(vm.PresentationTiles, t => t.IsGauge && t.MetricKey == "VDC.mean");
    }

    [Fact]
    public async Task Refresh_loads_runs()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "r1",
            PlanName = "P",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        });
        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.Runs);
        Assert.Contains("1 run", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Open_without_selection_sets_status()
    {
        var vm = new ResultsViewModel(new FakeRunStore(), new FakeReportService());
        await vm.OpenCommand.ExecuteAsync();
        Assert.Contains("Select a run", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reprint_raises_report_opened()
    {
        var store = new FakeRunStore();
        var run = new TestRunRecord
        {
            RunId = "r1",
            PlanName = "P",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        store.Seed(run);
        var reports = new FakeReportService { PdfPath = Path.Combine(Path.GetTempPath(), "rpt.pdf") };
        await File.WriteAllTextAsync(reports.PdfPath, "pdf");
        var vm = new ResultsViewModel(store, reports);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];

        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        await vm.ReprintCommand.ExecuteAsync();

        Assert.Equal(reports.PdfPath, opened);
        Assert.Equal(1, reports.GenerateCount);
    }

    [Fact]
    public async Task Selecting_run_opens_detail_without_opening_report()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(Path.GetTempPath(), "status-sel.pdf");
        await File.WriteAllTextAsync(pdf, "pdf");
        store.Seed(new TestRunRecord
        {
            RunId = "sel-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            ReportPdfPath = pdf,
            Reports =
            [
                new RunReportArtifact
                {
                    Kind = ReportKinds.Status,
                    Title = "Status",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        vm.SelectedRun = vm.Runs[0];
        await Task.Delay(50);
        Assert.True(vm.ShowDetail);
        Assert.NotEmpty(vm.StepDetails);
        Assert.Null(opened);
    }

    [Fact]
    public async Task Open_default_report_uses_catalog_default_kind()
    {
        var store = new FakeRunStore();
        var statusPdf = Path.Combine(Path.GetTempPath(), "def-status.pdf");
        var certPdf = Path.Combine(Path.GetTempPath(), "def-cert.pdf");
        await File.WriteAllTextAsync(statusPdf, "pdf");
        await File.WriteAllTextAsync(certPdf, "pdf");
        store.Seed(new TestRunRecord
        {
            RunId = "def-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Kind = ReportKinds.Certification,
                    Title = "Certification",
                    PdfPath = certPdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
                new RunReportArtifact
                {
                    Kind = ReportKinds.Status,
                    Title = "Status",
                    PdfPath = statusPdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await Task.Delay(50);
        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        await vm.OpenSelectedRunDefaultReportAsync();
        Assert.Equal(statusPdf, opened);
        Assert.Contains(vm.ReportItems, r => r.IsDefault && r.Kind == ReportKinds.Status);
    }

    [Fact]
    public void ResolveDefaultReportPath_prefers_default_kind()
    {
        var run = new TestRunRecord
        {
            PlanId = "sample",
            Reports =
            [
                new RunReportArtifact { Kind = ReportKinds.Certification, PdfPath = "c.pdf" },
                new RunReportArtifact { Kind = ReportKinds.Status, PdfPath = "s.pdf" },
            ],
        };
        Assert.Equal("s.pdf", ResultsViewModel.ResolveDefaultReportPath(run));
    }

    [Fact]
    public async Task Search_and_result_filter_narrow_run_list()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "a",
            PlanId = "sample",
            PlanName = "Sample Hardware Suite",
            DutSerial = "SN-A",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            Result = RunResult.Passed,
        });
        store.Seed(new TestRunRecord
        {
            RunId = "b",
            PlanId = "board-demo",
            PlanName = "Board Demo",
            DutSerial = "SN-B",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Result = RunResult.Failed,
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(2, vm.Runs.Count);

        vm.SearchText = "Board";
        Assert.Single(vm.Runs);
        Assert.Equal("b", vm.Runs[0].RunId);

        vm.SearchText = string.Empty;
        vm.ResultFilter = nameof(RunResult.Passed);
        Assert.Single(vm.Runs);
        Assert.Equal("a", vm.Runs[0].RunId);
        Assert.Contains("Showing 1 of 2", vm.FilterStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_populates_history_metric_rows_and_reports()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-M",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
        });
        store.Seed(new TestRunRecord
        {
            RunId = "current",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-M",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 8, Timestamp = DateTimeOffset.UtcNow, HistoryEnabled = true }],
            Reports =
            [
                new RunReportArtifact
                {
                    Kind = ReportKinds.Status,
                    Title = "Status Report",
                    PdfPath = Path.Combine(Path.GetTempPath(), "status.pdf"),
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var history = new DutHistoryService(store);
        var vm = new ResultsViewModel(store, new FakeReportService(), history);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs.First(r => r.RunId == "current");
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasHistory);
        Assert.NotEmpty(vm.HistoryMetrics);
        Assert.True(vm.HasReports);
        Assert.Contains(vm.ReportItems, r => r.Kind == ReportKinds.Status);
    }

    [Fact]
    public async Task Operator_and_date_filters_narrow_run_list_with_yield()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 24, 18, 0, 0, TimeSpan.Zero));
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "today-pass",
            PlanName = "Sample",
            OperatorName = "Ada",
            StartedAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            Result = RunResult.Passed,
        });
        store.Seed(new TestRunRecord
        {
            RunId = "today-fail",
            PlanName = "Sample",
            OperatorName = "Ada",
            StartedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            Result = RunResult.Failed,
        });
        store.Seed(new TestRunRecord
        {
            RunId = "old-fail",
            PlanName = "Sample",
            OperatorName = "Bob",
            StartedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            Result = RunResult.Failed,
        });

        var vm = new ResultsViewModel(store, new FakeReportService(), clock: clock);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(3, vm.Runs.Count);
        Assert.Contains("Passed 1", vm.FilterStatus, StringComparison.Ordinal);
        Assert.Contains("Failed 2", vm.FilterStatus, StringComparison.Ordinal);

        vm.OperatorFilter = "Ada";
        Assert.Equal(2, vm.Runs.Count);
        Assert.Contains("Passed 1", vm.YieldSummary, StringComparison.Ordinal);
        Assert.Contains("Failed 1", vm.YieldSummary, StringComparison.Ordinal);

        vm.DateFilter = ResultsViewModel.DateToday;
        Assert.Equal(2, vm.Runs.Count);
        Assert.DoesNotContain(vm.Runs, r => r.RunId == "old-fail");

        vm.OperatorFilter = ResultsViewModel.AllFilter;
        vm.DateFilter = ResultsViewModel.DateLast7Days;
        Assert.Equal(2, vm.Runs.Count);
        Assert.Contains(vm.Runs, r => r.RunId == "today-pass");
        Assert.Contains(vm.Runs, r => r.RunId == "today-fail");
    }

    [Fact]
    public async Task OpenRunById_after_fail_sets_result_filter_and_failed_steps_only()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "pass",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            Result = RunResult.Passed,
            Steps = [new StepResultRecord { StepId = "Ok", StepType = "Id", Passed = true, Message = "ok" }],
        });
        var t1 = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 4, 1, 0, 0, 2, TimeSpan.Zero);
        store.Seed(new TestRunRecord
        {
            RunId = "fail",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Result = RunResult.Failed,
            Steps =
            [
                new StepResultRecord { StepId = "Ok", StepPath = "Ok", StepType = "Id", Passed = true, Message = "ok", CompletedAt = t1 },
                new StepResultRecord { StepId = "Bad", StepPath = "Bad", StepType = "Acquire", Passed = false, Message = "out", CompletedAt = t2 },
            ],
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "Ok",
                    StepName = "Ok",
                    AttemptCount = 1,
                    PassedCount = 1,
                    LatestPassed = true,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Ok", StepPath = "Ok", StepType = "Id", Passed = true, Message = "ok", CompletedAt = t1 },
                    ],
                },
                new StepAttemptSummary
                {
                    StepPath = "Bad",
                    StepName = "Bad",
                    AttemptCount = 2,
                    PassedCount = 1,
                    FailedCount = 1,
                    LatestPassed = false,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Bad", StepPath = "Bad", StepType = "Acquire", Passed = true, AttemptNumber = 1, Message = "ok", CompletedAt = t1 },
                        new StepResultRecord { StepId = "Bad", StepPath = "Bad", StepType = "Acquire", Passed = false, AttemptNumber = 2, Message = "out", CompletedAt = t2 },
                    ],
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.OpenRunByIdAsync("fail");

        Assert.Equal(nameof(RunResult.Failed), vm.ResultFilter);
        Assert.Single(vm.Runs);
        Assert.Equal("fail", vm.SelectedRun?.RunId);
        Assert.True(vm.ShowFailedStepsOnly);
        Assert.True(vm.HasFirstFail);
        Assert.Contains("Bad", vm.FirstFailSummary, StringComparison.Ordinal);
        Assert.Contains(vm.StepDetails, line => line.Contains("First fail", StringComparison.Ordinal));
        Assert.Contains(vm.StepDetails, line => line.Contains("2 (1F/1P)", StringComparison.Ordinal));
        Assert.Contains(vm.StepDetails, line => line.Contains("#2 FAIL", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.StepDetails, line => line.Contains("[Id]", StringComparison.Ordinal) && line.Contains("PASS"));

        vm.ShowFailedStepsOnly = false;
        Assert.Contains(vm.StepDetails, line => line.Contains("[Id]", StringComparison.Ordinal));
    }
}
