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

public partial class StationBindingItemViewModel : ReactiveObject
{
    public StationBindingItemViewModel(string role, string instrumentId)
    {
        Role = role;
        InstrumentId = instrumentId;
    }

    [Reactive] private string _role = string.Empty;
    [Reactive] private string _instrumentId = string.Empty;
}

public partial class OpenTapSlotItemViewModel : ReactiveObject
{
    public OpenTapSlotItemViewModel(OpenTapInstrumentSlot slot)
    {
        Name = slot.Name;
        TypeName = slot.TypeName;
        RoleHint = slot.RoleHint;
        ResourceName = slot.ResourceName;
    }

    public string Name { get; }
    public string TypeName { get; }
    public string RoleHint { get; }

    [Reactive] private string _resourceName = string.Empty;

    public string Summary => string.IsNullOrWhiteSpace(ResourceName)
        ? $"{Name} ({RoleHint}) — unbound"
        : $"{Name} ({RoleHint}) → {ResourceName}";
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
        Instruments = new ObservableCollection<VisaInstrument>(settingsStore.AppSettings.Instruments);
        StationBindings = new ObservableCollection<StationBindingItemViewModel>(
            settingsStore.AppSettings.StationBindings.Select(b =>
                new StationBindingItemViewModel(b.Role, b.InstrumentId)));
        OpenTapSlots = [];
        Discovered = [];
        Status = "Discover VISA resources, bind OpenTAP slots via station roles, and save.";
        RefreshSlots();
        RefreshSlotsCommand = ReactiveCommand.Create(RefreshSlots);
        RefreshDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshDiscoverAsync);
        AddSelectedCommand = ReactiveCommand.Create(AddSelected);
        RemoveSelectedCommand = ReactiveCommand.Create(RemoveSelected);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        AddManualCommand = ReactiveCommand.Create(AddManual);
        AddStationBindingCommand = ReactiveCommand.Create(AddStationBinding);
        RemoveStationBindingCommand = ReactiveCommand.Create(RemoveStationBinding);
        BindSlotFromSelectedCommand = ReactiveCommand.Create(BindSlotFromSelected);
    }

    public ObservableCollection<VisaInstrument> Instruments { get; }
    public ObservableCollection<DiscoveredResourceItem> Discovered { get; }
    public ObservableCollection<StationBindingItemViewModel> StationBindings { get; }
    public ObservableCollection<OpenTapSlotItemViewModel> OpenTapSlots { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshSlotsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshDiscoverCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddSelectedCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RemoveSelectedCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddManualCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddStationBindingCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RemoveStationBindingCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> BindSlotFromSelectedCommand { get; }

    [Reactive] private DiscoveredResourceItem? _selectedDiscovered;
    [Reactive] private VisaInstrument? _selectedInstrument;
    [Reactive] private StationBindingItemViewModel? _selectedStationBinding;
    [Reactive] private OpenTapSlotItemViewModel? _selectedOpenTapSlot;
    [Reactive] private string _manualResource = "MOCK::INSTR0";
    [Reactive] private string _manualName = "New instrument";
    [Reactive] private string _newRole = "dmm";
    [Reactive] private string _status = string.Empty;

    private void RefreshSlots()
    {
        OpenTapSlots.Clear();
        foreach (var slot in _openTap.InstrumentSlots)
        {
            OpenTapSlots.Add(new OpenTapSlotItemViewModel(slot));
        }

        Status = OpenTapSlots.Count == 0
            ? "No OpenTAP instrument slots (load a program on Run first)."
            : $"Loaded {OpenTapSlots.Count} OpenTAP slot(s) from the active plan.";
    }

    private void BindSlotFromSelected()
    {
        if (SelectedOpenTapSlot is null)
        {
            Status = "Select an OpenTAP slot.";
            return;
        }

        var resource = SelectedInstrument?.Resource
                       ?? SelectedDiscovered?.Resource
                       ?? ManualResource;
        if (string.IsNullOrWhiteSpace(resource))
        {
            Status = "Select a registry instrument or discovered resource.";
            return;
        }

        if (!_openTap.TryBindSlotResource(SelectedOpenTapSlot.Name, resource.Trim()))
        {
            Status = $"Could not bind slot '{SelectedOpenTapSlot.Name}'.";
            return;
        }

        SelectedOpenTapSlot.ResourceName = resource.Trim();
        var role = string.IsNullOrWhiteSpace(SelectedOpenTapSlot.RoleHint)
            ? SelectedOpenTapSlot.Name
            : SelectedOpenTapSlot.RoleHint;
        NewRole = role;
        if (SelectedInstrument is not null)
        {
            AddStationBinding();
        }

        Status = $"Bound slot '{SelectedOpenTapSlot.Name}' → {resource}.";
        RefreshSlots();
    }

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
                : $"Found {found.Count} resource(s).";
        }
        catch (Exception ex)
        {
            Status = $"Discovery failed: {ex.Message}";
        }
    }

    private void AddSelected()
    {
        if (SelectedDiscovered is null)
        {
            Status = "Select a discovered resource first.";
            return;
        }

        if (Instruments.Any(i => string.Equals(i.Resource, SelectedDiscovered.Resource, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "Instrument already in registry.";
            return;
        }

        Instruments.Add(new VisaInstrument
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            DisplayName = SelectedDiscovered.Description,
            Resource = SelectedDiscovered.Resource,
            Enabled = true,
        });
        Status = $"Added {SelectedDiscovered.Resource}.";
    }

    private void AddManual()
    {
        if (string.IsNullOrWhiteSpace(ManualResource))
        {
            Status = "Enter a resource string.";
            return;
        }

        Instruments.Add(new VisaInstrument
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            DisplayName = string.IsNullOrWhiteSpace(ManualName) ? ManualResource : ManualName,
            Resource = ManualResource.Trim(),
            Enabled = true,
        });
        Status = $"Added {ManualResource}.";
    }

    private void RemoveSelected()
    {
        if (SelectedInstrument is null)
        {
            Status = "Select a registry instrument to remove.";
            return;
        }

        Instruments.Remove(SelectedInstrument);
        SelectedInstrument = null;
        Status = "Removed instrument.";
    }

    private void AddStationBinding()
    {
        if (string.IsNullOrWhiteSpace(NewRole))
        {
            Status = "Enter a role name (e.g. dmm).";
            return;
        }

        var instrumentId = SelectedInstrument?.Id
                           ?? Instruments.FirstOrDefault(i => i.Enabled)?.Id
                           ?? string.Empty;
        if (string.IsNullOrWhiteSpace(instrumentId))
        {
            Status = "Add a registry instrument before binding roles.";
            return;
        }

        var role = NewRole.Trim();
        var existing = StationBindings.FirstOrDefault(b =>
            string.Equals(b.Role, role, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.InstrumentId = instrumentId;
            Status = $"Updated role '{role}' → {instrumentId}.";
            return;
        }

        StationBindings.Add(new StationBindingItemViewModel(role, instrumentId));
        Status = $"Bound role '{role}' → {instrumentId}.";
    }

    private void RemoveStationBinding()
    {
        if (SelectedStationBinding is null)
        {
            Status = "Select a station binding to remove.";
            return;
        }

        StationBindings.Remove(SelectedStationBinding);
        SelectedStationBinding = null;
        Status = "Removed station binding.";
    }

    private async Task SaveAsync()
    {
        _settingsStore.AppSettings.Instruments = Instruments.ToList();
        _settingsStore.AppSettings.StationBindings = StationBindings
            .Where(b => !string.IsNullOrWhiteSpace(b.Role))
            .Select(b => new StationBinding { Role = b.Role.Trim(), InstrumentId = b.InstrumentId.Trim() })
            .ToList();
        if (Instruments.FirstOrDefault(i => i.Enabled) is { } first)
        {
            _settingsStore.AppSettings.DefaultVisaResource = first.Resource;
        }

        await _settingsStore.SaveAppSettingsAsync();
        Status = $"Saved {Instruments.Count} instrument(s), {StationBindings.Count} role binding(s).";
    }
}
