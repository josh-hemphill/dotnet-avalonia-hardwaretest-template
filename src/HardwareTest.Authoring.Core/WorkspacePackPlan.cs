namespace HardwareTest.Authoring;

/// One file that will land inside the program TapPackage.
public sealed record PackFileLine(string RelativePath, string Role);

/// Manifest package.xml dependency (required or optional).
public sealed record PackDependencyLine(string Package, string Version, bool Optional, string? When)
{
    public string DisplayText
        => Optional
            ? $"{Package} {Version} (optional{(string.IsNullOrWhiteSpace(When) ? string.Empty : $", {When}")})"
            : $"{Package} {Version}";
}

/// Declared plugin TapPackage or shell-app csproj from authoring.json.
public sealed record PackDeclaredProjectLine(
    string ManifestEntry,
    string ResolvedPath,
    bool Exists,
    string ExpectedKind)
{
    public string DisplayText
        => $"{ManifestEntry} ({ExpectedKind}, {(Exists ? "found" : "missing")})";
}

/// Package installed in the isolated authoring OpenTAP home.
public sealed record OpenTapEnvironmentLine(string Name, string Version, string Source)
{
    public string DisplayText => $"{Name} {Version}";
}

/// What Pack will write, plus last ship-manifest if one exists.
public sealed record WorkspacePackPreview(
    string ProgramPackageName,
    string ProgramPackageVersion,
    IReadOnlyList<PackFileLine> ProgramPackContents,
    IReadOnlyList<PackDependencyLine> PackageDependencies,
    string? OpenTapVersionPin,
    IReadOnlyList<PackDeclaredProjectLine> PluginProjects,
    IReadOnlyList<PackDeclaredProjectLine> ShellAppProjects,
    IReadOnlyList<OpenTapEnvironmentLine> AuthoringHomePackages,
    string? AuthoringHomePath,
    bool IncludeTui,
    string? InstrumentComponentsPackagePath,
    ShipManifest? LastShipManifest,
    string? LastOutputDirectory);

/// Describes pack/environment contents without running tap package create.
public static class WorkspacePackPlan
{
    public const string DistDirectoryName = "dist";

    public static WorkspacePackPreview Empty { get; } = new(
        string.Empty,
        string.Empty,
        [],
        [],
        null,
        [],
        [],
        [],
        null,
        true,
        null,
        null,
        null);

    /// Snapshot of what this workspace will pack and which OpenTAP home it uses.
    public static WorkspacePackPreview Describe(AuthoringWorkspace workspace, string? homeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var manifest = workspace.Manifest;
        var homePath = ResolveHomePath(workspace, homeOverride);
        var homePackages = Directory.Exists(homePath)
            ? OpenTapHomeBootstrapper.ListInstalledPackages(new OpenTapHome(homePath))
                .Select(package => new OpenTapEnvironmentLine(package.Name, package.Version, "installed in home"))
                .ToArray()
            : [];
        var lastOutput = Path.Combine(workspace.Root, DistDirectoryName);
        var lastManifest = TryReadShipManifest(lastOutput);
        return new WorkspacePackPreview(
            manifest.Package.Name,
            manifest.Package.Version,
            PackageXmlRenderer.EnumeratePackFiles(workspace)
                .Select(file => new PackFileLine(file, "in TapPackage"))
                .ToArray(),
            EnumerateDependencies(manifest),
            OpenTapPin(manifest),
            DescribeDeclared(workspace.Root, manifest.PluginProjects, "TapPackage"),
            DescribeDeclared(workspace.Root, manifest.ShellAppProjects, "csproj"),
            homePackages,
            Directory.Exists(homePath) ? homePath : null,
            manifest.IncludeTui,
            manifest.InstrumentComponentsPackage,
            lastManifest,
            lastManifest is null ? null : lastOutput);
    }

    public static WorkspacePackPreview WithLastPack(
        WorkspacePackPreview preview,
        ShipManifest manifest,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return preview with
        {
            LastShipManifest = manifest,
            LastOutputDirectory = Path.GetFullPath(outputDirectory),
        };
    }

    public static ShipManifest? TryReadShipManifest(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return null;
        }

        var path = Path.Combine(outputDirectory, WorkspacePacker.ShipManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var raw = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize(raw, AuthoringJsonContext.Default.ShipManifest);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string ResolveHomePath(AuthoringWorkspace workspace, string? homeOverride)
    {
        if (!string.IsNullOrWhiteSpace(homeOverride))
        {
            return Path.GetFullPath(homeOverride);
        }

        return Path.GetFullPath(Path.Combine(workspace.Root, OpenTapHomeBootstrapper.DefaultHomeRelativePath));
    }

    private static IReadOnlyList<PackDependencyLine> EnumerateDependencies(AuthoringManifest manifest)
    {
        var lines = new List<PackDependencyLine>();
        foreach (var dep in manifest.Dependencies)
        {
            lines.Add(new PackDependencyLine(dep.Package, dep.Version, false, null));
        }

        foreach (var dep in manifest.OptionalDependencies)
        {
            lines.Add(new PackDependencyLine(dep.Package, dep.Version, true, dep.When));
        }

        return lines;
    }

    private static string? OpenTapPin(AuthoringManifest manifest)
        => manifest.Dependencies.FirstOrDefault(dep =>
                string.Equals(dep.Package, "OpenTAP", StringComparison.OrdinalIgnoreCase))
            ?.Version;

    private static IReadOnlyList<PackDeclaredProjectLine> DescribeDeclared(
        string root,
        IEnumerable<string> entries,
        string expectedKind)
    {
        var lines = new List<PackDeclaredProjectLine>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var resolved = Path.IsPathRooted(entry)
                ? Path.GetFullPath(entry)
                : Path.GetFullPath(Path.Combine(root, entry));
            lines.Add(new PackDeclaredProjectLine(entry.Trim(), resolved, File.Exists(resolved), expectedKind));
        }

        return lines;
    }
}
