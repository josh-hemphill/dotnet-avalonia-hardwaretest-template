using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    // An explicit visible creation choice, independent of imported resource ordering.
    private string _newInstrumentTypeId = typeof(MockDmmInstrument).FullName!;

    public IReadOnlyList<AuthoringInstrumentAdapter> InstrumentTypeChoices => AuthoringInstrumentCatalog.All;

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

    private string? NewInstrumentCreationIssue()
    {
        if (!AuthoringInstrumentCatalog.TryGet(NewInstrumentTypeId, out var adapter))
            return "Choose a registered instrument type before adding the slot.";
        if (adapter.TypeId == AuthoringVisaInstrumentAdapter.InstrumentType.FullName)
        {
            if (Workspace is not null && !AuthoringInstrumentCatalog.DeclaresVisa(Workspace))
                return "Declare the HardwareTest VISA workspace dependency before adding a VISA DMM.";
            if (string.IsNullOrWhiteSpace(NewInstrumentVisa)) return "Enter the VISA address for the selected VISA DMM.";
        }
        try { return adapter.Availability(InstrumentCreationHome).Reason; }
        catch (ArgumentException) { return "Correct the selected OpenTAP home path before adding an instrument."; }
    }
}
