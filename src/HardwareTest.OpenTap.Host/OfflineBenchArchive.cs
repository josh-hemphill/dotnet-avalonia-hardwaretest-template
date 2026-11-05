using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using OpenTap;
using OpenTap.Package;

namespace HardwareTest.OpenTap.Host;

internal sealed record OfflineBenchArchiveData(
    string Name, string Version, string OS, string Architecture,
    IReadOnlyList<OfflineBenchRequirement> Dependencies, IReadOnlyDictionary<string, byte[]> Payload,
    IReadOnlyDictionary<string, UnixFileMode> ExecutableModes)
{
    public bool SetsTapExecutable { get; init; }
}

internal static class OfflineBenchArchive
{
    internal const long MaximumFileBytes = 512L * 1024 * 1024;

    public static OfflineBenchArchiveData Read(byte[] archiveBytes, string targetOS, string targetArchitecture, CancellationToken cancellationToken,
        OfflineBenchReadBudget? sharedBudget = null)
    {
        using var input = new MemoryStream(archiveBytes, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var budget = sharedBudget ?? new OfflineBenchReadBudget();
        // Reserve the complete expansion before allocating any of this archive.
        foreach (var entry in zip.Entries) { cancellationToken.ThrowIfCancellationRequested(); budget.Reserve(entry.Length); }
        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = OfflineBenchPaths.Relative(entry.FullName.TrimEnd('/'));
            if (!names.Add(name) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Offline package contains duplicate paths, case collisions or symbolic links.");
            if (entry.FullName.EndsWith('/')) continue;
            if (entry.Length > MaximumFileBytes)
                throw new InvalidDataException("Offline package exceeds the supported payload size.");
            using var stream = entry.Open();
            var owned = OfflineBenchReadBudget.Read(stream, entry.Length, cancellationToken);
            if (owned.LongLength != entry.Length) throw new InvalidDataException("Offline archive entry length changed while reading.");
            entries.Add(name, owned);
            var mode = (UnixFileMode)(entry.ExternalAttributes >> 16 & 0x1FF);
            if (mode != 0) modes[name] = mode;
        }
        var metadataEntries = entries.Where(entry => Path.GetFileName(entry.Key).Equals("package.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (metadataEntries.Length != 1) throw new InvalidDataException("An offline package must contain exactly one package.xml identity.");
        var metadata = metadataEntries[0];
        var xml = ReadXml(metadata.Value).Root;
        if (xml?.Name.LocalName != "Package") throw new InvalidDataException("Offline archive has no Package identity.");
        var nameValue = Name((string?)xml.Attribute("Name"));
        var version = SemanticVersion.Parse((string?)xml.Attribute("Version") ?? "").ToString();
        var os = ((string?)xml.Attribute("OS") ?? string.Empty).Trim();
        var architecture = ((string?)xml.Attribute("Architecture") ?? "AnyCPU").Trim();
        if (!PlatformMatches(os, architecture, targetOS, targetArchitecture))
            throw new InvalidDataException($"Package '{nameValue}' targets {os}/{architecture}, not {targetOS}/{targetArchitecture}.");
        var canonicalMetadata = $"Packages/{nameValue}/package.xml";
        if (metadata.Key != "package.xml" && metadata.Key != canonicalMetadata)
            throw new InvalidDataException("Package metadata must be at the archive root or Packages/<name>/package.xml.");
        OfflineBenchBundle.ValidatePackageMetadata(xml);
        var dependencies = xml.Elements().Where(element => element.Name.LocalName == "Dependencies")
            .SelectMany(element => element.Elements()).Select(element =>
            {
                if (element.Name.LocalName != "PackageDependency") throw new InvalidDataException("Unsupported package dependency declaration.");
                var dependency = new OfflineBenchRequirement { Name = Name((string?)element.Attribute("Package")), VersionRange = ((string?)element.Attribute("Version") ?? string.Empty).Trim() };
                if (string.IsNullOrWhiteSpace(dependency.VersionRange)) throw new InvalidDataException("Package dependency must declare an explicit version constraint.");
                OfflineBenchVersions.RequireValid(dependency.VersionRange);
                return dependency;
            }).ToArray();
        if (dependencies.Select(dependency => dependency.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dependencies.Length)
            throw new InvalidDataException("Offline package has ambiguous duplicate dependencies.");
        var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [canonicalMetadata] = metadata.Value };
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { metadata.Key };
        foreach (var file in xml.Elements().Where(element => element.Name.LocalName == "Files").SelectMany(element => element.Elements()))
        {
            if (file.Name.LocalName != "File") throw new InvalidDataException("Unsupported package payload declaration.");
            var path = OfflineBenchPaths.Relative((string?)file.Attribute("Path"));
            if (!declared.Add(path))
            {
                if (path == metadata.Key) continue;
                throw new InvalidDataException("Offline package declares duplicate or case-colliding payload paths.");
            }
            if (!entries.Keys.Contains(path, StringComparer.Ordinal)) throw new InvalidDataException("Declared payload casing must match its archive entry exactly.");
            if (!entries.TryGetValue(path, out var bytes)) throw new InvalidDataException($"Declared package payload is missing: {path}");
            var segments = path.Split('/');
            if (segments[0].Equals("Packages", StringComparison.OrdinalIgnoreCase)
                && (segments.Length < 3 || !segments[1].Equals(nameValue, StringComparison.Ordinal)))
                throw new InvalidDataException("A package cannot supply another package's payload.");
            if (new[] { "Programs", "runs", "logs", "credentials", "bench-references", "data", "settings" }.Contains(segments[0], StringComparer.OrdinalIgnoreCase)
                || new[] { "settings.json", "ui-state.json", OfflineBenchStartup.InstallationFileName, OfflineBenchStartup.LockFileName, OfflineBenchStartup.DataOwnershipFileName, OfflineBenchRuntime.CapabilityFileName }.Contains(path, StringComparer.OrdinalIgnoreCase)
                || new[] { ".sys", ".inf", ".ko", ".msi", ".msix", ".deb", ".rpm" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Offline packages cannot write station data, installation ownership or OS driver/install payloads.");
            if (payload.TryGetValue(path, out var prior) && !prior.SequenceEqual(bytes))
                throw new InvalidDataException("Package payload cannot replace its identity metadata.");
            payload[path] = bytes;
        }
        foreach (var path in payload.Keys)
            if (payload.Keys.Any(other => other.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Package payload contains conflicting file and directory paths.");
        if (entries.Keys.Any(path => !declared.Contains(path)))
            throw new InvalidDataException("Offline archive contains undeclared payload. Rebuild it with a complete Files inventory.");
        if (payload.Count <= 1) throw new InvalidDataException("Offline package contains no declared payload.");
        foreach (var path in payload.Keys)
        {
            modes.TryGetValue(path, out var mode);
            var declaredMode = OfflineBenchBundle.DeclarativePayloadMode(xml, path, mode, targetOS);
            if (declaredMode != 0) modes[path] = declaredMode;
        }
        return new(nameValue, version, os, architecture, dependencies, payload, modes)
        {
            SetsTapExecutable = payload.ContainsKey("tap") && OfflineBenchBundle.DeclarativePayloadMode(xml, "tap", 0, targetOS) != 0,
        };
    }

    public static XDocument ReadXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    public static bool PlatformMatches(string os, string architecture, string targetOS, string targetArchitecture)
        => (string.IsNullOrWhiteSpace(os) || os.Split(',', StringSplitOptions.TrimEntries).Contains(targetOS, StringComparer.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(architecture) || architecture.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase)
                || architecture.Split(',', StringSplitOptions.TrimEntries).Contains(targetArchitecture, StringComparer.OrdinalIgnoreCase));

    public static string Name(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        OfflineBenchPaths.Relative(name);
        if (name.Contains('/')) throw new InvalidDataException("Package names must be a single portable path segment.");
        return name;
    }
}
