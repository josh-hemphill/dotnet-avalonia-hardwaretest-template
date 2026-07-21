using HardwareTest.Core.Settings;
using HardwareTest.Features.Instruments;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class InstrumentsViewModelTests
{
    [Fact]
    public async Task RefreshSlots_shows_plan_slot_overrides()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        Assert.NotEmpty(vm.SlotOverrides);
        Assert.Contains(vm.SlotOverrides, s => s.SlotName == "DMM");
    }

    [Fact]
    public async Task Apply_and_save_override_persists_plan_slot()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        await vm.RefreshDiscoverCommand.ExecuteAsync();
        vm.SelectedSlot = vm.SlotOverrides[0];
        vm.SelectedDiscovered = vm.Discovered[0];
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.Equal(vm.Discovered[0].Resource, vm.SelectedSlot.OverrideResource);
        Assert.True(vm.SelectedSlot.IsOverridden);
        Assert.Equal("Overridden", vm.SelectedSlot.StatusText);
        await vm.SaveCommand.ExecuteAsync();
        Assert.Contains(store.AppSettings.PlanSlotOverrides, o =>
            o.SlotName == "DMM" && o.Resource == vm.Discovered[0].Resource);
    }

    [Fact]
    public async Task Discover_title_prefers_description_subtitle_is_resource()
    {
        var store = new FakeSettingsStore();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery(), new FakeOpenTapSession());
        await vm.RefreshDiscoverCommand.ExecuteAsync();
        Assert.True(vm.Discovered.Count >= 1);
        var item = vm.Discovered.First(d => !string.Equals(d.Description, d.Resource, StringComparison.Ordinal));
        Assert.Equal(item.Description, item.Title);
        Assert.Equal(item.Resource, item.Subtitle);
    }
}
