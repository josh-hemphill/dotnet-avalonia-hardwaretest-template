using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorProgramOwnershipTests
{
    [Fact]
    public async Task Active_required_prompt_prevents_catalog_refresh_and_plan_picker_without_changing_context_or_evidence_then_recovers()
    {
        var tap = new FakeOpenTapSession { EmitOperatorInteractionDuringRun = true };
        using var store = new FakeRunStore();
        var prior = new TestRunRecord { RunId = "prior-owned-evidence", DutSerial = "PRIOR-DUT", OperatorName = "Prior operator", Result = RunResult.Failed };
        store.Seed(prior);
        var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(), new FakeRunControl(), new FakeReportService(), store,
            new AppSettings { UseMockVisa = true, IsEngineerDebugMode = true })
        { UiScheduler = action => action() };
        await vm.ProgramSelection.RefreshProgramsAsync();
        vm.SessionPanel.DutSerialInput = "OWNED-DUT";
        vm.SessionPanel.OperatorInput = "Run owner";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        var programs = vm.ProgramSelection.Programs.ToArray();
        var selected = vm.ProgramSelection.SelectedProgram;
        var session = vm.Session.SessionId;
        var pickerCalls = 0;
        var explainedOwnership = false;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Status) && vm.Status.Contains("Finish or Stop Run", StringComparison.Ordinal))
                explainedOwnership = true;
        };
        var pickedPath = Path.Combine(Path.GetTempPath(), "owner-recovery.TapPlan");
        vm.ProgramSelection.RequestPlanFilePath = _ =>
        {
            pickerCalls++;
            return Task.FromResult<string?>(pickedPath);
        };
        var running = vm.Run.RunCommand.ExecuteAsync();
        try
        {
            await WaitUntilAsync(() => vm.IsRunning && tap.IsAwaitingOperator);
            var savedBefore = (await store.ListAsync()).Select(record => (record.RunId, record.Result)).OrderBy(record => record.RunId).ToArray();
            var loadedPath = tap.LoadedPlanPath;
            tap.ThrowOnLoad = true;
            await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await vm.ProgramSelection.OpenPlanFileCommand.ExecuteAsync();
            Assert.Equal(0, pickerCalls);
            Assert.Equal(programs, vm.ProgramSelection.Programs.ToArray());
            Assert.Same(selected, vm.ProgramSelection.SelectedProgram);
            Assert.Equal(loadedPath, tap.LoadedPlanPath);
            Assert.Equal(session, vm.Session.SessionId);
            Assert.Equal("OWNED-DUT", vm.Session.DutSerial);
            Assert.Equal("Run owner", vm.Session.OperatorName);
            Assert.True(tap.IsAwaitingOperator);
            Assert.True(explainedOwnership, "The refused command must explain that the active run owns the program context.");
            Assert.Equal(savedBefore, (await store.ListAsync()).Select(record => (record.RunId, record.Result)).OrderBy(record => record.RunId).ToArray());
            Assert.Same(prior, await store.LoadAsync(prior.RunId));
        }
        finally
        {
            tap.ThrowOnLoad = false;
            if (vm.IsRunning) vm.Run.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(vm.IsRunning);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.NotSame(selected, vm.ProgramSelection.SelectedProgram);
        Assert.True(vm.IsSelectedProgramReady);
        await vm.ProgramSelection.OpenPlanFileCommand.ExecuteAsync();
        Assert.Equal(1, pickerCalls);
        Assert.Equal(pickedPath, vm.ProgramSelection.SelectedProgram!.Path);
        Assert.Equal(pickedPath, tap.LoadedPlanPath);
        Assert.Same(prior, await store.LoadAsync(prior.RunId));
    }

    [Fact]
    public async Task A_run_that_acquires_ownership_while_a_picker_is_open_prevents_its_late_selection()
    {
        var busy = false;
        var loads = 0;
        var status = string.Empty;
        var picked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var programs = new ProgramSelectionViewModel(value => status = value, () => true,
            _ => { loads++; return Task.CompletedTask; }, isRunning: () => busy)
        { RequestPlanFilePath = _ => picked.Task };
        await programs.RefreshProgramsAsync();
        var selected = programs.SelectedProgram;
        var snapshot = programs.Programs.ToArray();
        var initialLoads = loads;
        var opening = programs.OpenPlanFileCommand.ExecuteAsync();
        Assert.True(programs.IsBusy);
        busy = true;
        picked.SetResult("late-selection.TapPlan");
        await opening;
        Assert.Same(selected, programs.SelectedProgram);
        Assert.Equal(snapshot, programs.Programs.ToArray());
        Assert.Equal(initialLoads, loads);
        Assert.Contains("Finish or Stop Run", status, StringComparison.Ordinal);
        Assert.False(programs.IsBusy);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The fake run did not reach its active required prompt.");
            await Task.Delay(10);
        }
    }
}
