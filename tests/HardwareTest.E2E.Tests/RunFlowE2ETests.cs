using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class RunFlowE2ETests
{
    private static async Task RunToCompletionAsync(RunTestViewModel runVm)
    {
        var runTask = runVm.Run.RunCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () =>
            {
                if (runVm.Interaction.IsAwaitingOperator)
                {
                    FillInteractionFieldsIfNeeded(runVm);
                    runVm.ContinueOperatorAttention();
                }

                return !runVm.IsRunning && runTask.IsCompleted;
            },
            TimeSpan.FromMinutes(2),
            "Run did not finish (operator prompt may be stuck).");
        await runTask;
    }

    private static void FillInteractionFieldsIfNeeded(RunTestViewModel runVm)
    {
        foreach (var field in runVm.Interaction.InteractionFields)
        {
            if (field.IsBoolean)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(field.Value))
            {
                continue;
            }

            field.Value = field.IsNumber ? "0" : "e2e-fixture";
        }
    }

    [AvaloniaFact]
    public void Session_dut_serial_textbox_binds_two_way_without_assigning_viewmodel()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.Name == "DutSerialBox");
        Assert.NotNull(box);

        box.Text = "E2E-TYPED-SN";
        Dispatcher.UIThread.RunJobs();

        var runVm = E2EHarness.RunTestVm(main);
        Assert.Equal("E2E-TYPED-SN", runVm.SessionPanel.DutSerialInput);
        Assert.True(runVm.Workspace.ShowPreparation);

        var host = window.GetVisualDescendants().OfType<InteractionHostView>().FirstOrDefault();
        Assert.NotNull(host);
        var continueButton = host.FindControl<Button>("ContinueButton");
        var scroller = host.FindControl<ScrollViewer>("PromptBodyScroller");
        Assert.NotNull(continueButton);
        Assert.NotNull(scroller);
        Assert.False(
            scroller.GetVisualDescendants().Contains(continueButton),
            "Continue must stay outside the prompt body scroller so it remains visible at 900×600.");
    }

    [AvaloniaFact]
    public async Task Run_test_confirm_dut_start_finish_sets_last_run_id()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        runVm.SessionPanel.DutSerialInput = "E2E-SN-1";
        runVm.SessionPanel.OperatorInput = "E2E-Tech";
        await runVm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(runVm.Workspace.ShowSteps);
        Assert.False(runVm.Workspace.CanOpenChart);

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

        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        runVm.SessionPanel.DutSerialInput = "E2E-SN-SEL";
        runVm.SessionPanel.OperatorInput = "E2E-Tech";
        await runVm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();

        var leaf = runVm.StepTree.StepRows.FirstOrDefault()
            ?? Flatten(runVm.StepTree.Hierarchy).FirstOrDefault(s => s.Children.Count == 0);
        Assert.NotNull(leaf);
        runVm.StepTree.SelectedStep = leaf;

        var runTask = runVm.Run.RunSelectedCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () =>
            {
                if (runVm.Interaction.IsAwaitingOperator)
                {
                    FillInteractionFieldsIfNeeded(runVm);
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
        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();

        main.NavigateToPageId("Inspect");
        Assert.Same(main.Inspect, main.CurrentPage);
        Assert.Contains(main.Inspect.Hierarchy, r => r.Children.Count > 0 || r.Name.Length > 0);
    }

    [AvaloniaFact]
    public async Task Inspect_OpenOnRun_selects_step_on_run_board()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);
        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();

        main.NavigateToPageId("Inspect");
        main.Inspect.Refresh();
        var leaf = Flatten(main.Inspect.Hierarchy).First(s => s.Children.Count == 0);
        main.Inspect.SelectedStep = leaf;
        await main.Inspect.OpenOnRunCommand.ExecuteAsync();

        Assert.Equal("RunTest", main.SelectedItem?.Id);
        Assert.Equal(leaf.Path, runVm.StepTree.SelectedStep?.Path);
    }

    [AvaloniaFact]
    public async Task Board_demo_program_loads_and_Inspect_shows_nested_sections()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        runVm.ProgramSelection.SelectedProgram = runVm.ProgramSelection.Programs.First(p =>
            string.Equals(p.Path, BoardDemoProgramFactory.EmbeddedName, StringComparison.OrdinalIgnoreCase));
        await E2EHarness.WaitUntilAsync(
            () => Flatten(runVm.StepTree.Hierarchy).Any(s => s.Path.Contains("Power Rails", StringComparison.OrdinalIgnoreCase)),
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

        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        runVm.SessionPanel.DutSerialInput = "E2E-SN-2";
        runVm.SessionPanel.OperatorInput = "E2E-Tech";
        await runVm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
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

        await runVm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        runVm.SessionPanel.DutSerialInput = "E2E-SN-3";
        runVm.SessionPanel.OperatorInput = "E2E-Tech";
        await runVm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
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

    [AvaloniaFact]
    public void SetLimits_does_not_drop_the_next_live_sample()
    {
        var plot = new HardwareTest.Widgets.MeasurementPlot.MeasurementPlotView();
        plot.UpdateTimeSeries([0], [1.0], 1, followLive: true, force: true);
        Assert.Equal(1, plot.LastRenderedPointCount);

        plot.SetLimits(0, 2);
        plot.UpdateTimeSeries([0, 1], [1.0, 2.0], 2, followLive: true, force: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, plot.LastRenderedPointCount);
    }
}
