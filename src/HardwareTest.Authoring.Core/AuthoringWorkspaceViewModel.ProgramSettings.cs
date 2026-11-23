using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private string? _selectedInstrumentSlot;
    private int _formulaSaveGeneration;
    private int _formulaSaveStamp = -1;
    private IReadOnlyList<string> _metricFunctionIds = [];

    public IReadOnlyList<string> DisplayRoleOptions => AuthoringEditorCatalog.DisplayRoles;

    public IReadOnlyList<string> YUnitOptions
    {
        get
        {
            var current = AuthoringWorkspaceCatalog.YUnitOptions(SelectedProgram);
            if (!field.SequenceEqual(current, StringComparer.Ordinal))
            {
                field = current;
            }

            return field;
        }
    } = [];

    public IReadOnlyList<string> TfMethodOptions => AuthoringEditorCatalog.TfMethods;

    public IReadOnlyList<string> ReportKindOptions
        => AuthoringWorkspaceCatalog.ReportKindOptions(Workspace?.Manifest, Programs, SelectedProgram);

    public IReadOnlyList<AuthoringCatalogToggle> ReportKindChoices
        => ReportKindOptions
            .Select(kind => new AuthoringCatalogToggle(kind, HasReportKind(kind), CanRemoveCatalogItem(kind, AuthoringWorkspaceCatalog.IsProtectedReportKind)))
            .ToArray();

    public IReadOnlyList<string> IncludedReportKinds
        => SelectedProgram?.Sidecar.ReportKinds is { Length: > 0 } kinds
            ? kinds
            : ["status"];

    public IReadOnlyList<string> ProgramKindOptions
        => AuthoringWorkspaceCatalog.ProgramKindOptions(Workspace?.Manifest, Programs, SelectedProgram);

    public IReadOnlyList<AuthoringCatalogToggle> ProgramKindChoices
        => ProgramKindOptions
            .Select(kind => new AuthoringCatalogToggle(
                kind,
                string.Equals(ProgramKind, kind, StringComparison.OrdinalIgnoreCase),
                CanRemoveCatalogItem(kind, AuthoringWorkspaceCatalog.IsProtectedProgramKind)))
            .ToArray();

    public IReadOnlyList<string> RequiredFieldOptions
        => AuthoringWorkspaceCatalog.RequiredFieldOptions(Workspace?.Manifest, Programs, SelectedProgram);

    public IReadOnlyList<AuthoringCatalogToggle> RequiredFieldChoices
        => RequiredFieldOptions
            .Select(id => new AuthoringCatalogToggle(id, HasRequiredField(id), CanRemoveCatalogItem(id, AuthoringWorkspaceCatalog.IsProtectedRequiredField)))
            .ToArray();

    public IReadOnlyList<string> StationHealthGateOptions => AuthoringEditorCatalog.StationHealthGates;

    public IReadOnlyList<string> ChannelKeys => AuthoringEditorCatalog.ChannelKeys(SelectedProgram);

    public IReadOnlyList<string> InstrumentSlots { get; private set; } = [];

    public IReadOnlyList<InstrumentRef> Instruments
        => SelectedProgram?.Instruments ?? [];

    public string SelectedInstrumentSlot
    {
        get => Instruments.Any(instrument =>
                   string.Equals(instrument.SlotName, _selectedInstrumentSlot, StringComparison.OrdinalIgnoreCase))
               ? _selectedInstrumentSlot!
               : SelectedProgram?.Instruments.FirstOrDefault()?.SlotName
                 ?? string.Empty;
        set
        {
            var slot = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(slot)
                || string.Equals(SelectedInstrumentSlot, slot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (SetField(ref _selectedInstrumentSlot, slot))
            {
                RaiseHardwareProperties();
                RaiseSequenceOperations();
                OnPropertyChanged(nameof(SelectedInstrumentVisa));
                OnPropertyChanged(nameof(SelectedInstrument));
                OnPropertyChanged(nameof(CanRemoveSelectedInstrumentSlot));
                OnPropertyChanged(nameof(InstrumentRemovalGuardText));
            }
        }
    }

    public string SelectedInstrumentVisa
    {
        get => Instruments.FirstOrDefault(instrument =>
                   string.Equals(instrument.SlotName, SelectedInstrumentSlot, StringComparison.OrdinalIgnoreCase))
               ?.VisaAddress
               ?? VisaAddress;
        set
        {
            if (string.Equals(SelectedInstrumentVisa, value, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedInstrumentSlot))
            {
                VisaAddress = value;
                return;
            }

            SetInstrumentVisa(SelectedInstrumentSlot, value);
            OnPropertyChanged();
        }
    }

    public string NewReportKind
    {
        get;
        set => SetField(ref field, value ?? string.Empty);
    } = string.Empty;

    public string NewProgramKind
    {
        get;
        set => SetField(ref field, value ?? string.Empty);
    } = string.Empty;

    public string NewInstrumentSlot
    {
        get;
        set
        {
            if (SetField(ref field, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(NewInstrumentAvailabilityText));
                OnPropertyChanged(nameof(CanAddInstrumentSlot));
            }
        }
    } = string.Empty;

    public string NewInstrumentVisa
    {
        get;
        set
        {
            if (!SetField(ref field, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(NewInstrumentAvailabilityText));
            OnPropertyChanged(nameof(CanAddInstrumentSlot));
        }
    } = string.Empty;

    public string NewRequiredField
    {
        get;
        set => SetField(ref field, value ?? string.Empty);
    } = string.Empty;

    public bool CanAddInstrumentSlot
        => Workspace is not null
           && !Workspace.IsReadOnly
           && SelectedProgram is not null
           && AuthoringWorkspaceCatalog.Normalize(NewInstrumentSlot) is { } slot
           && !InstrumentSlots.Any(existing => string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase))
           && NewInstrumentCreationIssue() is null;

    public IReadOnlyList<FormulaCatalog.Item> FormulaCompletions
        => FormulaCatalog.Completions(ChannelKeys);

    public bool HasFormula
        => SelectedSequence?.Kind == SequenceRowKind.Metric
           && SelectedMetric?.Source is ExpressionAlgorithm;

    public bool HasTransferFunction
        => SelectedSequence?.Kind == SequenceRowKind.Metric
           && SelectedMetric?.Source is TransferFunctionAlgorithm;

    public bool HasRawStep
        => SelectedSequence?.Kind == SequenceRowKind.Raw
           && SelectedMeasure is RawStepNode;

    private void RaiseSidecarProperties()
    {
        RaiseHardwareProperties();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DutFamily));
        OnPropertyChanged(nameof(RequireSerial));
        OnPropertyChanged(nameof(RequirePartNumber));
        OnPropertyChanged(nameof(RequireRevision));
        OnPropertyChanged(nameof(RequireOperator));
        OnPropertyChanged(nameof(RequiredFieldOptions));
        OnPropertyChanged(nameof(RequiredFieldChoices));
        OnPropertyChanged(nameof(SelectionIncludesCleanup));
        OnPropertyChanged(nameof(ReportStatus));
        OnPropertyChanged(nameof(ReportCertification));
        OnPropertyChanged(nameof(ReportKindOptions));
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(DefaultReportKind));
        OnPropertyChanged(nameof(ProgramKind));
        OnPropertyChanged(nameof(ProgramKindOptions));
        OnPropertyChanged(nameof(ProgramKindChoices));
        OnPropertyChanged(nameof(RequireStationHealth));
        OnPropertyChanged(nameof(StationHealthGate));
        OnPropertyChanged(nameof(StationHealthMaxAgeHours));
        OnPropertyChanged(nameof(StationHealthProfileId));
        OnPropertyChanged(nameof(Instruments));
        RefreshInstrumentSlots();
        OnPropertyChanged(nameof(SelectedInstrumentSlot));
        OnPropertyChanged(nameof(SelectedInstrumentVisa));
        OnPropertyChanged(nameof(SelectedInstrument));
        OnPropertyChanged(nameof(CanRemoveSelectedInstrumentSlot));
        OnPropertyChanged(nameof(InstrumentRemovalGuardText));
        OnPropertyChanged(nameof(CanEditProgramSettings));
        OnPropertyChanged(nameof(HasImportedProcedure));
        RaiseEditorProperties();
    }

}
