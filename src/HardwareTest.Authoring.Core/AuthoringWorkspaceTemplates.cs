namespace HardwareTest.Authoring;

public enum WorkspaceTemplateKind { Empty, ProductVoltage, DemoVoltage }

public sealed record AuthoringWorkspaceTemplate(WorkspaceTemplateKind Kind, string Name, string SupportedTask,
    bool IsDemo, IReadOnlyList<AuthoringPackageDependency> RequiredPackages, IReadOnlyList<string> Contents)
{
    public string Classification => Kind == WorkspaceTemplateKind.Empty ? "Empty" : IsDemo ? "Demo" : "Product";
}

/// Explicit package and source inventory; product templates never supply mock hardware.
public static class AuthoringWorkspaceTemplates
{
    public static IReadOnlyList<AuthoringWorkspaceTemplate> All =>
    [
        Template(WorkspaceTemplateKind.Empty, "Empty workspace", "Create test plans later", false),
        Template(WorkspaceTemplateKind.ProductVoltage, "Product hardware scaffold", "Configure library resource bindings and instrument checks; no measurement recipe", false),
        Template(WorkspaceTemplateKind.DemoVoltage, "Demo voltage task", "Voltage acquisition using an explicit Mock DMM", true)
    ];

    private static AuthoringWorkspaceTemplate Template(WorkspaceTemplateKind kind, string name, string task, bool demo)
    {
        List<AuthoringPackageDependency> packages = [new() { Package = "OpenTAP", Version = "^9.32.2" }, new() { Package = "HardwareTest Basic", Version = "^0.2.0" }, new() { Package = "HardwareTest Mixins", Version = "^0.1.0" }];
        if (kind == WorkspaceTemplateKind.ProductVoltage) packages.Add(new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "^0.1.0" });
        return new(kind, name, task, demo, packages,
            kind == WorkspaceTemplateKind.Empty ? ["Manifest and workspace source", "Empty plans directory", "Schemas and ignore conventions"]
            : ["Manifest and workspace source", kind == WorkspaceTemplateKind.ProductVoltage ? "Physical hardware scaffold (no measurement recipe)" : "Voltage task authoring draft", "Empty compiled plans directory", "Schemas and ignore conventions"]);
    }
}

public sealed record WorkspaceCreationRequest(string Destination, string DisplayName, string PackageName)
{
    public WorkspaceTemplateKind Template { get; init; }
    public string PackageVersion { get; init; } = "0.1.0";
    public string PackageOs { get; init; } = "Windows,Linux,MacOS";
    public string PlanId { get; init; } = "voltage";
    public string DeviceFamily { get; init; } = "generic";
    public bool RequireSerial { get; init; } = true;
    public bool IncludeTui { get; init; }
    public bool IncludeVisaPackage { get; init; } // Explicit legacy API/serialized choice.
    public bool IncludeLibraryPackage { get; init; }
}

public sealed record WorkspaceCreationPreview(string Destination, AuthoringManifest Manifest,
    AuthoringWorkspaceTemplate Template, IReadOnlyList<string> Files, PlanInitializationResult? Plan);
