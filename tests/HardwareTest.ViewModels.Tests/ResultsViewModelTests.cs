using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsViewModelTests
{
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
