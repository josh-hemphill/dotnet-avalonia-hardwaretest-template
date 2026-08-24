using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features;
using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
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
            openTap,
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
            new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap, new MockVisaSessionFactory(new VisaSessionGate())),
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
    public void Operator_navigation_hides_engineer_pages()
    {
        var store = new FakeSettingsStore();
        var vm = CreateMain(store, new FakeOpenTapSession(), new FakeRunControl());
        Assert.Equal(4, vm.NavigationItems.Count);
        Assert.DoesNotContain(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Inspect);
        Assert.DoesNotContain(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Instruments);
        Assert.DoesNotContain(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.ReportPreview);
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Home);
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.RunTest);
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Results);
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Settings);
    }

    [Fact]
    public void Engineer_navigation_includes_inspect_and_instruments()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.IsEngineerDebugMode = true;
        var vm = CreateMain(store, new FakeOpenTapSession(), new FakeRunControl());
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Inspect);
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Instruments);
        Assert.DoesNotContain(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.ReportPreview);
    }

    [Fact]
    public async Task Saving_engineer_mode_rebuilds_nav_and_report_preview_stays_reachable()
    {
        var store = new FakeSettingsStore();
        var vm = CreateMain(store, new FakeOpenTapSession(), new FakeRunControl());
        Assert.DoesNotContain(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Inspect);

        store.AppSettings.IsEngineerDebugMode = true;
        await store.SaveAppSettingsAsync();
        Assert.Contains(vm.NavigationItems, i => i.Id == ShellNavigationPolicy.Inspect);

        vm.NavigateToPageId(ShellNavigationPolicy.ReportPreview);
        Assert.Same(vm.ReportPreview, vm.CurrentPage);
        Assert.Equal(ShellNavigationPolicy.Results, vm.SelectedItem?.Id);
    }

    [Fact]
    public void Startup_on_hidden_engineer_page_falls_back_to_home()
    {
        var store = new FakeSettingsStore();
        store.UiState.SelectedPageId = ShellNavigationPolicy.Inspect;
        var vm = CreateMain(store, new FakeOpenTapSession(), new FakeRunControl());
        Assert.Equal(ShellNavigationPolicy.Home, vm.SelectedItem?.Id);
    }

    [Fact]
    public void NavigationItems_include_Inspect()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.IsEngineerDebugMode = true;
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
        Assert.Equal(FluentAvalonia.UI.Controls.FASymbol.PauseFilled, vm.PauseResumeSymbol);

        await vm.PauseResumeCommand.ExecuteAsync();
        Assert.True(runControl.IsPaused);
        Assert.Equal("Resume", vm.PauseResumeLabel);
        Assert.False(vm.ShowPauseIcon);
        Assert.True(vm.ShowResumeIcon);
        Assert.Equal(FluentAvalonia.UI.Controls.FASymbol.PlayFilled, vm.PauseResumeSymbol);

        await vm.PauseResumeCommand.ExecuteAsync();
        Assert.False(runControl.IsPaused);
        Assert.Equal("Pause", vm.PauseResumeLabel);
        Assert.Equal(FluentAvalonia.UI.Controls.FASymbol.PauseFilled, vm.PauseResumeSymbol);
    }

    [Fact]
    public async Task PauseResume_when_awaiting_shows_Continue_and_invokes_run_continue()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var runTest = new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            new FakeRunStore(),
            new AppSettings());
        var vm = CreateMain(store, openTap, runControl, runTest: runTest);

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly("Install fixture"));

        Assert.Equal("Continue", vm.PauseResumeLabel);
        Assert.True(vm.ShowContinueIcon);
        Assert.False(vm.ShowPauseIcon);
        Assert.False(vm.ShowResumeIcon);
        Assert.Equal(FluentAvalonia.UI.Controls.FASymbol.Accept, vm.PauseResumeSymbol);
        Assert.Contains("Install fixture", vm.ControlStatus, StringComparison.OrdinalIgnoreCase);

        await vm.PauseResumeCommand.ExecuteAsync();
        Assert.False(openTap.IsAwaitingOperator);
        Assert.Equal("RunTest", vm.SelectedItem?.Id);
    }

    [Fact]
    public void SafetyStopTip_matches_shared_stop_run_copy()
    {
        var vm = CreateMain(new FakeSettingsStore(), new FakeOpenTapSession(), new FakeRunControl());
        Assert.Equal(StopRunCopy.Label, vm.SafetyStopLabel);
        Assert.Equal(StopRunCopy.CooperativeTip, vm.SafetyStopTip);
        Assert.Contains("Not a hardware interlock", vm.SafetyStopTip, StringComparison.Ordinal);
    }
}
