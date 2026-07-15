using System.Collections.ObjectModel;
using System.Threading.Tasks;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
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

public partial class InstrumentsViewModel : ReactiveObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IVisaResourceDiscovery _discovery;

    public InstrumentsViewModel(ISettingsStore settingsStore, IVisaResourceDiscovery discovery)
    {
        _settingsStore = settingsStore;
        _discovery = discovery;
        Instruments = new ObservableCollection<VisaInstrument>(settingsStore.AppSettings.Instruments);
        StationBindings = new ObservableCollection<StationBindingItemViewModel>(
            settingsStore.AppSettings.StationBindings.Select(b =>
                new StationBindingItemViewModel(b.Role, b.InstrumentId)));
        Discovered = [];
        Status = "Discover VISA resources, bind roles to registry ids, and save.";
        RefreshDiscoverCommand = ReactiveCommand.CreateFromTask(RefreshDiscoverAsync);
        AddSelectedCommand = ReactiveCommand.Create(AddSelected);
        RemoveSelectedCommand = ReactiveCommand.Create(RemoveSelected);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        AddManualCommand = ReactiveCommand.Create(AddManual);
        AddStationBindingCommand = ReactiveCommand.Create(AddStationBinding);
        RemoveStationBindingCommand = ReactiveCommand.Create(RemoveStationBinding);
    }

    public ObservableCollection<VisaInstrument> Instruments { get; }
    public ObservableCollection<DiscoveredResourceItem> Discovered { get; }
    public ObservableCollection<StationBindingItemViewModel> StationBindings { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshDiscoverCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddSelectedCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RemoveSelectedCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddManualCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddStationBindingCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RemoveStationBindingCommand { get; }

    [Reactive] private DiscoveredResourceItem? _selectedDiscovered;
    [Reactive] private VisaInstrument? _selectedInstrument;
    [Reactive] private StationBindingItemViewModel? _selectedStationBinding;
    [Reactive] private string _manualResource = "MOCK::INSTR0";
    [Reactive] private string _manualName = "New instrument";
    [Reactive] private string _newRole = "dmm";
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
