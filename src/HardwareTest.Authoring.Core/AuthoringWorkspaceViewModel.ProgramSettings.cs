using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private string? _selectedInstrumentSlot;
    private string _newReportKind = string.Empty;
    private string _newProgramKind = string.Empty;
    private string _newInstrumentSlot = string.Empty;
    private string _newInstrumentVisa = string.Empty;
    private string _newRequiredField = string.Empty;

    public IReadOnlyList<string> DisplayRoleOptions => AuthoringEditorCatalog.DisplayRoles;

    public IReadOnlyList<string> YUnitOptions => AuthoringWorkspaceCatalog.YUnitOptions(SelectedProgram);

    public IReadOnlyList<string> TfMethodOptions => AuthoringEditorCatalog.TfMethods;

    public IReadOnlyList<string> ReportKindOptions
        => AuthoringWorkspaceCatalog.ReportKindOptions(Workspace?.Manifest, Programs, SelectedProgram);

    public IReadOnlyList<AuthoringCatalogToggle> ReportKindChoices
        => ReportKindOptions.Select(kind => new AuthoringCatalogToggle(kind, HasReportKind(kind))).ToArray();

    public IReadOnlyList<string> IncludedReportKinds
        => SelectedProgram?.Sidecar.ReportKinds is { Length: > 0 } kinds
            ? kinds
            : ["status"];

    public IReadOnlyList<string> ProgramKindOptions
        => AuthoringWorkspaceCatalog.ProgramKindOptions(Workspace?.Manifest, Programs, SelectedProgram);

    public IReadOnlyList<string> RequiredFieldOptions
        => AuthoringWorkspaceCatalog.RequiredFieldOptions(Workspace?.Manifest, Programs, SelectedProgram);

    public IReadOnlyList<AuthoringCatalogToggle> RequiredFieldChoices
        => RequiredFieldOptions.Select(id => new AuthoringCatalogToggle(id, HasRequiredField(id))).ToArray();

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
                OnPropertyChanged(nameof(SelectedInstrumentVisa));
                OnPropertyChanged(nameof(SelectedInstrument));
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
        get => _newReportKind;
        set => SetField(ref _newReportKind, value ?? string.Empty);
    }

    public string NewProgramKind
    {
        get => _newProgramKind;
        set => SetField(ref _newProgramKind, value ?? string.Empty);
    }

    public string NewInstrumentSlot
    {
        get => _newInstrumentSlot;
        set
        {
            if (SetField(ref _newInstrumentSlot, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanAddInstrumentSlot));
            }
        }
    }

    public string NewInstrumentVisa
    {
        get => _newInstrumentVisa;
        set => SetField(ref _newInstrumentVisa, value ?? string.Empty);
    }

    public string NewRequiredField
    {
        get => _newRequiredField;
        set => SetField(ref _newRequiredField, value ?? string.Empty);
    }

    public bool CanAddInstrumentSlot
        => Workspace is not null
           && !Workspace.IsReadOnly
           && SelectedProgram is not null
           && AuthoringWorkspaceCatalog.Normalize(NewInstrumentSlot) is { } slot
           && !InstrumentSlots.Any(existing => string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase));

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

    public bool HasMetricPresentation
        => SelectedSequence?.Kind == SequenceRowKind.Metric && SelectedMetric is not null;

    public bool HasRepeatEditor => SelectedSequence?.Kind == SequenceRowKind.Repeat;

    public bool HasSetupEditor => SelectedSequence?.Kind == SequenceRowKind.Setup;

    public bool HasPromptSetup => SelectedSetup is OperatorPromptSetup;

    public bool HasInputSetup => SelectedSetup is OperatorInputSetup;

    public bool HasIdentitySetup => SelectedSetup is IdentitySetup;

    public bool HasCleanupEditor => SelectedSequence?.Kind == SequenceRowKind.Cleanup;

    public bool ShowThreshold
        => HasMetricPresentation
           && string.Equals(DisplayRole, PresentationRoles.Scalar, StringComparison.OrdinalIgnoreCase);

    public bool ShowBandLimits
        => HasMetricPresentation
           && string.Equals(DisplayRole, PresentationRoles.Passband, StringComparison.OrdinalIgnoreCase);

    public string FormulaHelp =>
        "MATLAB-flavored subset, not MATLAB. Save plan only lowers mean(x)+threshold → Mean GTE, or top-level filter/filtfilt → transfer function.";

    public string FormulaSaveNote
        => HasFormula && SelectedMetric?.Source is ExpressionAlgorithm expr
            ? FormulaLowerer.DescribeSave(expr.Source, SelectedMetric.Limits)
            : string.Empty;

    public string TransferFunctionHelp =>
        "Discrete SISO IIR. filtfilt is zero-phase on the whole series; filter is causal. Needs uniform elapsedMs.";

    public string SidecarHelp =>
        "Sidecar is session/DUT/Typst only. Instrument requirements stay on the TapPackage, not here.";

    public bool RequireSerial
    {
        get => HasRequiredField(RequiredFieldIds.Serial);
        set => SetRequiredFieldIncluded(RequiredFieldIds.Serial, value);
    }

    public bool RequirePartNumber
    {
        get => HasRequiredField(RequiredFieldIds.PartNumber);
        set => SetRequiredFieldIncluded(RequiredFieldIds.PartNumber, value);
    }

    public bool RequireRevision
    {
        get => HasRequiredField(RequiredFieldIds.Revision);
        set => SetRequiredFieldIncluded(RequiredFieldIds.Revision, value);
    }

    public bool RequireOperator
    {
        get => HasRequiredField(RequiredFieldIds.Operator);
        set => SetRequiredFieldIncluded(RequiredFieldIds.Operator, value);
    }

    public bool SelectionIncludesCleanup
    {
        get => SelectedProgram?.Sidecar.SelectionIncludesCleanup ?? true;
        set => SetSidecarIfUnchanged(SelectionIncludesCleanup, value, s => { s.SelectionIncludesCleanup = value; });
    }

    public bool ReportStatus
    {
        get => HasReportKind("status");
        set => SetReportKind("status", value);
    }

    public bool ReportCertification
    {
        get => HasReportKind("certification");
        set => SetReportKind("certification", value);
    }

    public string DefaultReportKind
    {
        get => SelectedProgram?.Sidecar.DefaultReportKind ?? "status";
        set
        {
            var kind = AuthoringWorkspaceCatalog.Normalize(value);
            if (kind is null
                || !IncludedReportKinds.Any(existing =>
                    string.Equals(existing, kind, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            SetSidecarIfUnchanged(DefaultReportKind, kind, s => { s.DefaultReportKind = kind; });
        }
    }

    public string ProgramKind
    {
        get => SelectedProgram?.Sidecar.ProgramKind ?? "dut";
        set
        {
            var kind = AuthoringWorkspaceCatalog.Normalize(value);
            if (kind is null)
            {
                return;
            }

            SetSidecarIfUnchanged(ProgramKind, kind, s => { s.ProgramKind = kind; });
        }
    }

    public bool RequireStationHealth
    {
        get => SelectedProgram?.Sidecar.RequireStationHealth ?? false;
        set => SetSidecarIfUnchanged(RequireStationHealth, value, s => { s.RequireStationHealth = value; });
    }

    public string StationHealthGate
    {
        get => SelectedProgram?.Sidecar.StationHealthGate ?? "warn";
        set => SetSidecarIfUnchanged(StationHealthGate, value, s => { s.StationHealthGate = value; });
    }

    public string StationHealthMaxAgeHours
    {
        get => FormatOptional(SelectedProgram?.Sidecar.StationHealthMaxAgeHours);
        set
        {
            if (string.Equals(StationHealthMaxAgeHours, value, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                SetSidecar(s => { s.StationHealthMaxAgeHours = null; });
                return;
            }

            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours)
                && hours > 0)
            {
                SetSidecar(s => { s.StationHealthMaxAgeHours = hours; });
            }
        }
    }

    public string StationHealthProfileId
    {
        get => SelectedProgram?.Sidecar.StationHealthProfileId ?? "default";
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
            SetSidecarIfUnchanged(StationHealthProfileId, next, s => { s.StationHealthProfileId = next; });
        }
    }

    public string PromptMessage
    {
        get => SelectedSetup is OperatorPromptSetup prompt ? prompt.Message : string.Empty;
        set => UpdateSelectedSetup(action => action is OperatorPromptSetup prompt
            ? prompt with { Message = value }
            : action);
    }

    public string InputTitle
    {
        get => SelectedSetup is OperatorInputSetup input ? input.Title : string.Empty;
        set => UpdateSelectedSetup(action => action is OperatorInputSetup input
            ? input with { Title = value }
            : action);
    }

    public string InputMessage
    {
        get => SelectedSetup is OperatorInputSetup input ? input.Message : string.Empty;
        set => UpdateSelectedSetup(action => action is OperatorInputSetup input
            ? input with { Message = value }
            : action);
    }

    public string InputStringFieldId
    {
        get => SelectedSetup is OperatorInputSetup input ? input.StringFieldId ?? string.Empty : string.Empty;
        set => UpdateSelectedSetup(action => action is OperatorInputSetup input
            ? input with { StringFieldId = string.IsNullOrWhiteSpace(value) ? null : value.Trim() }
            : action);
    }

    public string InputNumberFieldId
    {
        get => SelectedSetup is OperatorInputSetup input ? input.NumberFieldId ?? string.Empty : string.Empty;
        set => UpdateSelectedSetup(action => action is OperatorInputSetup input
            ? input with { NumberFieldId = string.IsNullOrWhiteSpace(value) ? null : value.Trim() }
            : action);
    }

    public string SetupInstrumentSlot
    {
        get => SelectedSetup is IdentitySetup identity ? identity.InstrumentSlot : string.Empty;
        set
        {
            var slot = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(slot)
                || string.Equals(SetupInstrumentSlot, slot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UpdateSelectedSetup(action => action is IdentitySetup identity
                ? identity with { InstrumentSlot = slot }
                : action);
        }
    }

    public string CleanupInstrumentSlot
    {
        get => HasCleanupEditor ? SelectedProgram?.Cleanup.InstrumentSlot ?? string.Empty : string.Empty;
        set
        {
            var slot = value?.Trim() ?? string.Empty;
            if (!HasCleanupEditor
                || SelectedProgram is null
                || string.IsNullOrWhiteSpace(slot)
                || string.Equals(CleanupInstrumentSlot, slot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ReplaceSelected(SelectedProgram with
            {
                Cleanup = SelectedProgram.Cleanup with { InstrumentSlots = [slot] },
            }, rebuildLists: false);
        }
    }

    public IReadOnlyList<AuthoringCatalogToggle> CleanupSlotChoices
        => InstrumentSlots.Select(slot => new AuthoringCatalogToggle(slot, HasCleanupSlot(slot))).ToArray();

    public bool IncludeMeasureSlots
    {
        get => HasCleanupEditor && (SelectedProgram?.Cleanup.IncludeMeasureSlots ?? false);
        set
        {
            if (!HasCleanupEditor || SelectedProgram is null || IncludeMeasureSlots == value)
            {
                return;
            }

            SelectedProgram.Sidecar.IncludeMeasureSlots = value ? true : null;
            ReplaceSelected(SelectedProgram with
            {
                Cleanup = SelectedProgram.Cleanup with { IncludeMeasureSlots = value },
            }, rebuildLists: false);
        }
    }

    public bool IncludeSafeShutdown
    {
        get => HasCleanupEditor && (SelectedProgram?.Cleanup.IncludeSafeShutdown ?? false);
        set
        {
            if (!HasCleanupEditor || SelectedProgram is null || IncludeSafeShutdown == value)
            {
                return;
            }

            ReplaceSelected(SelectedProgram with
            {
                Cleanup = SelectedProgram.Cleanup with { IncludeSafeShutdown = value },
            }, rebuildLists: false);
        }
    }

    public string MetricInstrumentSlot
    {
        get => HasMetricPresentation && SelectedMetric?.Source is MeasureSource measure
            ? measure.InstrumentSlot
            : string.Empty;
        set
        {
            if (!HasMetricPresentation)
            {
                return;
            }

            var slot = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(slot)
                || string.Equals(MetricInstrumentSlot, slot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UpdateSelectedMetric(metric => metric.Source is MeasureSource measure
                ? metric with { Source = measure with { InstrumentSlot = slot } }
                : metric);
        }
    }

    public void SetInstrumentVisa(string slotName, string visaAddress)
    {
        if (SelectedProgram is null || string.IsNullOrWhiteSpace(slotName))
        {
            return;
        }

        var current = Instruments.FirstOrDefault(instrument =>
            string.Equals(instrument.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
        if (current is not null && string.Equals(current.VisaAddress, visaAddress, StringComparison.Ordinal))
        {
            return;
        }

        var updated = SelectedProgram.Instruments
            .Select(instrument =>
                string.Equals(instrument.SlotName, slotName, StringComparison.OrdinalIgnoreCase)
                    ? instrument with { VisaAddress = visaAddress }
                    : instrument)
            .ToArray();
        ReplaceSelected(SelectedProgram with { Instruments = updated });
    }

    public void InsertFormulaToken(string token)
        => ApplyFormulaCompletion(token, FormulaSource.Length);

    public IReadOnlyList<FormulaCatalog.Item> CompletionsAt(int caret)
        => HasFormula ? FormulaCatalog.CompletionsFor(FormulaCatalog.IdentAt(FormulaSource, caret).Text, ChannelKeys) : [];

    public int ApplyFormulaCompletion(string insertText, int caret)
    {
        if (!HasFormula || string.IsNullOrWhiteSpace(insertText))
        {
            return caret;
        }

        var source = FormulaSource;
        var span = FormulaCatalog.IdentAt(source, caret);
        FormulaSource = source.Remove(span.Start, span.Length).Insert(span.Start, insertText);
        return span.Start + insertText.Length;
    }

    public void SetReportKindIncluded(string kind, bool include)
        => SetReportKind(kind, include);

    public void AddReportKind()
    {
        var kind = AuthoringWorkspaceCatalog.Normalize(NewReportKind);
        if (kind is null)
        {
            return;
        }

        EnsureWritableWorkspace("add a report kind");
        RememberWorkspaceCatalog(
            catalogs => RememberUnlessDefault(catalogs.ReportKinds, kind, AuthoringWorkspaceCatalog.DefaultReportKinds));
        SetReportKind(kind, include: true);
        NewReportKind = string.Empty;
        OnPropertyChanged(nameof(ReportKindOptions));
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(DefaultReportKind));
    }

    public void AddProgramKind()
    {
        var kind = AuthoringWorkspaceCatalog.Normalize(NewProgramKind);
        if (kind is null)
        {
            return;
        }

        EnsureWritableWorkspace("add a program kind");
        RememberWorkspaceCatalog(
            catalogs => RememberUnlessDefault(catalogs.ProgramKinds, kind, AuthoringWorkspaceCatalog.DefaultProgramKinds));
        ProgramKind = kind;
        NewProgramKind = string.Empty;
        OnPropertyChanged(nameof(ProgramKindOptions));
    }

    public void AddInstrumentSlot()
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before adding an instrument slot.");
        }

        if (Workspace.IsReadOnly)
        {
            throw new AuthoringWorkspaceException("Workspace is read-only; cannot add an instrument slot.");
        }

        if (SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Select a program before adding an instrument slot.");
        }

        var slot = AuthoringWorkspaceCatalog.Normalize(NewInstrumentSlot);
        if (slot is null)
        {
            throw new AuthoringWorkspaceException("Instrument slot name is required.");
        }

        if (SelectedProgram.Instruments.Any(instrument =>
                string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AuthoringWorkspaceException($"Instrument slot '{slot}' already exists.");
        }

        var typeId = SelectedProgram.Instruments.FirstOrDefault()?.TypeId
                     ?? typeof(HardwareTest.OpenTap.Plugins.Basic.MockDmmInstrument).FullName!;
        var visa = AuthoringWorkspaceCatalog.Normalize(NewInstrumentVisa)
                   ?? $"MOCK::INSTR{SelectedProgram.Instruments.Count}";
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Remember(catalogs.InstrumentSlotNames, slot));
        ReplaceSelected(SelectedProgram with
        {
            Instruments = [.. SelectedProgram.Instruments, new InstrumentRef(slot, typeId, visa)],
        });
        SelectedInstrumentSlot = slot;
        NewInstrumentSlot = string.Empty;
        NewInstrumentVisa = string.Empty;
        Status = $"Added slot {slot}";
        Error = null;
    }

    public void SetCleanupSlotIncluded(string slotName, bool include)
    {
        if (!HasCleanupEditor || SelectedProgram is null)
        {
            return;
        }

        var slot = AuthoringWorkspaceCatalog.Normalize(slotName);
        if (slot is null || HasCleanupSlot(slot) == include)
        {
            return;
        }

        var current = SelectedProgram.Cleanup.InstrumentSlots.ToList();
        if (include)
        {
            current.Add(slot);
        }
        else
        {
            current.RemoveAll(existing => string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceSelected(SelectedProgram with
        {
            Cleanup = SelectedProgram.Cleanup with { InstrumentSlots = current },
        }, rebuildLists: false);
    }

    private bool HasCleanupSlot(string slot)
        => SelectedProgram is not null
           && SelectedProgram.Cleanup.InstrumentSlots.Any(existing =>
               string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase));

    public void SetRequiredFieldIncluded(string fieldId, bool include)
    {
        if (SelectedProgram is null)
        {
            return;
        }

        var id = AuthoringWorkspaceCatalog.Normalize(fieldId);
        if (id is null || HasRequiredField(id) == include)
        {
            return;
        }

        var current = RequiredFieldIds.FromSidecar(SelectedProgram.Sidecar).ToList();
        if (include)
        {
            current.Add(id);
        }
        else
        {
            current.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        }

        RequiredFieldIds.Apply(SelectedProgram.Sidecar, current);
        RaiseSidecarProperties();
    }

    public void AddRequiredField()
    {
        var id = AuthoringWorkspaceCatalog.Normalize(NewRequiredField);
        if (id is null)
        {
            return;
        }

        EnsureWritableWorkspace("add a required field");
        RememberWorkspaceCatalog(
            catalogs => RememberUnlessDefault(catalogs.RequiredFields, id, RequiredFieldIds.Known));
        SetRequiredFieldIncluded(id, include: true);
        NewRequiredField = string.Empty;
        OnPropertyChanged(nameof(RequiredFieldOptions));
        OnPropertyChanged(nameof(RequiredFieldChoices));
    }

    private bool HasRequiredField(string fieldId)
        => SelectedProgram is not null
           && RequiredFieldIds.Contains(RequiredFieldIds.FromSidecar(SelectedProgram.Sidecar), fieldId);

    private bool HasReportKind(string kind)
        => SelectedProgram?.Sidecar.ReportKinds?.Contains(kind, StringComparer.OrdinalIgnoreCase) == true
           || (kind == "status" && SelectedProgram?.Sidecar.ReportKinds is null);

    private void SetReportKind(string kind, bool include)
    {
        if (SelectedProgram is null || HasReportKind(kind) == include)
        {
            return;
        }

        var current = SelectedProgram.Sidecar.ReportKinds?.ToList() ?? ["status"];
        if (include && !current.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            current.Add(kind);
        }
        else if (!include)
        {
            current.RemoveAll(existing => string.Equals(existing, kind, StringComparison.OrdinalIgnoreCase));
        }

        if (current.Count == 0)
        {
            current.Add("status");
        }

        SetSidecar(s =>
        {
            s.ReportKinds = [.. current];
            if (!current.Contains(s.DefaultReportKind ?? "status", StringComparer.OrdinalIgnoreCase))
            {
                s.DefaultReportKind = current[0];
            }
        });
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(DefaultReportKind));
    }

    internal void RefreshInstrumentSlots()
    {
        var next = SelectedProgram?.Instruments.Select(instrument => instrument.SlotName).ToArray() ?? [];
        if (InstrumentSlots.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        InstrumentSlots = next;
        OnPropertyChanged(nameof(InstrumentSlots));
        OnPropertyChanged(nameof(CanAddInstrumentSlot));
    }

    private void SetSidecarIfUnchanged<T>(T current, T next, Action<ProgramSidecar> mutate)
    {
        if (EqualityComparer<T>.Default.Equals(current, next))
        {
            return;
        }

        SetSidecar(mutate);
    }

    private void SetSidecar(Action<ProgramSidecar> mutate)
    {
        if (SelectedProgram is null)
        {
            return;
        }

        mutate(SelectedProgram.Sidecar);
        RaiseSidecarProperties();
    }

    private void EnsureWritableWorkspace(string action)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException($"Open a workspace before {action}.");
        }

        if (Workspace.IsReadOnly)
        {
            throw new AuthoringWorkspaceException($"Workspace is read-only; cannot {action}.");
        }
    }

    private static void RememberUnlessDefault(
        List<string> items,
        string token,
        IReadOnlyList<string> defaults)
    {
        if (AuthoringWorkspaceCatalog.Contains(defaults, token))
        {
            return;
        }

        AuthoringWorkspaceCatalog.Remember(items, token);
    }

    private void RememberWorkspaceCatalog(Action<AuthoringWorkspaceCatalogs> mutate)
    {
        if (Workspace is null || Workspace.IsReadOnly)
        {
            return;
        }

        var catalogs = Workspace.Manifest.Catalogs ??= new AuthoringWorkspaceCatalogs();
        mutate(catalogs);
        AuthoringWorkspaceLoader.SaveManifest(Workspace.Root, Workspace.Manifest);
        OnPropertyChanged(nameof(ReportKindOptions));
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(RequiredFieldOptions));
        OnPropertyChanged(nameof(RequiredFieldChoices));
        OnPropertyChanged(nameof(ProgramKindOptions));
    }

    private void UpdateSelectedSetup(Func<SetupAction, SetupAction> mutate)
    {
        if (SelectedProgram is null || SelectedSequence is not { Section: SequenceSection.Setup } row
            || row.IndexPath.Count != 1)
        {
            return;
        }

        var setup = SelectedProgram.Setup.ToArray();
        var index = row.IndexPath[0];
        if (index < 0 || index >= setup.Length)
        {
            return;
        }

        var next = mutate(setup[index]);
        if (Equals(next, setup[index]))
        {
            return;
        }

        setup[index] = next;
        ReplaceSelected(SelectedProgram with { Setup = setup }, rebuildLists: false);
    }

    private static string FormatOptional(double? value)
        => value is { } number
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
