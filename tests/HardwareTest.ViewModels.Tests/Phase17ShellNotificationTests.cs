using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
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

/// Phase 17 — Shell notification strip: precedence, Publish/Clear, Run/Home wiring.
public sealed class Phase17ShellNotificationTests
{
    private sealed class FakeStorageHealth : IStorageHealthService
    {
        public required StorageHealthSnapshot Snapshot { get; init; }
        public StorageHealthSnapshot GetDataVolumeHealth() => Snapshot;
    }

    [Fact]
    public void Publish_lower_severity_does_not_replace_higher_from_other_source()
    {
        var shell = new ShellNotificationViewModel();
        shell.Publish(
            ShellNotificationSeverity.Critical,
            "Disk full",
            dismissible: false,
            sourceKey: ShellNotificationViewModel.SourceStorage);
        shell.Publish(
            ShellNotificationSeverity.Warning,
            "Soft warn",
            sourceKey: ShellNotificationViewModel.SourceRun);

        Assert.True(shell.HasContent);
        Assert.Equal(ShellNotificationSeverity.Critical, shell.Severity);
        Assert.Equal("Disk full", shell.Message);
    }

    [Fact]
    public void Publish_same_source_replaces_regardless_of_severity()
    {
        var shell = new ShellNotificationViewModel();
        shell.Publish(ShellNotificationSeverity.Error, "First", sourceKey: ShellNotificationViewModel.SourceRun);
        shell.Publish(ShellNotificationSeverity.Info, "Second", sourceKey: ShellNotificationViewModel.SourceRun);

        Assert.Equal(ShellNotificationSeverity.Info, shell.Severity);
        Assert.Equal("Second", shell.Message);
    }

    [Fact]
    public void Clear_only_matches_source_key()
    {
        var shell = new ShellNotificationViewModel();
        shell.Publish(
            ShellNotificationSeverity.Warning,
            "Storage",
            sourceKey: ShellNotificationViewModel.SourceStorage);
        shell.Clear(ShellNotificationViewModel.SourceRun);
        Assert.True(shell.HasContent);
        shell.Clear(ShellNotificationViewModel.SourceStorage);
        Assert.False(shell.HasContent);
    }

    [Fact]
    public void Dismiss_invokes_onDismissed_callback()
    {
        var shell = new ShellNotificationViewModel();
        var called = false;
        shell.Publish(
            ShellNotificationSeverity.Info,
            "Tip",
            dismissible: true,
            sourceKey: ShellNotificationViewModel.SourceHistory,
            onDismissed: () => called = true);
        shell.Dismiss();
        Assert.False(shell.HasContent);
        Assert.True(called);
    }

    [Fact]
    public void RunTest_SetBanner_publishes_to_shell()
    {
        var shell = new ShellNotificationViewModel();
        var vm = CreateRun(shell);

        vm.SetBanner(RunBannerSeverity.Error, "Run failed");

        Assert.True(vm.HasBanner);
        Assert.True(shell.HasContent);
        Assert.Equal(ShellNotificationSeverity.Error, shell.Severity);
        Assert.Equal("Run failed", shell.Message);
    }

    [Fact]
    public async Task RunTest_DismissBanner_clears_shell_run_source()
    {
        var shell = new ShellNotificationViewModel();
        var vm = CreateRun(shell);

        vm.SetBanner(RunBannerSeverity.Warning, "Check program");
        Assert.True(shell.HasContent);
        await vm.DismissBannerCommand.ExecuteAsync();
        Assert.False(vm.HasBanner);
        Assert.False(shell.HasContent);
    }

    [Fact]
    public void RunTest_HistoryBanner_publishes_info_to_shell()
    {
        var shell = new ShellNotificationViewModel();
        var vm = CreateRun(shell);

        vm.HistoryBanner = "DUT drift watch on channel A";
        Assert.True(shell.HasContent);
        Assert.Equal(ShellNotificationSeverity.Info, shell.Severity);
        Assert.Contains("DUT drift", shell.Message, StringComparison.Ordinal);

        vm.HistoryBanner = string.Empty;
        Assert.False(shell.HasContent);
    }

    [Fact]
    public void RunTest_storage_critical_publishes_non_dismissible()
    {
        var shell = new ShellNotificationViewModel();
        var storage = new FakeStorageHealth
        {
            Snapshot = new StorageHealthSnapshot
            {
                Level = StorageHealthLevel.Critical,
                AvailableBytes = 1,
                WarnThresholdBytes = 10,
                CriticalThresholdBytes = 5,
                Message = "Data volume critically low",
            },
        };
        var openTap = new FakeOpenTapSession();
        var vm = new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            new FakeRunControl(),
            new FakeReportService(),
            new FakeRunStore(),
            new AppSettings(),
            storageHealth: storage,
            shellNotification: shell);

        Assert.True(vm.HasStorageBanner);
        Assert.True(shell.HasContent);
        Assert.Equal(ShellNotificationSeverity.Critical, shell.Severity);
        Assert.False(shell.IsDismissible);
    }

    [Fact]
    public void Home_RefreshCrashBanner_without_dossier_leaves_shell_idle()
    {
        var shell = new ShellNotificationViewModel();
        var home = new HomeViewModel(settingsStore: null, shellNotification: shell);
        home.RefreshCrashBanner();
        Assert.False(home.HasCrashBanner);
        Assert.False(shell.HasContent);
    }

    [Fact]
    public void MainWindow_exposes_ShellNotification_host()
    {
        var shell = new ShellNotificationViewModel();
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var runTest = CreateRun(shell, openTap, runControl);
        var vm = new MainWindowViewModel(
            store,
            new HomeViewModel(shellNotification: shell),
            runTest,
            new InspectViewModel(openTap),
            new ResultsViewModel(new FakeRunStore(), new FakeReportService()),
            new ReportPreviewViewModel(new FakeRunStore(), new FakeReportService()),
            new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap, new MockVisaSessionFactory(new VisaSessionGate())),
            new SettingsViewModel(store, openTap),
            runControl,
            openTap,
            shell);

        Assert.Same(shell, vm.ShellNotification);
        Assert.Equal("Ready", vm.ShellNotification.IdleHint);
        Assert.False(vm.ShellNotification.HasContent);
    }

    private static RunTestViewModel CreateRun(
        ShellNotificationViewModel shell,
        FakeOpenTapSession? openTap = null,
        FakeRunControl? runControl = null)
    {
        openTap ??= new FakeOpenTapSession();
        runControl ??= new FakeRunControl();
        return new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            new FakeRunStore(),
            new AppSettings(),
            shellNotification: shell);
    }
}
