using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private string? _selectedInstrumentSlot;

    public IReadOnlyList<string> DisplayRoleOptions => AuthoringEditorCatalog.DisplayRoles;

    public IReadOnlyList<string> YUnitOptions => AuthoringEditorCatalog.YUnits;

    public IReadOnlyList<string> TfMethodOptions => AuthoringEditorCatalog.TfMethods;

    public IReadOnlyList<string> ReportKindOptions => AuthoringEditorCatalog.ReportKinds;

    public IReadOnlyList<string> ProgramKindOptions => AuthoringEditorCatalog.ProgramKinds;

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

    public bool RequirePartNumber
    {
        get => SelectedProgram?.Sidecar.RequirePartNumber ?? false;
        set => SetSidecarIfUnchanged(RequirePartNumber, value, s => { s.RequirePartNumber = value; });
    }

    public bool RequireRevision
    {
        get => SelectedProgram?.Sidecar.RequireRevision ?? false;
        set => SetSidecarIfUnchanged(RequireRevision, value, s => { s.RequireRevision = value; });
    }

    public bool RequireOperator
    {
        get => SelectedProgram?.Sidecar.RequireOperator ?? false;
        set => SetSidecarIfUnchanged(RequireOperator, value, s => { s.RequireOperator = value; });
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
        set => SetSidecarIfUnchanged(DefaultReportKind, value, s => { s.DefaultReportKind = value; });
    }

    public string ProgramKind
    {
        get => SelectedProgram?.Sidecar.ProgramKind ?? "dut";
        set => SetSidecarIfUnchanged(ProgramKind, value, s => { s.ProgramKind = value; });
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
                Cleanup = SelectedProgram.Cleanup with { InstrumentSlot = slot },
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
