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

    public IReadOnlyList<string> InstrumentSlots
        => SelectedProgram?.Instruments.Select(instrument => instrument.SlotName).ToArray() ?? [];

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
            if (SetField(ref _selectedInstrumentSlot, value))
            {
                OnPropertyChanged(nameof(SelectedInstrumentVisa));
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
        set => SetSidecar(s => { s.RequirePartNumber = value; });
    }

    public bool RequireRevision
    {
        get => SelectedProgram?.Sidecar.RequireRevision ?? false;
        set => SetSidecar(s => { s.RequireRevision = value; });
    }

    public bool RequireOperator
    {
        get => SelectedProgram?.Sidecar.RequireOperator ?? false;
        set => SetSidecar(s => { s.RequireOperator = value; });
    }

    public bool SelectionIncludesCleanup
    {
        get => SelectedProgram?.Sidecar.SelectionIncludesCleanup ?? true;
        set => SetSidecar(s => { s.SelectionIncludesCleanup = value; });
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
        set => SetSidecar(s => { s.DefaultReportKind = value; });
    }

    public string ProgramKind
    {
        get => SelectedProgram?.Sidecar.ProgramKind ?? "dut";
        set => SetSidecar(s => { s.ProgramKind = value; });
    }

    public bool RequireStationHealth
    {
        get => SelectedProgram?.Sidecar.RequireStationHealth ?? false;
        set => SetSidecar(s => { s.RequireStationHealth = value; });
    }

    public string StationHealthGate
    {
        get => SelectedProgram?.Sidecar.StationHealthGate ?? "warn";
        set => SetSidecar(s => { s.StationHealthGate = value; });
    }

    public string StationHealthMaxAgeHours
    {
        get => FormatOptional(SelectedProgram?.Sidecar.StationHealthMaxAgeHours);
        set
        {
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
        set => SetSidecar(s => { s.StationHealthProfileId = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim(); });
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
            var slot = value.Trim();
            if (string.IsNullOrWhiteSpace(slot))
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
            if (!HasCleanupEditor || SelectedProgram is null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            ReplaceSelected(SelectedProgram with
            {
                Cleanup = SelectedProgram.Cleanup with { InstrumentSlot = value.Trim() },
            });
        }
    }

    public bool IncludeSafeShutdown
    {
        get => HasCleanupEditor && (SelectedProgram?.Cleanup.IncludeSafeShutdown ?? false);
        set
        {
            if (!HasCleanupEditor || SelectedProgram is null)
            {
                return;
            }

            ReplaceSelected(SelectedProgram with
            {
                Cleanup = SelectedProgram.Cleanup with { IncludeSafeShutdown = value },
            });
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

            var slot = value.Trim();
            if (string.IsNullOrWhiteSpace(slot))
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

        var updated = SelectedProgram.Instruments
            .Select(instrument =>
                string.Equals(instrument.SlotName, slotName, StringComparison.OrdinalIgnoreCase)
                    ? instrument with { VisaAddress = visaAddress }
                    : instrument)
            .ToArray();
        ReplaceSelected(SelectedProgram with { Instruments = updated });
    }

    public void InsertFormulaToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !HasFormula)
        {
            return;
        }

        var current = FormulaSource;
        FormulaSource = string.IsNullOrWhiteSpace(current) ? token : current + token;
    }

    private bool HasReportKind(string kind)
        => SelectedProgram?.Sidecar.ReportKinds?.Contains(kind, StringComparer.OrdinalIgnoreCase) == true
           || (kind == "status" && SelectedProgram?.Sidecar.ReportKinds is null);

    private void SetReportKind(string kind, bool include)
    {
        if (SelectedProgram is null)
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

        setup[index] = mutate(setup[index]);
        ReplaceSelected(SelectedProgram with { Setup = setup });
    }

    private static string FormatOptional(double? value)
        => value is { } number
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
