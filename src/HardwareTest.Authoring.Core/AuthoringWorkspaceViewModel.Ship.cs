namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public WorkspacePackPreview PackPreview { get; private set; } = WorkspacePackPlan.Empty;

    public IReadOnlyList<string> ExcludedPackPlans => _buildInspectionDispatch is not null ? _buildProgramCache.Where(program => !program.Included).Select(program => program.PlanId).ToArray() : Workspace is null ? [] : [.. AuthoringBuildInclusion.ProgramIds(Workspace).Where(id => !AuthoringBuildInclusion.Includes(Workspace.Manifest, id))];

    public bool HasExcludedPackPlans => ExcludedPackPlans.Count > 0;

    public string ShipPurpose => AuthoringChrome.ShipPurpose;

    public bool HasRawSteps => RawStepCount > 0;

    public int RawStepCount
        => SelectedProgram is null ? 0 : CountRaw(SelectedProgram.Measure);

    public string RawStepsBanner
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, AuthoringChrome.RawStepsBanner, RawStepCount);

    public bool HasLastPack => PackPreview.LastShipManifest is not null;

    public bool HasDeclaredPlugins => PackPreview.PluginProjects.Count > 0;

    public bool HasDeclaredShellApps => PackPreview.ShellAppProjects.Count > 0;

    public bool HasAuthoringHomePackages => PackPreview.AuthoringHomePackages.Count > 0;

    public IReadOnlyList<string> LastShippedFiles => PackPreview.LastShipManifest?.Files ?? [];

    public IReadOnlyList<string> LastShippedBakeTimeFiles
        => LastShippedFiles
            .Where(file => file.Contains(WorkspacePacker.BakeTimeSuffix, StringComparison.Ordinal))
            .ToArray();

    public bool HasPackageDependencies => PackPreview.PackageDependencies.Count > 0;

    public bool HasLastShippedBakeTimeFiles => LastShippedBakeTimeFiles.Count > 0;

    public bool HasInstrumentComponentsPackage
        => !string.IsNullOrWhiteSpace(PackPreview.InstrumentComponentsPackagePath);

    public string InstrumentComponentsText
        => HasInstrumentComponentsPackage
            ? $"InstrumentComponents package: {PackPreview.InstrumentComponentsPackagePath}"
            : string.Empty;

    public string OpenTapPinText
        => string.IsNullOrWhiteSpace(PackPreview.OpenTapVersionPin)
            ? "OpenTAP pin not declared in authoring.json"
            : $"OpenTAP {PackPreview.OpenTapVersionPin}";

    public string AuthoringHomeText
        => string.IsNullOrWhiteSpace(PackPreview.AuthoringHomePath)
            ? "OpenTAP home not bootstrapped yet (.authoring/opentap)"
            : PackPreview.AuthoringHomePath;

    public void RefreshPackPreview()
    {
        RequestEnvironmentInspection();
        if (_buildInspectionDispatch is not null)
        {
            RequestBuildInspection();
            return;
        }
        if (Workspace is null)
        {
            PackPreview = WorkspacePackPlan.Empty;
            RaisePackPreviewProperties();
            return;
        }

        var home = Prefs.OpenTapHomeOverride;
        PackPreview = WorkspacePackPlan.Describe(
            Workspace,
            string.IsNullOrWhiteSpace(home) ? null : home);
        if (LastCompletedBuild is { } completed)
            PackPreview = WorkspacePackPlan.WithLastPack(PackPreview, completed.Result.Manifest, completed.OutputDirectory);
        ClearResolvedHomePreviewWarning();
        RaisePackPreviewProperties();
    }

    private void RaisePackPreviewProperties()
    {
        RaiseBuildResultProperties();
        RaiseEnvironmentProperties();
        OnPropertyChanged(nameof(PackPreview));
        OnPropertyChanged(nameof(ExcludedPackPlans));
        OnPropertyChanged(nameof(HasExcludedPackPlans));
        OnPropertyChanged(nameof(HasLastPack));
        OnPropertyChanged(nameof(HasDeclaredPlugins));
        OnPropertyChanged(nameof(HasDeclaredShellApps));
        OnPropertyChanged(nameof(HasAuthoringHomePackages));
        OnPropertyChanged(nameof(HasPackageDependencies));
        OnPropertyChanged(nameof(HasLastShippedBakeTimeFiles));
        OnPropertyChanged(nameof(HasInstrumentComponentsPackage));
        OnPropertyChanged(nameof(InstrumentComponentsText));
        OnPropertyChanged(nameof(LastShippedFiles));
        OnPropertyChanged(nameof(LastShippedBakeTimeFiles));
        OnPropertyChanged(nameof(OpenTapPinText));
        OnPropertyChanged(nameof(AuthoringHomeText));
        OnPropertyChanged(nameof(EditingIssues));
        OnPropertyChanged(nameof(IssuesSummary));
    }

    internal void RaiseRawStepProperties()
    {
        OnPropertyChanged(nameof(RawStepCount));
        OnPropertyChanged(nameof(HasRawSteps));
        OnPropertyChanged(nameof(RawStepsBanner));
    }

    private static int CountRaw(IReadOnlyList<MeasureNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            switch (node)
            {
                case RawStepNode:
                    count++;
                    break;
                case SectionNode section:
                    count += CountRaw(section.Children);
                    break;
                case RepeatNode repeat:
                    count += CountRaw(repeat.Children);
                    break;
            }
        }

        return count;
    }
}
