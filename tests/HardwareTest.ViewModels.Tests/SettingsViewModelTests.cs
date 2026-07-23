using HardwareTest.Features.Settings;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Save_maps_fields_to_store()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store, new FakeOpenTapSession())
        {
            UseMockVisa = false,
            LogMinimumLevel = "Warning",
            PlotRefreshHz = 12,
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.False(store.AppSettings.UseMockVisa);
        Assert.Equal("Warning", store.AppSettings.LogMinimumLevel);
        Assert.Equal(12, store.AppSettings.PlotRefreshHz);
        Assert.True(store.SaveAppCount >= 1);
        Assert.Contains("Saved", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.LogLevelOptions, l => l == "Information");
    }

    [Fact]
    public void Refresh_loads_packages_and_plugin_directories_from_session()
    {
        var openTap = new FakeOpenTapSession();
        openTap.InstalledPackages.Clear();
        openTap.InstalledPackages.Add(new() { Name = "DemoPkg", Version = "2.0.0", Path = "/tmp/DemoPkg" });
        openTap.PluginDirectories.Clear();
        openTap.PluginDirectories.Add(new() { Path = "/tmp/plugins", Source = "Settings" });

        var vm = new SettingsViewModel(new FakeSettingsStore(), openTap);

        Assert.Single(vm.Packages);
        Assert.Equal("DemoPkg", vm.Packages[0].Name);
        Assert.Single(vm.PluginDirectories);
        Assert.Equal("/tmp/plugins", vm.PluginDirectories[0].Path);
        Assert.Contains("Packages: 1", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Copy_path_uses_selected_package_and_clipboard_hook()
    {
        var openTap = new FakeOpenTapSession();
        var vm = new SettingsViewModel(new FakeSettingsStore(), openTap);
        string? copied = null;
        vm.CopyTextAsync = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };
        vm.SelectedPackage = vm.Packages[0];

        await vm.CopyPathCommand.ExecuteAsync();

        Assert.Equal(vm.Packages[0].Path, copied);
        Assert.Contains("Copied path", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}
