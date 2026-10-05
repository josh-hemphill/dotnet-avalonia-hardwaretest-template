namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public string? EnvironmentPathError => Workspace is not null
        && !WorkspacePackPlan.TryResolveHomePath(Workspace, Prefs.OpenTapHomeOverride, out _, out var error) ? error : null;
    public IReadOnlyList<AuthoringPackageRequirement> EnvironmentPackages
    {
        get
        {
            if (Workspace is null) return [];
            if (!WorkspacePackPlan.TryResolveHomePath(Workspace, Prefs.OpenTapHomeOverride, out var path, out var error))
                return Workspace.Manifest.Dependencies.Select(d => new AuthoringPackageRequirement(d.Package, d.Version, "unavailable", false, false, error!))
                    .Concat(Workspace.Manifest.OptionalDependencies.Select(d => new AuthoringPackageRequirement(d.Package, d.Version, "unavailable", true, false, error!))).ToArray();
            return AuthoringEnvironmentAssessment.Packages(Workspace.Manifest, new(path!));
        }
    }
    public IReadOnlyList<string> EnvironmentRuntimeFiles => Workspace is null ? []
        : WorkspacePackPlan.TryResolveHomePath(Workspace, Prefs.OpenTapHomeOverride, out var path, out var error)
            ? AuthoringEnvironmentAssessment.RuntimeFiles(new(path!)) : [error!];
    public string LibraryEnvironmentReadinessText
    {
        get
        {
            var inspection = HardwareInspection;
            return inspection.Error ?? AuthoringInstrumentCatalog.LibraryReadiness(inspection.Home).Reason!;
        }
    }

    public bool CanDeclareLibraryDependency => Workspace is { IsReadOnly: false } && !OperationBusy
        && !Workspace.Manifest.Dependencies.Any(d => d.Package.Equals(AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase));

    public void DeclareLibraryDependency()
    {
        if (!CanDeclareLibraryDependency) throw new AuthoringWorkspaceException("Open a writable workspace without the library declaration and wait for the active operation.");
        RunCatalogEdit("Declare Instrument Components dependency", () =>
        {
            Workspace!.Manifest.Dependencies.Add(new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "^0.1.0" });
        });
        RefreshPackPreview();
        RaiseEnvironmentProperties();
        Status = "Instrument Components dependency staged. Use Save All before preparing/importing; Undo removes the staged declaration. Import the trusted library package or reuse a compatible installed package.";
    }

    public string EnvironmentRecoveryText => "Prepare the selected isolated authoring home from bundled prerequisites, or import a trusted offline .TapPackage/.zip. Required version mismatches must be repaired before build. These actions do not install a bench.";
    private IReadOnlyList<PackPreflightFinding> KnownEnvironmentBlockers => Workspace is not null && EnvironmentPathError is null
        ? AuthoringEnvironmentAssessment.BuildBlockers(Workspace.Manifest, new(AuthoringHomeText), allowMissingHome: true) : [];
    private bool HasKnownEnvironmentBlockers => KnownEnvironmentBlockers.Count != 0;
    private string EnvironmentBlockerText
    {
        get
        {
            var blockers = KnownEnvironmentBlockers;
            return blockers.Any(f => f.Code is "PACK_RUNTIME_MISSING" or "PACK_HOME_UNSAFE")
                ? "Repair unsafe or incomplete selected authoring home before building. " + blockers[0].Message
                : "Prepare or import missing required authoring packages before building.";
        }
    }
    private void RaiseEnvironmentProperties()
    {
        OnPropertyChanged(nameof(LibraryEnvironmentReadinessText));
        OnPropertyChanged(nameof(CanDeclareLibraryDependency));
        OnPropertyChanged(nameof(InstrumentTypeChoices));
        OnPropertyChanged(nameof(EnvironmentPathError));
        OnPropertyChanged(nameof(EnvironmentPackages));
        OnPropertyChanged(nameof(EnvironmentRuntimeFiles));
        RaisePackGuardProperties();
    }
}
