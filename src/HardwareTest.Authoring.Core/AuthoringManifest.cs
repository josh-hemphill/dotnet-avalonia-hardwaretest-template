using System.Text.Json.Serialization;

namespace HardwareTest.Authoring;

/// Current schema version for authoring.json (bump deliberately).
public static class AuthoringSchemaVersions
{
    public const int Manifest = 1;

    public const int Preferences = 1;
}

/// Versioned workspace manifest beside TapPlans. Session/DUT/Typst stay in program sidecars.
public sealed class AuthoringManifest
{
    public int SchemaVersion { get; set; }

    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string PlansDirectory { get; set; } = "plans";

    public AuthoringPackageSpec Package { get; set; } = new();

    public List<AuthoringPackageDependency> Dependencies { get; set; } = [];

    public List<AuthoringOptionalDependency> OptionalDependencies { get; set; } = [];

    public string? InstrumentComponentsPackage { get; set; }

    public List<string> PluginProjects { get; set; } = [];

    public List<string> ShellAppProjects { get; set; } = [];

    public bool IncludeTui { get; set; }

    /// Optional operator run.json goldens. Default recordings/. Folder need not exist.
    public string RecordingsDirectory { get; set; } = "recordings";

    /// Suggested report kinds, program kinds, required DUT fields, and slot names for this workspace.
    public AuthoringWorkspaceCatalogs? Catalogs { get; set; }
}

/// Workspace-level suggestions. Not the closed demo-program set.
public sealed class AuthoringWorkspaceCatalogs
{
    public List<string> ReportKinds { get; set; } = [];

    public List<string> ProgramKinds { get; set; } = [];

    public List<string> InstrumentSlotNames { get; set; } = [];

    public List<string> RequiredFields { get; set; } = [];
}

public sealed class AuthoringPackageSpec
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = "0.1.0";

    public string Os { get; set; } = "Windows,Linux,MacOS";
}

public class AuthoringPackageDependency
{
    public string Package { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}

public sealed class AuthoringOptionalDependency : AuthoringPackageDependency
{
    public string? When { get; set; }
}

/// Files on disk for one planning directory. Drafts live beside this in DraftWorkspace.
public sealed record AuthoringWorkspace(
    string Root,
    AuthoringManifest Manifest,
    IReadOnlyList<string> TapPlanPaths)
{
    public bool IsReadOnly { get; init; }
}

public sealed class AuthoringWorkspaceException : InvalidOperationException
{
    public AuthoringWorkspaceException(string message)
        : base(message)
    {
    }

    public AuthoringWorkspaceException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
