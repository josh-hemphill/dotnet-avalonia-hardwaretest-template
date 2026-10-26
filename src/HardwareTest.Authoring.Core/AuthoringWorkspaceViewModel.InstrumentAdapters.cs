using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    // An explicit visible creation choice, independent of imported resource ordering.
    private string _newInstrumentTypeId = typeof(MockDmmInstrument).FullName!;

    public IReadOnlyList<AuthoringInstrumentAdapter> InstrumentTypeChoices => _environmentInspection.Choices;

    public string InstrumentCatalogSummary
    {
        get
        {
            if (EnvironmentInspectionPending) return "Checking available instrument types in the selected home…";
            var physical = InstrumentTypeChoices.Count(adapter => AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId));
            return physical > 0
                ? $"{physical} supported physical instrument types in the selected home. Mock DMM is available for demo plans."
                : "No physical instrument types available in the selected home. Prepare Instrument Components in Environment. Mock DMM is available for demo plans.";
        }
    }

    public string NewInstrumentTypeId
    {
        get => _newInstrumentTypeId;
        set
        {
            if (!SetField(ref _newInstrumentTypeId, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(SelectedNewInstrumentType));
            OnPropertyChanged(nameof(NewInstrumentTypeGuidance));
            OnPropertyChanged(nameof(NewInstrumentAvailabilityText));
            OnPropertyChanged(nameof(CanAddInstrumentSlot));
        }
    }

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
        if (EnvironmentInspectionPending) return "Wait for instrument package inspection before adding a slot.";
        return _environmentInspection.Error ?? _environmentInspection.CreationAvailability.GetValueOrDefault(adapter.TypeId)?.Reason
            ?? (_environmentInspection.CreationAvailability.ContainsKey(adapter.TypeId) ? null : "Refresh the environment before creating this instrument type.");
    }
}
