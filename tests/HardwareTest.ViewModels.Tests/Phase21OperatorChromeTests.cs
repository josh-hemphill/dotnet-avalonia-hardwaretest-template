using Avalonia.Automation;
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

/// Phase 21 — Operator chrome & accessibility: type floor, compact labels, live regions, Settings headings.
public sealed class Phase21OperatorChromeTests
{
    [Fact]
    public void Operational_type_floor_is_12_to_13px()
    {
        Assert.InRange(OperatorTouchDensity.OperationalFontSize, 12, 13);
        Assert.True(OperatorTouchDensity.OperatorControlMinHeight >= 40);
    }

    [Fact]
    public void App_axaml_defines_op_type_and_settings_heading_styles()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/App/App.axaml"));
        Assert.Contains("OperatorTouchDensity.OperationalFontSize", axaml, StringComparison.Ordinal);
        Assert.Contains("TextBlock.op-type", axaml, StringComparison.Ordinal);
        Assert.Contains("TextBlock.settings-h2", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_board_operational_text_uses_type_floor_not_10_or_11px()
    {
        var markup = ConcatRunBoardMarkup();
        Assert.Contains("OperatorTouchDensity.OperationalFontSize", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"10\"", markup, StringComparison.Ordinal);
        Assert.Contains("Engineer/Debug only", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_pause_stop_captions_are_on_screen_without_tooltip()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/App/MainWindow.axaml"));
        Assert.Contains("PauseResumeLabel", axaml, StringComparison.Ordinal);
        Assert.Contains("SafetyStopLabel", axaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", axaml, StringComparison.Ordinal);
        Assert.Contains("CompactControlStatus", axaml, StringComparison.Ordinal);
        Assert.Contains("LiveSetting=\"Polite\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MaxLines=\"1\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TextWrapping=\"Wrap\"", CompactStatusBlock(axaml), StringComparison.Ordinal);
        var host = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/InteractionHostView.axaml"));
        Assert.Contains("LiveSetting=\"Assertive\"", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_board_does_not_live_announce_hero_or_plot_floods()
    {
        var run = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunTestView.axaml"));
        var header = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunHeaderView.axaml"));
        var host = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/InteractionHostView.axaml"));
        Assert.Equal(0, CountOccurrences(run, "AutomationProperties.LiveSetting"));
        Assert.Equal(0, CountOccurrences(header, "AutomationProperties.LiveSetting"));
        Assert.Equal(1, CountOccurrences(host, "AutomationProperties.LiveSetting"));
        Assert.Contains("AutomationProperties.Name=\"Operator prompt\"", host, StringComparison.Ordinal);
        Assert.Contains("HeroStatusLine", header, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.LiveSetting", run, StringComparison.Ordinal);
    }

    private static string ConcatRunBoardMarkup()
        => string.Concat(
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunTestView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunHeaderView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunStepsWorkspaceView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunDetailsWorkspaceView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunChartWorkspaceView.axaml")));

    [Fact]
    public void Settings_sections_are_automation_headings()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/Settings/SettingsView.axaml"));
        Assert.Contains("HeadingLevel=\"1\"", axaml, StringComparison.Ordinal);
        Assert.Contains("HeadingLevel=\"2\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Theme\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Engineer\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Storage\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Clock\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"About\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Diagnostics\"", axaml, StringComparison.Ordinal);
        Assert.Contains("OpenTAP packages", axaml, StringComparison.Ordinal);
        Assert.Equal(7, CountOccurrences(axaml, "HeadingLevel=\"2\""));
        var dataIdx = axaml.IndexOf("Text=\"Data directory\"", StringComparison.Ordinal);
        var themeHeadingIdx = axaml.IndexOf("Text=\"Theme\" Classes=\"settings-h2\"", StringComparison.Ordinal);
        var themeComboIdx = axaml.IndexOf("x:Name=\"ThemeLabel\"", StringComparison.Ordinal);
        Assert.True(dataIdx >= 0 && themeHeadingIdx >= 0 && themeComboIdx >= 0);
        Assert.True(dataIdx < themeHeadingIdx, "Data directory must not sit under the Theme heading.");
        Assert.True(themeHeadingIdx < themeComboIdx, "Theme heading must precede the theme combo.");

        var storageIdx = axaml.IndexOf("Text=\"Storage\" Classes=\"settings-h2\"", StringComparison.Ordinal);
        var clockIdx = axaml.IndexOf("Text=\"Clock\" Classes=\"settings-h2\"", StringComparison.Ordinal);
        var aboutIdx = axaml.IndexOf("Text=\"About\" Classes=\"settings-h2\"", StringComparison.Ordinal);
        var exportIdx = axaml.IndexOf("x:Name=\"ExportDirectoryLabel\"", StringComparison.Ordinal);
        var warnIdx = axaml.IndexOf("x:Name=\"WarnFreeLabel\"", StringComparison.Ordinal);
        var criticalIdx = axaml.IndexOf("x:Name=\"CriticalFreeLabel\"", StringComparison.Ordinal);
        var ntpIdx = axaml.IndexOf("x:Name=\"NtpHostLabel\"", StringComparison.Ordinal);
        Assert.True(storageIdx >= 0 && clockIdx >= 0 && aboutIdx >= 0);
        Assert.True(exportIdx >= 0 && warnIdx >= 0 && criticalIdx >= 0 && ntpIdx >= 0);
        Assert.True(
            storageIdx < exportIdx && exportIdx < warnIdx && warnIdx < criticalIdx && criticalIdx < clockIdx,
            "Export and free-space controls must stay under Storage, not Clock.");
        Assert.True(clockIdx < ntpIdx && ntpIdx < aboutIdx, "Clock controls must sit between Storage and About.");
    }

    [Fact]
    public void ControlStatusLiveSetting_is_polite_when_idle()
    {
        var vm = CreateMain();
        Assert.Equal("Idle", vm.ControlStatus);
        Assert.Equal(AutomationLiveSetting.Polite, vm.ControlStatusLiveSetting);
    }

    [Fact]
    public void ControlStatusLiveSetting_is_assertive_while_stopping_or_awaiting()
    {
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(openTap, runControl);

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        Assert.Equal(AutomationLiveSetting.Polite, vm.ControlStatusLiveSetting);

        runControl.RequestSafetyStop();
        Assert.Equal("Stopping…", vm.ControlStatus);
        Assert.Equal("Stopping…", vm.CompactControlStatus);
        Assert.Equal(AutomationLiveSetting.Assertive, vm.ControlStatusLiveSetting);

        runControl = new FakeRunControl();
        vm = CreateMain(openTap, runControl);
        runControl.AttachRun(new CancellationTokenSource());
        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly("Install fixture"));
        Assert.Equal(AutomationLiveSetting.Assertive, vm.ControlStatusLiveSetting);
    }

    [Fact]
    public void Compact_nav_still_exposes_pause_and_stop_labels()
    {
        var vm = CreateMain();
        vm.IsNavPaneOpen = false;
        Assert.Equal("Pause", vm.PauseResumeLabel);
        Assert.Equal("Stop Run", vm.SafetyStopLabel);
        Assert.Equal("Idle", vm.ControlStatus);
        Assert.Equal("Idle", vm.CompactControlStatus);
    }

    [Fact]
    public void CompactControlStatus_does_not_use_the_operator_prompt()
    {
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(openTap, runControl);
        runControl.AttachRun(new CancellationTokenSource());
        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly(
            "Install fixture and confirm the sweep area is clear before continuing"));

        Assert.Contains("Install fixture", vm.ControlStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Awaiting operator", vm.CompactControlStatus);
        Assert.True(vm.CompactControlStatus.Length < 24);
    }

    private static MainWindowViewModel CreateMain(
        FakeOpenTapSession? openTap = null,
        FakeRunControl? runControl = null)
    {
        openTap ??= new FakeOpenTapSession();
        runControl ??= new FakeRunControl();
        var store = new FakeSettingsStore();
        var runStore = new FakeRunStore();
        var runTest = new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            runStore,
            new AppSettings());
        return new MainWindowViewModel(
            store,
            new HomeViewModel(),
            runTest,
            new InspectViewModel(openTap),
            new ResultsViewModel(runStore, new FakeReportService()),
            new ReportPreviewViewModel(runStore, new FakeReportService()),
            new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap, new MockVisaSessionFactory(new VisaSessionGate())),
            new SettingsViewModel(store, openTap),
            runControl,
            openTap);
    }

    private static string CompactStatusBlock(string axaml)
    {
        var start = axaml.IndexOf("CompactControlStatus", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = axaml.IndexOf("<!-- Pause", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return axaml[start..end];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = 0; (i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length)
        {
            count++;
        }

        return count;
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
