using HardwareTest.Features.Shell;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Compact 900×600 Run/Home chrome and engineer-mode Home tiles.
public sealed class OperatorShellLayoutTests
{
    [Fact]
    public void Compact_run_board_keeps_stage_rail_hidden()
    {
        var vm = RunTestViewModelTestFactory.Create();
        Assert.False(vm.IsRunning);
        vm.IsCompactLayout = true;
        Assert.True(ShellLayoutBreakpoints.CompactBoardWidth < 900);
        Assert.True(ShellLayoutBreakpoints.CompactBoardHeight < 600);
    }

    [Fact]
    public void Run_board_axaml_wraps_secondary_controls_and_binds_compact_stage_picker()
    {
        var header = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunHeaderView.axaml"));
        var steps = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunStepsWorkspaceView.axaml"));
        Assert.Contains("IsCompactLayout", steps, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"Stage\"", steps, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel Grid.Row=\"2\"", steps, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsEngineerDebugMode}\"", header, StringComparison.Ordinal);
        Assert.Contains("Header=\"Inspect\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_wraps_tiles_and_hides_instruments_until_engineer_mode()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/Home/HomeView.axaml"));
        Assert.Contains("WrapPanel", axaml, StringComparison.Ordinal);
        Assert.Contains("ShellLayoutBreakpoints.HomeTileMinWidth", axaml, StringComparison.Ordinal);
        Assert.Contains("IsEngineerMode", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UniformGrid", axaml, StringComparison.Ordinal);

        var home = new HardwareTest.Features.Home.HomeViewModel(new FakeSettingsStore());
        Assert.False(home.IsEngineerMode);
    }

    [Fact]
    public async Task Home_engineer_tile_follows_saved_settings()
    {
        var store = new FakeSettingsStore();
        var home = new HardwareTest.Features.Home.HomeViewModel(store);
        Assert.False(home.IsEngineerMode);
        store.AppSettings.IsEngineerDebugMode = true;
        await store.SaveAppSettingsAsync();
        Assert.True(home.IsEngineerMode);
    }

    [Fact]
    public void Report_preview_and_settings_document_contextual_engineer_mode()
    {
        var preview = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/ReportPreview/ReportPreviewView.axaml"));
        Assert.Contains("Back to Results", preview, StringComparison.Ordinal);
        var settings = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/Settings/SettingsView.axaml"));
        Assert.Contains("Presentation only", settings, StringComparison.Ordinal);
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
