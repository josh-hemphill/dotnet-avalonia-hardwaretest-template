using System.Xml.Linq;
using OpenTap;
using OpenTap.Package;

namespace HardwareTest.Authoring;

public sealed record AuthoringPackageRequirement(string Package, string RequiredVersion, string InstalledVersion, bool Optional, bool Satisfied, string State)
{
    public string DisplayText => $"{Package}: required {RequiredVersion}; installed {InstalledVersion}; {State}{(Optional ? " (optional)" : "")}";
}

/// The same OpenTAP version semantics drive the display and build prerequisites.
public static class AuthoringEnvironmentAssessment
{
    public static IReadOnlyList<AuthoringPackageRequirement> Packages(AuthoringManifest manifest, OpenTapHome home)
        => Packages(manifest, home, UnsafeInstalledPaths(home).Count != 0);

    private static IReadOnlyList<AuthoringPackageRequirement> Packages(AuthoringManifest manifest, OpenTapHome home, bool unsafeHome)
    {
        if (unsafeHome)
            return manifest.Dependencies.Select(d => new AuthoringPackageRequirement(d.Package, d.Version, "unavailable", false, false, "unsafe installed home"))
                .Concat(manifest.OptionalDependencies.Select(d => new AuthoringPackageRequirement(d.Package, d.Version, "unavailable", true, false, "unsafe installed home"))).ToArray();
        var installed = OpenTapHomeBootstrapper.ListInstalledPackages(home);
        return manifest.Dependencies.Select(d => Assess(d, false)).Concat(manifest.OptionalDependencies.Select(d => Assess(d, true))).ToArray();
        AuthoringPackageRequirement Assess(AuthoringPackageDependency dependency, bool optional)
        {
            var versions = installed.Where(p => string.Equals(p.Name, dependency.Package, StringComparison.OrdinalIgnoreCase)).Select(p => p.Version).ToArray();
            try
            {
                var requirement = VersionSpecifier.Parse(dependency.Version);
                var compatible = installed.Where(p => string.Equals(p.Name, dependency.Package, StringComparison.OrdinalIgnoreCase)
                    && requirement.IsCompatible(SemanticVersion.Parse(p.Version))).ToArray();
                var satisfied = compatible.Any(p => HasDeclaredPayload(p, home));
                return new(dependency.Package, dependency.Version, versions.Length == 0 ? "none" : string.Join(", ", versions), optional,
                    satisfied, satisfied ? "available" : versions.Length == 0 ? "missing" : compatible.Length == 0 ? "version mismatch" : "declared payload missing");
            }
            catch (Exception error) when (error is FormatException or ArgumentException)
            {
                return new(dependency.Package, dependency.Version, string.Join(", ", versions), optional, false, "invalid version metadata");
            }
        }
    }

    private static bool HasDeclaredPayload(AuthoringInstalledPackage package, OpenTapHome home)
    {
        try
        {
            // Match capture's selected-root boundary before following metadata or runtime links.
            AuthoringBuildService.EnsureContained(home.Root, AuthoringBuildService.ResolvedPath(home.Root, directory: true));
            var metadata = ExistingContainedFile(home.Root, Path.Combine(package.Path, "package.xml"));
            if (metadata is null) return false;
            // Managed identities and runtime configuration are subsequently checked by preflight.
            if (package.Name.Equals("OpenTAP", StringComparison.OrdinalIgnoreCase)
                && !RuntimeFileNames.All(file => ExistingContainedFile(home.Root, Path.Combine(home.Root, file)) is not null)) return false;
            return DeclaredPayloadAvailable(package.Path, metadata, home);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or AuthoringWorkspaceException or ArgumentException) { return false; }
    }

    // Safety is independent of optional availability and version compatibility. Inspect
    // every installed metadata file before a package consumer can follow its links.
    internal static IReadOnlyList<string> UnsafeInstalledPaths(OpenTapHome home)
    {
        var failures = new List<string>();
        var packages = Path.Combine(home.Root, "Packages");
        try
        {
            AuthoringBuildService.EnsureContained(home.Root, AuthoringBuildService.ResolvedPath(home.Root, directory: true));
            AuthoringBuildService.EnsureContained(home.Root, AuthoringBuildService.ResolvedPath(packages, directory: true));
            if (!Directory.Exists(packages)) return failures;
            foreach (var package in Directory.EnumerateDirectories(packages))
            {
                try
                {
                    var metadata = ExistingContainedFile(home.Root, Path.Combine(package, "package.xml"));
                    if (metadata is not null) _ = DeclaredPayloadAvailable(package, metadata, home);
                }
                catch (System.Xml.XmlException) { } // Invalid metadata is an availability issue, not a path escape.
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or AuthoringWorkspaceException or ArgumentException)
                { failures.Add(package + ": " + error.Message); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AuthoringWorkspaceException or ArgumentException)
        { failures.Add(packages + ": " + error.Message); }
        return failures;
    }

    private static bool DeclaredPayloadAvailable(string package, string metadata, OpenTapHome home)
    {
        var available = true;
        foreach (var file in XDocument.Load(metadata).Descendants().Where(e => e.Name.LocalName == "File"))
        {
            var relative = (string?)file.Attribute("Path");
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
                || relative.Replace('\\', '/').Split('/').Contains(".."))
                throw new AuthoringWorkspaceException("Unsafe declared package payload path.");
            relative = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(package, relative));
            // Inspect every declaration even when another payload is missing. An unsafe
            // package-relative candidate must not borrow a safe home-root fallback.
            if (ExistingContainedFile(home.Root, path) is null
                && ExistingContainedFile(home.Root, Path.GetFullPath(Path.Combine(home.Root, relative))) is null) available = false;
        }
        return available;
    }

    internal static IReadOnlyList<PackPreflightFinding> BuildBlockers(AuthoringManifest manifest, OpenTapHome home)
    {
        var findings = new List<PackPreflightFinding>();
        foreach (var file in RuntimeFileNames.Where(file => !RuntimeFileAvailable(home, file)))
            findings.Add(new("PACK_RUNTIME_MISSING", $"Required OpenTAP runtime file '{file}' is missing or resolves outside this home; bootstrap this home.", true, home.Root));
        var unsafePaths = UnsafeInstalledPaths(home);
        foreach (var failure in unsafePaths)
            findings.Add(new("PACK_HOME_UNSAFE", "Unsafe installed package path: " + failure, true, home.Root));
        foreach (var requirement in Packages(manifest, home, unsafePaths.Count != 0).Where(p => !p.Optional && !p.Satisfied))
            findings.Add(new("PACK_PACKAGE_MISSING", requirement.DisplayText + "; prepare or import an offline package into this home.", true, home.Root));
        return findings;
    }

    private static readonly string[] RuntimeFileNames = ["OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json"];

    private static string? ExistingContainedFile(string root, string path)
    {
        AuthoringBuildService.EnsureContained(root, path);
        var target = AuthoringBuildService.ResolvedPath(path, directory: false);
        AuthoringBuildService.EnsureContained(root, target);
        return File.Exists(target) ? target : null;
    }

    public static IReadOnlyList<string> RuntimeFiles(OpenTapHome home)
        => RuntimeFileNames.Select(file => $"{file}: {(RuntimeFileAvailable(home, file) ? "present (validated during build)" : "missing or unsafe — prepare environment")}").ToArray();

    internal static bool RuntimeFileAvailable(OpenTapHome home, string file)
    {
        try
        {
            AuthoringBuildService.EnsureContained(home.Root, AuthoringBuildService.ResolvedPath(home.Root, directory: true));
            return ExistingContainedFile(home.Root, Path.Combine(home.Root, file)) is not null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AuthoringWorkspaceException or ArgumentException) { return false; }
    }
}
