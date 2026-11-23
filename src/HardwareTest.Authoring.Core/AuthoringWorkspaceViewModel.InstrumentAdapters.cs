using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Instruments;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public IReadOnlyList<AuthoringInstrumentAdapter> InstrumentTypeChoices => _environmentInspection.Choices;

    public string InstrumentCatalogSummary
    {
        get
        {
            if (EnvironmentInspectionPending) return "Checking available instrument types in the selected home…";
            var physical = InstrumentTypeChoices.Count(adapter => AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId));
            return physical > 0
                ? $"{DeviceDriverCatalog.All.Count} registered device models across five vendor packages ({PublishedInstrumentComponents.Version}). Use one slot per physical device; capability views share its connection."
                : "No physical instrument types available in the selected home. Prepare Instrument Components in Environment. Mock DMM is available for demo plans.";
        }
    }

    // An explicit visible creation choice, independent of imported resource ordering.
    public string NewInstrumentTypeId
    {
        get;
        set
        {
            if (!SetField(ref field, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(SelectedNewInstrumentType));
            OnPropertyChanged(nameof(IsNewRegisteredDevice));
            OnPropertyChanged(nameof(NewInstrumentTypeGuidance));
            OnPropertyChanged(nameof(NewInstrumentAvailabilityText));
            OnPropertyChanged(nameof(CanAddInstrumentSlot));
        }
    } = typeof(MockDmmInstrument).FullName!;

    public AuthoringInstrumentAdapter? SelectedNewInstrumentType
    {
        get => AuthoringInstrumentCatalog.TryGet(NewInstrumentTypeId, out var adapter) ? adapter : null;
        set { if (value is not null) NewInstrumentTypeId = value.TypeId; }
    }

    public string NewInstrumentAvailabilityText => NewInstrumentCreationIssue() ?? string.Empty;

    private OpenTapHome? InstrumentCreationHome => string.IsNullOrWhiteSpace(Prefs.OpenTapHomeOverride)
        ? null : new OpenTapHome(Prefs.OpenTapHomeOverride);

    private OpenTapHome? InstrumentCreationHomeFor(string typeId)
        => AuthoringInstrumentCatalog.IsLibrary(typeId) ? HardwareInspection.Home : InstrumentCreationHome;

    private string? NewInstrumentCreationIssue()
    {
        if (!AuthoringInstrumentCatalog.TryGet(NewInstrumentTypeId, out var adapter))
            return "Choose a registered instrument type before adding the slot.";
        if (AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId) && string.IsNullOrWhiteSpace(NewInstrumentVisa))
            return "Enter the instrument address for the selected library device.";
        if (IsNewRegisteredDevice && NewDeviceDriver is null) return "Choose a vendor and model before adding the device.";
        if (AuthoringWorkspaceCatalog.Normalize(NewInstrumentSlot) is not { } slot) return "Enter a logical slot name for this instrument.";
        if (InstrumentSlots.Any(existing => string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase))) return $"Slot '{slot}' already exists. Choose a unique logical name.";
        if (EnvironmentInspectionPending) return "Wait for instrument package inspection before adding a slot.";
        return _environmentInspection.Error ?? _environmentInspection.CreationAvailability.GetValueOrDefault(adapter.TypeId)?.Reason
            ?? (_environmentInspection.CreationAvailability.ContainsKey(adapter.TypeId) ? null : "Refresh the environment before creating this instrument type.");
    }
}
