using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HardwareTest.Features.Shell;
using HardwareTest.ShellApps.Notes;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class ShellAppE2ETests
{
    [AvaloniaFact]
    public async Task Engineer_mode_shows_baked_notes_shell_app()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        Assert.Equal(4, main.NavigationItems.Count);
        Assert.DoesNotContain(main.NavigationItems, i => i.Id == NotesApplication.PageId);

        var store = E2EHarness.RequireApp().SettingsStore;
        store.AppSettings.IsEngineerDebugMode = true;
        await Dispatcher.UIThread.InvokeAsync(main.ApplyNavigationPolicy);

        Assert.Contains(main.NavigationItems, i => i.Id == NotesApplication.PageId);
        var guest = main.NavigationItems.ToList().FindIndex(i => i.Id == NotesApplication.PageId);
        var settings = main.NavigationItems.ToList().FindIndex(i => i.Id == ShellNavigationPolicy.Settings);
        Assert.InRange(guest, 0, settings - 1);
        main.NavigateToPageId(NotesApplication.PageId);
        Assert.IsType<NotesViewModel>(main.CurrentPage);
    }
}
