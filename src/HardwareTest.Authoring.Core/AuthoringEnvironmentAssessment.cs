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
        // Runtime configuration and OpenTAP managed identities are checked by preflight.
        if (package.Name.Equals("OpenTAP", StringComparison.OrdinalIgnoreCase)
            && !new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json" }.All(file => File.Exists(Path.Combine(home.Root, file)))) return false;
        try
        {
            foreach (var file in XDocument.Load(Path.Combine(package.Path, "package.xml")).Descendants().Where(e => e.Name.LocalName == "File"))
            {
                var relative = (string?)file.Attribute("Path");
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
                    || relative.Replace('\\', '/').Split('/').Contains("..")) return false;
                var path = Path.GetFullPath(Path.Combine(package.Path, relative));
                AuthoringBuildService.EnsureContained(home.Root, path);
                if (!File.Exists(path))
                {
                    path = Path.GetFullPath(Path.Combine(home.Root, relative));
                    AuthoringBuildService.EnsureContained(home.Root, path);
                    if (!File.Exists(path)) return false;
                }
            }
            return true;
        }
        catch (Exception error) when (error is IOException or System.Xml.XmlException or AuthoringWorkspaceException or ArgumentException) { return false; }
    }

    public static IReadOnlyList<string> RuntimeFiles(OpenTapHome home)
        => new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json" }
            .Select(file => $"{file}: {(File.Exists(Path.Combine(home.Root, file)) ? "present (validated during build)" : "missing — prepare environment")}").ToArray();
}
