using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
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
}
