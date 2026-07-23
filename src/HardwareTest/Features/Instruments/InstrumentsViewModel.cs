using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
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
        string? overrideResource)
    {
        PlanId = planId;
        PlanDisplayName = planDisplayName;
        SlotName = slot.Name;
        TypeName = slot.TypeName;
        RoleHint = slot.RoleHint;
        PlanDefaultResource = slot.ResourceName;
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
    private readonly IOpenTapSession _openTap;
    private readonly IVisaSessionFactory _visaSessions;
    private string? _restorePlanPath;
    private bool _suppressSelectionSync;

    public InstrumentsViewModel(
        ISettingsStore settingsStore,
        IVisaResourceDiscovery discovery,
        IOpenTapSession openTap,
        IVisaSessionFactory visaSessions)
    {
        _settingsStore = settingsStore;
        _discovery = discovery;
        _openTap = openTap;
        _visaSessions = visaSessions;
        DiscoveredVisa = [];
        DiscoveredOpenTap = [];
        SlotOverrides = [];
        Status = "Discover VISA or OpenTAP resources, then set per-plan OpenTAP slot overrides.";
        RefreshVisaDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshVisaDiscoverAsync);
        RefreshOpenTapDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshOpenTapDiscoverAsync);
        RefreshSlotsCommand = ReactiveCommand.CreateFromTask(RefreshSlotsAsync);
        ApplySelectedResourceCommand = ReactiveCommand.Create(ApplySelectedResource);
        ClearOverrideCommand = ReactiveCommand.Create(ClearOverride);
        QuerySelectedIdnCommand = ReactiveCommand.CreateFromTask(QuerySelectedIdnAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);

        PropertyChanged += (_, args) =>
        {
            if (_suppressSelectionSync)
            {
                return;
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

    [Reactive] private DiscoveredResourceItem? _selectedVisa;
    [Reactive] private OpenTapDiscoveredResourceItem? _selectedOpenTap;
    [Reactive] private SlotOverrideItemViewModel? _selectedSlot;
    [Reactive] private string _status = string.Empty;

    /// Backward-compatible alias for SelectedVisa.
    public DiscoveredResourceItem? SelectedDiscovered
    {
        get => SelectedVisa;
        set => SelectedVisa = value;
    }

    private async Task RefreshVisaDiscoverAsync()
    {
        DiscoveredVisa.Clear();
        try
        {
            var found = await _discovery.FindAsync();
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

            Status = found.Count == 0
                ? "No VISA resources found (enable mock VISA or install a vendor runtime)."
                : $"Found {found.Count} VISA resource(s).";
        }
        catch (Exception ex)
        {
            Status = $"VISA discovery failed: {ex.Message}";
        }
    }

    private Task RefreshOpenTapDiscoverAsync()
    {
        DiscoveredOpenTap.Clear();
        try
        {
            var found = _openTap.ListDiscoveredDeviceAddresses();
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

            Status = found.Count == 0
                ? "No OpenTAP device addresses found (IDeviceDiscovery / VisaAddress)."
                : $"Found {found.Count} OpenTAP device address(es).";
        }
        catch (Exception ex)
        {
            Status = $"OpenTAP discovery failed: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private async Task RefreshSlotsAsync()
    {
        SlotOverrides.Clear();
        var saved = _settingsStore.AppSettings.PlanSlotOverrides;
        _restorePlanPath = _openTap.LoadedPlanPath;
        try
        {
            foreach (var entry in ProgramCatalog.Enumerate())
            {
                await LoadCatalogEntryAsync(entry).ConfigureAwait(false);
                AddSlotsFromLoadedPlan(entry.Id, entry.DisplayName, saved);
            }

            await RestoreLoadedPlanAsync().ConfigureAwait(false);
            Status = SlotOverrides.Count == 0
                ? "No OpenTAP instrument slots found in available plans."
                : $"Loaded {SlotOverrides.Count} slot(s) from available plans.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to load plan slots: {ex.Message}";
        }
    }

    private Task LoadCatalogEntryAsync(ProgramCatalogEntry entry)
        => entry.LoadKind switch
        {
            ProgramLoadKind.FactorySample => _openTap.LoadSampleProgramAsync(),
            ProgramLoadKind.FactoryBoardDemo => _openTap.LoadBoardDemoProgramAsync(),
            ProgramLoadKind.FactorySweepDemo => _openTap.LoadSweepDemoProgramAsync(),
            _ => _openTap.LoadPlanAsync(entry.Path),
        };

    private async Task RestoreLoadedPlanAsync()
    {
        var catalog = ProgramCatalog.Enumerate();
        var restore = catalog.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(_restorePlanPath)
            && string.Equals(e.Path, _restorePlanPath, StringComparison.OrdinalIgnoreCase));
        restore ??= catalog.FirstOrDefault();
        if (restore is null)
        {
            return;
        }

        await LoadCatalogEntryAsync(restore).ConfigureAwait(false);
    }

    private void AddSlotsFromLoadedPlan(string planId, string displayName, List<PlanSlotOverride> saved)
    {
        foreach (var slot in _openTap.InstrumentSlots)
        {
            var existing = saved.FirstOrDefault(o =>
                string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.SlotName, slot.Name, StringComparison.OrdinalIgnoreCase));
            SlotOverrides.Add(new SlotOverrideItemViewModel(planId, displayName, slot, existing?.Resource));
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

        Status = $"Querying *IDN? on {address}…";
        using var cts = new CancellationTokenSource(IdnTimeout);
        try
        {
            await using var session = await _visaSessions.OpenAsync(address, cts.Token).ConfigureAwait(false);
            var raw = await session.QueryAsync("*IDN?", cts.Token).ConfigureAwait(false);
            var (_, _, _, _, summary) = VisaResourceParser.FormatIdn(raw);
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
        }
        catch (OperationCanceledException)
        {
            Status = $"*IDN? timed out for {address}.";
        }
        catch (Exception ex)
        {
            Status = $"*IDN? failed for {address}: {ex.Message}";
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

        await _settingsStore.SaveAppSettingsAsync();
        Status = $"Saved {_settingsStore.AppSettings.PlanSlotOverrides.Count} slot override(s).";
    }
}
