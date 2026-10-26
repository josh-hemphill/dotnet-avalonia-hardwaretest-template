namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public string? EnvironmentPathError => _environmentInspection.Error;
    public IReadOnlyList<AuthoringPackageRequirement> EnvironmentPackages => _environmentInspection.Packages;
    public IReadOnlyList<string> EnvironmentRuntimeFiles => _environmentInspection.RuntimeFiles;
    public string LibraryEnvironmentReadinessText => _environmentInspection.Error ?? _environmentInspection.Library.Reason ?? string.Empty;
    public bool RequiresInstrumentLibrary => Workspace?.Manifest.Dependencies.Any(dependency =>
        dependency.Package.Equals(AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase)) == true;
    public string StandaloneVisaReadinessText => _environmentInspection.VisaText;

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
        Status = "Instrument Components dependency staged. Use Save All before preparing/importing; Undo removes the staged declaration. Prepare installs the bundled library or reuses a compatible installed package.";
    }

    public string EnvironmentRecoveryText => "Preparation uses saved requirements. Downloads are checked before updating the selected home. These actions do not install a bench.";
    private IReadOnlyList<PackPreflightFinding> KnownEnvironmentBlockers => Workspace is null ? [] : _environmentInspection.Blockers;
    private bool HasKnownEnvironmentBlockers => KnownEnvironmentBlockers.Count != 0;
    private string EnvironmentBlockerText
    {
        get
        {
            var blockers = KnownEnvironmentBlockers;
            if (EnvironmentInspectionPending) return "Checking the selected authoring environment before building…";
            return blockers.Any(f => f.Code is "PACK_RUNTIME_MISSING" or "PACK_HOME_UNSAFE")
                ? "Repair unsafe or incomplete selected authoring home before building. " + blockers[0].Message
                : "Prepare or import missing required authoring packages before building.";
        }
    }
    private void RaiseEnvironmentProperties()
    {
        OnPropertyChanged(nameof(EnvironmentLibraryReadiness));
        OnPropertyChanged(nameof(EnvironmentExecutionReadiness));
        OnPropertyChanged(nameof(PackageRequirementsText));
        OnPropertyChanged(nameof(LibraryEnvironmentReadinessText));
        OnPropertyChanged(nameof(StandaloneVisaReadinessText));
        OnPropertyChanged(nameof(RequiresInstrumentLibrary));
        OnPropertyChanged(nameof(CanDeclareLibraryDependency));
        OnPropertyChanged(nameof(InstrumentTypeChoices));
        OnPropertyChanged(nameof(InstrumentCatalogSummary));
        OnPropertyChanged(nameof(SelectedNewInstrumentType));
        OnPropertyChanged(nameof(NewInstrumentAvailabilityText));
        OnPropertyChanged(nameof(CanAddInstrumentSlot));
        OnPropertyChanged(nameof(EnvironmentPathError));
        OnPropertyChanged(nameof(EnvironmentPackages));
        OnPropertyChanged(nameof(EnvironmentRuntimeFiles));
        RaisePackGuardProperties();
    }
}
