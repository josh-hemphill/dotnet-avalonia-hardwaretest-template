using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Avalonia-free workspace session: program list, metric editor, compiler Save, preview.
public sealed partial class AuthoringWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly IPlanCompiler _compiler;
    private readonly IAuthoringPreferencesStore? _preferences;
    private AuthoringWorkspace? _workspace;
    private IReadOnlyList<ProgramDraft> _programs = [];
    private ProgramDraft? _selectedProgram;
    private IReadOnlyList<PlanContractFinding> _findings = [];
    private IReadOnlyList<AuthoringFindingRow> _findingRows = [];
    private IReadOnlyList<string> _measureTree = [];
    private string? _status;
    private string? _error;
    private int _selectedMeasureIndex = -1;
    private string? _selectedRecipeId;
    private IReadOnlyList<RunDataset> _datasets = [];
    private IReadOnlyList<RunDataset>? _openingDatasets;
    private IReadOnlyList<string> _datasetItems = [];
    private int _selectedDatasetIndex = -1;
    private readonly HashSet<string> _dirtyPlans = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtySidecars = new(StringComparer.OrdinalIgnoreCase);

    public bool HasUnsavedChanges
    {
        get
        {
            ObserveContentDirty();
            return _dirtyPlans.Count > 0 || _dirtySidecars.Count > 0 || WorkspaceCatalogDirty;
        }
    }
    public string ValidationScope => HasUnsavedChanges
        ? WorkspaceCatalogDirty ? "Use Save All to save workspace catalog changes and edited programs before validating saved TapPlans." : "Save all edited programs before validating saved TapPlans."
        : "Validation checks saved TapPlans in this workspace.";

    private void RefreshDirtyState()
    {
        RefreshProgramRows();
        OnPropertyChanged(nameof(DirtyPrograms));
        OnPropertyChanged(nameof(UnsavedChangesSummary));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RaisePackGuardProperties();
        OnPropertyChanged(nameof(ValidationScope));
    }

    public AuthoringWorkspaceViewModel(IPlanCompiler? compiler = null, IAuthoringPreferencesStore? preferences = null)
    {
        _compiler = compiler ?? new PlanCompiler();
        _preferences = preferences;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<AuthoringRecipe> Recipes => AuthoringRecipeCatalog.Palette
        .Where(recipe => string.IsNullOrWhiteSpace(RecipeSearch) || $"{recipe.ListLabel} {recipe.Summary}".Contains(RecipeSearch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(recipe => recipe.Category switch { "Measure" => 0, "Check" => 1, "Operator action" => 2, _ => 3 }).ToArray();
    public bool HasWorkspace => Workspace is not null;

    public AuthoringWorkspace? Workspace
    {
        get => _workspace;
        private set
        {
            if (SetField(ref _workspace, value))
            {
                OnPropertyChanged(nameof(HasWorkspace));
                RaisePackGuardProperties();
            }
        }
    }

    public IReadOnlyList<ProgramDraft> Programs
    {
        get => _programs;
        private set
        {
            if (SetField(ref _programs, value)) RefreshProgramRows();
        }
    }

    public ProgramDraft? SelectedProgram
    {
        get => _selectedProgram;
        set
        {
            if (value is null)
            {
                return;
            }

            if (ReferenceEquals(_selectedProgram, value))
            {
                return;
            }

            if (string.Equals(_selectedProgram?.PlanId, value.PlanId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectProgram(value.PlanId);
        }
    }

    public IReadOnlyList<string> MeasureTree
    {
        get => _measureTree;
        private set => SetField(ref _measureTree, value);
    }

    public IReadOnlyList<PlanContractFinding> Findings
    {
        get => _findings;
        private set => SetField(ref _findings, value);
    }

    public IReadOnlyList<AuthoringFindingRow> FindingRows
    {
        get => _findingRows;
        private set { if (SetField(ref _findingRows, value)) OnPropertyChanged(nameof(IssuesSummary)); }
    }

    public IReadOnlyList<RunDataset> Datasets => _datasets;

    public IReadOnlyList<string> DatasetItems => _datasetItems;

    public int SelectedDatasetIndex
    {
        get => _selectedDatasetIndex;
        set => SelectDataset(value);
    }

    public RunDataset? SelectedDataset
        => _selectedDatasetIndex < 0 || _selectedDatasetIndex >= _datasets.Count
            ? null
            : _datasets[_selectedDatasetIndex];

    public string? Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetField(ref _error, value);
    }

    public string? MeasureHint
        => SelectedProgram is not null && SelectedProgram.Measure.Count == 0
            ? AuthoringChrome.EmptyMeasureHint
            : null;

    public void ReportError(string message)
    {
        if (HasWorkspace && HasUnsavedChanges && message == ValidationScope)
        {
            Error = null;
            Status = null;
            OnPropertyChanged(nameof(ValidationScope));
            return;
        }
        Error = message;
        Status = message;
    }

    public string DisplayName
    {
        get => SelectedProgram?.Sidecar.DisplayName ?? string.Empty;
        set
        {
            if (SelectedProgram is null
                || string.Equals(DisplayName, value, StringComparison.Ordinal))
            {
                return;
            }

            SetSidecar(sidecar => sidecar.DisplayName = value);
            OnPropertyChanged();
        }
    }

    public string DutFamily
    {
        get => SelectedProgram?.Sidecar.DutFamily ?? string.Empty;
        set
        {
            if (SelectedProgram is null
                || string.Equals(DutFamily, value, StringComparison.Ordinal))
            {
                return;
            }

            SetSidecar(sidecar => sidecar.DutFamily = value);
            OnPropertyChanged();
        }
    }

    public void Open(string root)
    {
        if (HasUnsavedChanges)
            throw new AuthoringWorkspaceException("Use Save All to save edited programs and workspace catalog changes, or explicitly discard them, before opening a workspace.");
        CommitOpen(PrepareOpen(root));
    }

    public PreparedAuthoringWorkspace PrepareOpen(string root)
    {
        var draft = LoadWithSources(root);
        var home = Prefs.OpenTapHomeOverride;
        var preview = WorkspacePackPlan.Describe(draft.Files, string.IsNullOrWhiteSpace(home) ? null : home);
        var datasets = RunDatasetCatalog.List(draft.Files);
        return new PreparedAuthoringWorkspace(draft, preview, datasets);
    }

    public void CommitOpen(PreparedAuthoringWorkspace prepared, bool discardUnsavedChanges = false)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (HasUnsavedChanges && !discardUnsavedChanges)
            throw new AuthoringWorkspaceException("Use Save All to save edited programs and workspace catalog changes, or explicitly discard them, before opening a workspace.");
        var files = prepared.Draft.Files;
        _openingDatasets = prepared.Datasets;
        try
        {
            _dirtyPlans.Clear();
            _dirtySidecars.Clear();
            ReplaceOperationWorkspace();
            WorkspaceCatalogDirty = false;
            WorkspaceCatalogSaveFailure = null;
            Workspace = files;
            InitializeDocuments(prepared.Draft.Programs);
            _selectedInstrumentSlot = null;
            AssignSelectedProgram(Programs.FirstOrDefault());
            RefreshDirtyState();
            Findings = [];
            FindingRows = [];
            LastPackPreflight = null;
            OnPropertyChanged(nameof(LastPackPreflight));
            OnPropertyChanged(nameof(PackPreflightHomeText));
            OnPropertyChanged(nameof(PackPreflightFindings));
            LastSaveAllResult = null;
            SavePreviewWarning = null;
            OnPropertyChanged(nameof(LastSaveAllResult));
            OnPropertyChanged(nameof(SaveAllResults));
            Error = null;
            InitializeSourceState();
            Status = $"{files.Manifest.DisplayName}: {Programs.Count} program(s)";
            RefreshDatasets();
            RememberLastWorkspace(files.Root);
            RaiseSidecarProperties();
            _packPreview = prepared.PackPreview;
            RaisePackPreviewProperties();
        }
        finally { _openingDatasets = null; }
    }

    public void SelectProgram(string planId)
    {
        RememberNodeSelection();
        _selectedInstrumentSlot = null;
        AssignSelectedProgram(Programs.FirstOrDefault(p =>
            string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase)));
        RestoreNodeSelection(SelectedDocument?.SelectedNodeId);
        RaiseSidecarProperties();
        RaiseHistoryProperties();
    }

    public void SelectDataset(int index)
    {
        var count = _datasets.Count;
        var clamped = count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
        if (!SetField(ref _selectedDatasetIndex, clamped, nameof(SelectedDatasetIndex)))
        {
            OnPropertyChanged(nameof(SelectedDataset));
            RaiseEditorProperties();
            return;
        }

        OnPropertyChanged(nameof(SelectedDataset));
        RaiseEditorProperties();
    }

    private void RefreshDatasets()
    {
        var all = _openingDatasets ?? (Workspace is null ? [] : RunDatasetCatalog.List(Workspace));
        var planId = SelectedProgram?.PlanId;
        _datasets = string.IsNullOrWhiteSpace(planId)
            ? []
            : all.Where(dataset =>
                    string.Equals(dataset.Run.PlanId, planId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        _datasetItems = _datasets.Select(FormatDataset).ToArray();
        _selectedDatasetIndex = _datasets.Count == 0
            ? -1
            : Math.Clamp(_selectedDatasetIndex < 0 ? 0 : _selectedDatasetIndex, 0, _datasets.Count - 1);
        OnPropertyChanged(nameof(Datasets));
        OnPropertyChanged(nameof(DatasetItems));
        OnPropertyChanged(nameof(SelectedDatasetIndex));
        OnPropertyChanged(nameof(SelectedDataset));
    }

    private static string FormatDataset(RunDataset dataset)
    {
        var id = string.IsNullOrWhiteSpace(dataset.Run.RunId)
            ? Path.GetFileName(Path.GetDirectoryName(dataset.Path)) ?? "run"
            : dataset.Run.RunId;
        return string.IsNullOrWhiteSpace(dataset.Run.DutSerial)
            ? id
            : $"{id} ({dataset.Run.DutSerial})";
    }

    public void ApplyRecipe(string recipeId)
    {
        if (SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Select a program before adding a recipe.");
        }

        EnsureWritableWorkspace("add a recipe");
        EnsurePresentedHistoryCurrent();
        var updated = recipeId == AuthoringRecipeIds.Repeat
            ? AuthoringSequenceOperations.Repeat(SelectedProgram, SelectedSequence)
            : AuthoringRecipeCatalog.Apply(SelectedProgram, recipeId, SelectedInstrumentSlot);
        if (AuthoringDocumentSnapshot.Capture(SelectedProgram).ContentEquals(AuthoringDocumentSnapshot.Capture(updated)))
        {
            Status = recipeId == AuthoringRecipeIds.TestGroup ? AuthoringChrome.TestGroupHint : "No change to the sequence.";
            Error = null;
            return;
        }
        ReplaceSelected(updated);
        if (updated.Measure.Count > 0 && recipeId != AuthoringRecipeIds.Repeat)
        {
            SelectMeasure(updated.Measure.Count - 1);
        }
        SelectedDocument?.CompleteEditSelection();

        Status = string.Equals(recipeId, AuthoringRecipeIds.TestGroup, StringComparison.OrdinalIgnoreCase)
            ? AuthoringChrome.TestGroupHint
            : $"Added {recipeId}";
        Error = null;
    }

    public PlanContractBatchReport Validate(bool strict = true)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before validating.");
        }

        if (HasUnsavedChanges)
        {
            throw new AuthoringWorkspaceException(ValidationScope);
        }

        var checkedState = PrepareFindingCheck();
        var report = ValidateSavedPlans(
            Workspace.TapPlanPaths,
            new PlanContractOptions
            {
                Strict = strict,
                ExcludeVisaAdapter = !AuthoringInstrumentCatalog.DeclaresVisa(Workspace),
            });
        AcceptFindings(report, checkedState);
        SetFindingValidationStatus(report);
        return report;
    }

    public OpenTapHome Bootstrap(BootstrapOptions? options = null)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before bootstrap.");
        }

        var home = new OpenTapHomeBootstrapper().Bootstrap(Workspace, ResolveBootstrapOptions(options));
        Status = $"OpenTAP home {home.Root}";
        Error = null;
        RefreshPackPreview();
        return home;
    }

    public static IReadOnlyList<string> DescribeMeasure(IReadOnlyList<MeasureNode> nodes)
        => DescribeMeasure(nodes, indent: string.Empty).ToArray();

    private static IEnumerable<string> DescribeMeasure(IReadOnlyList<MeasureNode> nodes, string indent)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode metric:
                    yield return $"{indent}{metric.Metric.Name} [{metric.Metric.DisplayRole}] {metric.Metric.ChannelKey}";
                    break;
                case RepeatNode repeat:
                    yield return $"{indent}Repeat x{repeat.Count}";
                    foreach (var child in DescribeMeasure(repeat.Children, indent + "  "))
                    {
                        yield return child;
                    }

                    break;
                case RawStepNode raw:
                    yield return $"{indent}{raw.TypeName}";
                    break;
                default:
                    yield return $"{indent}{node.GetType().Name}";
                    break;
            }
        }
    }

    internal void ReplaceSelected(ProgramDraft draft, bool rebuildLists = true)
    {
        if (!CommitDocument(draft)) return;
        var samePlan = string.Equals(_selectedProgram?.PlanId, draft.PlanId, StringComparison.OrdinalIgnoreCase);
        Programs = Programs.Select(p =>
                string.Equals(p.PlanId, draft.PlanId, StringComparison.OrdinalIgnoreCase) ? draft : p)
            .ToArray();
        _selectedProgram = draft;
        OnPropertyChanged(nameof(SelectedProgram));
        if (rebuildLists || !samePlan)
        {
            RefreshMeasurePresentation();
        }
        else
        {
            RefreshSequencePresentation();
            RaiseEditorProperties();
        }

        RememberNodeSelection();
        SelectedDocument?.CompleteEditSelection();
        RecomputeDocumentDirty();
        RaiseSidecarProperties();
    }

    internal void MapAllPrograms(Func<ProgramDraft, ProgramDraft> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var selectedId = _selectedProgram?.PlanId;
        Programs = Programs.Select(mutate).ToArray();
        foreach (var program in Programs)
        {
            CommitDocument(program);
        }
        if (selectedId is not null)
        {
            _selectedProgram = Programs.FirstOrDefault(program =>
                string.Equals(program.PlanId, selectedId, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(SelectedProgram));
        }

        RecomputeDocumentDirty();
        RaiseSidecarProperties();
    }

    private string? TryExistingTapPlanPath(string planId)
        => Workspace?.TapPlanPaths.FirstOrDefault(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), planId, StringComparison.OrdinalIgnoreCase));

    private string ResolveTapPlanPath(string planId)
    {
        var workspace = Workspace
            ?? throw new AuthoringWorkspaceException("Open a workspace before resolving a TapPlan path.");
        var existing = TryExistingTapPlanPath(planId);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var relative = AuthoringManifest.RelativePlansDirectory(workspace.Manifest.PlansDirectory);
        var directory = Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(workspace.Root, relative));
        return Path.Combine(directory, $"{planId}.TapPlan");
    }

    private string NextProgramId()
    {
        for (var i = 1; i < 1000; i++)
        {
            var id = $"program-{i}";
            if (!Programs.Any(p => string.Equals(p.PlanId, id, StringComparison.OrdinalIgnoreCase)))
            {
                return id;
            }
        }

        return "program-" + Guid.NewGuid().ToString("N")[..8];
    }

    private static ProgramDraft WithCatalogSlots(ProgramDraft draft, AuthoringManifest manifest)
    {
        var extra = manifest.Catalogs?.InstrumentSlotNames;
        if (extra is null || extra.Count == 0)
        {
            return draft;
        }

        var instruments = draft.Instruments.ToList();
        var typeId = instruments.FirstOrDefault()?.TypeId
                     ?? typeof(HardwareTest.OpenTap.Plugins.Basic.MockDmmInstrument).FullName!;
        foreach (var raw in extra)
        {
            var slot = AuthoringWorkspaceCatalog.Normalize(raw);
            if (slot is null
                || instruments.Any(instrument =>
                    string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            instruments.Add(new InstrumentRef(slot, typeId, $"MOCK::INSTR{instruments.Count}"));
        }

        return draft with { Instruments = instruments };
    }

    private void AssignSelectedProgram(ProgramDraft? draft)
    {
        if (SetField(ref _selectedProgram, draft, nameof(SelectedProgram)))
        {
            _selectedSequenceKey = null;
            _selectedMeasureIndex = -1;
            RefreshMeasurePresentation();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Failed to delete '{path}'.", ex);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        if (name == nameof(SelectedProgram))
        {
            OnPropertyChanged(nameof(SelectedProgramRow));
            RaiseHistoryProperties();
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
