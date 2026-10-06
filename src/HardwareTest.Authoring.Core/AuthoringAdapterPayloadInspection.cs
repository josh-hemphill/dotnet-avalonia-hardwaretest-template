using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;

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
                if (AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId)) { _ = LibraryPayloadPath(home, file); continue; }
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
        var declared = metadata.Descendants().Single(element => element.Name.LocalName == "File"
            && (string?)element.Attribute("Path") == file);
        RejectAlternateLibraryPayloads(home.Root);
        var path = Path.Combine(home.Root, file);
        var issue = FileIssue(home.Root, path);
        if (issue is not null) throw new IOException($"Library payload '{file}' {issue}.");
        ValidateLibraryFile(path, file, declared);
        return path;
    }

    internal static readonly string[] LibraryFiles = ["InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll"];

    internal static void RejectAlternateLibraryPayloads(string home)
    {
        var packages = Path.Combine(home, "Packages");
        if (Directory.Exists(packages) && Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories)
            .Any(path => LibraryFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Instrument Components has an unsupported installed payload layout. Select a fresh home and Prepare in Environment.");
    }

    internal static void ValidateLibraryFile(string path, string file, XElement declaration)
    {
        var hashes = declaration.Elements().Where(element => element.Name.LocalName == "Hash").ToArray();
        if (hashes.Length > 1) throw new IOException($"Malformed library payload hash for '{file}'.");
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
