namespace HardwareTest.Authoring;

public sealed record AuthoringEnvironmentReadiness(string State, string Summary, string NextStep, string Details);

public sealed partial class AuthoringWorkspaceViewModel
{
    public AuthoringEnvironmentReadiness EnvironmentLibraryReadiness
    {
        get
        {
            var inspection = HardwareInspection;
            if (inspection.Error is not null || inspection.Home is null)
                return new("Cannot inspect", "The selected authoring home cannot be inspected.",
                    "Review the selected home in Settings before preparing packages.", inspection.Error ?? LibraryEnvironmentReadinessText);
            var available = AuthoringInstrumentCatalog.LibraryReadiness(inspection.Home);
            var packages = EnvironmentPackages;
            if (packages.Any(package => package.State == "unsafe installed home"))
                return new("Cannot inspect", "The selected home contains unsafe package paths.",
                    "Repair the home before preparing or importing packages.", available.Reason ?? string.Empty);
            var requirement = packages.FirstOrDefault(package => package.Package.Equals(AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase));
            if (available.Available && (requirement is null || requirement.Satisfied))
                return new("Ready", "Compatible Instrument Components types are available.",
                    "Return to Instruments to choose a supported type.", available.Reason ?? string.Empty);
            var summary = requirement?.State switch
            {
                "version mismatch" => "The installed instrument library does not match the required version.",
                "declared payload missing" => "The installed instrument library has missing files.",
                "invalid version metadata" => "The instrument library version declaration or metadata is invalid.",
                "unsafe installed home" => "The selected home contains unsafe package paths.",
                _ => "Compatible physical instrument types are not available in this home."
            };
            if (requirement?.State == "invalid version metadata")
                return new("Needs attention", summary, "Correct the workspace version requirement or installed package metadata, then check again.", available.Reason ?? string.Empty);
            return new(RequiresInstrumentLibrary ? "Needs attention" : "Optional for mock plans", summary,
                RequiresInstrumentLibrary ? "Save requirements, then prepare bundled packages or import a matching library release."
                    : "For physical hardware, declare the instrument library, save, then prepare it. Mock plans can continue.",
                requirement is { Satisfied: false } ? requirement.DisplayText + Environment.NewLine + available.Reason : available.Reason ?? string.Empty);
        }
    }

    public AuthoringEnvironmentReadiness EnvironmentExecutionReadiness
    {
        get
        {
            var inspection = HardwareInspection;
            if (inspection.Error is not null || inspection.Home is null)
                return new("Cannot inspect", "The selected execution home cannot be inspected.",
                    "Review the selected home in Settings.", StandaloneVisaReadinessText);
            var readiness = StandaloneVisaReadiness.Assess(inspection.Home);
            if (readiness.Available)
                return new("Provider ready", "The standalone VISA package is ready in this home.",
                    "Install the vendor VISA runtime on the execution machine before using physical hardware. It has not been probed here.", StandaloneVisaReadinessText);
            var required = RequiresInstrumentLibrary || StandaloneVisaReadiness.RequiresStandaloneReadiness(inspection.Home);
            return required
                ? new("Needs attention", "The standalone VISA provider or its required packages need repair.",
                    "Prepare this home or import supported package versions. Expand diagnostics for the exact prerequisite.", StandaloneVisaReadinessText)
                : new("Optional for mock plans", "No standalone VISA provider is required for ordinary mock plans.",
                    "Prepare the instrument library and its standalone provider before physical execution.", StandaloneVisaReadinessText);
        }
    }

    public string PackageRequirementsText => "Required packages for this workspace" + Environment.NewLine
        + string.Join(Environment.NewLine, EnvironmentPackages.Select(package => package.DisplayText))
        + Environment.NewLine + "Request matching .TapPackage or .zip archives from the package publisher or your team's release artifacts."
        + Environment.NewLine + "Instrument Components releases: https://github.com/josh-hemphill/instrument-components/releases";
}
