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
        var vm = new SettingsViewModel(store)
        {
            DefaultVisaResource = "MOCK::X",
            UseMockVisa = false,
            LogMinimumLevel = "Warning",
            PlotRefreshHz = 12,
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal("MOCK::X", store.AppSettings.DefaultVisaResource);
        Assert.False(store.AppSettings.UseMockVisa);
        Assert.Equal("Warning", store.AppSettings.LogMinimumLevel);
        Assert.Equal(12, store.AppSettings.PlotRefreshHz);
        Assert.True(store.SaveAppCount >= 1);
        Assert.Contains("Saved", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}
