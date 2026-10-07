using System.Xml;
using System.Xml.Linq;

namespace HardwareTest.OpenTap.Host;

/// Validates an explicitly selected execution root before any payload or plugin search.
internal static class ExecutionLibraryHome
{
    private static readonly string[] Files = ["InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll"];

    internal static string Validate(string directory, Action<string, string>? containedEntry = null)
    {
        var root = ResolvePath(Path.GetFullPath(directory));
        foreach (var file in Files) EnsureContained(root, Path.Combine(root, file));
        EnsureContained(root, Path.Combine(root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"));
        if (!Directory.Exists(root)) return root;
        var visited = 0;
        var active = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        active.Add(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (++visited > 100000) throw new IOException("Selected execution home contains too many package paths.");
            EnsureContained(root, entry);
            containedEntry?.Invoke(root, entry);
            var name = Path.GetFileName(entry);
            if (Files.Contains(name, StringComparer.OrdinalIgnoreCase) && !Files.Contains(name, StringComparer.Ordinal))
                throw new InvalidOperationException("Instrument Components execution requires canonical root DLL filenames.");
            if (name.Equals("package.xml", StringComparison.OrdinalIgnoreCase))
            {
                if (IsLibraryIdentity(entry))
                    throw new InvalidOperationException("Instrument Components execution requires an installed home root with root DLLs and Packages/InstrumentComponents.OpenTap/package.xml. Import package directories before execution.");
            }
            if (Directory.Exists(entry)) Scan(entry, 0);
        }
        return root;

        void Scan(string directoryPath, int depth)
        {
            EnsureContained(root, directoryPath);
            if (!Directory.Exists(directoryPath)) return;
            var resolved = ResolvePath(directoryPath);
            if (depth > 64 || !active.Add(resolved)) throw new IOException("Selected execution home contains a package directory link cycle or excessive depth.");
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath))
                {
                    if (++visited > 100000) throw new IOException("Selected execution home contains too many package paths.");
                    EnsureContained(root, entry);
                    containedEntry?.Invoke(root, entry);
                    var name = Path.GetFileName(entry);
                    if (Files.Contains(name, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Instrument Components execution cannot use obsolete package-directory library DLLs. Import or repair the selected package to keep library DLLs only in the installed home root.");
                    if (name.Equals("package.xml", StringComparison.OrdinalIgnoreCase) && IsLibraryIdentity(entry)
                        && !entry.Equals(Path.Combine(root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"), StringComparison.Ordinal))
                        throw new InvalidOperationException("Instrument Components execution cannot use noncanonical or duplicate installed package identities.");
                    if (Directory.Exists(entry)) Scan(entry, depth + 1);
                }
            }
            finally { active.Remove(resolved); }
        }
    }

    internal static byte[] ReadPayload(string root, string file)
    {
        var path = Path.Combine(root, file);
        EnsureContained(root, path);
        return File.ReadAllBytes(ResolvePath(path));
    }

    private static bool IsLibraryIdentity(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1_048_576
            });
            var package = XDocument.Load(reader).Root;
            var name = ((string?)package?.Attribute("Name"))?.Trim();
            if (package?.Name.LocalName != "Package" || string.IsNullOrWhiteSpace(name))
                throw new IOException("Selected execution home package metadata has no valid Package identity: " + path);
            return name.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException error)
        { throw new IOException("Selected execution home package metadata is malformed or exceeds the inspection limit: " + path, error); }
    }

    private static void EnsureContained(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var boundary = Path.TrimEndingDirectorySeparator(root);
        var prefix = boundary + Path.DirectorySeparatorChar;
        var resolved = Path.TrimEndingDirectorySeparator(ResolvePath(path));
        if (!Path.GetFullPath(path).StartsWith(prefix, comparison)
            || !(resolved.Equals(boundary, comparison) || resolved.StartsWith(prefix, comparison)))
            throw new IOException("Selected execution home path resolves outside its root: " + path);
    }

    private static string ResolvePath(string path)
    {
        var activeLinks = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var traversals = 0;
        return Resolve(path);

        string Resolve(string candidate)
        {
            var current = Path.GetPathRoot(candidate)!;
            foreach (var component in candidate[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (component == ".") continue;
                if (component == "..") { current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? current; continue; }
                current = Path.Combine(current, component);
                FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                var target = entry.LinkTarget;
                if (target is null) continue;
                if (++traversals > 64 || !activeLinks.Add(current)) throw new IOException("Selected execution home contains a symlink cycle or excessive link chain.");
                var link = current;
                try
                {
                    if (Path.IsPathRooted(target) && !Path.IsPathFullyQualified(target)) throw new IOException("Selected execution home contains an ambiguous symlink target.");
                    // Resolve ancestors before '..', matching filesystem lookup rather than lexical normalization.
                    current = Resolve(Path.IsPathFullyQualified(target) ? target : Path.Combine(Path.GetDirectoryName(link)!, target));
                }
                finally { activeLinks.Remove(link); }
            }
            return Path.GetFullPath(current);
        }
    }
}
