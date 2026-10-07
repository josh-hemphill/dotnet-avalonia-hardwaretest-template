using System.Security.Cryptography;
using System.Xml.Linq;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Shared installed-library declarations; readers enforce their own physical boundary.
internal static class InstrumentLibraryMetadata
{
    internal static readonly string[] Files = ["InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll"];

    internal static XElement[] Validate(XElement package, Func<string, byte[]> readPayload)
    {
        if (package.Name.LocalName != "Package"
            || !string.Equals((string?)package.Attribute("Name"), PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase))
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
            if (Files.Contains(relative.Split('/')[^1], StringComparer.OrdinalIgnoreCase)
                && !Files.Contains(relative, StringComparer.Ordinal))
                throw new IOException("Library package declares an alternate DLL layout; required DLLs must be at the package input root.");
            var bytes = readPayload(relative);
            ValidateHash(bytes, relative, declaration);
        }
        foreach (var required in Files)
            if (!declarations.Any(element => (string?)element.Attribute("Path") == required))
                throw new IOException($"Library package must declare required payload '{required}'.");
        return declarations;
    }

    private static void ValidateHash(byte[] bytes, string file, XElement declaration)
    {
        var hashes = declaration.Descendants().Where(element => element.Name.LocalName == "Hash").ToArray();
        if (hashes.Length > 1 || hashes.Any(element => element.Parent != declaration || element.HasElements)) throw new IOException($"Malformed library payload hash for '{file}'.");
        if (hashes.Length == 1)
        {
            var hash = hashes[0].Value.Trim();
            if (hash.Length != 40 || hash.Any(character => !Uri.IsHexDigit(character)))
                throw new IOException($"Malformed library payload hash for '{file}'.");
            if (!Convert.ToHexString(SHA1.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Library payload '{file}' does not match its package metadata hash.");
        }
    }
}
