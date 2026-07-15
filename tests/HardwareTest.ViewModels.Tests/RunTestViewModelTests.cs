using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class RunTestViewModelTests
{
    [Fact]
    public async Task LoadSampleSuite_enqueues_suite_and_sets_status()
    {
        var plans = new FakePlanLoader();
        var vm = new RunTestViewModel(plans, new FakeSuiteEngine(), new FakeReportService(), new AppSettings(), new FakeRunControl());
        await vm.LoadPlanCommand.ExecuteAsync();
        Assert.Single(vm.SuiteQueue);
        Assert.NotNull(vm.Suite);
        Assert.Contains("Fake Suite", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_without_suite_sets_status()
    {
        var vm = new RunTestViewModel(new FakePlanLoader(), new FakeSuiteEngine(), new FakeReportService(), new AppSettings(), new FakeRunControl());
        await vm.StartCommand.ExecuteAsync();
        Assert.Contains("Load a suite", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manual_run_executes_selected_suite_only()
    {
        var engine = new FakeSuiteEngine();
        var reports = new FakeReportService();
        var vm = new RunTestViewModel(new FakePlanLoader(), engine, reports, new AppSettings(), new FakeRunControl())
        {
            IsAutoMode = false,
        };
        await vm.LoadPlanCommand.ExecuteAsync();
        await vm.LoadPlanCommand.ExecuteAsync();
        Assert.Equal(2, vm.SuiteQueue.Count);

        await vm.StartCommand.ExecuteAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal(1, engine.ExecuteCount);
        Assert.Equal(1, reports.GenerateCount);
        Assert.NotEmpty(vm.PlanItems);
    }

    [Fact]
    public async Task Auto_run_advances_to_next_suite()
    {
        var engine = new FakeSuiteEngine();
        var reports = new FakeReportService();
        var vm = new RunTestViewModel(new FakePlanLoader(), engine, reports, new AppSettings(), new FakeRunControl())
        {
            IsAutoMode = true,
        };
        await vm.LoadPlanCommand.ExecuteAsync();
        await vm.LoadPlanCommand.ExecuteAsync();

        await vm.StartCommand.ExecuteAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal(2, engine.ExecuteCount);
        Assert.Equal(2, reports.GenerateCount);
    }

    [Fact]
    public async Task Start_completed_run_generates_report_and_sets_last_run_id()
    {
        var engine = new FakeSuiteEngine
        {
            Result = new SuiteRunRecord
            {
                SuiteRunId = "abc",
                SuiteName = "Fake Suite",
                StartedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Passed,
                PlanRuns =
                [
                    new TestRunRecord
                    {
                        RunId = "r1",
                        PlanId = "fake",
                        PlanName = "Fake Plan",
                        StartedAt = DateTimeOffset.UtcNow,
                        Result = RunResult.Passed,
                        Samples = [new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1 }],
                    },
                ],
            },
        };
        var reports = new FakeReportService();
        var vm = new RunTestViewModel(new FakePlanLoader(), engine, reports, new AppSettings(), new FakeRunControl())
        {
            IsAutoMode = false,
        };
        await vm.LoadPlanCommand.ExecuteAsync();
        await vm.StartCommand.ExecuteAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal("abc", vm.LastRunId);
        Assert.Equal(1, reports.GenerateCount);
        Assert.NotEmpty(vm.PlanItems);
    }

    [Fact]
    public async Task Double_start_is_guarded()
    {
        var engine = new FakeSuiteEngine { Delay = TimeSpan.FromMilliseconds(200) };
        var vm = new RunTestViewModel(new FakePlanLoader(), engine, new FakeReportService(), new AppSettings(), new FakeRunControl())
        {
            IsAutoMode = false,
        };
        await vm.LoadPlanCommand.ExecuteAsync();

        var first = vm.StartCommand.ExecuteAsync();
        await Task.Delay(20);
        await vm.StartCommand.ExecuteAsync();
        Assert.Contains("Already running", vm.Status, StringComparison.OrdinalIgnoreCase);
        await first;
        Assert.Equal(1, engine.ExecuteCount);
    }

    [Fact]
    public async Task Cancel_stops_run_as_cancelled()
    {
        var engine = new FakeSuiteEngine { Delay = TimeSpan.FromSeconds(5) };
        var reports = new FakeReportService();
        var vm = new RunTestViewModel(new FakePlanLoader(), engine, reports, new AppSettings(), new FakeRunControl())
        {
            IsAutoMode = false,
        };
        await vm.LoadPlanCommand.ExecuteAsync();

        var start = vm.StartCommand.ExecuteAsync();
        await Task.Delay(30);
        Assert.True(vm.IsRunning);
        await vm.CancelCommand.ExecuteAsync();
        await start;

        Assert.False(vm.IsRunning);
        Assert.Contains("Cancelled", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, reports.GenerateCount);
    }

    [Fact]
    public async Task Auto_stops_on_failed_suite()
    {
        var engine = new FakeSuiteEngine { CompletionResult = RunResult.Failed };
        var vm = new RunTestViewModel(new FakePlanLoader(), engine, new FakeReportService(), new AppSettings(), new FakeRunControl())
        {
            IsAutoMode = true,
        };
        await vm.LoadPlanCommand.ExecuteAsync();
        await vm.LoadPlanCommand.ExecuteAsync();
        await vm.StartCommand.ExecuteAsync();

        Assert.Equal(1, engine.ExecuteCount);
        Assert.Contains("Auto stopped", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}
