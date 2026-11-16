using System.Collections;
using System.Text.Json;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Instruments;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

[CollectionDefinition("OpenTAP catalog fixtures", DisableParallelization = true)]
public sealed class OpenTapCatalogFixtureCollection;

// These cases serialize real TapPlans into the process catalog. OpenTAP's plugin
// metadata cache is global, so isolate this collection from other VM collections.
[Collection("OpenTAP catalog fixtures")]
public sealed class InstrumentsPersonaWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_program_names_keep_deep_link_filter_selection_and_apply_on_the_exact_program(bool specifySlot)
    {
        using var fixture = new StationFixture(createPlans: true, duplicateNames: true);
        var settings = new FakeSettingsStore();
        var vm = CreateVm(settings);
        var first = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == fixture.FirstPlanId && slot.SlotName == "DMM");
        var second = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == fixture.SecondPlanId && slot.SlotName == "DMM");
        Assert.Equal(first.PlanDisplayName, second.PlanDisplayName);
        Assert.Contains(vm.PlanFilterOptions, label => label.Contains(first.PlanId, StringComparison.Ordinal));
        Assert.Contains(vm.PlanFilterOptions, label => label.Contains(second.PlanId, StringComparison.Ordinal));
        vm.SelectedSlot = first;

        vm.FocusProgram(second.PlanId, specifySlot ? "DMM" : null);

        Assert.Same(second, vm.SelectedSlot);
        Assert.Contains(second.PlanId, vm.PlanFilter, StringComparison.Ordinal);
        Assert.All(vm.VisibleSlots, slot => Assert.Equal(second.PlanId, slot.PlanId));
        vm.SelectedVisa = new DiscoveredResourceItem
        {
            Resource = "MOCK::SCOPE1",
            Description = "Exact program test instrument",
            SupportsMessageQuery = true,
        };
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.Equal("MOCK::SCOPE1", second.OverrideResource);
        Assert.Empty(first.OverrideResource);
        Assert.Empty(settings.AppSettings.PlanSlotOverrides);
        fixture.AssertSourcesUnchanged();
    }

    [Fact]
    public async Task Missing_deep_link_program_or_slot_clears_the_binding_target_without_falling_back()
    {
        using var fixture = new StationFixture(createPlans: true, duplicateNames: true);
        var vm = CreateVm(new FakeSettingsStore());
        vm.FocusProgram(fixture.FirstPlanId, "DMM");
        Assert.Equal(fixture.FirstPlanId, vm.SelectedSlot?.PlanId);
        vm.SelectedVisa = new DiscoveredResourceItem
        {
            Resource = "MOCK::SCOPE1",
            Description = "Different instrument",
            SupportsMessageQuery = true,
        };

        vm.FocusProgram(fixture.SecondPlanId, "missing-slot");

        Assert.Null(vm.SelectedSlot);
        Assert.Contains("missing-slot", vm.Status, StringComparison.Ordinal);
        Assert.Contains(fixture.SecondPlanId, vm.Status, StringComparison.Ordinal);
        Assert.Contains("no binding target", vm.Status, StringComparison.Ordinal);
        Assert.All(vm.VisibleSlots, slot => Assert.Equal(fixture.SecondPlanId, slot.PlanId));
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.All(vm.SlotOverrides, slot => Assert.Empty(slot.OverrideResource));

        vm.FocusProgram("missing-program", "DMM");

        Assert.Null(vm.SelectedSlot);
        Assert.Contains("Program missing-program was not found", vm.Status, StringComparison.Ordinal);
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.All(vm.SlotOverrides, slot => Assert.Empty(slot.OverrideResource));
        fixture.AssertSourcesUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployment_owned_bindings_are_read_only_and_never_report_station_edits_saved(bool commandLine)
    {
        using var fixture = new StationFixture();
        var store = new SettingsStore(fixture.Root);
        await store.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        store.AppSettings.UseMockVisa = true;
        store.AppSettings.PlanSlotOverrides =
        [
            new() { PlanId = "sample", SlotName = "DMM", RoleHint = "dmm", Resource = "MOCK::file-binding" },
        ];
        await store.SaveAppSettingsAsync();
        var savedBytes = File.ReadAllBytes(store.SettingsPath);
        // Use the real indexed environment-name mapping, without ambient process state.
        var overlay = AppSettingsEnvironmentBinder.ReadEnvironment(new Hashtable
        {
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__PLAN_ID"] = "sample",
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__SLOT_NAME"] = "DMM",
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__ROLE_HINT"] = "dmm",
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__RESOURCE"] = "MOCK::deployment-binding",
        });
        await store.LoadAsync(commandLine ? new Dictionary<string, string>() : overlay,
            commandLine ? overlay : new Dictionary<string, string>());
        Assert.True(store.IsOverridden(nameof(AppSettings.PlanSlotOverrides)));
        var vm = CreateVm(store);
        var slot = Assert.Single(vm.SlotOverrides, item => item.PlanId == "sample" && item.SlotName == "DMM");
        vm.SelectedSlot = slot;
        vm.SelectedVisa = new DiscoveredResourceItem
        {
            Resource = "MOCK::attempted-station-change",
            Description = "Different instrument",
            SupportsMessageQuery = true,
        };
        Assert.True(vm.BindingsReadOnly);
        Assert.False(vm.CanEditBindings);
        Assert.Equal("MOCK::deployment-binding", slot.OverrideResource);
        Assert.Contains("Deployment", vm.BindingOwnershipHint, StringComparison.Ordinal);

        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        Assert.Equal("MOCK::deployment-binding", slot.OverrideResource);
        Assert.Equal(vm.BindingOwnershipHint, vm.Status);
        await vm.ClearOverrideCommand.ExecuteAsync();
        Assert.Equal("MOCK::deployment-binding", slot.OverrideResource);
        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(vm.BindingOwnershipHint, vm.Status);
        Assert.DoesNotContain("Saved", vm.Status, StringComparison.Ordinal);
        Assert.Equal("MOCK::deployment-binding", Assert.Single(store.AppSettings.PlanSlotOverrides).Resource);
        Assert.Equal(savedBytes, File.ReadAllBytes(store.SettingsPath));
        Assert.False(vm.IsBusy);
        var reloaded = new SettingsStore(fixture.Root);
        await reloaded.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.Equal("MOCK::file-binding", Assert.Single(reloaded.AppSettings.PlanSlotOverrides).Resource);
        Assert.Contains("Deployment", vm.CommissionTitle, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", vm.CommissionHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_message_query_never_opens_visa_or_records_identity_and_preserves_inputs()
    {
        using var fixture = new StationFixture();
        var sessions = new CountingVisaFactory();
        var bench = new BenchOperationCoordinator();
        var vm = CreateVm(new FakeSettingsStore(), sessions, fixture.Identities, bench);
        var slot = Slot("manual-program", "DMM");
        slot.OverrideResource = "PXI0::1::INSTR";
        SetSlots(vm, slot);
        vm.SelectedSlot = slot;
        var resource = new DiscoveredResourceItem
        {
            Resource = "PXI0::1::INSTR",
            Description = "Register-based PXI instrument",
            SupportsMessageQuery = false,
        };
        vm.SelectedVisa = resource;

        await vm.QuerySelectedIdnCommand.ExecuteAsync();

        Assert.Equal(0, sessions.OpenCount);
        Assert.Empty(fixture.Identities.List());
        Assert.False(slot.HasLastIdn);
        Assert.Equal("PXI0::1::INSTR", slot.OverrideResource);
        Assert.Same(slot, vm.SelectedSlot);
        Assert.Same(resource, vm.SelectedVisa);
        Assert.Contains("Skipping *IDN?", vm.Status, StringComparison.Ordinal);
        Assert.Contains("not typically message-based", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);
        Assert.Null(bench.Current);
    }

    [Fact]
    public async Task Manual_binding_changes_immediately_update_readiness_next_action_and_identity()
    {
        using var fixture = new StationFixture();
        var vm = CreateVm(new FakeSettingsStore(), idn: fixture.Identities);
        var slot = new SlotOverrideItemViewModel("manual-program", "Manual program", new OpenTapInstrumentSlot
        {
            Name = "DMM",
            TypeName = "MockInstrument",
            RoleHint = "dmm",
            ResourceName = string.Empty,
        }, null, useMockVisa: true);
        SetSlots(vm, slot);
        vm.PlanFilter = "Manual program";
        vm.SelectedSlot = slot;
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        Assert.Equal(1, vm.CommissionStepIndex);
        Assert.Contains("Bind unbound", vm.ReadinessSummary, StringComparison.Ordinal);

        slot.OverrideResource = "MOCK::INSTR0";

        Assert.Equal("Station ready (1 slot(s)).", vm.ReadinessSummary);
        Assert.Equal(2, vm.CommissionStepIndex);
        Assert.Contains("Query *IDN?", vm.CommissionHint, StringComparison.Ordinal);
        await vm.QuerySelectedIdnCommand.ExecuteAsync();
        Assert.True(slot.HasLastIdn, vm.Status);
        Assert.Equal(3, vm.CommissionStepIndex);
        slot.OverrideResource = "MOCK::replacement";
        Assert.False(slot.HasLastIdn);
        Assert.Equal(2, vm.CommissionStepIndex);
        Assert.Contains("Query *IDN?", vm.CommissionHint, StringComparison.Ordinal);
        slot.OverrideResource = string.Empty;
        Assert.Equal(1, vm.CommissionStepIndex);
        Assert.Contains("Bind unbound", vm.ReadinessSummary, StringComparison.Ordinal);
        Assert.Contains("Apply selected resource", vm.CommissionHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Querying_a_shared_resource_verifies_only_the_selected_program_and_slot()
    {
        using var fixture = new StationFixture();
        var vm = CreateVm(new FakeSettingsStore(), idn: fixture.Identities);
        var first = Slot("first-program", "DMM");
        var second = Slot("second-program", "DMM");
        var otherSlot = Slot("second-program", "Scope");
        SetSlots(vm, first, second, otherSlot);
        vm.SelectedSlot = second;

        await vm.QuerySelectedIdnCommand.ExecuteAsync();

        Assert.True(second.HasLastIdn, vm.Status);
        Assert.False(first.HasLastIdn);
        Assert.False(otherSlot.HasLastIdn);
        var identity = Assert.Single(fixture.Identities.List());
        Assert.Equal("second-program", identity.PlanId);
        Assert.Equal("DMM", identity.SlotName);
        Assert.Equal(second.EffectiveResource, identity.Resource);
        Assert.Contains("Recorded for second-program / DMM", vm.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_async_query_cannot_be_redirected_by_selection_or_rebinding(bool rebind)
    {
        using var fixture = new StationFixture();
        var query = new ControlledQuery();
        var bench = new BenchOperationCoordinator();
        var vm = CreateVm(new FakeSettingsStore(), query, fixture.Identities, bench);
        var first = Slot("first-program", "DMM");
        var second = Slot("second-program", "DMM");
        SetSlots(vm, first, second);
        vm.SelectedSlot = second;
        var pending = vm.QuerySelectedIdnCommand.ExecuteAsync();
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BenchOperation.IdQuery, bench.Current);
        vm.SelectedSlot = first;
        if (rebind) second.OverrideResource = "MOCK::replacement";
        query.Completion.SetResult("MOCK,SelectedDevice,Serial-42,1.0");

        await pending;

        Assert.Same(first, vm.SelectedSlot);
        Assert.False(first.HasLastIdn);
        Assert.Null(fixture.Identities.Find(first.PlanId, first.SlotName));
        Assert.Null(bench.Current);
        Assert.False(vm.IsBusy);
        if (rebind)
        {
            Assert.False(second.HasLastIdn);
            Assert.Empty(fixture.Identities.List());
            Assert.Contains("Not recorded", vm.Status, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(second.HasLastIdn);
            Assert.Equal("Serial-42", VisaResourceParser.FormatIdn(Assert.Single(fixture.Identities.List()).IdnRaw).Item3);
            Assert.Equal(second.PlanId, Assert.Single(fixture.Identities.List()).PlanId);
        }
    }

    [Fact]
    public async Task Failed_query_preserves_binding_and_releases_the_bench_without_recording_identity()
    {
        using var fixture = new StationFixture();
        var query = new ControlledQuery();
        var bench = new BenchOperationCoordinator();
        var vm = CreateVm(new FakeSettingsStore(), query, fixture.Identities, bench);
        var slot = Slot("second-program", "DMM");
        SetSlots(vm, slot);
        vm.SelectedSlot = slot;
        var pending = vm.QuerySelectedIdnCommand.ExecuteAsync();
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        query.Completion.SetException(new IOException("Review instrument disconnected"));

        await pending;

        Assert.Empty(fixture.Identities.List());
        Assert.False(slot.HasLastIdn);
        Assert.Equal("MOCK::INSTR0", slot.EffectiveResource);
        Assert.Contains("failed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disconnected", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
        Assert.Null(bench.Current);
        Assert.True(bench.TryEnter(BenchOperation.Run, out var lease, out _));
        lease!.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_failure_retains_edits_restores_effective_settings_and_allows_retry(bool throws)
    {
        var store = new FailingSettingsStore { ThrowOnFailure = throws };
        store.AppSettings.PlanSlotOverrides =
        [
            new() { PlanId = "first-program", SlotName = "DMM", RoleHint = "dmm", Resource = "MOCK::prior" },
        ];
        var vm = CreateVm(store);
        var first = Slot("first-program", "DMM");
        first.OverrideResource = "MOCK::prior";
        var second = Slot("second-program", "DMM");
        second.OverrideResource = "MOCK::changed";
        SetSlots(vm, first, second);
        vm.SelectedSlot = second;

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal("first-program", Assert.Single(store.AppSettings.PlanSlotOverrides).PlanId);
        Assert.Equal("MOCK::prior", store.AppSettings.PlanSlotOverrides[0].Resource);
        Assert.Equal("MOCK::changed", second.OverrideResource);
        Assert.Same(second, vm.SelectedSlot);
        Assert.Contains("Could not save", vm.Status, StringComparison.Ordinal);
        Assert.Contains("retry Save", vm.Status, StringComparison.Ordinal);
        Assert.Contains("disk full", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);

        store.FailSave = false;
        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(2, store.AppSettings.PlanSlotOverrides.Count);
        Assert.Contains(store.AppSettings.PlanSlotOverrides, entry => entry.PlanId == second.PlanId && entry.Resource == "MOCK::changed");
        Assert.Contains("Saved 2", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Saved_binding_and_identity_reload_only_for_the_intended_program_without_rewriting_sources()
    {
        using var fixture = new StationFixture(createPlans: true);
        var store = new SettingsStore(fixture.Root);
        await store.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        store.AppSettings.UseMockVisa = true;
        store.AppSettings.PlanSlotOverrides =
        [
            new() { PlanId = "sample", SlotName = "DMM", RoleHint = "dmm", Resource = "MOCK::prior" },
        ];
        await store.SaveAppSettingsAsync();
        var vm = CreateVm(store, idn: fixture.Identities);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        var first = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == fixture.FirstPlanId && slot.SlotName == "DMM");
        var second = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == fixture.SecondPlanId && slot.SlotName == "DMM");
        vm.SelectedSlot = second;
        vm.SelectedVisa = new DiscoveredResourceItem { Resource = "MOCK::SCOPE1", Description = "Review scope", SupportsMessageQuery = true };
        await vm.ApplySelectedResourceCommand.ExecuteAsync();
        await vm.QuerySelectedIdnCommand.ExecuteAsync();
        Assert.True(second.HasLastIdn, vm.Status);

        await vm.SaveCommand.ExecuteAsync();

        Assert.Contains("Saved", vm.Status, StringComparison.Ordinal);
        var reloadedStore = new SettingsStore(fixture.Root);
        await reloadedStore.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        var saved = Assert.Single(reloadedStore.AppSettings.PlanSlotOverrides, entry => entry.PlanId == fixture.SecondPlanId);
        Assert.Equal("DMM", saved.SlotName);
        Assert.Equal("MOCK::SCOPE1", saved.Resource);
        Assert.DoesNotContain(reloadedStore.AppSettings.PlanSlotOverrides, entry => entry.PlanId == fixture.FirstPlanId);
        Assert.Contains(reloadedStore.AppSettings.PlanSlotOverrides, entry => entry.PlanId == "sample" && entry.Resource == "MOCK::prior");
        var reopened = CreateVm(reloadedStore, idn: new FileStationIdnStore(fixture.Root));
        await reopened.RefreshSlotsCommand.ExecuteAsync();
        var reopenedTarget = Assert.Single(reopened.SlotOverrides, slot => slot.PlanId == second.PlanId && slot.SlotName == "DMM");
        Assert.Equal("MOCK::SCOPE1", reopenedTarget.EffectiveResource);
        Assert.True(reopenedTarget.HasLastIdn);
        Assert.False(Assert.Single(reopened.SlotOverrides, slot => slot.PlanId == first.PlanId && slot.SlotName == "DMM").HasLastIdn);

        reopenedTarget.OverrideResource = "MOCK::replacement";
        Assert.False(reopenedTarget.HasLastIdn);
        await reopened.SaveCommand.ExecuteAsync();
        var changedStore = new SettingsStore(fixture.Root);
        await changedStore.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        var changed = CreateVm(changedStore, idn: new FileStationIdnStore(fixture.Root));
        await changed.RefreshSlotsCommand.ExecuteAsync();
        var changedTarget = Assert.Single(changed.SlotOverrides, slot => slot.PlanId == second.PlanId && slot.SlotName == "DMM");
        Assert.Equal("MOCK::replacement", changedTarget.EffectiveResource);
        Assert.False(changedTarget.HasLastIdn);
        fixture.AssertSourcesUnchanged();
    }

    private static InstrumentsViewModel CreateVm(ISettingsStore settings, IVisaSessionFactory? sessions = null,
        IStationIdnStore? idn = null, IBenchOperationCoordinator? bench = null)
        => new(settings, new FakeVisaDiscovery(), new FakeOpenTapSession(),
            sessions ?? new MockVisaSessionFactory(new VisaSessionGate()), bench: bench, idnStore: idn)
        { UiScheduler = action => action() };

    private static SlotOverrideItemViewModel Slot(string planId, string name)
        => new(planId, planId, new OpenTapInstrumentSlot
        {
            Name = name,
            TypeName = "MockInstrument",
            RoleHint = "dmm",
            ResourceName = "MOCK::INSTR0",
        }, null, useMockVisa: true);

    private static void SetSlots(InstrumentsViewModel vm, params SlotOverrideItemViewModel[] slots)
    {
        vm.SlotOverrides.Clear();
        foreach (var slot in slots) vm.SlotOverrides.Add(slot);
    }

    private sealed class ControlledQuery : IVisaSessionFactory, IVisaSession
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ResourceName => "MOCK::INSTR0";
        public int IoTimeoutMilliseconds { get; set; }
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
            => Task.FromResult<IVisaSession>(this);
        public Task WriteAsync(string command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
        {
            Assert.Equal("*IDN?", command);
            Started.SetResult();
            return Completion.Task.WaitAsync(cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingVisaFactory : IVisaSessionFactory
    {
        internal int OpenCount { get; private set; }
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            throw new InvalidOperationException("Non-message instruments must not be opened for *IDN?.");
        }
    }

    private sealed class StationFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "persona-station-" + Guid.NewGuid().ToString("N"));
        internal string FirstPlanId { get; } = "persona-first-" + Guid.NewGuid().ToString("N");
        internal string SecondPlanId { get; } = "persona-second-" + Guid.NewGuid().ToString("N");
        internal FileStationIdnStore Identities { get; }
        private readonly Dictionary<string, byte[]> _sourceFiles = [];

        internal StationFixture(bool createPlans = false, bool duplicateNames = false)
        {
            Identities = new FileStationIdnStore(Root);
            if (!createPlans) return;
            try
            {
                var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
                Directory.CreateDirectory(programs);
                foreach (var id in new[] { FirstPlanId, SecondPlanId })
                {
                    var path = Path.Combine(programs, id + ".TapPlan");
                    _sourceFiles[path] = [];
                    SampleProgramFactory.Create().Save(path);
                    _sourceFiles[path] = File.ReadAllBytes(path);
                    var sidecar = Path.ChangeExtension(path, ".program.json");
                    _sourceFiles[sidecar] = [];
                    File.WriteAllText(sidecar, JsonSerializer.Serialize(new ProgramSidecar
                    { DisplayName = duplicateNames ? "Shared commissioning program" : id },
                        ProgramCatalogJsonContext.Default.ProgramSidecar));
                    _sourceFiles[sidecar] = File.ReadAllBytes(sidecar);
                }
            }
            catch { Dispose(); throw; }
        }

        internal void AssertSourcesUnchanged()
        {
            foreach (var (path, bytes) in _sourceFiles) Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        public void Dispose()
        {
            foreach (var path in _sourceFiles.Keys) File.Delete(path);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FailingSettingsStore : ISettingsStore
    {
        private readonly FakeSettingsStore _inner = new();
        private bool _writable = true;
        private string? _failure;
        internal bool FailSave { get; set; } = true;
        internal bool ThrowOnFailure { get; init; }
        public AppSettings AppSettings => _inner.AppSettings;
        public UiState UiState => _inner.UiState;
        public string RootDirectory => _inner.RootDirectory;
        public string RunsDirectory => _inner.RunsDirectory;
        public string SettingsPath => _inner.SettingsPath;
        public IReadOnlyList<SettingProvenance> Provenance => _inner.Provenance;
        public bool IsSettingsWritable => _writable;
        public string? LastPersistenceError => _failure;
        public string? SettingsSchemaWarning => null;
        public bool IsOverridden(string key) => false;
        public event EventHandler? AppSettingsSaved { add { } remove { } }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(IReadOnlyDictionary<string, string>? environmentOverlays,
            IReadOnlyDictionary<string, string>? commandLineOverlays, Action<string>? warn = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
        {
            _writable = !FailSave;
            _failure = FailSave ? "Review disk full" : null;
            return FailSave && ThrowOnFailure ? Task.FromException(new IOException(_failure)) : Task.CompletedTask;
        }
        public Task SaveUiStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
