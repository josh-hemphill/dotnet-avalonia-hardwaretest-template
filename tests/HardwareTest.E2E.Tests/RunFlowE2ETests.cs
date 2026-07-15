using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class RunFlowE2ETests
{
    [AvaloniaFact]
    public async Task Run_test_load_start_finish_sets_last_run_id()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("RunTest");
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.LoadPlanCommand.ExecuteAsync();
        Assert.NotNull(runVm.Suite);

        await runVm.StartCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () => !runVm.IsRunning,
            TimeSpan.FromMinutes(2),
            "Run did not finish.");

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

        await runVm.LoadPlanCommand.ExecuteAsync();
        await runVm.StartCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(() => !runVm.IsRunning, TimeSpan.FromMinutes(2));

        main.NavigateToPageId("Results");
        var results = E2EHarness.ResultsVm(main);
        await results.RefreshCommand.ExecuteAsync();
        Assert.True(results.Runs.Count >= 1);

        results.SelectedRun = results.Runs[0];
        await results.OpenCommand.ExecuteAsync();
        Assert.NotNull(results.OpenedRun);
        Assert.Equal(RunResult.Passed, results.OpenedRun!.Result);
    }

    [AvaloniaFact]
    public async Task Report_preview_load_latest_renders_pages()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        var runVm = E2EHarness.RunTestVm(main);

        await runVm.LoadPlanCommand.ExecuteAsync();
        await runVm.StartCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(
            () => !runVm.IsRunning && !string.IsNullOrWhiteSpace(runVm.LastRunId),
            TimeSpan.FromMinutes(2));

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
