using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.UiThreading;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Instruments;

public partial class DiscoveredResourceItem : ReactiveObject
{
    public required string Resource { get; init; }
    public required string Description { get; init; }
    public string Interface { get; init; } = "Other";
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }

    [Reactive] private string _idnRaw = string.Empty;
    [Reactive] private string _idnSummary = string.Empty;

    public string Title =>
        string.IsNullOrWhiteSpace(Description) || string.Equals(Description, Resource, StringComparison.Ordinal)
            ? Resource
            : Description;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { Interface };
            if (!string.IsNullOrWhiteSpace(Detail))
            {
                parts.Add(Detail);
            }

            if (LooksLikeAlias)
            {
                parts.Add("Alias?");
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasIdn => !string.IsNullOrWhiteSpace(IdnSummary);
}

public partial class OpenTapDiscoveredResourceItem : ReactiveObject
{
    public required string Address { get; init; }
    public required string Source { get; init; }
    public required string Kind { get; init; }
    public string Interface { get; init; } = "Other";
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }

    [Reactive] private string _idnRaw = string.Empty;
    [Reactive] private string _idnSummary = string.Empty;

    public string Title => Address;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { Interface, Source };
            if (!string.IsNullOrWhiteSpace(Detail))
            {
                parts.Add(Detail);
            }

            if (LooksLikeAlias)
            {
                parts.Add("Alias?");
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasIdn => !string.IsNullOrWhiteSpace(IdnSummary);
}

public partial class SlotOverrideItemViewModel : ReactiveObject
{
    public SlotOverrideItemViewModel(
        string planId,
        string planDisplayName,
        OpenTapInstrumentSlot slot,
        string? overrideResource,
        bool useMockVisa)
    {
        PlanId = planId;
        PlanDisplayName = planDisplayName;
        SlotName = slot.Name;
        TypeName = slot.TypeName;
        RoleHint = slot.RoleHint;
        PlanDefaultResource = slot.ResourceName;
        UseMockVisa = useMockVisa;
        OverrideResource = overrideResource ?? string.Empty;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(OverrideResource))
            {
                this.RaisePropertyChanged(nameof(EffectiveResource));
                this.RaisePropertyChanged(nameof(StatusText));
                this.RaisePropertyChanged(nameof(Summary));
                this.RaisePropertyChanged(nameof(IsOverridden));
            }
        };
    }

    public string PlanId { get; }
    public string PlanDisplayName { get; }
    public string SlotName { get; }
    public string TypeName { get; }
    public string RoleHint { get; }
    public string PlanDefaultResource { get; }
    public bool UseMockVisa { get; }

    [Reactive] private string _overrideResource = string.Empty;

    public bool IsOverridden => !string.IsNullOrWhiteSpace(OverrideResource);

    public string EffectiveResource =>
        string.IsNullOrWhiteSpace(OverrideResource) ? PlanDefaultResource : OverrideResource.Trim();

    public string StatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(EffectiveResource))
            {
                return "Unbound";
            }

            if (!UseMockVisa
                && (MockResourceGuard.LooksLikeMockResource(EffectiveResource)
                    || MockResourceGuard.IsMockInstrumentType(TypeName)))
            {
                return "Demo only";
            }

            return IsOverridden ? "Overridden" : "Ready";
        }
    }

    public string Summary =>
        $"{PlanDisplayName} / {SlotName} ({RoleHint}) → {EffectiveResource}";
}

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
    private readonly object _slotsGate = new();
    private bool _suppressSelectionSync;

    public InstrumentsViewModel(
        ISettingsStore settingsStore,
        IVisaResourceDiscovery discovery,
        IOpenTapHostCatalog hostCatalog,
        IVisaSessionFactory visaSessions,
        OperatorSession? operatorSession = null,
        IVisaModeController? visaModeController = null,
        IBenchOperationCoordinator? bench = null)
    {
        _settingsStore = settingsStore;
        _discovery = discovery;
        _hostCatalog = hostCatalog;
        _visaSessions = visaSessions;
        _operatorSession = operatorSession;
        _visaModeController = visaModeController;
        _bench = bench;
        DiscoveredVisa = [];
        DiscoveredOpenTap = [];
        SlotOverrides = [];
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

    public ObservableCollection<DiscoveredResourceItem> DiscoveredVisa { get; }
    public ObservableCollection<OpenTapDiscoveredResourceItem> DiscoveredOpenTap { get; }
    public ObservableCollection<SlotOverrideItemViewModel> SlotOverrides { get; }

    /// Backward-compatible alias used by older tests/callers.
    public ObservableCollection<DiscoveredResourceItem> Discovered => DiscoveredVisa;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshVisaDiscoverCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshOpenTapDiscoverCommand { get; }
    /// Alias for VISA discover (toolbar / existing tests).
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshDiscoverCommand => RefreshVisaDiscoverCommand;
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshSlotsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ApplySelectedResourceCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearOverrideCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> QuerySelectedIdnCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToRunCommand { get; }

    [Reactive] private DiscoveredResourceItem? _selectedVisa;
    [Reactive] private OpenTapDiscoveredResourceItem? _selectedOpenTap;
    [Reactive] private SlotOverrideItemViewModel? _selectedSlot;
    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isBusy;
    [Reactive] private bool _hasDiscoveredVisa;
    [Reactive] private bool _hasDiscoveredOpenTap;

    public bool ShowDiscoverEmpty => !HasDiscoveredVisa && !HasDiscoveredOpenTap && !IsBusy;

    public event EventHandler? NavigateToRunRequested;

    /// Backward-compatible alias for SelectedVisa.
    public DiscoveredResourceItem? SelectedDiscovered
    {
        get => SelectedVisa;
        set => SelectedVisa = value;
    }

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
            SlotOverrides.Clear();
            var saved = _settingsStore.AppSettings.PlanSlotOverrides;
            var useMockVisa = _visaModeController?.EffectiveUseMockVisa ?? _settingsStore.AppSettings.UseMockVisa;
            var failures = 0;
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
                        Status = $"Plan '{entry.DisplayName}' slots skipped: {ex.Message}";
                    }
                }

                if (SlotOverrides.Count == 0)
                {
                    Status = failures > 0
                        ? $"No OpenTAP instrument slots found ({failures} plan load error(s))."
                        : "No OpenTAP instrument slots found in available plans.";
                }
                else if (failures == 0)
                {
                    Status = $"Loaded {SlotOverrides.Count} slot(s) from available plans.";
                }
                else
                {
                    Status = $"Loaded {SlotOverrides.Count} slot(s); {failures} plan(s) failed.";
                }
            }
            catch (Exception ex)
            {
                Status = $"Failed to load plan slots: {ex.Message}";
            }
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
        foreach (var slot in slots)
        {
            var existing = saved.FirstOrDefault(o =>
                string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.SlotName, slot.Name, StringComparison.OrdinalIgnoreCase));
            SlotOverrides.Add(new SlotOverrideItemViewModel(planId, displayName, slot, existing?.Resource, useMockVisa));
        }
    }

    private void ApplySelectedResource()
    {
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
    }

    private async Task QuerySelectedIdnAsync()
    {
        var address = SelectedVisa?.Resource ?? SelectedOpenTap?.Address;
        var supports = SelectedVisa?.SupportsMessageQuery ?? SelectedOpenTap?.SupportsMessageQuery ?? false;
        if (string.IsNullOrWhiteSpace(address))
        {
            Status = "Select a VISA or OpenTAP resource to query.";
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

        Status = $"Querying *IDN? on {address}…";
        await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
        using var cts = new CancellationTokenSource(IdnTimeout);
        using (lease)
        {
            try
            {
                await using var session = await _visaSessions.OpenAsync(address, cts.Token).ConfigureAwait(false);
                var raw = await session.QueryAsync("*IDN?", cts.Token).ConfigureAwait(false);
                var (_, _, _, _, summary) = VisaResourceParser.FormatIdn(raw);
                await RunOnUiAsync(() =>
                {
                    if (SelectedVisa is not null
                        && string.Equals(SelectedVisa.Resource, address, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedVisa.IdnRaw = raw;
                        SelectedVisa.IdnSummary = summary;
                        SelectedVisa.RaisePropertyChanged(nameof(DiscoveredResourceItem.HasIdn));
                    }
                    else if (SelectedOpenTap is not null
                             && string.Equals(SelectedOpenTap.Address, address, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedOpenTap.IdnRaw = raw;
                        SelectedOpenTap.IdnSummary = summary;
                        SelectedOpenTap.RaisePropertyChanged(nameof(OpenTapDiscoveredResourceItem.HasIdn));
                    }

                    Status = string.IsNullOrWhiteSpace(summary)
                        ? $"*IDN? returned empty for {address}."
                        : $"IDN {address}: {summary}";
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
    }

    private void ClearOverride()
    {
        if (SelectedSlot is null)
        {
            Status = "Select a slot override to clear.";
            return;
        }

        SelectedSlot.OverrideResource = string.Empty;
        Status = $"Cleared override for {SelectedSlot.SlotName}.";
    }

    private async Task SaveAsync()
    {
        _operatorSession?.TouchActivity();
        await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
        try
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

            await _settingsStore.SaveAppSettingsAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
                Status = $"Saved {_settingsStore.AppSettings.PlanSlotOverrides.Count} slot override(s).")
                .ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }
}
