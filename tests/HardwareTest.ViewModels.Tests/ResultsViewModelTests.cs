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
            Samples = [new StoredSample { Channel = "VDC", Value = 8.5, Timestamp = DateTimeOffset.UtcNow }],
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
}
