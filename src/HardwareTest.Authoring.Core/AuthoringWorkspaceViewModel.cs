using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Avalonia-free workspace session: program list, metric editor, compiler Save, preview.
public sealed partial class AuthoringWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly IPlanCompiler _compiler;
    private AuthoringWorkspace? _workspace;
    private IReadOnlyList<ProgramDraft> _programs = [];
    private ProgramDraft? _selectedProgram;
    private IReadOnlyList<PlanContractFinding> _findings = [];
    private IReadOnlyList<string> _measureTree = [];
    private string? _status;
    private string? _error;
    private int _selectedMeasureIndex = -1;
    private string? _selectedRecipeId;
    private IReadOnlyList<RunDataset> _datasets = [];
    private IReadOnlyList<string> _datasetItems = [];
    private int _selectedDatasetIndex = -1;

    public AuthoringWorkspaceViewModel(IPlanCompiler? compiler = null)
    {
        _compiler = compiler ?? new PlanCompiler();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<AuthoringRecipe> Recipes => AuthoringRecipeCatalog.Palette;

    public AuthoringWorkspace? Workspace
    {
        get => _workspace;
        private set => SetField(ref _workspace, value);
    }

    public IReadOnlyList<ProgramDraft> Programs
    {
        get => _programs;
        private set => SetField(ref _programs, value);
    }

    public ProgramDraft? SelectedProgram
    {
        get => _selectedProgram;
        private set
        {
            if (SetField(ref _selectedProgram, value))
            {
                RefreshMeasurePresentation();
            }
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
            ? "What do you want to measure? Pick a recipe."
            : null;

    public void ReportError(string message)
    {
        Error = message;
        Status = message;
    }

    public string DisplayName
    {
        get => SelectedProgram?.Sidecar.DisplayName ?? string.Empty;
        set
        {
            if (SelectedProgram is null)
            {
                return;
            }

            SelectedProgram.Sidecar.DisplayName = value;
            OnPropertyChanged();
        }
    }

    public string DutFamily
    {
        get => SelectedProgram?.Sidecar.DutFamily ?? string.Empty;
        set
        {
            if (SelectedProgram is null)
            {
                return;
            }

            SelectedProgram.Sidecar.DutFamily = value;
            OnPropertyChanged();
        }
    }

    public bool RequireSerial
    {
        get => SelectedProgram?.Sidecar.RequireSerial ?? false;
        set
        {
            if (SelectedProgram is null)
            {
                return;
            }

            SelectedProgram.Sidecar.RequireSerial = value;
            OnPropertyChanged();
        }
    }

    public void Open(string root)
    {
        Error = null;
        var files = AuthoringWorkspaceLoader.Load(root);
        var draft = _compiler.LoadAll(files);
        Workspace = files;
        Programs = draft.Programs;
        SelectedProgram = draft.Programs.FirstOrDefault();
        Findings = [];
        Status = $"{files.Manifest.DisplayName}: {draft.Programs.Count} program(s)";
        RefreshDatasets();
        RaiseSidecarProperties();
    }

    public void SelectProgram(string planId)
    {
        SelectedProgram = Programs.FirstOrDefault(p =>
            string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase));
        RaiseSidecarProperties();
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
        var all = Workspace is null ? [] : RunDatasetCatalog.List(Workspace);
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

    public void CreateProgram(string? planId = null)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before creating a program.");
        }

        var id = string.IsNullOrWhiteSpace(planId) ? NextProgramId() : planId.Trim();
        if (Programs.Any(p => string.Equals(p.PlanId, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AuthoringWorkspaceException($"Program '{id}' already exists in this session.");
        }

        var created = AuthoringRecipeCatalog.CreateProgram(id);
        Programs = [.. Programs, created];
        SelectedProgram = created;
        Status = SelectedProgram.Measure.Count == 0
            ? "What do you want to measure? Pick a recipe."
            : $"Created {id}";
        Error = null;
        RefreshDatasets();
        RaiseSidecarProperties();
    }

    public void ApplyRecipe(string recipeId)
    {
        if (SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Select a program before adding a recipe.");
        }

        var updated = AuthoringRecipeCatalog.Apply(SelectedProgram, recipeId);
        ReplaceSelected(updated);
        if (updated.Measure.Count > 0)
        {
            SelectMeasure(updated.Measure.Count - 1);
        }

        Status = string.Equals(recipeId, AuthoringRecipeIds.TestGroup, StringComparison.OrdinalIgnoreCase)
            ? "Setup, measure, and Cleanup groups are written when you save the plan."
            : $"Added {recipeId}";
        Error = null;
    }

    public void Apply()
    {
        if (Workspace is null || SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace and select a program before applying.");
        }

        if (Workspace.IsReadOnly)
        {
            throw new AuthoringWorkspaceException("Workspace is read-only; cannot save the plan.");
        }

        AuthoringRecipeCatalog.EnsureScalarLimits(SelectedProgram);
        var planId = SelectedProgram.PlanId;
        var tapPlanPath = ResolveTapPlanPath(planId);
        var others = Programs
            .Where(p => !string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _compiler.Save(SelectedProgram, tapPlanPath);
        Open(Workspace.Root);
        Programs = MergeSessionPrograms(Programs, others);
        SelectProgram(planId);
        Status = $"Saved {Path.GetFileName(tapPlanPath)}";
        Error = null;
    }

    public void SaveSidecar()
    {
        if (Workspace is null || SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace and select a program before saving.");
        }

        var tapPlanPath = Workspace.TapPlanPaths.FirstOrDefault(path =>
            string.Equals(
                Path.GetFileNameWithoutExtension(path),
                SelectedProgram.PlanId,
                StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(tapPlanPath))
        {
            throw new AuthoringWorkspaceException($"No TapPlan path for '{SelectedProgram.PlanId}'.");
        }

        _compiler.SaveSidecar(tapPlanPath, SelectedProgram.Sidecar);
        Status = $"Saved {Path.GetFileName(PlanCompiler.SidecarPath(tapPlanPath))}";
        Error = null;
    }

    public PlanContractBatchReport Validate(bool strict = true)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before validating.");
        }

        var report = PlanContractValidator.Validate(
            Workspace.TapPlanPaths,
            new PlanContractOptions
            {
                Strict = strict,
                ExcludeVisaAdapter = true,
            });
        Findings = report.Plans.SelectMany(p => p.Findings).ToArray();
        Status = report.HasErrors
            ? $"{report.ErrorCount} contract error(s)"
            : $"{report.WarningCount} contract warning(s)";
        Error = report.HasErrors ? Status : null;
        return report;
    }

    public ShipManifest Pack(string outputDirectory, PackOptions? options = null)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before packing.");
        }

        var manifest = WorkspacePacker.Pack(
            Workspace,
            outputDirectory,
            options ?? new PackOptions { Offline = true });
        Status = $"Packed {manifest.PackageName} {manifest.Version}";
        Error = null;
        return manifest;
    }

    public OpenTapHome Bootstrap(BootstrapOptions? options = null)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before bootstrap.");
        }

        var home = new OpenTapHomeBootstrapper().Bootstrap(
            Workspace,
            options ?? new BootstrapOptions { Offline = true });
        Status = $"OpenTAP home {home.Root}";
        Error = null;
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

    private void ReplaceSelected(ProgramDraft draft)
    {
        Programs = Programs.Select(p =>
                string.Equals(p.PlanId, draft.PlanId, StringComparison.OrdinalIgnoreCase) ? draft : p)
            .ToArray();
        SelectedProgram = draft;
        RaiseSidecarProperties();
    }

    private static IReadOnlyList<ProgramDraft> MergeSessionPrograms(
        IReadOnlyList<ProgramDraft> fromDisk,
        IReadOnlyList<ProgramDraft> sessionOthers)
    {
        var merged = fromDisk
            .Select(disk => sessionOthers.FirstOrDefault(other =>
                string.Equals(other.PlanId, disk.PlanId, StringComparison.OrdinalIgnoreCase)) ?? disk)
            .ToList();
        foreach (var other in sessionOthers)
        {
            if (!merged.Any(p => string.Equals(p.PlanId, other.PlanId, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(other);
            }
        }

        return merged;
    }

    private string ResolveTapPlanPath(string planId)
    {
        var existing = Workspace!.TapPlanPaths.FirstOrDefault(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), planId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var relative = string.IsNullOrWhiteSpace(Workspace.Manifest.PlansDirectory)
            ? "."
            : Workspace.Manifest.PlansDirectory.Trim();
        var directory = Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(Workspace.Root, relative));
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

    private void RaiseSidecarProperties()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DutFamily));
        OnPropertyChanged(nameof(RequireSerial));
        RaiseEditorProperties();
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
