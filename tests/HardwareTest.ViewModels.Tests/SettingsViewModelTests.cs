using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Settings;
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

    [Fact]
    public async Task Save_skips_env_overridden_fields()
    {
        var store = new FakeSettingsStore
        {
            Provenance =
            [
                new SettingProvenance
                {
                    Key = "UseMockVisa",
                    EffectiveValue = "false",
                    Source = SettingSource.Environment,
                    SourceDetail = "HARDWARETEST_USE_MOCK_VISA",
                },
            ],
        };
        store.AppSettings.UseMockVisa = false;
        var vm = new SettingsViewModel(store, new FakeOpenTapSession())
        {
            UseMockVisa = true,
            ThemePreference = "Dark",
        };

        Assert.True(vm.UseMockVisaReadOnly);
        await vm.SaveCommand.ExecuteAsync();
        Assert.False(store.AppSettings.UseMockVisa);
        Assert.Equal("Dark", store.AppSettings.ThemePreference);
    }

    [Fact]
    public void Diagnostics_lists_provenance_rows()
    {
        var store = new FakeSettingsStore
        {
            Provenance =
            [
                new SettingProvenance
                {
                    Key = "LogMinimumLevel",
                    EffectiveValue = "Debug",
                    Source = SettingSource.CommandLine,
                    SourceDetail = "--log-level",
                },
            ],
        };
        var vm = new SettingsViewModel(store, new FakeOpenTapSession());
        Assert.Contains(vm.ProvenanceRows, r => r.Key == "LogMinimumLevel" && r.Source == "CommandLine");
    }

    [Fact]
    public async Task Copy_diagnostics_includes_build_support_block()
    {
        var buildInfo = BuildInfo.FromAssembly(typeof(SettingsViewModel).Assembly)
            .WithOpenTapEngineVersion("1.2.3");
        var store = new FakeSettingsStore
        {
            Provenance =
            [
                new SettingProvenance
                {
                    Key = "ThemePreference",
                    EffectiveValue = "Dark",
                    Source = SettingSource.SettingsFile,
                    SourceDetail = "settings.json",
                },
            ],
        };
        var vm = new SettingsViewModel(store, new FakeOpenTapSession(), buildInfo);
        string? copied = null;
        vm.CopyTextAsync = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await vm.CopyDiagnosticsCommand.ExecuteAsync();

        Assert.NotNull(copied);
        Assert.Contains("HardwareTest diagnostics", copied, StringComparison.Ordinal);
        Assert.Contains(buildInfo.InformationalVersion, copied, StringComparison.Ordinal);
        Assert.Contains("OpenTAP: 1.2.3", copied, StringComparison.Ordinal);
        Assert.Contains("ThemePreference", copied, StringComparison.Ordinal);
        Assert.Contains("Copied diagnostics", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(vm.AboutVersion));
        Assert.Equal(buildInfo.Version, vm.AboutVersion);
        Assert.Equal("1.2.3", vm.AboutOpenTapEngine);
    }
}
