using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HardwareTest.Features.Home;
using HardwareTest.Features.Settings;
using HardwareTest.Features.Shell;
using HardwareTest.ShellApps.Notes;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class ShellAppE2ETests
{
    [AvaloniaFact]
    public void Operator_home_hides_engineer_notes_tile_and_settings_lists_baked_app()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        var tile = Assert.Single(home.GuestTiles, t => t.Title == "Station notes");
        Assert.False(tile.IsVisible);

        main.NavigateToPageId(ShellNavigationPolicy.Settings);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
        Assert.True(settings.HasShellApps);
        Assert.Contains("Station notes 1.0.0", settings.ShellAppSummaries);
        main.NavigateToPageId(ShellNavigationPolicy.Home);
    }

    [AvaloniaFact]
    public async Task Engineer_mode_shows_baked_notes_shell_app()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        Assert.Equal(4, main.NavigationItems.Count);
        Assert.DoesNotContain(main.NavigationItems, i => i.Id == NotesApplication.PageId);

        var store = E2EHarness.RequireApp().SettingsStore;
        var previousEngineer = store.AppSettings.IsEngineerDebugMode;
        try
        {
            store.AppSettings.IsEngineerDebugMode = true;
            await Dispatcher.UIThread.InvokeAsync(main.ApplyNavigationPolicy);

            Assert.Contains(main.NavigationItems, i => i.Id == NotesApplication.PageId);
            var guest = main.NavigationItems.ToList().FindIndex(i => i.Id == NotesApplication.PageId);
            var settings = main.NavigationItems.ToList().FindIndex(i => i.Id == ShellNavigationPolicy.Settings);
            Assert.InRange(guest, 0, settings - 1);

            main.NavigateToPageId(ShellNavigationPolicy.Home);
            var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
            var tile = Assert.Single(home.GuestTiles, t => t.Title == "Station notes");
            Assert.True(tile.IsVisible);
            await tile.NavigateCommand.ExecuteAsync();
            Assert.IsType<NotesViewModel>(main.CurrentPage);
        }
        finally
        {
            store.AppSettings.IsEngineerDebugMode = previousEngineer;
            await Dispatcher.UIThread.InvokeAsync(main.ApplyNavigationPolicy);
            await Dispatcher.UIThread.InvokeAsync(() => main.NavigateToPageId(ShellNavigationPolicy.Home));
            Assert.Equal(4, main.NavigationItems.Count);
            Assert.DoesNotContain(main.NavigationItems, i => i.Id == NotesApplication.PageId);
        }
    }
}
