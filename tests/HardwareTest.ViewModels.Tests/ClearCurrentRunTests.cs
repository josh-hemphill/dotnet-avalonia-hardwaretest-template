using System.Reactive.Linq;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ClearCurrentRunTests
{
    [Fact]
    public async Task Clear_removes_current_results_and_attempts_but_preserves_saved_runs_and_dut()
    {
        var host = new FakeOpenTapSession();
        using var store = new FakeRunStore();
        var session = new OperatorSession();
        var vm = new RunTestViewModel(host, host, host, session, new FakeRunControl(),
            new FakeReportService(), store, new AppSettings())
        { UiScheduler = action => action() };
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.SessionPanel.DutSerialInput = "SN-CLEAR";
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        var program = vm.ProgramSelection.SelectedProgram;
        var selected = vm.StepTree.SelectedStep;
        var runId = vm.LastRunId;
        Assert.NotNull(runId);
        Assert.NotNull(await store.LoadAsync(runId));
        Assert.Contains(StepHierarchy.Flatten(vm.StepTree.FullHierarchy), step => vm.Run.FindAttempt(step.Path) is not null);
        vm.IterationText = "3 / 3";

        await vm.ClearRunCommand.ExecuteAsync();

        Assert.Equal("SN-CLEAR", session.DutSerial);
        Assert.Same(program, vm.ProgramSelection.SelectedProgram);
        Assert.Same(selected, vm.StepTree.SelectedStep);
        Assert.Null(vm.LastRunId);
        Assert.Empty(vm.IterationText);
        Assert.Empty(vm.CurrentStepPath);
        Assert.Empty(vm.StepDetail.AttemptHistoryLines);
        Assert.Empty(vm.StepDetail.DetailLines);
        Assert.Empty(vm.Live.AvailableSeries);
        Assert.False(vm.Live.HasChartData);
        Assert.Equal(0, vm.Live.PlotYsLength);
        Assert.All(StepHierarchy.Flatten(vm.StepTree.FullHierarchy), step =>
        {
            Assert.Equal("Pending", step.ChipText);
            Assert.Null(step.KeyValue);
            Assert.Empty(step.AttemptsText);
            Assert.Null(vm.Run.FindAttempt(step.Path));
        });
        Assert.NotNull(await store.LoadAsync(runId));
        Assert.Contains("Saved runs remain", vm.Status);

        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(2, host.RunCount);
        Assert.All(StepHierarchy.Flatten(vm.StepTree.FullHierarchy)
            .Select(step => vm.Run.FindAttempt(step.Path)).Where(attempt => attempt is not null),
            attempt => Assert.Single(attempt!.Attempts));
        Assert.Equal(2, (await store.ListAsync()).Count);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("coordinator running")]
    [InlineData("paused")]
    [InlineData("shutdown")]
    [InlineData("prompt")]
    [InlineData("interaction prompt")]
    [InlineData("loading")]
    public async Task Clear_is_disabled_during_active_work(string state)
    {
        var host = new FakeOpenTapSession();
        var control = new FakeRunControl();
        var vm = RunTestViewModelTestFactory.Create(host, runControl: control);
        using var cts = new CancellationTokenSource();
        using var canExecute = vm.ClearRunCommand.CanExecute.Subscribe(_ => { });
        Assert.True(await vm.ClearRunCommand.CanExecute.FirstAsync());
        if (state == "coordinator running")
        {
            vm.IsRunning = true;
        }
        else if (state is "running" or "paused" or "shutdown")
        {
            control.AttachRun(cts);
            if (state == "paused") control.Pause();
            if (state == "shutdown") control.RequestSafetyStop();
        }
        else if (state == "prompt")
        {
            host.IsAwaitingOperator = true;
        }
        else if (state == "interaction prompt")
        {
            vm.Interaction.IsAwaitingOperator = true;
        }
        else
        {
            vm.ProgramSelection.IsBusy = true;
        }

        Assert.False(vm.CanClearRun);
        Assert.False(await vm.ClearRunCommand.CanExecute.FirstAsync());
        vm.IterationText = "2 / 3";
        await vm.ClearRunCommand.ExecuteAsync();
        Assert.Equal("2 / 3", vm.IterationText);
    }
}
