using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Features.RunTest;
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
        await runVm.ConfirmDutCommand.ExecuteAsync();

        await RunToCompletionAsync(runVm);

        Assert.False(runVm.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(runVm.LastRunId));
        Assert.Contains("Finished", runVm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Results_refresh_and_open_after_run()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.RefreshProgramsCommand.ExecuteAsync();
        runVm.DutSerialInput = "E2E-SN-2";
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
