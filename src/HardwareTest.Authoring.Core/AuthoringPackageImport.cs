using System.IO.Compression;
using System.Xml.Linq;
using OpenTap;
using OpenTap.Package;

namespace HardwareTest.Authoring;

/// Validates an incoming package against its own bytes before merging into a selected home.
internal static class AuthoringPackageImport
{
    internal static void Install(string path, string home, AuthoringManifest manifest, Action<string, string> copy)
    {
        if (Directory.Exists(path))
        {
            // Validate and publish the same owned bytes, even if the unpacked source changes later.
            var captured = AuthoringBuildService.CaptureTree(path, "import", true);
            if (captured.Files.Any(file => file.Target != file.Path)) throw new AuthoringWorkspaceException("Offline package cannot contain links.");
            var ownedFolder = Path.Combine(Path.GetTempPath(), "ht-import-folder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ownedFolder);
            try
            {
                foreach (var file in captured.Files)
                {
                    var destination = Path.Combine(ownedFolder, file.RelativePath);
                    AuthoringBuildService.EnsureContained(ownedFolder, destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    AuthoringBuildService.Materialize(file, destination);
                }
                var package = Validate(ownedFolder, Path.Combine(ownedFolder, "package.xml"), manifest);
                PublishLayout(ownedFolder, "package.xml", package, home, copy);
            }
            finally { Directory.Delete(ownedFolder, recursive: true); }
            return;
        }
        if (!path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new AuthoringWorkspaceException("Use a .TapPackage, .zip or unpacked package folder.");
        var owned = Path.Combine(Path.GetTempPath(), "ht-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        try
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var relative = SafeRelative(entry.FullName.TrimEnd('/'));
                    if (!names.Add(relative) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new AuthoringWorkspaceException("Offline archive contains duplicate paths or links.");
                    var destination = Path.Combine(owned, relative);
                    AuthoringBuildService.EnsureContained(owned, destination);
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }
            var metadata = Directory.GetFiles(owned, "package.xml", SearchOption.AllDirectories);
            if (metadata.Length != 1) throw new AuthoringWorkspaceException("Offline archive must contain one package.xml identity.");
            var package = Validate(owned, metadata[0], manifest);
            var relativeMetadata = Path.GetRelativePath(owned, metadata[0]).Replace('\\', '/');
            PublishLayout(owned, relativeMetadata, package, home, copy);
        }
        finally { Directory.Delete(owned, recursive: true); }
    }

    private sealed record PackageLayout(string Name, IReadOnlyList<string> Files);

    private static PackageLayout Validate(string root, string metadata, AuthoringManifest manifest)
    {
        var xml = XDocument.Load(metadata).Root;
        if (xml?.Name.LocalName != "Package") throw new AuthoringWorkspaceException("Offline package has no package identity.");
        var name = ((string?)xml.Attribute("Name"))?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new AuthoringWorkspaceException("Invalid offline package identity.");
        try
        {
            var version = SemanticVersion.Parse((string?)xml.Attribute("Version") ?? "");
            foreach (var dependency in manifest.Dependencies.Where(d => d.Package.Equals(name, StringComparison.OrdinalIgnoreCase)))
                RequireVersion(dependency.Version, version);
            foreach (var dependency in manifest.OptionalDependencies.Where(d => d.Package.Equals(name, StringComparison.OrdinalIgnoreCase)))
                RequireVersion(dependency.Version, version);
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { throw new AuthoringWorkspaceException("Invalid offline package version metadata.", error); }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in xml.Descendants().Where(element => element.Name.LocalName == "File"))
        {
            var relative = SafeRelative((string?)file.Attribute("Path"));
            var source = Path.Combine(root, relative);
            if (!files.Add(relative)) throw new AuthoringWorkspaceException("Offline package contains duplicate or case-colliding declared paths.");
            AuthoringBuildService.EnsureContained(root, source);
            if (!File.Exists(source)) throw new AuthoringWorkspaceException($"Offline package declared payload missing: '{relative}'.");
        }
        if (name.Equals("OpenTAP", StringComparison.OrdinalIgnoreCase))
            foreach (var runtime in new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json" })
            {
                if (!File.Exists(Path.Combine(root, runtime))) throw new AuthoringWorkspaceException($"Offline OpenTAP runtime missing: '{runtime}'.");
                files.Add(runtime);
            }
        return new(name, files.ToArray());
    }

    private static void PublishLayout(string source, string metadata, PackageLayout package, string home, Action<string, string> copy)
    {
        var rooted = metadata == $"Packages/{package.Name}/package.xml";
        if (!rooted && metadata != "package.xml")
            throw new AuthoringWorkspaceException("Offline archive package.xml must be at its root or Packages/<name>/package.xml.");
        var engine = package.Name.Equals("OpenTAP", StringComparison.OrdinalIgnoreCase);
        // Only declared payload and identity are published. Extra archive entries never overwrite an existing home.
        var filtered = Path.Combine(Path.GetTempPath(), "ht-import-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(filtered);
        try
        {
            Stage(metadata, rooted || engine ? $"Packages/{package.Name}/package.xml" : "package.xml");
            foreach (var relative in package.Files)
            {
                var parts = relative.Replace('\\', '/').Split('/');
                if ((rooted || engine) && parts[0].Equals("Packages", StringComparison.OrdinalIgnoreCase)
                    && (parts.Length < 3 || !parts[1].Equals(package.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new AuthoringWorkspaceException("Offline package cannot replace another package's payload.");
                if (rooted && !engine && parts.Length == 1 && new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json", "tap", "tap.exe" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                    throw new AuthoringWorkspaceException("Only an OpenTAP package can replace engine runtime files.");
                if ((rooted || engine) && parts.Length == 1
                    && !package.Name.Equals(OpenTapHomeBootstrapper.InstrumentComponentsPackageName, StringComparison.OrdinalIgnoreCase)
                    && new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                    throw new AuthoringWorkspaceException("Only an InstrumentComponents.OpenTap package can replace its library payload.");
                Stage(relative, relative);
            }
            copy(filtered, rooted || engine ? home : Path.Combine(home, "Packages", package.Name));
            // Importing custom bytes invalidates any earlier bundled-source attestation.
            if (package.Name.Equals(OpenTapHomeBootstrapper.InstrumentComponentsPackageName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var directory in Directory.EnumerateDirectories(Path.Combine(home, "Packages"))
                    .Where(directory => Path.GetFileName(directory).Equals(package.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    var provenance = Path.Combine(directory, "hardwaretest-provenance.json");
                    // Home publication merges files, so publish invalidation as an
                    // overwrite rather than leaving a removed file in the selected home.
                    if (File.Exists(provenance)) File.WriteAllText(provenance, """{"source":"custom"}""");
                }
            }
        }
        finally { Directory.Delete(filtered, recursive: true); }
        void Stage(string incoming, string outgoing)
        {
            var destination = Path.Combine(filtered, outgoing.Replace('/', Path.DirectorySeparatorChar));
            AuthoringBuildService.EnsureContained(filtered, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(source, incoming.Replace('/', Path.DirectorySeparatorChar)), destination, overwrite: true);
        }
    }

    private static void RequireVersion(string requirement, SemanticVersion version)
    {
        if (!VersionSpecifier.Parse(requirement).IsCompatible(version))
            throw new AuthoringWorkspaceException($"Offline package version {version} does not satisfy declared requirement {requirement}.");
    }

    private static string SafeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
            throw new AuthoringWorkspaceException("Invalid offline package payload path.");
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part is ".." or "." or ""))
            throw new AuthoringWorkspaceException("Offline package payload escapes its owned layout.");
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }
}
