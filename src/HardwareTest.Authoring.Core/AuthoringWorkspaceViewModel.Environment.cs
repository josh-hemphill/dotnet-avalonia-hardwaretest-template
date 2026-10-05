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
    public string EnvironmentRecoveryText => "Prepare the selected isolated authoring home from bundled prerequisites, or import a trusted offline .TapPackage/.zip. Required version mismatches must be repaired before build. These actions do not install a bench.";
    private IReadOnlyList<PackPreflightFinding> KnownEnvironmentBlockers => Workspace is not null && EnvironmentPathError is null
        && Directory.Exists(AuthoringHomeText)
        ? AuthoringEnvironmentAssessment.BuildBlockers(Workspace.Manifest, new(AuthoringHomeText)) : [];
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
        OnPropertyChanged(nameof(EnvironmentPathError));
        OnPropertyChanged(nameof(EnvironmentPackages));
        OnPropertyChanged(nameof(EnvironmentRuntimeFiles));
        RaisePackGuardProperties();
    }
}
