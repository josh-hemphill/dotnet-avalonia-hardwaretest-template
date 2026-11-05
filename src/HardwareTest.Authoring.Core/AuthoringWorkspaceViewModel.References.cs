using System.Collections.ObjectModel;
using System.ComponentModel;
using HardwareTest.Core.BenchReferences;

namespace HardwareTest.Authoring;

public sealed class AuthoringReferenceCriterionOption(string ruleId, string label) : INotifyPropertyChanged
{
    public string RuleId { get; } = ruleId;
    public string Label { get; private set; } = label;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void UpdateLabel(string value)
    {
        if (Label == value) return;
        Label = value;
        PropertyChanged?.Invoke(this, new(nameof(Label)));
    }
    public override string ToString() => Label;
}

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly Dictionary<(Guid Session, string Plan, Guid Producer), string> _referenceCriterionSelection = [];
    private (Guid Session, string Plan, Guid Producer)? _referenceOptionsOwner;
    private IReadOnlyList<BenchReferenceRule> _referenceOptionsSource = [];
    private readonly ObservableCollection<AuthoringReferenceCriterionOption> _referenceCriterionOptions = [];
    private bool _publishingReferenceOptions;
    private (Guid Session, string Plan, Guid Producer)? ReferenceSelectionOwner => SelectedProgram is { } program && SelectedSequence?.NodeId is { } id
        ? (WorkspaceSessionId, program.PlanId, id) : null;
    public bool HasRecordedReferences => RecordingReferenceCount > 0;
    private int RecordingReferenceCount => _previewDispatch is not null ? CachedPreview.ReferenceInputCount
        : SelectedDataset?.RecordingSnapshot?.Restore().ReferenceUses.Count ?? 0;
    public string RecordingReferenceSummary => $"{RecordingReferenceCount} recorded reference input(s) · preview only";
    public string RecordingReferenceReview => _previewDispatch is not null ? CachedPreview.ReferenceReview
        : SelectedDataset?.RecordingSnapshot?.Restore() is { } recording
            ? AuthoringReferenceReview.Describe(SelectedProgram, recording) : string.Empty;

    private IReadOnlyList<BenchReferenceRule> SelectedReferenceRules => SelectedSequence?.NodeId is { } id
        ? SelectedProgram?.Sidecar.ReferenceRules?.Where(rule => rule is not null && rule.ProducerStepId == id).ToArray() ?? []
        : [];
    private BenchReferenceRule? SelectedReferenceRule => ReferenceSelectionOwner is { } owner
        && _referenceCriterionSelection.TryGetValue(owner, out var selected)
        ? SelectedReferenceRules.FirstOrDefault(rule => rule.RuleId == selected) ?? SelectedReferenceRules.FirstOrDefault()
        : SelectedReferenceRules.FirstOrDefault();
    public IReadOnlyList<AuthoringReferenceCriterionOption> ReferenceCriterionOptions => _referenceCriterionOptions;

    private void RefreshReferenceOptions()
    {
        if (_publishingReferenceOptions) return;
        var owner = ReferenceSelectionOwner;
        var rules = SelectedReferenceRules;
        if (owner == _referenceOptionsOwner && rules.SequenceEqual(_referenceOptionsSource)) return;
        var reset = owner != _referenceOptionsOwner;
        // Compiled bindings read the selection during CollectionChanged. Publish
        // the cache before any notification, and keep all property getters pure.
        _referenceOptionsOwner = owner;
        _referenceOptionsSource = rules;
        _publishingReferenceOptions = true;
        try
        {
            if (reset) _referenceCriterionOptions.Clear();
            foreach (var stale in _referenceCriterionOptions.Where(option => !rules.Any(rule => rule.RuleId == option.RuleId)).ToArray())
                _referenceCriterionOptions.Remove(stale);
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                var label = $"{index + 1}. {rule.ReferenceKey} · {BenchReferenceEvidence.DescribeRule(rule)}";
                var option = _referenceCriterionOptions.FirstOrDefault(item => item.RuleId == rule.RuleId);
                if (option is null) _referenceCriterionOptions.Insert(index, new(rule.RuleId, label));
                else
                {
                    var previousIndex = _referenceCriterionOptions.IndexOf(option);
                    if (previousIndex != index) _referenceCriterionOptions.Move(previousIndex, index);
                    option.UpdateLabel(label);
                }
            }
        }
        finally { _publishingReferenceOptions = false; }
    }
    public AuthoringReferenceCriterionOption? SelectedReferenceCriterion
    {
        get => ReferenceCriterionOptions.FirstOrDefault(option => option.RuleId == SelectedReferenceRule?.RuleId);
        set { if (!_publishingReferenceOptions && value is not null) SelectReferenceCriterion(value.RuleId); }
    }
    public void SelectReferenceCriterion(string ruleId)
    {
        if (ReferenceSelectionOwner is not { } owner || !SelectedReferenceRules.Any(rule => rule.RuleId == ruleId)) return;
        if (_referenceCriterionSelection.GetValueOrDefault(owner) == ruleId) return;
        _referenceCriterionSelection[owner] = ruleId;
        RaiseReferenceProperties();
    }

    public string ReferenceRequirementLabel => SelectedReferenceRules.Count > 1
        ? $"Require all {SelectedReferenceRules.Count} bench reference criteria (uncheck removes all)"
        : "Require a bench reference for this test";
    public string ReferenceCriteriaOverview => SelectedReferenceRules.Count < 2 ? string.Empty
        : $"All {SelectedReferenceRules.Count} criteria must pass. Select a criterion to edit. Undo restores removals.";
    public string ReferenceRequirementHelp => string.Join(Environment.NewLine,
        new[] { SelectedReferenceRules.Count > 1 ? ReferenceRequirementLabel : string.Empty, ReferenceTargetError }.Where(text => text.Length > 0));

    public bool RequireGoldenReference
    {
        get => HasMetricPresentation && SelectedReferenceRule is not null;
        set
        {
            if (!HasMetricPresentation || RequireGoldenReference == value) return;
            UpdateReferenceRule(rule => value ? rule ?? CreateReferenceRule() : null);
        }
    }

    public string ReferenceKey
    {
        get => SelectedReferenceRule?.ReferenceKey ?? string.Empty;
        set => UpdateReferenceRule(rule => rule is null || rule.ReferenceKey == value ? rule : rule with { ReferenceKey = value });
    }

    public IReadOnlyList<string> ReferenceComparisonOptions { get; } = ["Absolute difference", "Relative difference (%)"];

    public string ReferenceComparison
    {
        get => SelectedReferenceRule?.Comparison switch
        {
            "percent" => ReferenceComparisonOptions[1],
            "absolute" or null => ReferenceComparisonOptions[0],
            _ => string.Empty,
        };
        set
        {
            if (!ReferenceComparisonOptions.Contains(value)) return;
            var comparison = value == ReferenceComparisonOptions[1] ? "percent" : "absolute";
            UpdateReferenceRule(rule => rule is null || rule.Comparison == comparison ? rule : rule with { Comparison = comparison });
        }
    }

    public string ReferenceTolerance
    {
        get => SelectedReferenceRule is { } rule && SelectedProgram is { } program
            && AuthoringReferenceRules.TryIncompleteTolerance(program, rule, out var text) ? text : FormatLimit(SelectedReferenceRule?.Tolerance);
        set
        {
            if (SelectedReferenceRule is not { } rule) return;
            var legacy = SelectedReferenceRules.FirstOrDefault()?.RuleId == rule.RuleId ? nameof(ReferenceTolerance) : null;
            SetNumericText(AuthoringReferenceRules.ToleranceField(rule), value,
                parsed => UpdateReferenceRule(current => current is null ? current : current with { Tolerance = parsed }), legacy);
            OnPropertyChanged(nameof(ReferenceTolerance));
        }
    }

    public string ReferenceReduction => "Arithmetic mean of all samples for this metric and producer, including repeat invocations.";
    public string ReferenceTarget => SelectedReferenceRule is { } rule
        ? $"Measurement: {rule.MetricKey} · unit: {(string.IsNullOrWhiteSpace(rule.Unit) ? "Set the measurement Y unit first" : rule.Unit)}"
        : string.Empty;
    public string ReferenceEquation => SelectedReferenceRule is not { } rule ? string.Empty
        : rule.Comparison == "percent"
            ? "Pass when 100 × |measured mean − reference| / |reference| ≤ tolerance (%). A zero reference is invalid."
            : rule.Comparison == "absolute" ? $"Pass when |measured mean − reference| ≤ tolerance ({rule.Unit})."
            : "Choose a supported comparison before compiling.";
    public string ReferenceKeyError => SelectedReferenceRule is { } rule && string.IsNullOrWhiteSpace(rule.ReferenceKey)
        ? "Name this reference input before compiling." : string.Empty;
    public string ReferenceComparisonError => SelectedReferenceRule is { Comparison: not ("absolute" or "percent") }
        ? "Choose absolute difference or relative difference." : string.Empty;
    public string ReferenceToleranceError => !RequireGoldenReference ? string.Empty
        : SelectedReferenceRule is { } rule && AuthoringReferenceRules.TryIncompleteTolerance(SelectedProgram!, rule, out _)
            ? "Enter a finite tolerance ≥ 0. Your unfinished value is retained in the draft."
            : SelectedReferenceRule?.Tolerance is not { } tolerance || !double.IsFinite(tolerance) || tolerance < 0
                ? "Enter a finite tolerance ≥ 0." : string.Empty;
    public string ReferenceTargetError => SelectedProgram is not { } draft || SelectedSequence?.NodeId is not { } id
        ? string.Empty
        : string.Join(Environment.NewLine, AuthoringReferenceRules.Validate(draft)
            .Where(finding => finding.NodeId == id && finding.RuleId == SelectedReferenceRule?.RuleId && finding.Field is "ReferenceTarget" or "ReferenceMeasurementUnit" or "ReferenceRuleIdentity")
            .Select(finding => finding.Message));
    public string ReferenceMeasurementUnitError => RequireGoldenReference && string.IsNullOrWhiteSpace(YUnit)
        ? "Set the measurement Y unit (1 if dimensionless), then rebind the affected reference criterion."
        : string.Empty;
    public string ReferenceCriterionError => SelectedProgram is not { } draft || SelectedSequence?.NodeId is not { } id
        ? string.Empty
        : SelectedReferenceRule is { } rule && AuthoringReferenceRules.TryIncompleteTolerance(draft, rule, out _)
            ? "Enter a finite tolerance ≥ 0. Your unfinished value is retained in the draft."
            : string.Join(Environment.NewLine, AuthoringReferenceRules.Validate(draft).Where(finding => finding.NodeId == id && finding.RuleId == SelectedReferenceRule?.RuleId).Select(finding => finding.Message));

    public void AddReferenceCriterion()
    {
        if (!HasMetricPresentation || SelectedProgram is not { } program || ReferenceSelectionOwner is not { } owner) return;
        EnsureWritableWorkspace("add a golden-reference criterion");
        var rule = CreateReferenceRule();
        var sidecar = PlanCompiler.CloneSidecar(program.Sidecar);
        sidecar.ReferenceRules ??= [];
        sidecar.ReferenceRules.Add(rule);
        _referenceCriterionSelection[owner] = rule.RuleId;
        ReplaceSelected(program with { Sidecar = sidecar }, rebuildLists: false);
    }

    public void RemoveSelectedReferenceCriterion()
    {
        if (SelectedProgram is not { } program || SelectedReferenceRule is not { } rule) return;
        EnsureWritableWorkspace("remove a golden-reference criterion");
        var sidecar = PlanCompiler.CloneSidecar(program.Sidecar);
        sidecar.ReferenceRules!.RemoveAll(item => item.RuleId == rule.RuleId);
        if (sidecar.ReferenceRules.Count == 0) sidecar.ReferenceRules = null;
        var state = program.AuthoringState.Clone();
        state.IncompleteNumericText.Remove(AuthoringDocumentState.FieldKey(rule.ProducerStepId, AuthoringReferenceRules.ToleranceField(rule)));
        if (SelectedReferenceRules.FirstOrDefault()?.RuleId == rule.RuleId)
            state.IncompleteNumericText.Remove(AuthoringDocumentState.FieldKey(rule.ProducerStepId, nameof(ReferenceTolerance)));
        ReplaceSelected(program with { Sidecar = sidecar, AuthoringState = state }, rebuildLists: false);
    }

    public void RebindReferenceToMeasurement()
    {
        if (SelectedMetric is not { } metric) return;
        UpdateReferenceRule(rule => rule is null ? null : rule with { MetricKey = metric.ChannelKey, Unit = metric.YUnit });
    }

    private BenchReferenceRule CreateReferenceRule() => new()
    {
        RuleId = Guid.NewGuid().ToString("N"),
        ProducerStepId = SelectedSequence!.NodeId!.Value,
        MetricKey = SelectedMetric!.ChannelKey,
        ReferenceKey = SelectedMetric.ChannelKey,
        Unit = SelectedMetric.YUnit,
        Comparison = "absolute",
        Tolerance = null,
    };

    private void UpdateReferenceRule(Func<BenchReferenceRule?, BenchReferenceRule?> change)
    {
        if (!HasMetricPresentation || SelectedProgram is not { } program || SelectedSequence?.NodeId is not { } id) return;
        EnsureWritableWorkspace("edit a golden-reference criterion");
        var current = SelectedReferenceRule;
        var next = change(current);
        if (Equals(current, next) && _pendingNumericState is null) return;
        var sidecar = PlanCompiler.CloneSidecar(program.Sidecar);
        // Editing the exposed criterion must not remove other imported rules for
        // this producer. Disabling the requirement deliberately removes all of
        // this measurement's reference criteria.
        var rules = sidecar.ReferenceRules?.Where(rule => rule is not null
            && (next is not null || rule.ProducerStepId != id)).ToList() ?? [];
        if (next is not null)
        {
            var index = current is null ? -1 : rules.FindIndex(rule => rule.RuleId == current.RuleId);
            if (index >= 0) rules[index] = next;
            else rules.Add(next);
        }
        sidecar.ReferenceRules = rules.Count == 0 ? null : rules;
        var state = _pendingNumericState ?? program.AuthoringState;
        if (next is null)
        {
            state = state.Clone();
            var prefix = AuthoringDocumentState.FieldKey(id, nameof(ReferenceTolerance));
            foreach (var key in state.IncompleteNumericText.Keys.Where(key => key == prefix || key.StartsWith(prefix + ":", StringComparison.Ordinal)).ToArray())
                state.IncompleteNumericText.Remove(key);
        }
        ReplaceSelected(program with { Sidecar = sidecar, AuthoringState = state }, rebuildLists: false);
    }

    private void RaiseReferenceProperties()
    {
        RefreshReferenceOptions();
        OnPropertyChanged(nameof(HasRecordedReferences));
        OnPropertyChanged(nameof(RecordingReferenceSummary));
        OnPropertyChanged(nameof(RecordingReferenceReview));
        OnPropertyChanged(nameof(RequireGoldenReference));
        OnPropertyChanged(nameof(ReferenceRequirementLabel));
        OnPropertyChanged(nameof(ReferenceCriteriaOverview));
        OnPropertyChanged(nameof(ReferenceRequirementHelp));
        OnPropertyChanged(nameof(ReferenceCriterionOptions));
        OnPropertyChanged(nameof(SelectedReferenceCriterion));
        OnPropertyChanged(nameof(ReferenceKey));
        OnPropertyChanged(nameof(ReferenceComparison));
        OnPropertyChanged(nameof(ReferenceTolerance));
        OnPropertyChanged(nameof(ReferenceReduction));
        OnPropertyChanged(nameof(ReferenceTarget));
        OnPropertyChanged(nameof(ReferenceEquation));
        OnPropertyChanged(nameof(ReferenceCriterionError));
        OnPropertyChanged(nameof(ReferenceKeyError));
        OnPropertyChanged(nameof(ReferenceComparisonError));
        OnPropertyChanged(nameof(ReferenceToleranceError));
        OnPropertyChanged(nameof(ReferenceTargetError));
        OnPropertyChanged(nameof(ReferenceMeasurementUnitError));
    }
}
