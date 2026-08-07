using HardwareTest.Features.Shell;
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
        Assert.Equal(16, OperatorTouchDensity.DetailsSplitterMinHeight);
    }

    [Fact]
    public void App_axaml_binds_operator_MinHeight_to_density_constants()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/App/App.axaml"));
        Assert.Contains("OperatorTouchDensity.OperatorControlMinHeight", axaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton.filter-chip", axaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.operator-list ListBoxItem", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight\" Value=\"28\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_compact_nav_binds_density_constants()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/App/MainWindow.axaml"));
        Assert.Contains("OperatorTouchDensity.CompactNavTargetSize", axaml, StringComparison.Ordinal);
        Assert.Contains("OperatorTouchDensity.OperatorControlMinHeight", axaml, StringComparison.Ordinal);
        Assert.Contains("ShellNotificationBrushConverter", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RunTestView_has_splitter_floor_and_single_blocked_tip()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunTestView.axaml"));
        Assert.Contains("OperatorTouchDensity.DetailsSplitterMinHeight", axaml, StringComparison.Ordinal);
        Assert.Contains("Open detail", axaml, StringComparison.Ordinal);
        Assert.Contains("ShowStartBlockedTip", axaml, StringComparison.Ordinal);
        Assert.Contains("Details +", axaml, StringComparison.Ordinal);
        Assert.Contains("Reset split", axaml, StringComparison.Ordinal);
        Assert.Contains("RunBoardStageRailView", axaml, StringComparison.Ordinal);
        // One inline tip only (program toolbar) — not duplicated near Run Selected.
        Assert.Equal(1, CountOccurrences(axaml, "ShowStartBlockedTip"));
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
        var vm = RunTestViewModelTestFactory.Create();
        Assert.False(vm.CanStartRun);
        Assert.True(vm.ShowStartBlockedTip);
        Assert.Contains("Confirm DUT", vm.CanStartRunTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowStartBlockedTip_false_after_session_confirm()
    {
        var vm = RunTestViewModelTestFactory.Create();
        vm.SessionPanel.DutSerialInput = "SN-TOUCH";
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();

        Assert.True(vm.CanStartRun);
        Assert.False(vm.ShowStartBlockedTip);
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
