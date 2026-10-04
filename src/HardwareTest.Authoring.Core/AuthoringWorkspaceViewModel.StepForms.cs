using System.Globalization;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public string MetricName
    {
        get => HasMetricPresentation ? SelectedMetric?.Name ?? string.Empty : string.Empty;
        set { if (HasMetricPresentation) UpdateSelectedMetric(metric => metric with { Name = value }); }
    }

    public bool HasMetricInputs => HasStepSettings && SelectedMetric?.Source is AlgorithmSource && AuthoringFunctionCatalog.ConsumesInputChannels(MetricFunctionId);

    public string MetricInputChannels
    {
        get => HasMetricInputs && SelectedMetric?.Source is AlgorithmSource algorithm
            ? string.Join(", ", algorithm.InputChannelKeys) : string.Empty;
        set
        {
            if (!HasMetricInputs) return;
            var channels = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            UpdateSelectedMetric(metric => metric.Source is AlgorithmSource algorithm
                ? metric with { Source = algorithm with { InputChannelKeys = channels } } : metric);
        }
    }

    public string SelectedNodeIdentity => SelectedSequence?.NodeId is { } id ? $"Step ID: {id:D}" : string.Empty;

    public bool NeedsMetricInstrument => HasStepSettings && AuthoringFunctionCatalog.HasInstrumentDependency(MetricFunctionId);

    public string SelectedStepErrors
    {
        get
        {
            if (SelectedProgram is null || SelectedSequence?.NodeId is not { } id) return string.Empty;
            var errors = SelectedProgram.AuthoringState.IncompleteNumericText
                .Where(pair => pair.Key.StartsWith($"{id:D}/", StringComparison.Ordinal))
                .Select(pair => $"{pair.Key[(pair.Key.IndexOf('/') + 1)..]}: Enter a valid finite number; incomplete text is saved.")
                .ToList();
            if (HasMetricPresentation && SelectedMetric is { } metric)
            {
                try { AuthoringCriteria.Validate(metric); }
                catch (AuthoringWorkspaceException ex) { errors.Add(ex.Message); }
                if (metric.Source is AlgorithmSource algorithm
                    && AuthoringFunctionCatalog.InputChannelIssue(algorithm.AlgorithmId, algorithm.InputChannelKeys) is { } inputIssue)
                    errors.Add(inputIssue);
                if (NeedsMetricInstrument && InstrumentBindingIssue(MetricInstrumentSlot, MetricFunctionId) is { } issue)
                    errors.Add(issue);
            }
            if (HasIdentitySetup && InstrumentBindingIssue(SetupInstrumentSlot, AuthoringFunctionIds.BasicIdentityCheck) is { } identityIssue)
                errors.Add(identityIssue);
            return string.Join(Environment.NewLine, errors);
        }
    }

    private string? InstrumentBindingIssue(string slot, string functionId)
    {
        var instrument = Instruments.FirstOrDefault(item => string.Equals(item.SlotName, slot, StringComparison.OrdinalIgnoreCase));
        if (instrument is null) return "Choose an existing instrument slot for this recipe.";
        try { AuthoringInstrumentCatalog.EnsureCompatible(instrument.TypeId, functionId); }
        catch (AuthoringWorkspaceException ex) { return ex.Message; }
        return null;
    }

    private IReadOnlyList<AuthoringSettingRow> _currentSettingRows = [];
    private readonly Dictionary<string, AuthoringSettingRow> _settingRows = new(StringComparer.Ordinal);

    private AuthoringSettingRow CreateSelectedSettingRow(string functionId, string key, string value)
    {
        var text = SelectedFieldText($"MetricSetting:{key}", value);
        var row = AuthoringMetricSettingCatalog.CreateRow(functionId, key, text, ChannelKeys) with { NodeId = SelectedSequence?.NodeId };
        var identity = $"{SelectedSequence?.NodeId:D}/{functionId}/{key}";
        if (_settingRows.TryGetValue(identity, out var existing)
            && (existing.Choices ?? []).SequenceEqual(row.Choices ?? [], StringComparer.Ordinal))
            row = existing;
        else _settingRows[identity] = row;
        row.Refresh(text, SettingError(key, AuthoringMetricSettingCatalog.Resolve(functionId, key), text));
        return row;
    }

    private static string SettingError(string key, AuthoringMetricSettingSpec spec, string text)
    {
        if (string.IsNullOrWhiteSpace(text) && key is "ElapsedMs" or "EventValue" or "DwellLimitMs") return string.Empty;
        if (spec.Kind is not (AuthoringSettingKind.Integer or AuthoringSettingKind.Double)) return string.Empty;
        var valid = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && double.IsFinite(number);
        if (spec.Kind == AuthoringSettingKind.Integer)
            valid = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        if (!valid) return spec.Kind == AuthoringSettingKind.Integer ? "Enter a whole number." : "Enter a finite number.";
        return spec.Minimum is { } minimum && number < minimum ? $"Enter a value of at least {minimum.ToString(CultureInfo.InvariantCulture)}." : string.Empty;
    }

    private IReadOnlyDictionary<string, string> MigrateSettings(string id, IReadOnlyDictionary<string, string> current)
    {
        var migrated = new Dictionary<string, string>(PlanCompiler.FunctionSettings(id), StringComparer.OrdinalIgnoreCase);
        foreach (var key in migrated.Keys.ToArray())
            if (current.TryGetValue(key, out var value)
                && SettingError(key, AuthoringMetricSettingCatalog.Resolve(id, key), value).Length == 0)
                migrated[key] = value;
        return migrated;
    }

    private void ChangeMetricFunction(string id)
    {
        if (SelectedProgram is not { } program || SelectedSequence?.NodeId is not { } nodeId) return;
        var current = SelectedMetric?.Source switch
        {
            MeasureSource measure => measure.Settings,
            AlgorithmSource algorithm => algorithm.Settings,
            _ => new Dictionary<string, string>(),
        };
        IReadOnlyDictionary<string, string> settings;
        try { settings = MigrateSettings(id, current); }
        catch (AuthoringWorkspaceException ex)
        {
            Error = ex.Message;
            OnPropertyChanged(nameof(SelectedMetricFunction));
            OnPropertyChanged(nameof(MetricFunctionId));
            return;
        }
        var state = program.AuthoringState.Clone();
        foreach (var key in state.IncompleteNumericText.Keys.Where(key =>
                     key.StartsWith($"{nodeId:D}/MetricSetting:", StringComparison.Ordinal)
                     && !settings.ContainsKey(key[(key.IndexOf("MetricSetting:", StringComparison.Ordinal) + "MetricSetting:".Length)..])).ToArray())
            state.IncompleteNumericText.Remove(key);
        _pendingNumericState = state;
        try
        {
            UpdateSelectedMetric(metric => metric.Source switch
            {
                MeasureSource measure => metric with { Source = measure with { FunctionId = id, Settings = settings, InstrumentSlot = AuthoringFunctionCatalog.HasInstrumentDependency(id) ? measure.InstrumentSlot : string.Empty } },
                AlgorithmSource algorithm => metric with
                {
                    Source = algorithm with
                    {
                        AlgorithmId = id,
                        Settings = settings,
                        InstrumentSlot = AuthoringFunctionCatalog.HasInstrumentDependency(id) ? algorithm.InstrumentSlot : null,
                        InputChannelKeys = AuthoringFunctionCatalog.ConsumesInputChannels(id) && AuthoringFunctionCatalog.ConsumesInputChannels(algorithm.AlgorithmId)
                        ? algorithm.InputChannelKeys : [],
                    }
                },
                _ => metric,
            });
        }
        finally { _pendingNumericState = null; }
        Status = "Recipe changed: compatible settings retained, unsupported settings removed, defaults added. Unused instrument bindings and inputs cleared. Review criteria and instrument binding.";
    }

    private bool TrySetNumericSetting(string key, string text)
    {
        var spec = AuthoringMetricSettingCatalog.Resolve(MetricFunctionId, key);
        if (spec.Kind is not (AuthoringSettingKind.Integer or AuthoringSettingKind.Double)) return false;
        if (SelectedSequence?.NodeId is not { } id) return true;
        SetNumericFieldText(id, $"MetricSetting:{key}", text, SettingError(key, spec, text).Length == 0, state =>
        {
            _pendingNumericState = state;
            try { UpdateMetricSetting(key, text); }
            finally { _pendingNumericState = null; }
        });
        OnPropertyChanged(nameof(MetricSettingRows));
        OnPropertyChanged(nameof(SelectedStepErrors));
        return true;
    }

    private void UpdateMetricSetting(string key, string value)
        => UpdateSelectedMetric(metric => metric.Source switch
        {
            MeasureSource measure => metric with { Source = measure with { Settings = WithSetting(measure.Settings, key, value) } },
            AlgorithmSource algorithm => metric with { Source = algorithm with { Settings = WithSetting(algorithm.Settings, key, value) } },
            _ => metric,
        });
}
