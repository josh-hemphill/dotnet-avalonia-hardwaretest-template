using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.UiThreading;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Instruments;

public partial class InstrumentsViewModel : ReactiveObject
{
    private static readonly TimeSpan IdnTimeout = TimeSpan.FromSeconds(5);

    private readonly ISettingsStore _settingsStore;
    private readonly IVisaResourceDiscovery _discovery;
    private readonly IOpenTapHostCatalog _hostCatalog;
    private readonly IVisaSessionFactory _visaSessions;
    private readonly OperatorSession? _operatorSession;
    private readonly IVisaModeController? _visaModeController;
    private readonly IBenchOperationCoordinator? _bench;
    private readonly IStationIdnStore? _idnStore;
    private readonly object _slotsGate = new();
    private readonly HashSet<SlotOverrideItemViewModel> _observedSlots = [];
    private bool _suppressSelectionSync;

    public const string AllPlanFilter = "All programs";

    public InstrumentsViewModel(
        ISettingsStore settingsStore,
        IVisaResourceDiscovery discovery,
        IOpenTapHostCatalog hostCatalog,
        IVisaSessionFactory visaSessions,
        OperatorSession? operatorSession = null,
        IVisaModeController? visaModeController = null,
        IBenchOperationCoordinator? bench = null,
        IStationIdnStore? idnStore = null)
    {
        _settingsStore = settingsStore;
        _discovery = discovery;
        _hostCatalog = hostCatalog;
        _visaSessions = visaSessions;
        _operatorSession = operatorSession;
        _visaModeController = visaModeController;
        _bench = bench;
        _idnStore = idnStore;
        DiscoveredVisa = [];
        DiscoveredOpenTap = [];
        SlotOverrides = [];
        SlotOverrides.CollectionChanged += (_, _) => SyncSlotSubscriptions();
        VisibleSlots = [];
        PlanFilterOptions = [AllPlanFilter];
        PlanFilter = AllPlanFilter;
        Status = "Discover VISA or OpenTAP resources, then set per-plan OpenTAP slot overrides.";

        if (visaModeController is not null)
        {
            visaModeController.ModeApplied += OnVisaModeApplied;
        }

        RefreshVisaDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshVisaDiscoverAsync);
        RefreshOpenTapDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshOpenTapDiscoverAsync);
        RefreshSlotsCommand = ReactiveCommand.CreateFromTask(RefreshSlotsAsync);
        ApplySelectedResourceCommand = ReactiveCommand.Create(ApplySelectedResource);
        ClearOverrideCommand = ReactiveCommand.Create(ClearOverride);
        QuerySelectedIdnCommand = ReactiveCommand.CreateFromTask(QuerySelectedIdnAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        NavigateToRunCommand = ReactiveCommand.Create(
            () => NavigateToRunRequested?.Invoke(this, EventArgs.Empty));

        PropertyChanged += (_, args) =>
        {
            if (_suppressSelectionSync)
            {
                return;
            }

            if (args.PropertyName is nameof(HasDiscoveredVisa) or nameof(HasDiscoveredOpenTap) or nameof(IsBusy))
            {
                this.RaisePropertyChanged(nameof(ShowDiscoverEmpty));
                this.RaisePropertyChanged(nameof(CanEditBindings));
                RefreshCommission();
            }

            if (args.PropertyName == nameof(PlanFilter))
            {
                ApplyPlanFilter();
            }

            if (args.PropertyName == nameof(SelectedVisa) && SelectedVisa is not null)
            {
                _suppressSelectionSync = true;
                SelectedOpenTap = null;
                _suppressSelectionSync = false;
            }
            else if (args.PropertyName == nameof(SelectedOpenTap) && SelectedOpenTap is not null)
            {
                _suppressSelectionSync = true;
                SelectedVisa = null;
                _suppressSelectionSync = false;
            }
        };

        _ = RefreshSlotsAsync();
    }

    /// Test seam: routes UI work synchronously instead of through the Avalonia dispatcher.
    public Action<Action>? UiScheduler { get; set; }

    private Task RunOnUiAsync(Action action) => UiDispatch.RunAsync(action, UiScheduler);

    private void SyncSlotSubscriptions()
    {
        foreach (var removed in _observedSlots.Where(slot => !SlotOverrides.Contains(slot)).ToArray())
        {
            removed.PropertyChanged -= OnSlotBindingChanged;
            _observedSlots.Remove(removed);
        }

        foreach (var added in SlotOverrides.Where(slot => _observedSlots.Add(slot)))
        {
            added.PropertyChanged += OnSlotBindingChanged;
        }
    }

    private void OnSlotBindingChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SlotOverrideItemViewModel.OverrideResource)
            or nameof(SlotOverrideItemViewModel.LastIdnSummary))
        {
            RefreshCommission();
        }
    }

    public ObservableCollection<DiscoveredResourceItem> DiscoveredVisa { get; }
    public ObservableCollection<OpenTapDiscoveredResourceItem> DiscoveredOpenTap { get; }
    public ObservableCollection<SlotOverrideItemViewModel> SlotOverrides { get; }
    public ObservableCollection<SlotOverrideItemViewModel> VisibleSlots { get; }
    public ObservableCollection<string> PlanFilterOptions { get; }

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RefreshVisaDiscoverCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RefreshOpenTapDiscoverCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RefreshSlotsCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ApplySelectedResourceCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ClearOverrideCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> QuerySelectedIdnCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> SaveCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> NavigateToRunCommand { get; }

    [Reactive] private DiscoveredResourceItem? _selectedVisa;
    [Reactive] private OpenTapDiscoveredResourceItem? _selectedOpenTap;
    [Reactive] private SlotOverrideItemViewModel? _selectedSlot;
    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isBusy;
    [Reactive] private bool _hasDiscoveredVisa;
    [Reactive] private bool _hasDiscoveredOpenTap;
    [Reactive] private string _planFilter = AllPlanFilter;

    public bool ShowDiscoverEmpty => !HasDiscoveredVisa && !HasDiscoveredOpenTap && !IsBusy;
    public bool BindingsReadOnly => _settingsStore.IsOverridden(nameof(AppSettings.PlanSlotOverrides))
        || AppSettingsEnvironmentBinder.IsListOverridden(_settingsStore.Provenance, nameof(AppSettings.PlanSlotOverrides));
    public bool CanEditBindings => !IsBusy && !BindingsReadOnly;
    public string BindingOwnershipHint => BindingsReadOnly
        ? "Deployment controls these bindings. Update the environment or command-line configuration to change them."
        : string.Empty;

    private bool TryEditBindings()
    {
        if (!BindingsReadOnly) return true;
        Status = BindingOwnershipHint;
        return false;
    }

    public event EventHandler? NavigateToRunRequested;

    private void OnVisaModeApplied(object? sender, EventArgs e)
    {
        DiscoveredVisa.Clear();
        DiscoveredOpenTap.Clear();
        _ = RefreshVisaDiscoverAsync();
        _ = RefreshSlotsAsync();
    }

    private async Task RefreshVisaDiscoverAsync()
    {
        _operatorSession?.TouchActivity();
        await RunOnUiAsync(() =>
        {
            IsBusy = true;
            DiscoveredVisa.Clear();
        }).ConfigureAwait(false);
        try
        {
            var found = await _discovery.FindAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                foreach (var item in found)
                {
                    DiscoveredVisa.Add(new DiscoveredResourceItem
                    {
                        Resource = item.Resource,
                        Description = item.Description,
                        Interface = item.Interface,
                        Detail = item.Detail,
                        LooksLikeAlias = item.LooksLikeAlias,
                        SupportsMessageQuery = item.SupportsMessageQuery,
                    });
                }

                HasDiscoveredVisa = found.Count > 0;
                Status = found.Count == 0
                    ? "No VISA resources found (enable mock VISA or install a vendor runtime)."
                    : $"Found {found.Count} VISA resource(s).";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                HasDiscoveredVisa = false;
                Status = $"VISA discovery failed: {ex.Message}";
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private async Task RefreshOpenTapDiscoverAsync()
    {
        IsBusy = true;
        DiscoveredOpenTap.Clear();
        try
        {
            var found = _hostCatalog.ListDiscoveredDeviceAddresses();
            foreach (var item in found)
            {
                DiscoveredOpenTap.Add(new OpenTapDiscoveredResourceItem
                {
                    Address = item.Address,
                    Source = item.Source,
                    Kind = item.Kind,
                    Interface = item.Interface,
                    Detail = item.Detail,
                    LooksLikeAlias = item.LooksLikeAlias,
                    SupportsMessageQuery = item.SupportsMessageQuery,
                });
            }

            HasDiscoveredOpenTap = found.Count > 0;
            Status = found.Count == 0
                ? "No OpenTAP device addresses found (IDeviceDiscovery / VisaAddress)."
                : $"Found {found.Count} OpenTAP device address(es).";
        }
        catch (Exception ex)
        {
            HasDiscoveredOpenTap = false;
            Status = $"OpenTAP discovery failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    private Task RefreshSlotsAsync()
    {
        lock (_slotsGate)
        {
            this.RaisePropertyChanged(nameof(BindingsReadOnly));
            this.RaisePropertyChanged(nameof(CanEditBindings));
            this.RaisePropertyChanged(nameof(BindingOwnershipHint));
            SlotOverrides.Clear();
            var saved = _settingsStore.AppSettings.PlanSlotOverrides;
            var useMockVisa = _visaModeController?.EffectiveUseMockVisa ?? _settingsStore.AppSettings.UseMockVisa;
            var failures = 0;
            string? firstFailure = null;
            try
            {
                foreach (var entry in ProgramCatalog.Enumerate())
                {
                    try
                    {
                        var plan = InstrumentSlotCollector.CreatePlan(entry);
                        AddSlotsFromPlan(entry.Id, entry.DisplayName, InstrumentSlotCollector.FromPlan(plan), saved, useMockVisa);
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        var cause = ex.GetBaseException();
                        firstFailure ??= $"Plan '{entry.DisplayName}': {cause.GetType().Name}: {cause.Message}";
                        System.Diagnostics.Trace.TraceError($"Plan '{entry.Id}' slots skipped: {ex}");
                    }
                }

                if (SlotOverrides.Count == 0)
                {
                    Status = failures > 0
                        ? $"No OpenTAP instrument slots found ({failures} plan load error(s)). {firstFailure}"
                        : "No OpenTAP instrument slots found in available plans.";
                }
                else if (failures == 0)
                {
                    Status = $"Loaded {SlotOverrides.Count} slot(s) from available plans.";
                }
                else
                {
                    Status = $"Loaded {SlotOverrides.Count} slot(s); {failures} plan(s) failed. {firstFailure}";
                }
            }
            catch (Exception ex)
            {
                Status = $"Failed to load plan slots: {ex.Message}";
            }

            RebuildPlanFilters();
            ApplyPlanFilter();
        }

        return Task.CompletedTask;
    }

    private void AddSlotsFromPlan(
        string planId,
        string displayName,
        IReadOnlyList<OpenTapInstrumentSlot> slots,
        List<PlanSlotOverride> saved,
        bool useMockVisa)
    {
        // Serialized steps can hold separate instances of the same logical binding.
        // Show identical metadata once; retain entries whose binding values differ.
        foreach (var slot in slots.DistinctBy(slot => (slot.Name, slot.TypeName, slot.ResourceName, slot.RoleHint)))
        {
            var existing = saved.FirstOrDefault(o =>
                string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.SlotName, slot.Name, StringComparison.OrdinalIgnoreCase));
            var effectiveResource = string.IsNullOrWhiteSpace(existing?.Resource) ? slot.ResourceName : existing.Resource.Trim();
            var identity = _idnStore?.Find(planId, slot.Name);
            SlotOverrides.Add(new SlotOverrideItemViewModel(
                planId,
                displayName,
                slot,
                existing?.Resource,
                useMockVisa,
                identity is not null && string.Equals(identity.Resource, effectiveResource, StringComparison.OrdinalIgnoreCase)
                    ? identity.IdnSummary : null));
        }
    }

    private void ApplySelectedResource()
    {
        if (!TryEditBindings()) return;
        if (SelectedSlot is null)
        {
            Status = "Select a plan slot.";
            return;
        }

        var resource = SelectedVisa?.Resource ?? SelectedOpenTap?.Address;
        string? source;
        if (SelectedVisa is not null)
        {
            source = "VISA";
        }
        else if (SelectedOpenTap is not null)
        {
            source = "OpenTAP";
        }
        else
        {
            source = null;
        }

        if (string.IsNullOrWhiteSpace(resource) || source is null)
        {
            Status = "Select a VISA or OpenTAP discovered resource.";
            return;
        }

        var effectiveMock = _visaModeController?.EffectiveUseMockVisa ?? _settingsStore.AppSettings.UseMockVisa;
        if (!effectiveMock && MockResourceGuard.LooksLikeMockResource(resource))
        {
            Status = $"Cannot bind mock resource '{resource}' while Use mock VISA is off.";
            return;
        }

        SelectedSlot.OverrideResource = resource;
        Status = $"Override {SelectedSlot.SlotName} → {resource} ({source}; save to persist).";
        RefreshCommission();
    }

    private async Task QuerySelectedIdnAsync()
    {
        var queriedVisa = SelectedVisa;
        var queriedOpenTap = SelectedOpenTap;
        var address = queriedVisa?.Resource ?? queriedOpenTap?.Address;
        var supports = queriedVisa?.SupportsMessageQuery ?? queriedOpenTap?.SupportsMessageQuery ?? false;
        if (string.IsNullOrWhiteSpace(address) && SelectedSlot is not null)
        {
            address = SelectedSlot.EffectiveResource;
            supports = VisaResourceParser.Parse(address).SupportsMessageQuery;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            Status = "Select a VISA or OpenTAP resource, or a bound slot, to query.";
            return;
        }

        if (!supports)
        {
            Status = $"Skipping *IDN? for '{address}' (interface not typically message-based). Narrow with hints, then pick USB/TCPIP/GPIB/ASRL/MOCK.";
            return;
        }

        IDisposable? lease = null;
        if (_bench is not null && !_bench.TryEnter(BenchOperation.IdQuery, out lease, out var busy))
        {
            Status = busy;
            return;
        }

        var queriedSlot = SelectedSlot is { } selectedSlot
            && string.Equals(selectedSlot.EffectiveResource, address, StringComparison.OrdinalIgnoreCase)
                ? selectedSlot : null;
        try
        {
            Status = $"Querying *IDN? on {address}…";
            using var cts = new CancellationTokenSource(IdnTimeout);
            try
            {
                await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
                await using var session = await _visaSessions.OpenAsync(address, cts.Token).ConfigureAwait(false);
                var raw = await session.QueryAsync("*IDN?", cts.Token).ConfigureAwait(false);
                var (_, _, _, _, summary) = VisaResourceParser.FormatIdn(raw);
                await RunOnUiAsync(() =>
                {
                    if (queriedVisa is not null)
                    {
                        queriedVisa.IdnRaw = raw;
                        queriedVisa.IdnSummary = summary;
                        queriedVisa.RaisePropertyChanged(nameof(DiscoveredResourceItem.HasIdn));
                    }
                    else if (queriedOpenTap is not null)
                    {
                        queriedOpenTap.IdnRaw = raw;
                        queriedOpenTap.IdnSummary = summary;
                        queriedOpenTap.RaisePropertyChanged(nameof(OpenTapDiscoveredResourceItem.HasIdn));
                    }

                    var recorded = PersistIdn(queriedSlot, address, raw, summary);
                    Status = string.IsNullOrWhiteSpace(summary)
                        ? $"*IDN? returned empty for {address}."
                        : recorded
                            ? $"IDN {address}: {summary}. Recorded for {queriedSlot!.PlanDisplayName} / {queriedSlot.SlotName}."
                            : $"IDN {address}: {summary}. Not recorded to a program slot; select its bound slot and query again.";
                    RefreshCommission();
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await RunOnUiAsync(() => Status = $"*IDN? timed out for {address}.").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RunOnUiAsync(() => Status = $"*IDN? failed for {address}: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private void ClearOverride()
    {
        if (!TryEditBindings()) return;
        if (SelectedSlot is null)
        {
            Status = "Select a slot override to clear.";
            return;
        }

        SelectedSlot.OverrideResource = string.Empty;
        Status = $"Cleared override for {SelectedSlot.SlotName}.";
        RefreshCommission();
    }

    private async Task SaveAsync()
    {
        if (!TryEditBindings()) return;
        _operatorSession?.TouchActivity();
        await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
        var previousOverrides = _settingsStore.AppSettings.PlanSlotOverrides;
        try
        {
            await RunOnUiAsync(() =>
            {
                _settingsStore.AppSettings.PlanSlotOverrides = SlotOverrides
                    .Where(s => !string.IsNullOrWhiteSpace(s.OverrideResource))
                    .Select(s => new PlanSlotOverride
                    {
                        PlanId = s.PlanId,
                        SlotName = s.SlotName,
                        RoleHint = s.RoleHint,
                        Resource = s.OverrideResource.Trim(),
                    })
                    .ToList();
            }).ConfigureAwait(false);

            await _settingsStore.SaveAppSettingsAsync().ConfigureAwait(false);
            if (!_settingsStore.IsSettingsWritable || !string.IsNullOrWhiteSpace(_settingsStore.LastPersistenceError))
            {
                throw new IOException(_settingsStore.LastPersistenceError ?? _settingsStore.SettingsSchemaWarning
                    ?? "The station settings file is read-only.");
            }
            await RunOnUiAsync(() =>
            {
                Status = $"Saved {_settingsStore.AppSettings.PlanSlotOverrides.Count} slot override(s).";
                RefreshCommission();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                _settingsStore.AppSettings.PlanSlotOverrides = previousOverrides;
                Status = $"Could not save overrides: {ex.Message}. Your edits are retained; fix the storage problem and retry Save.";
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }
}
