using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Time;
using ReactiveUI;

namespace HardwareTest.Features.Results;

public sealed class BenchReferenceCaptureRow : ReactiveObject
{
    private bool _isSelected;
    public required BenchReferenceRule Rule { get; init; }
    public BenchReferenceValue? Value { get; init; }
    public string? Error { get; init; }
    public bool CanSelect => Value is not null && Error is null;
    public string Label => Value is { } value
        ? $"{Rule.ReferenceKey} — {Rule.MetricKey}: {value.Value.ToString("G9", CultureInfo.InvariantCulture)} {value.Unit} — mean of {value.SampleCount} samples"
        : $"{Rule.ReferenceKey} — {Rule.MetricKey}: {Error}";
    public string AutomationName => $"{Label}; criterion {Rule.RuleId}";
    public string Details => $"Reference input: {Rule.ReferenceKey}; criterion {Rule.RuleId}\n"
        + (Value is { } value
            ? $"Exact mean: {value.Value.ToString("G17", CultureInfo.InvariantCulture)} {value.Unit}\n{value.SampleCount} samples; producer {value.ProducerStepId}\n{BenchReferenceEvidence.DescribeRule(Rule)}"
            : Error ?? string.Empty);
    public bool IsSelected { get => _isSelected; set { if (CanSelect) this.RaiseAndSetIfChanged(ref _isSelected, value); } }
}

/// Saves an owned snapshot of the exact opened terminal source, never the current live program.
public sealed class BenchReferencePromotionViewModel : ReactiveObject
{
    private readonly IBenchReferenceStore? _store;
    private readonly IClock _clock;
    private readonly Func<bool> _canManage;
    private readonly Func<Action, Task> _onUi;
    private TestRunRecord? _source;
    private int _generation;
    private string _name = string.Empty;
    private string _validDays = "0";
    private string _status = string.Empty;
    private bool _isSaving;

    public BenchReferencePromotionViewModel(IBenchReferenceStore? store, IClock clock, Func<bool> canManage, Func<Action, Task> onUi)
    {
        _store = store; _clock = clock; _canManage = canManage; _onUi = onUi;
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
    }
    public ObservableCollection<BenchReferenceCaptureRow> Candidates { get; } = [];
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> SaveCommand { get; }
    public event Action<string>? RequestFocusField;
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public string ValidDays { get => _validDays; set => this.RaiseAndSetIfChanged(ref _validDays, value); }
    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
    public bool IsSaving { get => _isSaving; private set { this.RaiseAndSetIfChanged(ref _isSaving, value); RefreshPermissions(); } }
    public bool CanEdit => _canManage() && !IsSaving;
    public bool HasCandidate => Candidates.Any(row => row.CanSelect);
    public bool CanPromote => CanEdit && HasCandidate;
    public string SourceDetails => _source is null ? "Open a qualified reference acquisition or full-suite run."
        : $"Source run: {_source.RunId}\nGolden DUT: {_source.DutSerial}; operator: {_source.OperatorName}\nProgram: {_source.PlanId}; VISA: {_source.ReferenceContext?.VisaMode}\n{_source.ExecutionScopeText}\nCompleted: {_source.CompletedAt:O}\nOnly the checked, actually measured keys will be saved. Existing profiles are never overwritten.";
    public string SourceIdentity => BenchReferenceEvidence.DescribeContext(_source?.ReferenceContext);

    public void Configure(TestRunRecord? source)
    {
        _generation++;
        Candidates.Clear(); _source = null;
        Name = string.Empty; ValidDays = "0"; Status = string.Empty;
        if (source is null || source.ReferenceRules.Count == 0) { RefreshPermissions(); return; }
        if (source.IsSchemaReadOnly) { Status = "This record uses a read-only schema and cannot qualify a reference."; RefreshPermissions(); return; }
        _source = JsonSerializer.Deserialize(JsonSerializer.Serialize(source, AppJsonContext.Default.TestRunRecord), AppJsonContext.Default.TestRunRecord);
        if (_source is not null)
            foreach (var rule in _source.ReferenceRules)
            {
                BenchReferenceCaptureRow row;
                try
                {
                    var candidate = BenchReferenceEvaluator.Capture("Candidate", _source, [rule], _clock.UtcNow);
                    row = new() { Rule = BenchReferenceEvaluator.CloneRule(rule), Value = candidate.Values.Single(), IsSelected = true };
                }
                catch (Exception ex) { row = new() { Rule = BenchReferenceEvaluator.CloneRule(rule), Error = ex.Message }; }
                Candidates.Add(row);
            }
        if (!HasCandidate) Status = "No declared measurement qualifies. Use successful reference acquisition with complete finite data and known provenance.";
        RefreshPermissions();
    }

    public void RefreshPermissions()
    {
        this.RaisePropertyChanged(nameof(CanEdit)); this.RaisePropertyChanged(nameof(CanPromote));
        this.RaisePropertyChanged(nameof(HasCandidate)); this.RaisePropertyChanged(nameof(SourceDetails));
        this.RaisePropertyChanged(nameof(SourceIdentity));
    }

    private async Task SaveAsync()
    {
        if (!CanEdit || _source is null || _store is null) { Status = "Reference saving requires an idle bench and engineer presentation."; return; }
        if (string.IsNullOrWhiteSpace(Name))
        { Status = "Enter a name for this golden reference."; RequestFocusField?.Invoke("Golden reference name"); return; }
        if (!int.TryParse(ValidDays, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days < 0 || days > 36500)
        { Status = "Validity must be a whole number of days from 0 to 36500; 0 means no automatic expiry."; RequestFocusField?.Invoke("Golden reference validity in days"); return; }
        var rules = Candidates.Where(row => row.CanSelect && row.IsSelected).Select(row => row.Rule).ToArray();
        if (rules.Length == 0) { Status = "Select at least one qualified measurement to save."; RequestFocusField?.Invoke("Reference measurements"); return; }
        var generation = _generation;
        var source = _source;
        try
        {
            IsSaving = true;
            var now = _clock.UtcNow;
            var profile = BenchReferenceEvaluator.Capture(Name.Trim(), source, rules, now, days == 0 ? null : now.AddDays(days));
            await _store.SaveAsync(profile).ConfigureAwait(false);
            await _onUi(() =>
            {
                if (generation == _generation)
                    Status = $"Saved {profile.Name} — reference {profile.Id}, revision {profile.Revision}. {profile.Values.Count} value(s). Reload Golden references on Run and choose the intended test inputs.";
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { await _onUi(() => { if (generation == _generation) Status = $"Could not save reference: {ex.Message}. Your name and choices are retained."; }).ConfigureAwait(false); }
        finally { await _onUi(() => { IsSaving = false; }).ConfigureAwait(false); }
    }
}
