using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    // An explicit visible creation choice, independent of imported resource ordering.
    private string _newInstrumentTypeId = typeof(MockDmmInstrument).FullName!;

    private IReadOnlyList<AuthoringInstrumentAdapter> _instrumentTypeChoices = [];
    public IReadOnlyList<AuthoringInstrumentAdapter> InstrumentTypeChoices
    {
        get
        {
            var inspection = HardwareInspection;
            var discovered = inspection.Home is not null && inspection.Error is null
                ? AuthoringInstrumentCatalog.Discover(inspection.Home) : [];
            var choices = discovered.Concat(AuthoringInstrumentCatalog.All.Where(a => !AuthoringInstrumentCatalog.IsLibrary(a.TypeId))).ToArray();
            if (!_instrumentTypeChoices.SequenceEqual(choices)) _instrumentTypeChoices = choices;
            return _instrumentTypeChoices;
        }
    }

    public string InstrumentCatalogSummary
    {
        get
        {
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
        try { return adapter.Availability(InstrumentCreationHomeFor(adapter.TypeId)).Reason; }
        catch (ArgumentException) { return "Correct the selected OpenTAP home path before adding an instrument."; }
    }
}
