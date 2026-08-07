using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Phase 18 — Operator touch density floor: tips without hover + documented constants.
public sealed class Phase18TouchDensityTests
{
    [Fact]
    public void OperatorTouchDensity_floor_constants_match_phase_plan()
    {
        Assert.True(OperatorTouchDensity.OperatorControlMinHeight >= 40);
        Assert.Equal(48, OperatorTouchDensity.CompactNavTargetSize);
        Assert.True(OperatorTouchDensity.DetailsSplitterMinHeight >= 12);
    }

    [Fact]
    public void App_axaml_ships_operator_MinHeight_floor()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/App/App.axaml"));
        Assert.Contains("MinHeight\" Value=\"40\"", axaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton.filter-chip", axaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.operator-list ListBoxItem", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight\" Value=\"28\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_compact_nav_targets_are_48()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/App/MainWindow.axaml"));
        Assert.Contains("Height=\"48\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"48\"", axaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"40\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RunTestView_has_splitter_floor_and_open_detail_control()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunTestView.axaml"));
        Assert.Contains("Height=\"16\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Open detail", axaml, StringComparison.Ordinal);
        Assert.Contains("ShowStartBlockedTip", axaml, StringComparison.Ordinal);
        Assert.Contains("Details +", axaml, StringComparison.Ordinal);
        Assert.Contains("Reset split", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsView_has_explicit_Open_report_button()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/Results/ResultsView.axaml"));
        Assert.Contains("Open report", axaml, StringComparison.Ordinal);
        Assert.Contains("OpenDefaultReportCommand", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowStartBlockedTip_true_when_session_blocked()
    {
        var openTap = new FakeOpenTapSession();
        var vm = new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            new FakeRunControl(),
            new FakeReportService(),
            new FakeRunStore(),
            new AppSettings());

        Assert.False(vm.CanStartRun);
        Assert.True(vm.ShowStartBlockedTip);
        Assert.Contains("Confirm DUT", vm.CanStartRunTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowStartBlockedTip_false_after_session_confirm()
    {
        var openTap = new FakeOpenTapSession();
        var vm = new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            new FakeRunControl(),
            new FakeReportService(),
            new FakeRunStore(),
            new AppSettings());

        vm.SessionPanel.DutSerialInput = "SN-TOUCH";
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();

        Assert.True(vm.CanStartRun);
        Assert.False(vm.ShowStartBlockedTip);
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
