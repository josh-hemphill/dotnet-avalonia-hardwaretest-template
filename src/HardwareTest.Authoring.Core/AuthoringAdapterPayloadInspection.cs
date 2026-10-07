using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using OpenTap;

namespace HardwareTest.Authoring;

/// Checks selected-home payloads independently of assemblies already loaded in this process.
internal static class AuthoringAdapterPayloadInspection
{
    public static AuthoringInstrumentAvailability Inspect(AuthoringInstrumentAdapter adapter, OpenTapHome home)
    {
        var packageDirectory = Path.Combine(home.Root, "Packages", adapter.RequiredPackage);
        var metadata = Path.Combine(packageDirectory, "package.xml");
        string? issue;
        try
        {
            issue = FileIssue(home.Root, metadata);
            if (issue is not null) return Unavailable($"package metadata {issue}");
            var document = XDocument.Load(metadata);
            if (document.Root?.Name.LocalName != "Package"
                || !string.Equals((string?)document.Root.Attribute("Name"), adapter.RequiredPackage, StringComparison.OrdinalIgnoreCase))
                return Unavailable("package metadata is invalid or names another package");
            if (AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId))
            {
                RejectAlternateLibraryPayloads(home.Root);
                ValidateLibraryMetadata(home.Root, document.Root!);
                return new(true, null);
            }
            var files = document.Root?.Elements().Where(element => element.Name.LocalName == "Files")
                .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "File"))
                .Select(element => (string?)element.Attribute("Path")).ToArray() ?? [];
            // Required adapter dependencies cannot disappear merely by editing metadata.
            string[] required = [adapter.AssemblyFile, .. adapter.RequiredPayloadFiles];
            foreach (var file in required)
                if (!files.Contains(file, StringComparer.Ordinal)) return Unavailable($"package metadata does not declare required payload '{file}'");
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) return Unavailable("package metadata declares an invalid payload path");
                issue = FileIssue(packageDirectory, Path.Combine(packageDirectory, file));
                if (issue is not null) return Unavailable($"payload '{file}' {issue}");
            }
            return new(true, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Xml.XmlException)
        {
            return Unavailable($"package payload cannot be read: {error.Message}");
        }

        AuthoringInstrumentAvailability Unavailable(string reason) => new(false,
            $"'{adapter.RequiredPackage}' is unavailable in selected OpenTAP home '{home.Root}': {reason}. Open Environment to prepare or import the declared package; compatible installed packages are reused.");
    }

    internal static string LibraryPayloadPath(OpenTapHome home, string file)
    {
        var directory = Path.Combine(home.Root, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        var metadata = XDocument.Load(Path.Combine(directory, "package.xml"));
        RejectAlternateLibraryPayloads(home.Root);
        var declarations = ValidateLibraryMetadata(home.Root, metadata.Root ?? throw new IOException("Library package metadata has no root element."));
        if (!declarations.Any(element => (string?)element.Attribute("Path") == file))
            throw new IOException($"Library metadata does not declare payload '{file}'.");
        return Path.Combine(home.Root, file);
    }

    internal static XElement[] ValidateLibraryMetadata(string root, XElement package)
    {
        if (package.Name.LocalName != "Package"
            || !string.Equals((string?)package.Attribute("Name"), AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Library package metadata is invalid or names another package.");
        try
        {
            var version = SemanticVersion.Parse((string?)package.Attribute("Version") ?? "");
            if (version.ToString().Split('+')[0] != PublishedInstrumentComponents.Version)
                throw new IOException($"Library package must use current version {PublishedInstrumentComponents.Version}.");
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { throw new IOException("Library package version metadata is invalid.", error); }
        var containers = package.Elements().Where(element => element.Name.LocalName == "Files").ToArray();
        if (containers.Length != 1 || package.Descendants().Any(element =>
            element.Name.LocalName == "Files" && element != containers[0]
            || element.Name.LocalName == "File" && element.Parent != containers[0]))
            throw new IOException("Library package must declare payload directly in one Package/Files element.");
        var declarations = containers[0].Elements().Where(element => element.Name.LocalName == "File").ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            var file = (string?)declaration.Attribute("Path");
            if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file) || file.Contains(':')
                || file.Replace('\\', '/').Split('/').Any(part => part is ".." or "." or ""))
                throw new IOException("Library package metadata declares an invalid payload path.");
            var relative = file.Replace('\\', '/');
            if (relative.Equals("package.xml", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Library package cannot declare a home-root package.xml payload; installed metadata belongs in its canonical Packages directory.");
            if (!paths.Add(relative)) throw new IOException("Library package contains duplicate or case-colliding declared paths.");
            if (LibraryFiles.Contains(relative.Split('/')[^1], StringComparer.OrdinalIgnoreCase)
                && !LibraryFiles.Contains(relative, StringComparer.Ordinal))
                throw new IOException("Library package declares an alternate DLL layout; required DLLs must be at the package input root.");
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var issue = FileIssue(root, path);
            if (issue is not null) throw new IOException($"Library payload '{file}' {issue}.");
            ValidateLibraryFile(path, relative, declaration);
        }
        foreach (var required in LibraryFiles)
            if (!declarations.Any(element => (string?)element.Attribute("Path") == required))
                throw new IOException($"Library package must declare required payload '{required}'.");
        return declarations;
    }

    internal static readonly string[] LibraryFiles = ["InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll"];

    internal static void RejectAlternateLibraryPayloads(string home)
    {
        if (Directory.Exists(home) && Directory.EnumerateFiles(home)
            .Any(path => Path.GetFileName(path).Equals("package.xml", StringComparison.OrdinalIgnoreCase) && IsLibraryIdentity(path)))
            throw new IOException("Instrument Components has a noncanonical home-root package identity. Select a fresh home and Prepare in Environment.");
        var packages = Path.Combine(home, "Packages");
        if (!Directory.Exists(packages)) return;
        if (Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories)
            .Any(path => LibraryFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Instrument Components has an unsupported installed payload layout. Select a fresh home and Prepare in Environment.");
        foreach (var directory in Directory.EnumerateDirectories(packages))
        {
            var metadata = Path.Combine(directory, "package.xml");
            if (IsLibraryIdentity(metadata) && Path.GetFileName(directory) != AuthoringInstrumentCatalog.LibraryPackage)
                throw new IOException("Instrument Components has a noncanonical or duplicate installed package identity. Select a fresh home and Prepare in Environment.");
        }
    }

    private static bool IsLibraryIdentity(string metadata)
    {
        if (!File.Exists(metadata)) return false;
        try
        {
            var package = XDocument.Load(metadata).Root;
            return string.Equals(((string?)package?.Attribute("Name"))?.Trim(), AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException) { return false; }
    }

    internal static void ValidateLibraryFile(string path, string file, XElement declaration)
    {
        var hashes = declaration.Descendants().Where(element => element.Name.LocalName == "Hash").ToArray();
        if (hashes.Length > 1 || hashes.Any(element => element.Parent != declaration || element.HasElements)) throw new IOException($"Malformed library payload hash for '{file}'.");
        if (hashes.Length == 1)
        {
            var hash = hashes[0].Value.Trim();
            if (hash.Length != 40 || hash.Any(character => !Uri.IsHexDigit(character)))
                throw new IOException($"Malformed library payload hash for '{file}'.");
            if (!Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(path))).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Library payload '{file}' does not match its package metadata hash.");
        }
        if (!LibraryFiles.Contains(file, StringComparer.Ordinal)) return;
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null)
                throw new BadImageFormatException("Payload has no managed metadata.");
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) != Path.GetFileNameWithoutExtension(file))
                throw new BadImageFormatException("Payload assembly identity does not match its required filename.");
            if (metadata.GetAssemblyDefinition().Version != new Version(PublishedInstrumentComponents.Version + ".0"))
                throw new BadImageFormatException($"Payload must use current assembly version {PublishedInstrumentComponents.Version}.0.");
        }
        catch (BadImageFormatException error)
        { throw new IOException($"Library payload '{file}' is not the expected managed assembly: {error.Message}", error); }
    }

    private static string? FileIssue(string root, string file)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(file).StartsWith(prefix, comparison)) return "escapes its package directory";
        var resolvedRoot = Path.TrimEndingDirectorySeparator(ResolvePath(root)) + Path.DirectorySeparatorChar;
        if (!ResolvePath(file).StartsWith(resolvedRoot, comparison)) return "resolves outside its package directory";
        if (!File.Exists(file)) return "is missing";
        using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return stream.Length == 0 ? "is empty" : null;
    }

    private static string ResolvePath(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var activeLinks = new HashSet<string>(comparison);
        var traversals = 0;
        return Resolve(Path.GetFullPath(path));

        string Resolve(string candidate)
        {
            var current = Path.GetPathRoot(candidate)!;
            foreach (var component in candidate[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (component == ".") continue;
                if (component == "..") { current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? current; continue; }
                current = Path.Combine(current, component);
                FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                var rawTarget = entry.LinkTarget;
                if (rawTarget is null) continue;
                var link = current;
                if (++traversals > 64 || !activeLinks.Add(link))
                    throw new IOException($"Symlink cycle or excessive link chain at '{link}'.");
                try
                {
                    // Resolve the immediate target component by component as well: a
                    // target's ancestor directory can itself link outside the package.
                    if (Path.IsPathRooted(rawTarget) && !Path.IsPathFullyQualified(rawTarget))
                        throw new IOException($"Cannot resolve ambiguous symlink target '{rawTarget}'.");
                    var target = Path.IsPathFullyQualified(rawTarget) ? rawTarget : Path.Combine(Path.GetDirectoryName(link)!, rawTarget);
                    // Do not normalize '..' lexically: it applies after any preceding
                    // directory symlink has been resolved, as it does for File.Open.
                    current = Resolve(target);
                }
                finally { activeLinks.Remove(link); }
            }
            return Path.GetFullPath(current);
        }
    }
}
