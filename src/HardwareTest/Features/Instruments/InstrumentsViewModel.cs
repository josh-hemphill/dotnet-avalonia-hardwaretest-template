using System.Collections.ObjectModel;
using System.Threading.Tasks;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Instruments;

public sealed class DiscoveredResourceItem
{
    public required string Resource { get; init; }
    public required string Description { get; init; }

    public string Title =>
        string.IsNullOrWhiteSpace(Description) || string.Equals(Description, Resource, StringComparison.Ordinal)
            ? Resource
            : Description;

    public string Subtitle => Resource;
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
    private readonly ISettingsStore _settingsStore;
    private readonly IVisaResourceDiscovery _discovery;
    private readonly IOpenTapSession _openTap;

    public InstrumentsViewModel(
        ISettingsStore settingsStore,
        IVisaResourceDiscovery discovery,
        IOpenTapSession openTap)
    {
        _settingsStore = settingsStore;
        _discovery = discovery;
        _openTap = openTap;
        Discovered = [];
        SlotOverrides = [];
        Status = "Discover VISA resources, then set per-plan OpenTAP slot overrides.";
        RefreshDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshDiscoverAsync);
        RefreshSlotsCommand = ReactiveCommand.CreateFromTask(RefreshSlotsAsync);
        ApplySelectedResourceCommand = ReactiveCommand.Create(ApplySelectedResource);
        ClearOverrideCommand = ReactiveCommand.Create(ClearOverride);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        _ = RefreshSlotsAsync();
    }

    public ObservableCollection<DiscoveredResourceItem> Discovered { get; }
    public ObservableCollection<SlotOverrideItemViewModel> SlotOverrides { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshDiscoverCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshSlotsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ApplySelectedResourceCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearOverrideCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }

    [Reactive] private DiscoveredResourceItem? _selectedDiscovered;
    [Reactive] private SlotOverrideItemViewModel? _selectedSlot;
    [Reactive] private string _status = string.Empty;

    private async Task RefreshDiscoverAsync()
    {
        Discovered.Clear();
        try
        {
            var found = await _discovery.FindAsync();
            foreach (var item in found)
            {
                Discovered.Add(new DiscoveredResourceItem
                {
                    Resource = item.Resource,
                    Description = item.Description,
                });
            }

            Status = found.Count == 0
                ? "No resources found (enable mock VISA or install a vendor runtime)."
                : $"Found {found.Count} VISA resource(s).";
        }
        catch (Exception ex)
        {
            Status = $"Discovery failed: {ex.Message}";
        }
    }

    private async Task RefreshSlotsAsync()
    {
        SlotOverrides.Clear();
        var saved = _settingsStore.AppSettings.PlanSlotOverrides;
        try
        {
            await _openTap.LoadSampleProgramAsync();
            AddSlotsFromLoadedPlan("sample", "Sample Hardware Suite", saved);

            foreach (var path in EnumeratePlanFiles())
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(id, "sample", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await _openTap.LoadPlanAsync(path);
                AddSlotsFromLoadedPlan(id, id, saved);
            }

            // Restore sample as default loaded plan for Run page.
            await _openTap.LoadSampleProgramAsync();
            Status = SlotOverrides.Count == 0
                ? "No OpenTAP instrument slots found in available plans."
                : $"Loaded {SlotOverrides.Count} slot(s) from available plans.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to load plan slots: {ex.Message}";
        }
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

    private static IEnumerable<string> EnumeratePlanFiles()
    {
        var dirs = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Programs"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plans", "opentap")),
        };
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.TapPlan"))
            {
                yield return file;
            }
        }
    }

    private void ApplySelectedResource()
    {
        if (SelectedSlot is null)
        {
            Status = "Select a plan slot.";
            return;
        }

        if (SelectedDiscovered is null)
        {
            Status = "Select a discovered VISA resource.";
            return;
        }

        SelectedSlot.OverrideResource = SelectedDiscovered.Resource;
        Status = $"Override {SelectedSlot.SlotName} → {SelectedDiscovered.Resource} (save to persist).";
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
