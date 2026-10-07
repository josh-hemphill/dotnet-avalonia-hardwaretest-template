using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
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
           && AuthoringCriteria.Requirements(SelectedMetric!)?.RequiresThreshold == true;

    public bool ShowBandLimits
        => HasMetricPresentation
           && AuthoringCriteria.Requirements(SelectedMetric!)?.RequiresBand == true;

    public string FormulaHelp =>
        "MATLAB-flavored subset for preview. The save plan line shows what packs into the test plan.";

    public string FormulaSaveNote => CurrentFormulaSaveOutcome.Message;

    public FormulaSaveOutcomeKind FormulaSaveOutcomeKind => CurrentFormulaSaveOutcome.Kind;

    internal void InvalidateFormulaSave()
        => _formulaSaveGeneration++;

    private FormulaSaveOutcome CurrentFormulaSaveOutcome
    {
        get
        {
            if (_formulaSaveStamp == _formulaSaveGeneration)
            {
                return _formulaSave;
            }

            _formulaSaveStamp = _formulaSaveGeneration;
            if (!HasFormula
                || SelectedMetric?.Source is not ExpressionAlgorithm expr
                || !string.IsNullOrEmpty(FormulaError))
            {
                _formulaSave = new FormulaSaveOutcome(FormulaSaveOutcomeKind.None, string.Empty);
                return _formulaSave;
            }

            _formulaSave = FormulaLowerer.DescribeSaveOutcome(expr.Source, SelectedMetric.Limits);
            return _formulaSave;
        }
    }
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
        get => SelectedProgram is { } program
            ? NumericFieldText(program.Cleanup.NodeId, nameof(StationHealthMaxAgeHours),
                FormatOptional(program.Sidecar.StationHealthMaxAgeHours)) : string.Empty;
        set => SetStationHealthAgeText(value);
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

            var tail = SelectedProgram.Cleanup.InstrumentSlots
                .Skip(1)
                .Where(existing => !string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase));
            IReadOnlyList<string> slots = [slot, .. tail];
            var next = SelectedProgram.Cleanup with { InstrumentSlots = slots };
            UpdateCleanupPolicy(next);
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

            var next = SelectedProgram.Cleanup with { IncludeMeasureSlots = value };
            UpdateCleanupPolicy(next);
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

            var next = SelectedProgram.Cleanup with { IncludeSafeShutdown = value };
            UpdateCleanupPolicy(next);
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

    public bool HasStepSettings
        => HasMetricPresentation
           && SelectedMetric?.Source is MeasureSource or AlgorithmSource;

    public IReadOnlyList<string> MetricFunctionIdOptions
    {
        get
        {
            if (!HasStepSettings)
            {
                return [];
            }

            var wantAlgorithm = SelectedMetric?.Source is AlgorithmSource;
            var ids = AuthoringFunctionCatalog.All
                .Where(spec => spec.IsAlgorithm == wantAlgorithm
                               && spec.Id != AuthoringFunctionIds.BasicApplyTransferFunction)
                .Select(spec => spec.Id)
                .ToList();
            var current = MetricFunctionId;
            if (!string.IsNullOrWhiteSpace(current)
                && !ids.Contains(current, StringComparer.Ordinal))
            {
                ids.Insert(0, current);
            }

            return ids;
        }
    }

    public IReadOnlyList<AuthoringFunctionDisplay> MetricFunctionChoices
    {
        get
        {
            var ids = MetricFunctionIdOptions;
            if (!_metricFunctionIds.SequenceEqual(ids, StringComparer.Ordinal))
            {
                _metricFunctionIds = ids;
                _metricFunctionChoices = AuthoringInspectorCopy.DescribeFunctions(ids);
            }

            return _metricFunctionChoices;
        }
    }

    public AuthoringFunctionDisplay? SelectedMetricFunction
    {
        get => MetricFunctionChoices.FirstOrDefault(choice =>
            string.Equals(choice.Id, MetricFunctionId, StringComparison.Ordinal));
        set
        {
            if (value is not null)
            {
                MetricFunctionId = value.Id;
            }
        }
    }

    public string MetricFunctionId
    {
        get => SelectedMetric?.Source switch
        {
            MeasureSource measure => measure.FunctionId,
            AlgorithmSource algorithm => algorithm.AlgorithmId,
            _ => string.Empty,
        };
        set
        {
            var id = value?.Trim() ?? string.Empty;
            if (!HasStepSettings || string.IsNullOrWhiteSpace(id) || string.Equals(MetricFunctionId, id, StringComparison.Ordinal))
            {
                return;
            }

            if (!AuthoringFunctionCatalog.TryGet(id, out var spec)
                || spec.Id == AuthoringFunctionIds.BasicApplyTransferFunction
                || spec.IsAlgorithm != (SelectedMetric?.Source is AlgorithmSource))
            {
                return;
            }

            UpdateSelectedMetric(metric => metric.Source switch
            {
                MeasureSource measure => metric with { Source = measure with { FunctionId = id } },
                AlgorithmSource algorithm => metric with { Source = algorithm with { AlgorithmId = id } },
                _ => metric,
            });
        }
    }

    public IReadOnlyList<AuthoringSettingRow> MetricSettingRows
        => SelectedMetric?.Source switch
        {
            MeasureSource measure => ToSettingRows(measure.Settings, measure.FunctionId),
            AlgorithmSource algorithm => ToSettingRows(algorithm.Settings, algorithm.AlgorithmId),
            _ => [],
        };

    public void SetMetricSettingBool(string key, bool value)
        => SetMetricSetting(key, AuthoringInvariantNumbers.FormatBool(value));

    public void SetMetricSettingNumber(string key, decimal? value)
    {
        if (value is not { } number || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var functionId = SelectedMetric?.Source switch
        {
            MeasureSource measure => measure.FunctionId,
            AlgorithmSource algorithm => algorithm.AlgorithmId,
            _ => "*",
        };
        var spec = AuthoringMetricSettingCatalog.Resolve(functionId, key);
        if (spec.Kind == AuthoringSettingKind.Integer)
        {
            number = decimal.Truncate(number);
        }

        if (spec.Minimum is { } minimum && number < (decimal)minimum)
        {
            number = (decimal)minimum;
        }

        SetMetricSetting(key, AuthoringInvariantNumbers.FormatDecimal(number));
    }

    public void SetMetricSetting(string key, string value)
    {
        if (!HasStepSettings || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var nextValue = value ?? string.Empty;
        var current = SelectedMetric?.Source switch
        {
            MeasureSource measure when measure.Settings.TryGetValue(key, out var existing) => existing,
            AlgorithmSource algorithm when algorithm.Settings.TryGetValue(key, out var existing) => existing,
            _ => null,
        };
        if (string.Equals(current, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        UpdateSelectedMetric(metric => metric.Source switch
        {
            MeasureSource measure => metric with { Source = measure with { Settings = WithSetting(measure.Settings, key, nextValue) } },
            AlgorithmSource algorithm => metric with { Source = algorithm with { Settings = WithSetting(algorithm.Settings, key, nextValue) } },
            _ => metric,
        });
    }

    public bool HistoryEnabled
    {
        get => HasMetricPresentation && (SelectedMetric?.History?.Enabled ?? DefaultHistoryEnabled);
        set
        {
            if (!HasMetricPresentation || HistoryEnabled == value)
            {
                return;
            }

            UpdateHistory(current => current with { Enabled = value });
        }
    }

}
