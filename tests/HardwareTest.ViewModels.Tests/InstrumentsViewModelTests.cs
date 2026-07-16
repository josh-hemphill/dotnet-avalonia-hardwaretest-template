using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Instruments;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class InstrumentsViewModelTests
{
    [Fact]
    public void RefreshSlots_shows_opentap_instruments()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap);
        Assert.NotEmpty(vm.OpenTapSlots);
        Assert.Contains(vm.OpenTapSlots, s => s.Name == "DMM");
    }

    [Fact]
    public async Task BindSlot_updates_resource_and_role()
    {
        var store = new FakeSettingsStore();
        var openTap = new FakeOpenTapSession();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap);
        vm.Instruments.Add(new VisaInstrument
        {
            Id = "inst1",
            DisplayName = "Mock",
            Resource = "MOCK::INSTR9",
            Enabled = true,
        });
        vm.SelectedInstrument = vm.Instruments[^1];
        vm.SelectedOpenTapSlot = vm.OpenTapSlots[0];
        await vm.BindSlotFromSelectedCommand.ExecuteAsync();
        Assert.Equal("MOCK::INSTR9", openTap.Slots[0].ResourceName);
        Assert.Contains(vm.StationBindings, b => b.Role.Equals("dmm", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task Discover_adds_mock_resources()
    {
        var store = new FakeSettingsStore();
        var vm = new InstrumentsViewModel(store, new FakeVisaDiscovery(), new FakeOpenTapSession());
        await vm.RefreshDiscoverCommand.ExecuteAsync();
        Assert.True(vm.Discovered.Count >= 1);
        vm.SelectedDiscovered = vm.Discovered[0];
        await vm.AddSelectedCommand.ExecuteAsync();
        Assert.Contains(vm.Instruments, i => i.Resource == vm.Discovered[0].Resource);
        await vm.SaveCommand.ExecuteAsync();
        Assert.True(store.SaveAppCount >= 1);
    }
}
