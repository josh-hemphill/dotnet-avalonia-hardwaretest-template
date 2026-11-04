using HardwareTest.Core.Runs;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorProgramAvailabilityTests
{
    [Fact]
    public async Task Initial_exact_host_plan_can_be_adopted_but_reselecting_after_a_failure_must_load_again()
    {
        var tap = new FakeOpenTapSession { ThrowOnLoad = true };
        var originalHostTree = tap.Tree.Single();
        originalHostTree.Children[0].Children[0].StatusText = "Pass";
        var vm = RunTestViewModelTestFactory.Create(tap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsAsync();
        var selected = vm.ProgramSelection.SelectedProgram!;
        Assert.True(vm.IsSelectedProgramReady);
        Assert.Same(originalHostTree, tap.Tree.Single());
        Assert.Equal("Pass", vm.StepTree.Hierarchy.Single().Children[0].Children[0].StatusText);
        vm.ProgramSelection.SelectedProgram = null;
        vm.ProgramSelection.SelectedProgram = selected;
        await ((IRunBoardHost)vm).LoadSelectedProgramAsync();
        Assert.Equal(selected.Path, tap.LoadedPlanPath);
        Assert.False(vm.IsSelectedProgramReady);
        Assert.Contains("Cannot load", vm.ProgramAvailabilityMessage, StringComparison.Ordinal);
        await vm.ProgramSelection.RefreshProgramsAsync();
        Assert.False(vm.IsSelectedProgramReady);
        Assert.Equal(0, tap.RunCount + tap.SelectionRunCount);
    }

    [Fact]
    public async Task Unloadable_assigned_program_never_runs_the_previous_plan_and_recovers_without_losing_input_or_saved_evidence()
    {
        var tap = new FakeOpenTapSession();
        using var store = new FakeRunStore();
        var prior = new TestRunRecord { RunId = "prior-evidence", PlanId = "original", DutSerial = "OLD-DUT", OperatorName = "Prior operator" };
        store.Seed(prior);
        var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(), new FakeRunControl(), new FakeReportService(), store,
            new HardwareTest.Core.Settings.AppSettings { UseMockVisa = true })
        { UiScheduler = action => action() };
        await vm.ProgramSelection.RefreshProgramsAsync();
        var correct = vm.ProgramSelection.SelectedProgram!;
        vm.SessionPanel.DutSerialInput = "RETAIN-DUT";
        vm.SessionPanel.OperatorInput = "Retain operator";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.CanStartRun);
        tap.ThrowOnLoad = true;
        vm.ProgramSelection.SelectedProgram = new ProgramItemViewModel
        {
            Id = "unavailable-assigned",
            DisplayName = "Assigned test unavailable",
            Path = SampleProgramFactory.EmbeddedName,
            LoadKind = ProgramLoadKind.FactorySample,
        };
        await ((IRunBoardHost)vm).LoadSelectedProgramAsync();
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(vm.SessionPanel.SessionBlocked);
        Assert.False(vm.CanStartRun);
        Assert.True(vm.ShowProgramRecovery);
        Assert.Contains("Cannot load", vm.ProgramAvailabilityMessage, StringComparison.Ordinal);
        Assert.Contains("station technician", vm.CanStartRunTip, StringComparison.Ordinal);
        await vm.Run.RunCommand.ExecuteAsync();
        await vm.Run.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(0, tap.RunCount);
        Assert.Equal(0, tap.SelectionRunCount);
        Assert.Equal("RETAIN-DUT", vm.SessionPanel.DutSerialInput);
        Assert.Equal("Retain operator", vm.SessionPanel.OperatorInput);
        Assert.Same(prior, await store.LoadAsync(prior.RunId));
        tap.ThrowOnLoad = false;
        vm.ProgramSelection.SelectedProgram = correct;
        await ((IRunBoardHost)vm).LoadSelectedProgramAsync();
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.CanStartRun);
        Assert.False(vm.ShowProgramRecovery);
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(1, tap.RunCount);
        Assert.Equal("RETAIN-DUT", tap.LastDut!.Serial);
        Assert.Same(prior, await store.LoadAsync(prior.RunId));
    }

    [Fact]
    public async Task Missing_selection_blocks_both_execution_scopes_and_a_catalog_reload_restores_a_deliberate_confirmation_path()
    {
        var tap = new FakeOpenTapSession();
        var vm = RunTestViewModelTestFactory.Create(tap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsAsync();
        vm.SessionPanel.DutSerialInput = "NO-SELECTION-DUT";
        vm.SessionPanel.OperatorInput = "Operator";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        vm.ProgramSelection.SelectedProgram = null;
        Assert.False(vm.CanStartRun);
        Assert.True(vm.ShowProgramRecovery);
        Assert.Contains("assigned test program", vm.CanStartRunTip, StringComparison.Ordinal);
        await vm.Run.RunCommand.ExecuteAsync();
        await vm.Run.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(0, tap.RunCount + tap.SelectionRunCount);
        Assert.Equal("NO-SELECTION-DUT", vm.SessionPanel.DutSerialInput);
        await vm.ProgramSelection.RefreshProgramsAsync();
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.CanStartRun);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Rapid_selection_changes_only_enable_the_exact_final_loaded_program()
    {
        var tap = new FakeOpenTapSession { YieldOnLoad = true };
        var vm = RunTestViewModelTestFactory.Create(tap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsAsync();
        var sample = vm.ProgramSelection.Programs.Single(program => program.Id == "sample");
        var board = vm.ProgramSelection.Programs.Single(program => program.Id == "board-demo");
        vm.ProgramSelection.SelectedProgram = board;
        vm.ProgramSelection.SelectedProgram = sample;
        Assert.False(vm.CanStartRun);
        await ((IRunBoardHost)vm).LoadSelectedProgramAsync();
        vm.SessionPanel.DutSerialInput = "FINAL-PROGRAM-DUT";
        vm.SessionPanel.OperatorInput = "Operator";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.CanStartRun);
        Assert.Equal(sample.Path, tap.LoadedPlanPath);
        Assert.Equal(sample.Id, vm.Session.ProgramId);
        Assert.Equal(0, tap.RunCount + tap.SelectionRunCount);
    }
}
