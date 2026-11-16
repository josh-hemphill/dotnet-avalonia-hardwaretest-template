using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using OpenTap;
using OpenTap.Package;

namespace HardwareTest.Authoring;

internal sealed record OfflineBenchCandidate(string Name, SemanticVersion Version, string OS, string Architecture,
    string Archive, string PayloadHash, int Priority, IReadOnlyList<OfflineBenchRequirement> Dependencies);

/// Captures package bytes without running installers or changing the selected OpenTAP home.
internal sealed class OfflineBenchPackageInventory(string owned, OfflineBenchExportOptions options, CancellationToken token,
    long maximumBytes = 512L * 1024 * 1024)
{
    internal readonly List<OfflineBenchCandidate> Candidates = [];
    private readonly Dictionary<string, string> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _inputModes = new(StringComparer.Ordinal);
    private readonly List<(string Root, string[] Files)> _folders = [];
    private sealed record Source(string? Name, string Path, int Priority, string? Home);
    private readonly List<Source> _pending = [];
    private readonly Dictionary<string, List<string>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _homeIssues = new(StringComparer.Ordinal);

    internal void EnsureCandidates(string name)
    {
        foreach (var source in _pending.Where(source => source.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true).ToArray())
        {
            _pending.Remove(source);
            try
            {
                if (source.Home is null) AddArchive(source.Path, source.Priority);
                else AddInstalledPackage(source.Path, source.Home);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                if (!_diagnostics.TryGetValue(name, out var errors)) _diagnostics[name] = errors = [];
                errors.Add(Path.GetFileName(source.Path) + ": " + error.Message);
            }
        }
    }

    internal string Diagnostics(string name)
        => _diagnostics.TryGetValue(name, out var errors) ? " Candidate problems: " + string.Join("; ", errors.Distinct()) : string.Empty;


    internal byte[] Capture(string path)
    {
        token.ThrowIfCancellationRequested();
        RequireRegular(path);
        var mode = ReadMode(path);
        var (bytes, hash) = ReadBounded(path, capture: true);
        if (mode != ReadMode(path)) throw Changed(path);
        if (_inputs.TryGetValue(path, out var prior) && prior != hash) throw Changed(path);
        _inputs[path] = hash;
        _inputModes[path] = mode;
        return bytes;
    }

    internal void Recheck()
    {
        foreach (var input in _inputs)
        {
            token.ThrowIfCancellationRequested();
            RequireRegular(input.Key);
            if (!File.Exists(input.Key) || ReadBounded(input.Key, capture: false).Hash != input.Value || ReadMode(input.Key) != _inputModes[input.Key]) throw Changed(input.Key);
        }
        foreach (var folder in _folders)
            if (!ArchiveFiles(folder.Root).SequenceEqual(folder.Files, StringComparer.Ordinal)) throw Changed(folder.Root);
    }

    internal void AddFolder(string directory, int priority)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new AuthoringWorkspaceException($"Package folder was not found: {directory}. Choose the folder containing matching .TapPackage or .zip archives.");
        RequireRegular(directory);
        var files = ArchiveFiles(directory);
        _folders.Add((directory, files));
        foreach (var file in files)
        {
            try
            {
                RequireRegular(file);
                using var archive = ZipFile.OpenRead(file);
                var metadata = archive.Entries.Where(entry => Path.GetFileName(entry.FullName).Equals("package.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
                var name = metadata.Length == 1 ? PeekArchiveName(metadata[0]) : null;
                _pending.Add(new(name, file, priority, null));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // An unrelated corrupt archive is not a required dependency.
                // A missing requested identity still produces the manual remedy.
                _pending.Add(new(null, file, priority, null));
            }
        }
    }

    internal void AddArchive(string path, int priority)
    {
        options.Progress?.Invoke("Reading package archive " + Path.GetFileName(path) + "…");
        var bytes = Capture(Path.GetFullPath(path));
        var snapshot = Path.Combine(owned, Guid.NewGuid().ToString("N") + ".TapPackage");
        File.WriteAllBytes(snapshot, bytes);
        AddSnapshot(snapshot, priority);
    }

    internal void AddSnapshot(string snapshot, int priority)
    {
        token.ThrowIfCancellationRequested();
        Capture(snapshot);
        using var archive = ZipFile.OpenRead(snapshot);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var path = SafeRelative(entry.FullName.TrimEnd('/'));
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || !entries.TryAdd(path, entry))
                throw new AuthoringWorkspaceException("Package archive contains links or duplicate/case-colliding paths.");
            total += entry.Length;
            if (total > maximumBytes) throw new AuthoringWorkspaceException("Package archive expands beyond the supported payload limit.");
        }
        var metadata = entries.Where(pair => Path.GetFileName(pair.Key).Equals("package.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (metadata.Length != 1) throw new AuthoringWorkspaceException("Each package archive must contain exactly one package.xml identity.");
        using var stream = metadata[0].Value.Open();
        var xml = ReadXml(stream);
        var identity = Identity(xml);
        if (metadata[0].Key != "package.xml" && metadata[0].Key != $"Packages/{identity.Name}/package.xml")
            throw new AuthoringWorkspaceException("Package metadata must be package.xml or Packages/<name>/package.xml.");
        var declared = PayloadPaths(xml);
        var allowed = new HashSet<string>(declared.Prepend(metadata[0].Key), StringComparer.OrdinalIgnoreCase);
        if (entries.Any(entry => !entry.Value.FullName.EndsWith('/') && !allowed.Contains(entry.Key)))
            throw new AuthoringWorkspaceException($"Package '{identity.Name}' contains undeclared archive payload. Obtain a package whose declared files match its contents.");
        var fingerprint = new StringBuilder();
        foreach (var dependency in Dependencies(xml).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            fingerprint.Append(dependency.Name).Append(':').Append(dependency.VersionRange).Append('\n');
        foreach (var file in declared.Where(file => !file.Equals(metadata[0].Key, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (!entries.TryGetValue(file, out var entry) || entry.FullName.EndsWith('/'))
                throw new AuthoringWorkspaceException($"Package '{identity.Name}' is missing declared payload '{file}'.");
            using var input = entry.Open();
            var declaredMode = OfflineBenchBundle.DeclarativePayloadMode(xml, file, (UnixFileMode)(entry.ExternalAttributes >> 16 & 0x1FF), options.TargetOS);
            var mode = (int)OfflineBenchBundle.EffectiveExecutableMode(file, declaredMode, options.TargetOS);
            fingerprint.Append(file).Append(':').Append(mode).Append(':').Append(Convert.ToHexString(SHA256.HashData(input))).Append('\n');
        }
        if (!Compatible(identity.OS, identity.Architecture)) return;
        Candidates.Add(new(identity.Name, identity.Version, identity.OS, identity.Architecture, snapshot,
            BenchProgramHandoff.Hash(Encoding.UTF8.GetBytes(fingerprint.ToString())), priority, Dependencies(xml)));
    }

    internal void AddInstalledHome(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) return;
        try { RequireRegular(root); }
        catch (AuthoringWorkspaceException) { return; }
        var hostOS = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "MacOS" : "Linux";
        if (options.TargetOS != hostOS || !options.TargetArchitecture.Equals(ArchitectureHelper.HostArchitecture.ToString(), StringComparison.OrdinalIgnoreCase)) return;
        var packages = Path.Combine(root, "Packages");
        if (!Directory.Exists(packages)) return;
        foreach (var metadata in MetadataFiles(packages).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                RequireRegular(metadata);
                using var input = File.OpenRead(metadata);
                var name = (string?)ReadXml(input).Attribute("Name");
                _pending.Add(new(name, metadata, 1, root));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { _pending.Add(new(Path.GetFileName(Path.GetDirectoryName(metadata)), metadata, 1, root)); }
        }
    }

    private void AddInstalledPackage(string metadata, string root)
    {
        if (!_homeIssues.TryGetValue(root, out var problem))
            _homeIssues[root] = problem = StandaloneVisaReadiness.OpenTapRuntimeIssue(new(root));
        if (problem is not null) throw new AuthoringWorkspaceException("Selected OpenTAP home cannot supply offline packages: " + problem + " Use matching manual archives instead.");
        var bytes = Capture(metadata);
        using var input = new MemoryStream(bytes);
        var xml = ReadXml(input);
        var identity = Identity(xml);
        if (!Compatible(identity.OS, identity.Architecture)) return;
        options.Progress?.Invoke($"Capturing installed package {identity.Name} {identity.Version} for the same bench platform…");
        // Installed payload is reused only on the same RID; metadata alone
        // cannot prove that an AnyCPU package contains no native assets.
        var payload = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var modes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in xml.Descendants().Where(element => element.Name.LocalName == "File").ToArray())
        {
            var relative = SafeRelative((string?)file.Attribute("Path") ?? "");
            var source = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source)) source = Path.Combine(Path.GetDirectoryName(metadata)!, relative.Replace('/', Path.DirectorySeparatorChar));
            var canonical = SafeRelative(Path.GetRelativePath(root, source));
            payload[canonical] = Capture(source);
            modes[canonical] = ReadMode(source);
            file.SetAttributeValue("Path", canonical);
        }
        payload[$"Packages/{identity.Name}/package.xml"] = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        var snapshot = Path.Combine(owned, Guid.NewGuid().ToString("N") + ".TapPackage");
        using (var archive = ZipFile.Open(snapshot, ZipArchiveMode.Create))
            foreach (var file in payload)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                modes.TryGetValue(file.Key, out var mode);
                var declaredMode = OfflineBenchBundle.DeclarativePayloadMode(xml, file.Key, (UnixFileMode)mode, options.TargetOS);
                entry.ExternalAttributes = (0x8000 | (int)declaredMode) << 16;
                using var output = entry.Open(); output.Write(file.Value);
            }
        AddSnapshot(snapshot, 1);
    }

    private static string? PeekArchiveName(ZipArchiveEntry metadata)
    {
        using var input = metadata.Open();
        return (string?)ReadXml(input).Attribute("Name");
    }

    private static int ReadMode(string path)
        => OperatingSystem.IsWindows() ? 0 : (int)File.GetUnixFileMode(path) & 0x1FF;

    private static IEnumerable<string> MetadataFiles(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "package.xml")) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
        }
    }

    private (byte[] Bytes, string Hash) ReadBounded(string path, bool capture)
    {
        token.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        if (input.Length > maximumBytes) throw new AuthoringWorkspaceException("Offline bundle input exceeds the supported size limit: " + path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var output = capture ? new MemoryStream() : null;
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            total += count;
            if (total > maximumBytes) throw new AuthoringWorkspaceException("Offline bundle input grew beyond the supported size limit: " + path);
            hash.AppendData(buffer, 0, count);
            output?.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        if (input.Length != total) throw Changed(path);
        RequireRegular(path);
        return (output?.ToArray() ?? [], Convert.ToHexString(hash.GetHashAndReset()));
    }

    internal bool Compatible(string os, string architecture)
        => (string.IsNullOrWhiteSpace(os) || os.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(options.TargetOS, StringComparer.OrdinalIgnoreCase))
            && (architecture.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase) || architecture.Equals(options.TargetArchitecture, StringComparison.OrdinalIgnoreCase));

    private static (string Name, SemanticVersion Version, string OS, string Architecture) Identity(XElement xml)
    {
        var name = ((string?)xml.Attribute("Name") ?? "").Trim();
        if (xml.Name.LocalName != "Package" || string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Any(char.IsControl))
            throw new AuthoringWorkspaceException("Invalid package identity in package.xml.");
        try { OfflineBenchBundle.ValidatePackageMetadata(xml); }
        catch (InvalidDataException error)
        { throw new AuthoringWorkspaceException($"Package '{name}' has unsupported install actions or native prerequisites. Resolve station prerequisites separately and obtain a data-only package release. {error.Message}", error); }
        return (name, SemanticVersion.Parse((string?)xml.Attribute("Version") ?? ""), (string?)xml.Attribute("OS") ?? "",
            (string?)xml.Attribute("Architecture") ?? "AnyCPU");
    }

    private static IReadOnlyList<OfflineBenchRequirement> Dependencies(XElement xml)
        => xml.Descendants().Where(element => element.Name.LocalName == "PackageDependency").Select(element => new OfflineBenchRequirement
        { Name = (string?)element.Attribute("Package") ?? "", VersionRange = (string?)element.Attribute("Version") ?? "" }).ToArray();

    private static string[] PayloadPaths(XElement xml)
    {
        var files = xml.Descendants().Where(element => element.Name.LocalName == "File").Select(element => SafeRelative((string?)element.Attribute("Path") ?? "")).ToArray();
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length) throw new AuthoringWorkspaceException("Package declares duplicate payload paths.");
        return files;
    }

    private static XElement ReadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        return XDocument.Load(reader).Root ?? throw new AuthoringWorkspaceException("Empty package.xml.");
    }

    internal static string SafeRelative(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new AuthoringWorkspaceException("Package payload contains an unsafe relative path.");
        return normalized;
    }

    internal static void RequireRegular(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new AuthoringWorkspaceException("Offline bundle inputs and destinations cannot contain symbolic links: " + path);
    }

    private static string[] ArchiveFiles(string root) => Directory.EnumerateFiles(root).Where(path => path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
    private static AuthoringWorkspaceException Changed(string path) => new("Offline bundle input changed during preparation; prior output was preserved. Retry after saving: " + path);
}
