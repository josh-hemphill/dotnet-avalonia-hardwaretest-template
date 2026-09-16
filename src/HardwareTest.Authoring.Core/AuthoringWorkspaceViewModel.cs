using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Avalonia-free workspace session: program list, read-only tree, sidecar-only save.
public sealed class AuthoringWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly IPlanCompiler _compiler;
    private AuthoringWorkspace? _workspace;
    private IReadOnlyList<ProgramDraft> _programs = [];
    private ProgramDraft? _selectedProgram;
    private IReadOnlyList<PlanContractFinding> _findings = [];
    private IReadOnlyList<string> _measureTree = [];
    private string? _status;
    private string? _error;

    public AuthoringWorkspaceViewModel(IPlanCompiler? compiler = null)
    {
        _compiler = compiler ?? new PlanCompiler();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
                MeasureTree = value is null ? [] : DescribeMeasure(value.Measure);
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
        RaiseSidecarProperties();
    }

    public void SelectProgram(string planId)
    {
        SelectedProgram = Programs.FirstOrDefault(p =>
            string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase));
        RaiseSidecarProperties();
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

    private void RaiseSidecarProperties()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DutFamily));
        OnPropertyChanged(nameof(RequireSerial));
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
