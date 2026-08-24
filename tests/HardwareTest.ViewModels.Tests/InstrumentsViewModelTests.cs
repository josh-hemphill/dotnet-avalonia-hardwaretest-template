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
        IVisaSessionFactory? visaSessions = null,
        IBenchOperationCoordinator? bench = null,
        IStationIdnStore? idnStore = null)
        => new(
            store ?? new FakeSettingsStore(),
            new FakeVisaDiscovery(),
            openTap ?? new FakeOpenTapSession(),
            visaSessions ?? new MockVisaSessionFactory(new VisaSessionGate()),
            bench: bench,
            idnStore: idnStore);

    [Fact]
    public async Task RefreshSlots_shows_plan_slot_overrides()
    {
        var vm = CreateVm();
        await vm.RefreshSlotsCommand.ExecuteAsync();
        Assert.True(vm.SlotOverrides.Count > 0, vm.Status);
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

    [Fact]
    public async Task Query_IDN_refused_when_bench_holds_a_run()
    {
        var bench = new BenchOperationCoordinator();
        Assert.True(bench.TryEnter(BenchOperation.Run, out var lease, out _));
        using (lease)
        {
            var vm = CreateVm(bench: bench);
            await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
            vm.SelectedVisa = vm.DiscoveredVisa[0];
            await vm.QuerySelectedIdnCommand.ExecuteAsync();
            Assert.Contains("run", vm.Status, StringComparison.OrdinalIgnoreCase);
            Assert.False(vm.SelectedVisa.HasIdn);
        }
    }

    [Fact]
    public async Task Query_IDN_succeeds_when_coordinator_is_idle()
    {
        var bench = new BenchOperationCoordinator();
        var vm = CreateVm(bench: bench);
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        vm.SelectedVisa = vm.DiscoveredVisa[0];
        await vm.QuerySelectedIdnCommand.ExecuteAsync();
        Assert.True(vm.SelectedVisa.HasIdn);
        Assert.Null(bench.Current);
    }

    [Fact]
    public async Task Query_IDN_releases_bench_when_ui_dispatch_fails()
    {
        var bench = new BenchOperationCoordinator();
        var vm = CreateVm(bench: bench);
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        vm.SelectedVisa = vm.DiscoveredVisa[0];
        var calls = 0;
        vm.UiScheduler = action =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("dispatcher failed");
            }

            action();
        };

        await vm.QuerySelectedIdnCommand.ExecuteAsync();

        Assert.Null(bench.Current);
        Assert.Contains("dispatcher failed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(bench.TryEnter(BenchOperation.ModeSwap, out var lease, out _));
        lease!.Dispose();
    }

    [Fact]
    public async Task Plan_filter_limits_visible_slots_and_focus_program_selects_slot()
    {
        var vm = CreateVm();
        await vm.RefreshSlotsCommand.ExecuteAsync();
        Assert.True(vm.SlotOverrides.Count > 0, vm.Status);
        Assert.Contains(vm.PlanFilterOptions, p => p == InstrumentsViewModel.AllPlanFilter);

        var sampleName = vm.SlotOverrides.First(s =>
            string.Equals(s.PlanId, "sample", StringComparison.OrdinalIgnoreCase)).PlanDisplayName;
        vm.FocusProgram("sample", "DMM");

        Assert.Equal(sampleName, vm.PlanFilter);
        Assert.True(vm.VisibleSlots.All(s =>
            string.Equals(s.PlanId, "sample", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("DMM", vm.SelectedSlot?.SlotName);
        Assert.True(vm.ShowCommissionStrip);
        Assert.False(string.IsNullOrWhiteSpace(vm.ReadinessSummary));
    }

    [Fact]
    public async Task Query_IDN_on_bound_slot_persists_sidecar_without_settings_schema()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hwtest-idn-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var idn = new FileStationIdnStore(dir);
            var vm = CreateVm(idnStore: idn);
            await vm.RefreshSlotsCommand.ExecuteAsync();
            vm.SelectedSlot = vm.SlotOverrides.First(s =>
                s.SlotName == "DMM"
                && string.Equals(s.PlanId, "sample", StringComparison.OrdinalIgnoreCase));
            vm.SelectedVisa = null;
            vm.SelectedOpenTap = null;
            await vm.QuerySelectedIdnCommand.ExecuteAsync();

            Assert.True(vm.SelectedSlot.HasLastIdn, vm.Status);
            var stored = idn.Find(vm.SelectedSlot.PlanId, vm.SelectedSlot.SlotName);
            Assert.NotNull(stored);
            Assert.Contains("MOCK", stored.IdnSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task Query_IDN_persists_onto_slot_matching_queried_resource()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hwtest-idn-slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var idn = new FileStationIdnStore(dir);
            var vm = CreateVm(idnStore: idn);
            await vm.RefreshSlotsCommand.ExecuteAsync();
            await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
            var slots = vm.SlotOverrides.ToList();
            Assert.True(slots.Count >= 2, $"Need two slots, got {slots.Count}. {vm.Status}");
            var selected = slots[0];
            var bound = slots[1];
            var visa = vm.DiscoveredVisa[0];
            selected.OverrideResource = "MOCK::not-queried";
            bound.OverrideResource = visa.Resource;
            vm.SelectedSlot = selected;
            vm.SelectedVisa = visa;
            await vm.QuerySelectedIdnCommand.ExecuteAsync();

            Assert.False(selected.HasLastIdn);
            Assert.True(bound.HasLastIdn, vm.Status);
            Assert.Null(idn.Find(selected.PlanId, selected.SlotName));
            Assert.NotNull(idn.Find(bound.PlanId, bound.SlotName));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
