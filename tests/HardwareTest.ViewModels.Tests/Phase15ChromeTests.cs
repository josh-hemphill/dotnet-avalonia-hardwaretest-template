using Avalonia.Media;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
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
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Phase 15 — Operator feedback & Settings chrome: targeted regression tests.
public sealed class Phase15ChromeTests
{
    private static RunTestViewModel CreateRunVm(FakeOpenTapSession? openTap = null)
        => RunTestViewModelTestFactory.Create(openTap);

    [Fact]
    public void CanStartRunTip_mentions_confirm_dut_when_session_blocked()
    {
        var vm = CreateRunVm();
        Assert.False(vm.CanStartRun);
        Assert.Contains("Confirm DUT", vm.CanStartRunTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanStartRunTip_mentions_stop_run_while_running()
    {
        var vm = CreateRunVm();
        vm.SessionPanel.DutSerialInput = "SN-1";
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        vm.IsRunning = true;

        Assert.False(vm.CanStartRun);
        Assert.Contains("Stop Run", vm.CanStartRunTip, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.ShowOverallProgress);
    }

    [Fact]
    public void ShowOverallProgress_false_when_idle()
    {
        var vm = CreateRunVm();
        Assert.False(vm.IsRunning);
        Assert.False(vm.ShowOverallProgress);
    }

    [Fact]
    public async Task BlockStart_without_program_sets_warning_banner()
    {
        var vm = CreateRunVm();
        vm.SessionPanel.DutSerialInput = "SN-1";
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        vm.ProgramSelection.Programs.Clear();
        vm.ProgramSelection.SelectedProgram = null;

        await vm.Run.RunCommand.ExecuteAsync();

        Assert.True(vm.HasBanner);
        Assert.Equal(RunBannerSeverity.Warning, vm.BannerSeverity);
        Assert.Contains("program", vm.BannerMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmSession_empty_serial_sets_field_error()
    {
        var vm = CreateRunVm();
        vm.SessionPanel.DutSerialInput = " ";
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();

        Assert.True(vm.SessionPanel.HasDutSerialError);
        Assert.False(string.IsNullOrWhiteSpace(vm.SessionPanel.DutSerialError));
    }

    [Fact]
    public void StepFilter_flags_track_status_filter()
    {
        var tree = new StepTreeViewModel();
        Assert.True(tree.IsFilterAll);

        tree.StepStatusFilter = StepStatusFilter.Fail;
        Assert.True(tree.IsFilterFail);
        Assert.False(tree.IsFilterAll);

        tree.StepStatusFilter = StepStatusFilter.Running;
        Assert.True(tree.IsFilterRunning);

        tree.StepStatusFilter = StepStatusFilter.Pending;
        Assert.True(tree.IsFilterPending);
    }

    [Fact]
    public void Instruments_ShowDiscoverEmpty_when_idle_with_no_resources()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = new InstrumentsViewModel(
            store,
            new FakeVisaDiscovery(),
            openTap,
            new MockVisaSessionFactory(new VisaSessionGate()));

        Assert.False(vm.IsBusy);
        Assert.True(vm.ShowDiscoverEmpty);
    }

    [Fact]
    public void Instruments_NavigateToRun_raises_event()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = new InstrumentsViewModel(
            store,
            new FakeVisaDiscovery(),
            openTap,
            new MockVisaSessionFactory(new VisaSessionGate()));
        var raised = false;
        vm.NavigateToRunRequested += (_, _) => raised = true;

        vm.NavigateToRunCommand.Execute().Subscribe();

        Assert.True(raised);
    }

    [Fact]
    public void ReportPreview_ShowEmptyState_and_NavigateToResults()
    {
        var vm = new ReportPreviewViewModel(new FakeRunStore(), new FakeReportService());
        Assert.True(vm.ShowEmptyState);

        var raised = false;
        vm.NavigateToResultsRequested += (_, _) => raised = true;
        vm.NavigateToResultsCommand.Execute().Subscribe();
        Assert.True(raised);
    }

    [Fact]
    public void MainWindow_wires_Instruments_NavigateToRun_and_Preview_NavigateToResults()
    {
        var store = new FakeSettingsStore();
        store.UiState.SelectedPageId = "Home";
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var runStore = new FakeRunStore();
        var instruments = new InstrumentsViewModel(
            store,
            new FakeVisaDiscovery(),
            openTap,
            new MockVisaSessionFactory(new VisaSessionGate()));
        var preview = new ReportPreviewViewModel(runStore, new FakeReportService());
        var results = new ResultsViewModel(runStore, new FakeReportService());
        var runTest = new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            runStore,
            new AppSettings());
        var main = new MainWindowViewModel(
            store,
            new HomeViewModel(),
            runTest,
            new InspectViewModel(openTap),
            results,
            preview,
            instruments,
            new SettingsViewModel(store, openTap),
            runControl,
            openTap);

        instruments.NavigateToRunCommand.Execute().Subscribe();
        Assert.Equal("RunTest", main.SelectedItem?.Id);

        main.NavigateToPageId("Home");
        preview.NavigateToResultsCommand.Execute().Subscribe();
        Assert.Equal("Results", main.SelectedItem?.Id);
    }

    [Fact]
    public void ChipBrushConverter_maps_RunResult_Passed_and_Failed()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var pass = ChipBrushConverter.Instance.Convert(
            RunResult.Passed,
            typeof(IBrush),
            null,
            culture);
        var fail = ChipBrushConverter.Instance.Convert(
            RunResult.Failed,
            typeof(IBrush),
            null,
            culture);
        Assert.IsAssignableFrom<IBrush>(pass);
        Assert.IsAssignableFrom<IBrush>(fail);
    }

    [Theory]
    [InlineData(ChipPalette.Pass)]
    [InlineData(ChipPalette.Fail)]
    [InlineData(ChipPalette.Running)]
    [InlineData(ChipPalette.Awaiting)]
    [InlineData(ChipPalette.Error)]
    [InlineData(ChipPalette.Pending)]
    public void Chip_backgrounds_meet_wcag_aa_against_white(string backgroundHex)
    {
        var ratio = HardwareTest.Core.Presentation.ContrastMath.RatioHex(ChipPalette.Foreground, backgroundHex);
        Assert.True(
            ratio >= HardwareTest.Core.Presentation.ContrastMath.WcagAaNormalText,
            $"{backgroundHex} vs white ratio={ratio:F2}");
    }

    [Fact]
    public void Settings_AboutVersion_uses_short_product_version()
    {
        var build = new HardwareTest.Core.Diagnostics.BuildInfo
        {
            Version = "1.2.3",
            InformationalVersion = "1.2.3+deadbeef.20260101",
            CommitSha = "deadbeef",
            BuildTimestampUtc = DateTimeOffset.Parse(
                "2026-01-01T00:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture),
            RuntimeVersion = "10.0",
            RuntimeIdentifier = "linux-x64",
            OsDescription = "test",
            ProcessArchitecture = "X64",
            IsSelfContained = false,
            ProcessStartUtc = DateTimeOffset.UtcNow,
            OpenTapEngineVersion = "9.0",
        };
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store, new FakeOpenTapSession(), build);
        Assert.Equal("1.2.3", vm.AboutVersion);
        Assert.DoesNotContain("deadbeef", vm.AboutVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Results_Refresh_clears_IsBusy_after_completion()
    {
        var vm = new ResultsViewModel(new FakeRunStore(), new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Results_concurrent_Refresh_does_not_drop_second_load()
    {
        var store = new FakeRunStore();
        var vm = new ResultsViewModel(store, new FakeReportService());

        // Simulate navigate-to-Results LoadRunsAsync racing with an explicit Refresh.
        var first = vm.LoadRunsAsync();
        store.Seed(new TestRunRecord
        {
            RunId = "race-1",
            PlanName = "P",
            DutSerial = "SN-RACE",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        });
        await vm.RefreshCommand.ExecuteAsync();
        await first;

        Assert.False(vm.IsBusy);
        Assert.True(vm.HasRuns);
        Assert.Contains(vm.Runs, r => r.RunId == "race-1");
    }
}
