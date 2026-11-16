using System.Xml.Linq;
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
        Assert.Contains("ShellNotification.LiveSetting", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Run status\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Shell notification\"", axaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(axaml, "IsEnabled=\"{Binding CanPauseResume}\""));
        Assert.Equal(2, CountOccurrences(axaml, "IsEnabled=\"{Binding CanSafetyStop}\""));
        Assert.Equal(2, CountOccurrences(axaml, "AutomationProperties.LiveSetting=\"{Binding ControlStatusLiveSetting}\""));
        Assert.Contains("MaxLines=\"2\"", CompactStatusBlock(axaml), StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", CompactStatusBlock(axaml), StringComparison.Ordinal);
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
        Assert.Equal(2, CountOccurrences(host, "AutomationProperties.LiveSetting"));
        Assert.Contains("AutomationProperties.Name=\"Operator prompt validation\"", host, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Interaction.InteractionTitle}\"", host, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Operator prompt\"", host, StringComparison.Ordinal);
        Assert.Contains("HeroStatusLine", header, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.LiveSetting", run, StringComparison.Ordinal);
    }

    private static string ConcatRunBoardMarkup()
        => string.Concat(
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunTestView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunHeaderView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunStepsWorkspaceView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunDetailsWorkspaceView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunChartWorkspaceView.axaml")),
            File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunOverviewSidebarView.axaml")));

    [Fact]
    public void Settings_prioritizes_readiness_and_retains_named_diagnostic_disclosures()
    {
        var document = XDocument.Load(FindRepoFile("src/HardwareTest/Features/Settings/SettingsView.axaml"));
        var headings = document.Descendants().Where(element => element.Attribute("AutomationProperties.HeadingLevel") is not null).ToArray();
        Assert.Contains(headings, element => (string?)element.Attribute("Text") == "Settings" && (string?)element.Attribute("AutomationProperties.HeadingLevel") == "1");
        Assert.Contains(headings, element => (string?)element.Attribute("Text") == "Station readiness");
        Assert.Contains(headings, element => (string?)element.Attribute("Text") == "Operator preferences");
        var disclosures = document.Descendants().Where(element => element.Name.LocalName == "Expander").ToArray();
        foreach (var name in new[] { "Engineer settings", "Badge and signing settings", "Storage and station policy", "About this station", "Settings diagnostics", "Installed instrument packages" })
            Assert.Contains(disclosures, element => (string?)element.Attribute("AutomationProperties.Name") == name);
        var storage = Assert.Single(disclosures, element => (string?)element.Attribute("AutomationProperties.Name") == "Storage and station policy");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var label in new[] { "ExportDirectoryLabel", "WarnFreeLabel", "CriticalFreeLabel", "NtpHostLabel" })
            Assert.Contains(storage.Descendants(), element => (string?)element.Attribute(xaml + "Name") == label);
        var elements = document.Descendants().ToList();
        var readiness = Assert.Single(headings, element => (string?)element.Attribute("Text") == "Station readiness");
        var preferences = Assert.Single(headings, element => (string?)element.Attribute("Text") == "Operator preferences");
        Assert.True(elements.IndexOf(readiness) < elements.IndexOf(preferences));
        Assert.True(elements.IndexOf(preferences) < elements.IndexOf(disclosures[0]));
    }

    [Fact]
    public void ControlStatusLiveSetting_is_polite_when_idle()
    {
        var vm = CreateMain();
        Assert.Equal("Idle", vm.ControlStatus);
        Assert.Equal(AutomationLiveSetting.Polite, vm.ControlStatusLiveSetting);
    }

    [Fact]
    public void ControlStatusLiveSetting_is_assertive_while_stopping()
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
        Assert.False(vm.CanPauseResume);
        Assert.True(vm.CanSafetyStop);
    }

    [Fact]
    public void ControlStatusLiveSetting_is_off_while_awaiting_without_a_run()
    {
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(openTap, runControl);

        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly("Install fixture"));

        Assert.False(vm.IsRunning);
        Assert.Equal(AutomationLiveSetting.Off, vm.ControlStatusLiveSetting);
        Assert.True(vm.CanPauseResume);
        Assert.True(vm.CanSafetyStop);
        Assert.Contains("Install fixture", vm.ControlStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Awaiting operator", vm.CompactControlStatus);
    }

    [Fact]
    public void ControlStatusLiveSetting_is_off_while_awaiting_during_a_run()
    {
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(openTap, runControl);

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly("Install fixture"));

        Assert.True(vm.IsRunning);
        Assert.Equal(AutomationLiveSetting.Off, vm.ControlStatusLiveSetting);
        Assert.True(vm.CanPauseResume);
        Assert.True(vm.CanSafetyStop);
    }

    [Fact]
    public void CanPauseResume_is_false_when_awaiting_during_stop()
    {
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(openTap, runControl);

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly("Install fixture"));
        Assert.True(vm.CanPauseResume);

        runControl.RequestSafetyStop();
        Assert.True(vm.IsAwaitingOperator);
        Assert.True(vm.IsSafetyStopping);
        Assert.False(vm.CanPauseResume);
        Assert.Equal(AutomationLiveSetting.Assertive, vm.ControlStatusLiveSetting);
        Assert.Equal("Cancel shutdown", vm.SafetyStopLabel);
        Assert.Equal(StopRunCopy.CancelShutdownTip, vm.SafetyStopTip);
    }

    [Fact]
    public void ControlStatusLiveSetting_raises_before_status_text_when_stop_starts_from_a_prompt()
    {
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var vm = CreateMain(openTap, runControl);

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        openTap.BeginInteraction(OperatorInteractionRequest.ConfirmOnly("Install fixture"));

        var order = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.ControlStatusLiveSetting)
                or nameof(MainWindowViewModel.ControlStatus)
                or nameof(MainWindowViewModel.CompactControlStatus))
            {
                order.Add(e.PropertyName!);
            }
        };

        runControl.RequestSafetyStop();

        var live = order.IndexOf(nameof(MainWindowViewModel.ControlStatusLiveSetting));
        var status = order.IndexOf(nameof(MainWindowViewModel.ControlStatus));
        var compact = order.IndexOf(nameof(MainWindowViewModel.CompactControlStatus));
        Assert.True(live >= 0 && status >= 0 && live < status);
        Assert.True(compact >= 0 && live < compact);
        Assert.Equal(AutomationLiveSetting.Assertive, vm.ControlStatusLiveSetting);
        Assert.Equal("Stopping…", vm.ControlStatus);
        Assert.Equal("Stopping…", vm.CompactControlStatus);
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
