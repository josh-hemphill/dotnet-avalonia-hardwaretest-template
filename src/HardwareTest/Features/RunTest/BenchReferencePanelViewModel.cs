using System.Collections.ObjectModel;
using System.Globalization;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public sealed record BenchReferenceChoice(BenchReferenceProfile Profile, BenchReferenceValue Value)
{
    public string Label => $"{Profile.Name} — {Value.Value.ToString("G9", CultureInfo.InvariantCulture)} {Value.Unit} — r{Profile.Revision} / {Profile.Id}";
    public string Details => $"{Label}\nExact value: {Value.Value.ToString("G17", CultureInfo.InvariantCulture)} {Value.Unit}\nMean of {Value.SampleCount} samples; reference key {Value.ReferenceKey}\nGolden DUT: {Profile.SourceDutSerial}; operator: {Profile.SourceOperator}\nSource run: {Profile.SourceRunId}; {Profile.SourceScope}; selected path: {Profile.SourceSelectedStepPath ?? "Full acquisition"}\nCaptured: {Profile.CreatedAt:O}; VISA: {Profile.Context.VisaMode}\nExpires: {Profile.ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "No automatic expiry"}\nContent SHA-256: {Profile.ContentSha256}";
}

public sealed class BenchReferenceRuleRow : ReactiveObject
{
    private readonly Func<bool> _canEdit;
    private readonly Action _changed;
    private BenchReferenceChoice? _selectedChoice;
    public BenchReferenceRuleRow(BenchReferenceRule rule, Func<bool> canEdit, Action changed)
    {
        Rule = BenchReferenceEvaluator.CloneRule(rule);
        _canEdit = canEdit;
        _changed = changed;
    }
    public BenchReferenceRule Rule { get; }
    public ObservableCollection<BenchReferenceChoice> Choices { get; } = [];
    public string Heading => $"{Rule.MetricKey} — {Rule.ReferenceKey} ({Rule.Unit})";
    public string AutomationName => $"Reference for {Heading}, rule {Rule.RuleId}";
    public string Equation => Rule.Comparison == BenchReferenceComparisons.Percent
        ? $"100 × |measured mean − reference| / |reference| ≤ {Rule.Tolerance:G9}% (reference must be nonzero)"
        : $"|measured mean − reference| ≤ {Rule.Tolerance:G9} {Rule.Unit}";
    public string TargetDetails => $"Reference input: {Rule.ReferenceKey}; rule: {Rule.RuleId}\nProducer: {Rule.ProducerStepId}\nArithmetic mean of all matching samples, including repeat invocations.\nExact criterion: {BenchReferenceEvidence.DescribeRule(Rule)}";
    public string MissingReason { get; internal set; } = "Choose a compatible reference.";
    public string SelectedDetails => SelectedChoice?.Details ?? MissingReason;
    public BenchReferenceChoice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (!_canEdit() || ReferenceEquals(value, _selectedChoice)) return;
            this.RaiseAndSetIfChanged(ref _selectedChoice, value);
            this.RaisePropertyChanged(nameof(SelectedDetails));
            _changed();
        }
    }
    internal void Restore(BenchReferenceChoice? choice) => _selectedChoice = choice;
}

public sealed record BenchReferenceCatalogItem(BenchReferenceCatalogEntry Entry)
{
    public string Label => Entry.Profile is { } profile
        ? $"{profile.Name} — {profile.Context.PlanId} — r{profile.Revision} / {profile.Id}{(Entry.IsRetired ? " — retired" : string.Empty)}"
        : $"Unavailable: {System.IO.Path.GetFileName(Entry.Path)}";
    public string Details => Entry.Error ?? (Entry.Profile is { } profile
        ? string.Join(Environment.NewLine,
            $"Reference: {profile.Name}; ID {profile.Id}; revision {profile.Revision}",
            $"Content SHA-256: {profile.ContentSha256}",
            $"Golden DUT: {profile.SourceDutSerial}; operator: {profile.SourceOperator}",
            $"Source run: {profile.SourceRunId}; {profile.SourceScope}; selected path: {profile.SourceSelectedStepPath ?? "Full acquisition"}",
            $"Source started: {profile.SourceStartedAt:O}; completed: {profile.SourceCompletedAt:O}; captured: {profile.CreatedAt:O}",
            $"Expiry: {profile.ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "No automatic expiry"}",
            Entry.Retirement is { } retired ? $"Retired: {retired.RetiredAt:O}; reason: {retired.Reason}" : Entry.IsRetired ? "Retired: unavailable for new runs" : "Immutable profile; replacement creates a new identity",
            BenchReferenceEvidence.DescribeContext(profile.Context),
            string.Join(Environment.NewLine, profile.Values.Select(value => $"Input {value.ReferenceKey}; metric {value.MetricKey}; producer {value.ProducerStepId}: {value.Value.ToString("G17", CultureInfo.InvariantCulture)} {value.Unit}; {value.Reduction}, {value.SampleCount} samples")))
        : "Reference data is unavailable.");
}

public sealed record BenchReferenceRunSnapshot(
    BenchReferenceContext Context,
    IReadOnlyList<BenchReferenceRule> Rules,
    IReadOnlyList<BenchReferenceUse> Uses,
    bool IsAcquisition);

/// Per-test choices and the bench catalog; no active run consults this mutable panel.
public sealed class BenchReferencePanelViewModel : ReactiveObject
{
    private readonly IBenchReferenceStore? _store;
    private readonly AppSettings _settings;
    private readonly ISettingsStore? _settingsStore;
    private readonly IClock _clock;
    private readonly Func<bool> _isRunning;
    private readonly Func<bool> _isEngineer;
    private readonly Func<Action, Task> _onUi;
    private readonly Action _contextChanged;
    private BenchReferenceContext? _context;
    private ProgramItemViewModel? _program;
    private int _generation;
    private bool _isBusy;
    private bool _managementPending;
    private bool _restoring;
    private bool _isAcquisition;
    private bool _catalogAvailable = true;
    private string _status = string.Empty;
    private string _retirementReason = string.Empty;
    private BenchReferenceCatalogItem? _selectedProfile;

    public BenchReferencePanelViewModel(IBenchReferenceStore? store, AppSettings settings, ISettingsStore? settingsStore,
        IClock clock, Func<bool> isRunning, Func<bool> isEngineer, Func<Action, Task> onUi, Action contextChanged)
    {
        _store = store; _settings = settings; _settingsStore = settingsStore; _clock = clock;
        _isRunning = isRunning; _isEngineer = isEngineer; _onUi = onUi; _contextChanged = contextChanged;
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        RetireCommand = ReactiveCommand.CreateFromTask(RetireAsync);
        SaveChoicesCommand = ReactiveCommand.CreateFromTask(SaveChoicesAsync);
    }

    public ObservableCollection<BenchReferenceRuleRow> Rules { get; } = [];
    public ObservableCollection<BenchReferenceCatalogItem> Profiles { get; } = [];
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RefreshCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RetireCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> SaveChoicesCommand { get; }
    public bool HasRules => _program?.ReferenceRules.Count > 0 || !string.IsNullOrWhiteSpace(_program?.ReferenceDeclarationError);
    public bool CanEdit => !_isRunning() && !IsBusy;
    public bool CanManage => CanEdit && _isEngineer();
    public bool ShowManagement => _isEngineer();
    public bool CanRetire => CanManage && SelectedProfile?.Entry is { Profile: not null, IsRetired: false, Error: null };
    public bool IsReady => !IsManagementPending && (!HasRules || IsReadyForRules(_program?.ReferenceRules ?? []));
    public bool IsReadyForRules(IReadOnlyList<BenchReferenceRule> rules)
        => !IsManagementPending && string.IsNullOrWhiteSpace(_program?.ReferenceDeclarationError) && !IsBusy
            && (rules.Count == 0 || IsAcquisition && _isEngineer() || _catalogAvailable && _context is not null && rules.All(rule => Rules.Any(row => row.Rule.RuleId == rule.RuleId
                && row.SelectedChoice is { } choice && BenchReferenceEvaluator.CompatibilityError(rule, choice.Profile, _context, _clock.UtcNow) is null)));
    public string Heading => IsAcquisition ? "Reference acquisition — no production acceptance" : $"Golden references ({Rules.Count})";
    public string SelectionSummary => string.Join("; ", Rules.Take(2).Select(row => row.SelectedChoice is { } choice
        ? $"{row.Rule.MetricKey}: {choice.Value.Value.ToString("G6", CultureInfo.InvariantCulture)} {choice.Value.Unit} ({choice.Profile.Name})"
        : $"{row.Rule.MetricKey}: reference required")) + (Rules.Count > 2 ? $"; {Rules.Count - 2} more inputs in Golden references" : string.Empty);
    public string ReadinessText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_program?.ReferenceDeclarationError)) return _program.ReferenceDeclarationError!;
            if (IsManagementPending) return "Reference retirement is still being saved. Wait for its result, inspect the remaining inputs and confirm the session again before Run.";
            if (IsBusy) return "Loading saved golden references. Wait until the matching values are verified.";
            if (IsAcquisition) return "Acquisition collects reference data. Reference acceptance is unevaluated; this is not a production pass.";
            if (!_catalogAvailable) return "Reference storage could not be verified. Correct the storage problem, then Reload references before Run.";
            if (_context is not null)
                foreach (var row in Rules)
                    if (row.SelectedChoice is { } choice && BenchReferenceEvaluator.CompatibilityError(row.Rule, choice.Profile, _context, _clock.UtcNow) is { } reason)
                        return $"{row.Rule.MetricKey}: {reason} Reload references or acquire a qualified replacement.";
            return IsReady ? "Reference values selected. Confirm the session before Run."
                : "Choose compatible golden references for the affected tests, or ask a technician to acquire the first reference.";
        }
    }
    public bool IsManagementPending => _managementPending;
    public bool IsBusy { get => _isBusy || _managementPending; private set { this.RaiseAndSetIfChanged(ref _isBusy, value); RefreshState(); } }
    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
    public string RetirementReason { get => _retirementReason; set => this.RaiseAndSetIfChanged(ref _retirementReason, value); }
    public BenchReferenceCatalogItem? SelectedProfile
    {
        get => _selectedProfile;
        set { this.RaiseAndSetIfChanged(ref _selectedProfile, value); this.RaisePropertyChanged(nameof(CanRetire)); }
    }
    public bool IsAcquisition
    {
        get => _isAcquisition;
        set
        {
            if (!CanManage || value == _isAcquisition) return;
            this.RaiseAndSetIfChanged(ref _isAcquisition, value);
            _contextChanged(); RefreshState();
        }
    }

    public event EventHandler? StateChanged;
    public event Action<string>? RequestFocusField;

    public void Configure(ProgramItemViewModel? program, BenchReferenceContext? context)
    {
        var changed = !Equals(_context, context) || !string.Equals(_program?.Id, program?.Id, StringComparison.Ordinal)
            || !(_program?.ReferenceRules ?? []).SequenceEqual(program?.ReferenceRules ?? []);
        _program = program; _context = context;
        if (!changed) { RefreshState(); return; }
        _generation++;
        _isAcquisition = false;
        Rules.Clear(); Profiles.Clear(); SelectedProfile = null; RetirementReason = string.Empty;
        if (program is not null)
            foreach (var rule in program.ReferenceRules)
                Rules.Add(new BenchReferenceRuleRow(rule, () => CanEdit && !_restoring, SelectionChanged));
        Status = string.Empty;
        RefreshState();
    }

    public async Task RefreshAsync()
    {
        if (_isRunning()) { Status = "Wait for the active run to finish before changing references."; return; }
        var generation = _generation;
        await _onUi(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            var entries = _store is null ? [] : await _store.ListAsync().ConfigureAwait(false);
            await _onUi(() =>
            {
                if (generation != _generation || _isRunning()) return;
                _restoring = true;
                var reconfirm = false;
                try
                {
                    _catalogAvailable = _store is not null;
                    var managementId = SelectedProfile?.Entry.Profile?.Id;
                    Profiles.Clear();
                    foreach (var entry in entries) Profiles.Add(new(entry));
                    SelectedProfile = managementId is null ? null : Profiles.FirstOrDefault(item => item.Entry.Profile?.Id == managementId);
                    foreach (var row in Rules)
                    {
                        var prior = row.SelectedChoice;
                        var wanted = row.SelectedChoice?.Profile.Id ?? _settings.BenchReferenceSelections
                            .FirstOrDefault(item => item.PlanId == _program?.Id && item.RuleId == row.Rule.RuleId)?.ReferenceId;
                        row.Choices.Clear();
                        var reason = "No compatible saved reference. Acquire the golden unit on this program, then save it from Results.";
                        foreach (var entry in entries)
                        {
                            if (entry.Profile is not { } profile) continue;
                            var error = entry.Error ?? (entry.IsRetired ? "Reference is retired." : _context is null ? "Program context is unavailable."
                                : BenchReferenceEvaluator.CompatibilityError(row.Rule, profile, _context, _clock.UtcNow));
                            if (profile.Id == wanted && error is not null) reason = $"Previously selected {profile.Name} / {profile.Id}: {error}";
                            if (error is not null) continue;
                            var value = profile.Values.Single(item => string.Equals(item.ReferenceKey, row.Rule.ReferenceKey, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(item.MetricKey, row.Rule.MetricKey, StringComparison.OrdinalIgnoreCase)
                                && item.ProducerStepId == row.Rule.ProducerStepId && string.Equals(item.Unit, row.Rule.Unit, StringComparison.Ordinal));
                            row.Choices.Add(new(profile, value));
                        }
                        row.MissingReason = reason;
                        var restored = row.Choices.FirstOrDefault(choice => choice.Profile.Id == wanted);
                        if (prior is not null && (restored is null || prior.Profile.Revision != restored.Profile.Revision
                            || prior.Profile.ContentSha256 != restored.Profile.ContentSha256)) reconfirm = true;
                        row.Restore(restored);
                        row.RaisePropertyChanged(nameof(row.SelectedChoice));
                        row.RaisePropertyChanged(nameof(row.SelectedDetails));
                    }
                    Status = entries.Any(entry => entry.Error is not null)
                        ? "Some reference files could not be read. Inspect Saved profiles; unavailable records are never substituted."
                        : $"Loaded {entries.Count} saved profile(s). Choose each required test input explicitly.";
                }
                finally { _restoring = false; }
                if (reconfirm)
                {
                    _contextChanged();
                    Status = "Previously selected reference content or availability changed. Inspect the displayed values and confirm the session again.";
                }
                RefreshState();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _onUi(() =>
            {
                if (generation != _generation) return;
                _catalogAvailable = false;
                Status = $"Cannot load references: {ex.Message}. Reload after correcting storage; ordinary reference-dependent Run is blocked.";
                RefreshState();
            }).ConfigureAwait(false);
        }
        finally { await _onUi(() => { if (generation == _generation) IsBusy = false; }).ConfigureAwait(false); }
    }

    public async Task<BenchReferenceRunSnapshot> FreezeAsync(BenchReferenceContext context, IReadOnlyList<BenchReferenceRule>? coveredRules = null)
    {
        if (IsManagementPending || IsBusy)
            throw new InvalidOperationException("Reference management is still in progress. Wait for its result, inspect the reference context and confirm the session again before Run.");
        if (_program is null || !string.IsNullOrWhiteSpace(_program.ReferenceDeclarationError))
            throw new InvalidOperationException(_program?.ReferenceDeclarationError ?? "Select a program first.");
        var rules = (coveredRules ?? _program.ReferenceRules).Select(BenchReferenceEvaluator.CloneRule).ToArray();
        var errors = BenchReferenceEvaluator.ValidateRules(rules);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        if (IsAcquisition)
        {
            if (!_isEngineer()) throw new InvalidOperationException("Reference acquisition requires engineer presentation.");
            return new(context with { }, rules, [], true);
        }
        if (rules.Length == 0) return new(context with { }, rules, [], false);
        var choices = Rules.ToDictionary(row => row.Rule.RuleId, row => row.SelectedChoice, StringComparer.Ordinal);
        var selections = new List<BenchReferenceSelection>();
        foreach (var rule in rules)
        {
            if (_store is null || !choices.TryGetValue(rule.RuleId, out var choice) || choice is null)
                throw new InvalidOperationException($"Choose a compatible golden reference for {rule.MetricKey} before Run.");
            selections.Add(new()
            {
                Rule = rule,
                ReferenceId = choice.Profile.Id,
                ExpectedRevision = choice.Profile.Revision,
                ExpectedSha256 = choice.Profile.ContentSha256,
            });
        }
        // The catalog owns the complete acceptance transaction, including retirement and
        // expiry checks at its one clock instant. Independent profile reads cannot do this.
        var acceptance = await _store!.FreezeAsync(selections, context).ConfigureAwait(false);
        return new(context with { }, rules, acceptance.Uses.Select(BenchReferenceEvaluator.CloneUse).ToArray(), false);
    }

    private void SelectionChanged()
    {
        if (_restoring) return;
        _contextChanged();
        Status = "Reference context changed. Save choices and confirm the session before Run.";
        RefreshState();
    }

    public async Task SaveChoicesAsync()
    {
        if (!CanEdit || _program is null) return;
        var generation = _generation;
        foreach (var row in Rules)
        {
            _settings.BenchReferenceSelections.RemoveAll(item => item.PlanId == _program.Id && item.RuleId == row.Rule.RuleId);
            if (row.SelectedChoice is { } choice)
                _settings.BenchReferenceSelections.Add(new() { PlanId = _program.Id, RuleId = row.Rule.RuleId, ReferenceId = choice.Profile.Id });
        }
        try
        {
            if (_settingsStore is not null) await _settingsStore.SaveAppSettingsAsync().ConfigureAwait(false);
            if (_settingsStore is not null && (!_settingsStore.IsSettingsWritable || !string.IsNullOrWhiteSpace(_settingsStore.LastPersistenceError)))
                throw new IOException(_settingsStore.LastPersistenceError ?? _settingsStore.SettingsSchemaWarning ?? "Settings storage is read-only.");
            await _onUi(() => { if (generation == _generation) Status = "Reference choices saved. Confirm the session before Run."; }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _onUi(() => { if (generation == _generation) Status = $"Choices are retained in this session but could not be saved: {ex.Message}"; }).ConfigureAwait(false);
        }
    }

    private async Task RetireAsync()
    {
        if (!CanManage || SelectedProfile?.Entry.Profile is not { } profile || _store is null) return;
        if (string.IsNullOrWhiteSpace(RetirementReason))
        { Status = "Enter a reason before retiring the selected profile."; RequestFocusField?.Invoke("Reference retirement reason"); return; }
        var generation = _generation;
        var completed = false;
        _managementPending = true;
        _contextChanged();
        Status = "Retiring the selected profile. Wait for the saved result before confirming or starting another run.";
        RefreshState();
        try
        {
            await _store.RetireAsync(profile.Id, RetirementReason.Trim()).ConfigureAwait(false);
            completed = true;
            await _onUi(() =>
            {
                if (generation != _generation) return;
                RetirementReason = string.Empty;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { await _onUi(() => { if (generation == _generation) Status = $"Could not retire reference: {ex.Message}"; }).ConfigureAwait(false); }
        finally
        {
            await _onUi(() =>
            {
                _managementPending = false;
                if (generation == _generation) _contextChanged();
                RefreshState();
            }).ConfigureAwait(false);
        }
        if (completed && generation == _generation)
        {
            await RefreshAsync().ConfigureAwait(false);
            await _onUi(() =>
            {
                if (generation == _generation) Status = $"Retired {profile.Name} / {profile.Id}. Historical runs retain their exact saved reference inputs. Confirm the session again before Run.";
            }).ConfigureAwait(false);
        }
    }

    public void RefreshState()
    {
        if (_isAcquisition && !_isEngineer() && !_isRunning())
        {
            _isAcquisition = false;
            _contextChanged();
        }
        this.RaisePropertyChanged(nameof(CanEdit)); this.RaisePropertyChanged(nameof(CanManage));
        this.RaisePropertyChanged(nameof(IsBusy)); this.RaisePropertyChanged(nameof(IsManagementPending));
        this.RaisePropertyChanged(nameof(ShowManagement));
        this.RaisePropertyChanged(nameof(CanRetire));
        this.RaisePropertyChanged(nameof(HasRules)); this.RaisePropertyChanged(nameof(IsReady));
        this.RaisePropertyChanged(nameof(Heading)); this.RaisePropertyChanged(nameof(ReadinessText));
        this.RaisePropertyChanged(nameof(SelectionSummary));
        this.RaisePropertyChanged(nameof(IsAcquisition));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
