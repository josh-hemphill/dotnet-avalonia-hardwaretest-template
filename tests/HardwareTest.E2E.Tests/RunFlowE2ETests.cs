using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class RunFlowE2ETests
{
    private static async Task RunToCompletionAsync(RunTestViewModel runVm)
    {
        var runTask = runVm.RunCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () =>
            {
                if (runVm.IsAwaitingOperator)
                {
                    runVm.ContinueOperatorAttention();
                }

                return !runVm.IsRunning && runTask.IsCompleted;
            },
            TimeSpan.FromMinutes(2),
            "Run did not finish (operator prompt may be stuck).");
        await runTask;
    }

    [AvaloniaFact]
    public async Task Run_test_confirm_dut_start_finish_sets_last_run_id()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.RefreshProgramsCommand.ExecuteAsync();
        runVm.DutSerialInput = "E2E-SN-1";
        runVm.OperatorInput = "E2E-Tech";
        await runVm.ConfirmDutCommand.ExecuteAsync();

        await RunToCompletionAsync(runVm);

        Assert.False(runVm.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(runVm.LastRunId));
        Assert.Contains("finished", runVm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Run_selected_leaf_completes_with_attempt_status()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.RefreshProgramsCommand.ExecuteAsync();
        runVm.DutSerialInput = "E2E-SN-SEL";
        runVm.OperatorInput = "E2E-Tech";
        await runVm.ConfirmDutCommand.ExecuteAsync();

        var leaf = runVm.StepRows.FirstOrDefault()
            ?? Flatten(runVm.Hierarchy).FirstOrDefault(s => s.Children.Count == 0);
        Assert.NotNull(leaf);
        runVm.SelectedStep = leaf;

        var runTask = runVm.RunSelectedCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () =>
            {
                if (runVm.IsAwaitingOperator)
                {
                    runVm.ContinueOperatorAttention();
                }

                return !runVm.IsRunning && runTask.IsCompleted;
            },
            TimeSpan.FromMinutes(2),
            "Run Selected did not finish.");
        await runTask;

        Assert.False(runVm.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(runVm.LastRunId));
        Assert.Contains("Attempt #", runVm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Results", runVm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Inspect_page_loads_hierarchy_tree()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);
        await runVm.RefreshProgramsCommand.ExecuteAsync();

        main.NavigateToPageId("Inspect");
        Assert.Equal("Inspect", main.SelectedItem?.Id);
        Assert.NotEmpty(main.Inspect.Hierarchy);
        Assert.Contains(main.Inspect.Hierarchy, r => r.Children.Count > 0 || r.Name.Length > 0);
    }

    [AvaloniaFact]
    public async Task Inspect_OpenOnRun_selects_step_on_run_board()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);
        await runVm.RefreshProgramsCommand.ExecuteAsync();

        main.NavigateToPageId("Inspect");
        main.Inspect.Refresh();
        var leaf = Flatten(main.Inspect.Hierarchy).First(s => s.Children.Count == 0);
        main.Inspect.SelectedStep = leaf;
        await main.Inspect.OpenOnRunCommand.ExecuteAsync();

        Assert.Equal("RunTest", main.SelectedItem?.Id);
        Assert.Equal(leaf.Path, runVm.SelectedStep?.Path);
    }

    [AvaloniaFact]
    public async Task Board_demo_program_loads_and_Inspect_shows_nested_sections()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.RefreshProgramsCommand.ExecuteAsync();
        runVm.SelectedProgram = runVm.Programs.First(p =>
            string.Equals(p.Path, BoardDemoProgramFactory.EmbeddedName, StringComparison.OrdinalIgnoreCase));
        await E2EHarness.WaitUntilAsync(
            () => Flatten(runVm.Hierarchy).Any(s => s.Path.Contains("Power Rails", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(30),
            "Board demo hierarchy did not load.");

        main.NavigateToPageId("Inspect");
        main.Inspect.Refresh();
        Assert.Contains(
            Flatten(main.Inspect.Hierarchy),
            s => s.Name.Contains("3V3", StringComparison.OrdinalIgnoreCase)
                 || s.Path.Contains("3V3", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<HierarchyStepViewModel> Flatten(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    [AvaloniaFact]
    public async Task Results_refresh_and_open_after_run()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.RefreshProgramsCommand.ExecuteAsync();
        runVm.DutSerialInput = "E2E-SN-2";
        runVm.OperatorInput = "E2E-Tech";
        await runVm.ConfirmDutCommand.ExecuteAsync();
        await RunToCompletionAsync(runVm);

        main.NavigateToPageId("Results");
        var results = E2EHarness.ResultsVm(main);
        await results.RefreshCommand.ExecuteAsync();
        Assert.True(results.Runs.Count >= 1);
        Assert.Equal("E2E-SN-2", results.Runs[0].DutSerial);

        results.SelectedRun = results.Runs[0];
        await results.OpenCommand.ExecuteAsync();
        Assert.NotNull(results.OpenedRun);
        Assert.Equal(RunResult.Passed, results.OpenedRun!.Result);
        Assert.Equal("E2E-SN-2", results.OpenedRun.DutSerial);
        Assert.True(results.ShowDetail);
    }

    [AvaloniaFact]
    public async Task Report_preview_load_latest_renders_pages()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.RefreshProgramsCommand.ExecuteAsync();
        runVm.DutSerialInput = "E2E-SN-3";
        runVm.OperatorInput = "E2E-Tech";
        await runVm.ConfirmDutCommand.ExecuteAsync();
        await RunToCompletionAsync(runVm);
        await E2EHarness.WaitUntilAsync(
            () => !string.IsNullOrWhiteSpace(runVm.LastRunId),
            TimeSpan.FromSeconds(30));

        main.NavigateToPageId("ReportPreview");
        var preview = E2EHarness.ReportPreviewVm(main);
        await preview.LoadLatestCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () => preview.Pages.Count >= 1 || preview.Status.Contains("failed", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromMinutes(2),
            "Report preview did not load pages.");

        Assert.True(preview.Pages.Count >= 1, preview.Status);
    }
}
