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
    {
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
            foreach (var file in XDocument.Load(metadata).Descendants().Where(e => e.Name.LocalName == "File"))
            {
                var relative = (string?)file.Attribute("Path");
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
                    || relative.Replace('\\', '/').Split('/').Contains("..")) return false;
                relative = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var path = Path.GetFullPath(Path.Combine(package.Path, relative));
                if (ExistingContainedFile(home.Root, path) is null
                    && ExistingContainedFile(home.Root, Path.GetFullPath(Path.Combine(home.Root, relative))) is null) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or AuthoringWorkspaceException or ArgumentException) { return false; }
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
