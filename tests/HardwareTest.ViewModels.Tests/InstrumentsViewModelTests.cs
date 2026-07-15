using HardwareTest.Core.Hardware;
using HardwareTest.Features.Instruments;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class InstrumentsViewModelTests
{
    [Fact]
    public async Task Discover_title_prefers_description_subtitle_is_resource()
    {
        var store = new FakeSettingsStore();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery());
        await vm.RefreshDiscoverCommand.ExecuteAsync();
        Assert.True(vm.Discovered.Count >= 1);
        var item = vm.Discovered.First(d => !string.Equals(d.Description, d.Resource, StringComparison.Ordinal));
        Assert.Equal(item.Description, item.Title);
        Assert.Equal(item.Resource, item.Subtitle);
    }

    [Fact]
    public async Task Discover_adds_mock_resources()
    {
        var store = new FakeSettingsStore();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery());
        await vm.RefreshDiscoverCommand.ExecuteAsync();
        Assert.True(vm.Discovered.Count >= 1);
        vm.SelectedDiscovered = vm.Discovered[0];
        await vm.AddSelectedCommand.ExecuteAsync();
        Assert.Contains(vm.Instruments, i => i.Resource == vm.Discovered[0].Resource);
        await vm.SaveCommand.ExecuteAsync();
        Assert.True(store.SaveAppCount >= 1);
    }
}
