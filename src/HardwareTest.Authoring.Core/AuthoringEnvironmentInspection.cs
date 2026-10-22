using System.Collections.Frozen;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Detached inspection result. Construction may read packages and load adapter metadata;
/// consumers only read immutable values and never inspect the filesystem.
internal sealed record AuthoringEnvironmentInspection(
    OpenTapHome? Home, string? Error,
    IReadOnlyList<AuthoringPackageRequirement> Packages, IReadOnlyList<string> RuntimeFiles,
    AuthoringInstrumentAvailability Library, AuthoringInstrumentAvailability Visa,
    bool VisaRequired, string VisaText, IReadOnlyList<PackPreflightFinding> Blockers,
    IReadOnlyList<AuthoringInstrumentAdapter> Choices,
    IReadOnlyDictionary<string, AuthoringInstrumentAvailability> Availability,
    IReadOnlyDictionary<string, AuthoringInstrumentAvailability> CreationAvailability)
{
    internal static AuthoringEnvironmentInspection Pending { get; } = Empty("Checking the selected authoring environment…");
    internal static AuthoringEnvironmentInspection Empty(string reason) => new(null, null, [], [],
        new(false, reason), new(false, reason), false, reason,
        [new("PACK_ENVIRONMENT_PENDING", reason, true)], [],
        FrozenDictionary<string, AuthoringInstrumentAvailability>.Empty,
        FrozenDictionary<string, AuthoringInstrumentAvailability>.Empty);

    internal static AuthoringEnvironmentInspection Inspect(AuthoringWorkspace workspace, string? homeOverride)
    {
        if (!WorkspacePackPlan.TryResolveHomePath(workspace, homeOverride, out var path, out var error))
            return Empty(error!) with
            {
                Error = error,
                Packages = Array.AsReadOnly(workspace.Manifest.Dependencies.Select(d => new AuthoringPackageRequirement(d.Package, d.Version, "unavailable", false, false, error!))
                    .Concat(workspace.Manifest.OptionalDependencies.Select(d => new AuthoringPackageRequirement(d.Package, d.Version, "unavailable", true, false, error!))).ToArray()),
                RuntimeFiles = Array.AsReadOnly(new[] { error! }),
            };
        var home = new OpenTapHome(path!);
        var requiresLibrary = workspace.Manifest.Dependencies.Any(dependency => dependency.Package.Equals(AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase));
        var packages = AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home);
        var library = AuthoringInstrumentCatalog.LibraryReadiness(home);
        var choices = AuthoringInstrumentCatalog.Discover(home).Concat(AuthoringInstrumentCatalog.All.Where(adapter => !AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId))).ToArray();
        var adapters = AuthoringInstrumentCatalog.All;
        var availability = adapters.ToFrozenDictionary(adapter => adapter.TypeId, adapter => adapter.Availability(home), StringComparer.Ordinal);
        var creation = adapters.ToFrozenDictionary(adapter => adapter.TypeId, adapter =>
            !AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId) && string.IsNullOrWhiteSpace(homeOverride)
                ? adapter.Availability() : availability[adapter.TypeId], StringComparer.Ordinal);
        var visa = StandaloneVisaReadiness.Assess(home);
        return new(home, null, Array.AsReadOnly(packages.ToArray()), Array.AsReadOnly(AuthoringEnvironmentAssessment.RuntimeFiles(home).ToArray()),
            library, visa, requiresLibrary || StandaloneVisaReadiness.RequiresStandaloneReadiness(home),
            StandaloneVisaReadiness.ExecutionPrerequisite(home, requiresLibrary) ?? visa.Reason ?? string.Empty,
            Array.AsReadOnly(AuthoringEnvironmentAssessment.BuildBlockers(workspace.Manifest, home, allowMissingHome: true).ToArray()),
            Array.AsReadOnly(choices), availability, creation);
    }
}
