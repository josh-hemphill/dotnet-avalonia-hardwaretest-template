using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace HardwareTest.OpenTap.Host;

/// Execution-only discovery. Never called by metadata consumers without a broker.
internal static class ExecutionInstrumentLibrary
{
    private static readonly Dictionary<string, string> Payloads = new(StringComparer.Ordinal);
    private static readonly Dictionary<Assembly, byte[]> LoadedHashes = [];
    private static readonly string[] Files = ["InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll"];

    internal static string EnsureLoaded(IEnumerable<string> trustedDirectories)
    {
        var selected = SelectInstalledRoot(trustedDirectories);
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == PublishedInstrumentComponents.PackageName);
        if (loaded is not null && selected is null)
        {
            VerifyContract(loaded);
            return Path.GetDirectoryName(loaded.Location)!;
        }

        var bytes = selected is null ? BundledPayload() : Files.Select(file => File.ReadAllBytes(Path.Combine(selected, file))).ToArray();
        var key = string.Join("", bytes.Select(payload => Convert.ToHexString(SHA256.HashData(payload))));
        if (!Payloads.TryGetValue(key, out var directory))
        {
            directory = Path.Combine(Path.GetTempPath(), "ht-library-execution-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            for (var i = 0; i < Files.Length; i++) File.WriteAllBytes(Path.Combine(directory, Files[i]), bytes[i]);
            Payloads.Add(key, directory);
            var retained = directory;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(retained, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
        }
        for (var i = 0; i < Files.Length; i++)
        {
            var path = Path.Combine(directory, Files[i]);
            if (!SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes[i])))
                throw new IOException("The staged execution library changed.");
            var identity = AssemblyName.GetAssemblyName(path);
            if (identity.Name != Path.GetFileNameWithoutExtension(Files[i]) || identity.Version != new Version(0, 1, 1, 0))
                throw new InvalidOperationException("Instrument Components execution requires current 0.1.1 library assemblies.");
            var assembly = Assembly.LoadFrom(path);
            if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location)
                || !LoadedFingerprint(assembly).SequenceEqual(SHA256.HashData(bytes[i])))
                throw new InvalidOperationException("The selected Instrument Components payload differs from the loaded execution library. Restart the executing process to use this home.");
            if (i == 1) VerifyContract(assembly);
        }
        return directory;
    }

    private static string? SelectInstalledRoot(IEnumerable<string> trustedDirectories)
    {
        foreach (var directory in trustedDirectories)
        {
            if (File.Exists(Path.Combine(directory, "package.xml")) && IsLibraryMetadata(Path.Combine(directory, "package.xml")))
                throw new InvalidOperationException("Instrument Components execution requires an installed home root with root DLLs and Packages/InstrumentComponents.OpenTap/package.xml. Import package directories before execution.");
            var metadata = Path.Combine(directory, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
            if (!Files.Any(file => File.Exists(Path.Combine(directory, file))) && !File.Exists(metadata)) continue;
            var packages = Path.Combine(directory, "Packages");
            if (Directory.Exists(packages) && Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories)
                .Any(file => Files.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Instrument Components execution cannot use obsolete package-directory library DLLs. Import or repair the selected package to keep library DLLs only in the installed home root.");
            if (!IsLibraryMetadata(metadata, PublishedInstrumentComponents.Version))
                throw new InvalidOperationException("Instrument Components execution requires installed InstrumentComponents.OpenTap 0.1.1 package metadata in the selected home root.");
            if (!Files.All(file => File.Exists(Path.Combine(directory, file))))
                throw new InvalidOperationException("Instrument Components execution requires both library DLLs in the installed home root. Import or repair the selected package before execution.");
            return directory;
        }
        return null;
    }

    private static bool IsLibraryMetadata(string metadata, string? version = null)
    {
        if (!File.Exists(metadata)) return false;
        try
        {
            var root = XDocument.Load(metadata).Root;
            return root?.Name.LocalName == "Package"
                && string.Equals((string?)root.Attribute("Name"), PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase)
                && (version is null || ((string?)root.Attribute("Version"))?.Split('+')[0] == version);
        }
        catch (System.Xml.XmlException) { return false; }
    }

    private static byte[] LoadedFingerprint(Assembly assembly)
    {
        if (LoadedHashes.TryGetValue(assembly, out var hash)) return hash;
        var bytes = File.ReadAllBytes(assembly.Location);
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.ManifestModule.ModuleVersionId)
            throw new InvalidOperationException("The loaded execution library no longer matches its source payload. Restart the executing process.");
        hash = SHA256.HashData(bytes);
        LoadedHashes.Add(assembly, hash);
        return hash;
    }

    private static void VerifyContract(Assembly assembly)
    {
        var identity = assembly.GetName();
        var provider = assembly.GetType(InstrumentComponentsScpiIo.OpenTapScpiIoTypeName)?.GetProperty("Provider", BindingFlags.Public | BindingFlags.Static);
        var open = provider?.PropertyType.GetMethod("Open", [typeof(string), typeof(TimeSpan)]);
        if (identity.Name != PublishedInstrumentComponents.PackageName || identity.Version != new Version(0, 1, 1, 0)
            || provider is null || !provider.CanWrite || !provider.PropertyType.IsInterface
            || provider.PropertyType.FullName != "InstrumentComponents.OpenTap.IOpenTapScpiIoProvider"
            || provider.PropertyType.Assembly != assembly
            || open is null || open.ReturnType.FullName != "InstrumentComponents.Scpi.IScpiIo" || !open.ReturnType.IsInterface
            || open.ReturnType.Assembly.GetName().Name != "InstrumentComponents"
            || open.ReturnType.Assembly.GetName().Version != new Version(0, 1, 1, 0))
            throw new InvalidOperationException("The selected Instrument Components library does not support the current 0.1.1 broker-managed execution contract.");
    }

    private static byte[][] BundledPayload()
    {
        using var stream = PublishedInstrumentComponents.OpenArchive();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        if (!Convert.ToHexString(SHA256.HashData(buffer.ToArray())).Equals(PublishedInstrumentComponents.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The bundled Instrument Components release hash does not match.");
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        return Files.Select(file =>
        {
            using var source = (zip.GetEntry(file) ?? throw new InvalidOperationException("Bundled library payload missing: " + file)).Open();
            using var payload = new MemoryStream();
            source.CopyTo(payload);
            return payload.ToArray();
        }).ToArray();
    }
}
