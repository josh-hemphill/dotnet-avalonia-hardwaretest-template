using HardwareTest.Core.Settings;
using HardwareTest.Features;
using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateMain(
        FakeSettingsStore store,
        FakeOpenTapSession openTap,
        FakeRunControl runControl,
        RunTestViewModel? runTest = null,
        ResultsViewModel? results = null,
        FakeRunStore? runStore = null)
    {
        runStore ??= new FakeRunStore();
        runTest ??= new RunTestViewModel(
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            runStore,
            new AppSettings());
        results ??= new ResultsViewModel(runStore, new FakeReportService());
        var inspect = new InspectViewModel(openTap);
        return new MainWindowViewModel(
            store,
            new HomeViewModel(),
            runTest,
            inspect,
            results,
            new ReportPreviewViewModel(runStore, new FakeReportService()),
            new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap),
            new SettingsViewModel(store, openTap),
            runControl,
            openTap);
    }

    [Fact]
    public void NavigateToPageId_updates_ui_state()
    {
        var store = new FakeSettingsStore();
        store.UiState.SelectedPageId = "Home";

        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var results = new ResultsViewModel(new FakeRunStore(), new FakeReportService());
        var vm = CreateMain(store, openTap, runControl, results: results);

        vm.NavigateToPageId("Results");

        Assert.Equal("Results", store.UiState.SelectedPageId);
        Assert.Same(results, vm.CurrentPage);
        Assert.Equal("Results", vm.SelectedItem?.Id);
    }

    [Fact]
    public void NavigationItems_include_Inspect()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = CreateMain(store, openTap, new FakeRunControl());
        Assert.Contains(vm.NavigationItems, i => i.Id == "Inspect");
    }

    [Fact]
    public async Task NavigateTo_Results_loads_runs()
    {
        var store = new FakeSettingsStore();
        store.UiState.SelectedPageId = "Home";
        var runStore = new FakeRunStore();
        await runStore.SaveAsync(new HardwareTest.Core.Runs.TestRunRecord
        {
            RunId = "run-nav-1",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-1",
            StartedAt = DateTimeOffset.UtcNow,
            Result = HardwareTest.Core.Runs.RunResult.Passed,
        });

        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var results = new ResultsViewModel(runStore, new FakeReportService());
        var vm = CreateMain(store, openTap, runControl, results: results, runStore: runStore);

        Assert.Empty(results.Runs);
        vm.NavigateToPageId("Results");
        await Task.Delay(100);
        Assert.NotEmpty(results.Runs);
        Assert.Contains("Loaded", results.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PauseResume_toggles_based_on_paused_state()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(store, openTap, runControl);

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        Assert.Equal("Pause", vm.PauseResumeLabel);
        Assert.True(vm.ShowPauseIcon);

        await vm.PauseResumeCommand.ExecuteAsync();
        Assert.True(runControl.IsPaused);
        Assert.Equal("Resume", vm.PauseResumeLabel);
        Assert.False(vm.ShowPauseIcon);

        await vm.PauseResumeCommand.ExecuteAsync();
        Assert.False(runControl.IsPaused);
        Assert.Equal("Pause", vm.PauseResumeLabel);
    }
}
