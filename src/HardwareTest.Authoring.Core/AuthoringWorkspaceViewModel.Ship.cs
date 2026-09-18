namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private WorkspacePackPreview _packPreview = WorkspacePackPlan.Empty;

    public WorkspacePackPreview PackPreview => _packPreview;

    public string ShipPurpose => AuthoringChrome.ShipPurpose;

    public string RecipeAdvancedHint => AuthoringChrome.RecipeAdvancedHint;

    public bool HasRawSteps => RawStepCount > 0;

    public int RawStepCount
        => SelectedProgram is null ? 0 : CountRaw(SelectedProgram.Measure);

    public string RawStepsBanner
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, AuthoringChrome.RawStepsBanner, RawStepCount);

    public bool HasLastPack => _packPreview.LastShipManifest is not null;

    public bool HasDeclaredPlugins => _packPreview.PluginProjects.Count > 0;

    public bool HasDeclaredShellApps => _packPreview.ShellAppProjects.Count > 0;

    public bool HasAuthoringHomePackages => _packPreview.AuthoringHomePackages.Count > 0;

    public IReadOnlyList<string> LastShippedFiles => _packPreview.LastShipManifest?.Files ?? [];

    public IReadOnlyList<string> LastShippedBakeTimeFiles
        => LastShippedFiles
            .Where(file => file.Contains(WorkspacePacker.BakeTimeSuffix, StringComparison.Ordinal))
            .ToArray();

    public bool HasPackageDependencies => _packPreview.PackageDependencies.Count > 0;

    public bool HasLastShippedBakeTimeFiles => LastShippedBakeTimeFiles.Count > 0;

    public bool HasInstrumentComponentsPackage
        => !string.IsNullOrWhiteSpace(_packPreview.InstrumentComponentsPackagePath);

    public string InstrumentComponentsText
        => HasInstrumentComponentsPackage
            ? $"InstrumentComponents package: {_packPreview.InstrumentComponentsPackagePath}"
            : string.Empty;

    public string OpenTapPinText
        => string.IsNullOrWhiteSpace(_packPreview.OpenTapVersionPin)
            ? "OpenTAP pin not declared in authoring.json"
            : $"OpenTAP {_packPreview.OpenTapVersionPin}";

    public string AuthoringHomeText
        => string.IsNullOrWhiteSpace(_packPreview.AuthoringHomePath)
            ? "OpenTAP home not bootstrapped yet (.authoring/opentap)"
            : _packPreview.AuthoringHomePath;

    public void RefreshPackPreview()
    {
        if (Workspace is null)
        {
            _packPreview = WorkspacePackPlan.Empty;
            RaisePackPreviewProperties();
            return;
        }

        var home = Prefs.OpenTapHomeOverride;
        _packPreview = WorkspacePackPlan.Describe(
            Workspace,
            string.IsNullOrWhiteSpace(home) ? null : home);
        RaisePackPreviewProperties();
    }

    private void RaisePackPreviewProperties()
    {
        OnPropertyChanged(nameof(PackPreview));
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
                case RepeatNode repeat:
                    count += CountRaw(repeat.Children);
                    break;
            }
        }

        return count;
    }
}
