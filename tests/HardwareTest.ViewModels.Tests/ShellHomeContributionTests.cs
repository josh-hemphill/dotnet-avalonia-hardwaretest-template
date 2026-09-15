using HardwareTest.Features.Home;
using HardwareTest.Shell;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellHomeContributionTests
{
    [Fact]
    public async Task Operator_tile_is_visible_and_navigates()
    {
        var home = new HomeViewModel(
            settingsStore: null,
            guestTiles:
            [
                new ShellHomeTile
                {
                    Title = "Planning",
                    Body = "Open planning",
                    ActionLabel = "Open Planning →",
                    NavigatePageId = "vendor.planning",
                    Placement = ShellPagePlacement.Operator,
                },
            ]);
        string? received = null;
        home.NavigateToPageRequested += (_, pageId) => received = pageId;

        var tile = Assert.Single(home.GuestTiles);
        Assert.True(tile.IsVisible);
        await tile.NavigateCommand.ExecuteAsync();
        Assert.Equal("vendor.planning", received);
    }

    [Fact]
    public async Task Engineer_tile_follows_saved_engineer_mode()
    {
        var store = new FakeSettingsStore();
        var home = new HomeViewModel(
            store,
            guestTiles:
            [
                new ShellHomeTile
                {
                    Title = "Notes",
                    Body = "Engineer notes",
                    ActionLabel = "Open Notes →",
                    NavigatePageId = "hardwaretest.notes",
                    Placement = ShellPagePlacement.Engineer,
                },
            ]);

        var tile = Assert.Single(home.GuestTiles);
        Assert.False(tile.IsVisible);

        store.AppSettings.IsEngineerDebugMode = true;
        await store.SaveAppSettingsAsync();
        Assert.True(tile.IsVisible);
    }

    [Fact]
    public void ApplyEngineerPresentation_shows_engineer_tile_without_save()
    {
        var store = new FakeSettingsStore();
        var home = new HomeViewModel(
            store,
            guestTiles:
            [
                new ShellHomeTile
                {
                    Title = "Notes",
                    Body = "Engineer notes",
                    ActionLabel = "Open Notes →",
                    NavigatePageId = "hardwaretest.notes",
                    Placement = ShellPagePlacement.Engineer,
                },
            ]);

        Assert.False(Assert.Single(home.GuestTiles).IsVisible);
        home.ApplyEngineerPresentation(engineerMode: true);
        Assert.True(Assert.Single(home.GuestTiles).IsVisible);
    }

    [Fact]
    public void Contextual_tile_stays_hidden_in_engineer_mode()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.IsEngineerDebugMode = true;
        var home = new HomeViewModel(
            store,
            guestTiles:
            [
                new ShellHomeTile
                {
                    Title = "Preview",
                    Body = "Opened from a parent",
                    ActionLabel = "Open →",
                    NavigatePageId = "vendor.preview",
                    Placement = ShellPagePlacement.Contextual,
                },
            ]);

        Assert.False(Assert.Single(home.GuestTiles).IsVisible);
        home.ApplyEngineerPresentation(engineerMode: true);
        Assert.False(Assert.Single(home.GuestTiles).IsVisible);
    }
}
