using Avalonia.Headless.XUnit;
using HardwareTest.Features.Home;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class SmokeNavigationTests
{
    [AvaloniaFact]
    public void MainWindow_opens_with_nav_and_home()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);

        Assert.Equal(7, main.NavigationItems.Count);
        Assert.All(main.NavigationItems, i => Assert.True((int)i.Symbol >= 0));
        Assert.Contains(main.NavigationItems, i => i.Id == "Home");
        Assert.Contains(main.NavigationItems, i => i.Id == "RunTest");
        Assert.Contains(main.NavigationItems, i => i.Id == "Inspect");
        Assert.Contains(main.NavigationItems, i => i.Id == "Results");
        Assert.Contains(main.NavigationItems, i => i.Id == "ReportPreview");
        Assert.Contains(main.NavigationItems, i => i.Id == "Instruments");
        Assert.Contains(main.NavigationItems, i => i.Id == "Settings");
        Assert.IsType<HomeViewModel>(main.CurrentPage);

        main.NavigateToPageId("RunTest");
        Assert.Equal("RunTest", main.SelectedItem?.Id);
        var runVm = E2EHarness.RunTestVm(main);
        Assert.Same(runVm, main.CurrentPage);
        Assert.NotEmpty(runVm.ProgramSelection.Programs);
        Assert.True(runVm.SessionPanel.NeedsDutConfirm);
        Assert.NotNull(main.PauseCommand);
        Assert.NotNull(main.ResumeCommand);
        Assert.NotNull(main.SafetyStopCommand);
        Assert.Equal("Idle", main.ControlStatus);
    }
}
