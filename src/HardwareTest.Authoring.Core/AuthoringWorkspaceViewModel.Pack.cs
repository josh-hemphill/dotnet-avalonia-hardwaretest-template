namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private bool _packCheckedClone;
    public PackPreflightReport? LastPackPreflight { get; private set; }
    public string PackPreflightHomeText => LastPackPreflight?.Home is { } home
        ? _packCheckedClone ? $"Checked isolated home cloned from selected path: {home.Root}"
            : $"Checked authoring home: {home.Root}; compatibility home: {LastPackPreflight.TuiHome?.Root}" : string.Empty;
    public IReadOnlyList<PackPreflightFinding> PackPreflightFindings => LastPackPreflight?.Findings ?? [];
    public IReadOnlyList<string> DirtyProgramIds
    {
        get
        {
            ObserveContentDirty();
            return _dirtyPlans.Concat(_dirtySidecars)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
    public bool CanPack => !OperationBusy && Workspace is not null && (_buildInspectionDispatch is not null ? !BuildInspectionBusy && _buildInspectionWritable : WorkspacePacker.IsWritableWorkspace(Workspace)) && !HasUnsavedChanges && !HasKnownSavedBuildBlockers && EnvironmentPathError is null && !HasKnownEnvironmentBlockers;
    public string PackGuardText => Workspace is { } workspace && _measureSectionMigrations.Any(id => AuthoringBuildInclusion.Includes(workspace.Manifest, id))
        ? "Use Save All to persist the configurable test sections before building."
        : BuildInspectionBusy ? "Checking saved build inputs…" : EnvironmentPathError ?? (HasKnownSavedBuildBlockers ? "Repair incomplete saved deployment input or reconcile source conflicts before building." : HasKnownEnvironmentBlockers ? EnvironmentBlockerText : HasUnsavedChanges
        ? WorkspaceCatalogDirty ? "Use Save All to save workspace catalog changes and edited programs before packing." : $"Save edited programs before packing: {string.Join(", ", DirtyProgramIds)}"
        : "Pack checks saved plans, required packages, plugin catalogs and in-process load/save round trips.");

    private void RaisePackGuardProperties()
    {
        OnPropertyChanged(nameof(BuildReadinessText));
        OnPropertyChanged(nameof(BuildPrograms));
        OnPropertyChanged(nameof(CanPack));
        OnPropertyChanged(nameof(CanInitializePlan));
        OnPropertyChanged(nameof(DirtyProgramIds));
        OnPropertyChanged(nameof(PackGuardText));
    }

    private void RetainPackPreflight(PackPreflightReport report)
    {
        _packCheckedClone = false;
        LastPackPreflight = report;
        OnPropertyChanged(nameof(LastPackPreflight));
        OnPropertyChanged(nameof(PackPreflightHomeText));
        OnPropertyChanged(nameof(PackPreflightFindings));
    }

    public ShipManifest Pack(string outputDirectory, PackOptions? options = null)
    {
        if (!CanPack)
        {
            var report = new PackPreflightReport(EnvironmentPathError is { } homeError
                ? [new PackPreflightFinding("PACK_HOME_INVALID", homeError, true)]
                : HasUnsavedChanges
                ? DirtyProgramIds.Select(id => new PackPreflightFinding("PACK_DIRTY", $"Save program '{id}' before packing.", true))
                    .Concat(WorkspaceCatalogDirty ? [new PackPreflightFinding("PACK_CATALOG_DIRTY", "Use Save All to save workspace catalog changes before packing.", true)] : []).ToArray()
                : HasKnownSavedBuildBlockers
                    ? [new PackPreflightFinding("PACK_SAVED_INPUT", "Repair incomplete deployment input or reconcile source conflicts before building.", true)]
                    : HasKnownEnvironmentBlockers
                        ? KnownEnvironmentBlockers
                    : [new PackPreflightFinding("PACK_WORKSPACE", "Open a writable workspace before packing.", true)]);
            RetainPackPreflight(report);
            throw new PackPreflightException(report);
        }
        var checkedContentIdentity = ContentFingerprint();
        var resolved = new PackOptions
        {
            CancellationToken = options?.CancellationToken ?? default,
            Home = options?.Home,
            TuiHome = options?.TuiHome,
            Compat = options?.Compat,
            Offline = options?.Offline ?? true,
            BootstrapHomeDirectory = options?.BootstrapHomeDirectory ?? Prefs.OpenTapHomeOverride,
            PreflightCompleted = report =>
            {
                RetainPackPreflight(report);
                options?.PreflightCompleted?.Invoke(report);
            },
        };
        var request = AuthoringBuildService.CaptureSaved(Workspace!, resolved);
        var checkedHome = request.Options.Home!.Root;
        var checkedBuildIdentity = checkedContentIdentity + "|" + checkedHome;
        var result = AuthoringBuildService.Execute(request, outputDirectory, resolved.CancellationToken);
        var manifest = result.Manifest;
        _packPreview = WorkspacePackPlan.Describe(Workspace!, Prefs.OpenTapHomeOverride);
        _packPreview = WorkspacePackPlan.WithLastPack(_packPreview, manifest, outputDirectory);
        RetainCompletedBuild(result, outputDirectory, checkedHome, checkedBuildIdentity);
        RaisePackPreviewProperties();
        Status = $"Packed {manifest.PackageName} {manifest.Version}";
        Error = null;
        return manifest;
    }
}
