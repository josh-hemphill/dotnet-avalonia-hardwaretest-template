using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Instruments;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class InstrumentsViewModelTests
{
    private static InstrumentsViewModel CreateVm(
        FakeSettingsStore? store = null,
        FakeOpenTapSession? openTap = null,
        IVisaSessionFactory? visaSessions = null)
        => new(
            store ?? new FakeSettingsStore(),
            new FakeVisaDiscovery(),
            openTap ?? new FakeOpenTapSession(),
            visaSessions ?? new MockVisaSessionFactory(new VisaSessionGate()));

    [Fact]
    public async Task RefreshSlots_shows_plan_slot_overrides()
    {
        var vm = CreateVm();
        await vm.RefreshSlotsCommand.ExecuteAsync();
        Assert.NotEmpty(vm.SlotOverrides);
        Assert.Contains(vm.SlotOverrides, s => s.SlotName == "DMM");
    }

    [Fact]
    public async Task Apply_and_save_override_persists_plan_slot()
    {
        var store = new FakeSettingsStore();
        var vm = CreateVm(store);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        vm.SelectedSlot = vm.SlotOverrides[0];
        vm.SelectedVisa = vm.DiscoveredVisa[0];
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.Equal(vm.DiscoveredVisa[0].Resource, vm.SelectedSlot.OverrideResource);
        Assert.True(vm.SelectedSlot.IsOverridden);
        Assert.Equal("Overridden", vm.SelectedSlot.StatusText);
        await vm.SaveCommand.ExecuteAsync();
        Assert.Contains(store.AppSettings.PlanSlotOverrides, o =>
            o.SlotName == "DMM" && o.Resource == vm.DiscoveredVisa[0].Resource);
    }

    [Fact]
    public async Task Discover_title_prefers_description_subtitle_shows_interface()
    {
        var vm = CreateVm();
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        Assert.True(vm.DiscoveredVisa.Count >= 1);
        var item = vm.DiscoveredVisa.First(d => !string.Equals(d.Description, d.Resource, StringComparison.Ordinal));
        Assert.Equal(item.Description, item.Title);
        Assert.Contains("MOCK", item.Subtitle, StringComparison.OrdinalIgnoreCase);
        Assert.True(item.SupportsMessageQuery);
    }

    [Fact]
    public async Task Apply_from_OpenTAP_selection_sets_override()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap: openTap);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        await vm.RefreshOpenTapDiscoverCommand.ExecuteAsync();
        Assert.NotEmpty(vm.DiscoveredOpenTap);
        vm.SelectedSlot = vm.SlotOverrides[0];
        vm.SelectedOpenTap = vm.DiscoveredOpenTap[0];
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.Equal(vm.DiscoveredOpenTap[0].Address, vm.SelectedSlot.OverrideResource);
        Assert.Contains("OpenTAP", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Selecting_VISA_clears_OpenTAP_selection()
    {
        var vm = CreateVm();
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        await vm.RefreshOpenTapDiscoverCommand.ExecuteAsync();
        vm.SelectedOpenTap = vm.DiscoveredOpenTap[0];
        Assert.NotNull(vm.SelectedOpenTap);
        vm.SelectedVisa = vm.DiscoveredVisa[0];
        Assert.NotNull(vm.SelectedVisa);
        Assert.Null(vm.SelectedOpenTap);
    }

    [Fact]
    public async Task Query_IDN_fills_summary_for_selected_VISA()
    {
        var vm = CreateVm();
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        vm.SelectedVisa = vm.DiscoveredVisa[0];
        await vm.QuerySelectedIdnCommand.ExecuteAsync();
        Assert.True(vm.SelectedVisa.HasIdn);
        Assert.Contains("MOCK", vm.SelectedVisa.IdnSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IDN", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_IDN_works_for_OpenTAP_selection()
    {
        var vm = CreateVm();
        await vm.RefreshOpenTapDiscoverCommand.ExecuteAsync();
        vm.SelectedOpenTap = vm.DiscoveredOpenTap[0];
        await vm.QuerySelectedIdnCommand.ExecuteAsync();
        Assert.True(vm.SelectedOpenTap.HasIdn);
        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedOpenTap.IdnSummary));
    }
}
