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
                issue = FileIssue(packageDirectory, Path.Combine(packageDirectory, file));
                if (issue is not null) return Unavailable($"payload '{file}' {issue}");
            }
            return new(true, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
        {
            return Unavailable($"package payload cannot be read: {error.Message}");
        }

        AuthoringInstrumentAvailability Unavailable(string reason) => new(false,
            $"Reinstall '{adapter.RequiredPackage}' in selected OpenTAP home '{home.Root}'; {reason}.");
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
