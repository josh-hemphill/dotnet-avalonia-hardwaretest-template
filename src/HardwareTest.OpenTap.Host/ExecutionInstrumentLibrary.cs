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
    private static readonly string[] Files = InstrumentLibraryMetadata.Files;

    internal static string EnsureLoaded(IEnumerable<string> trustedDirectories)
    {
        var selected = SelectInstalledRoot(trustedDirectories);
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == PublishedInstrumentComponents.PackageName);
        if (loaded is not null && selected is null)
        {
            var contract = VerifyContract(loaded);
            var assemblies = new[] { contract, loaded };
            var bundled = BundledPayload();
            for (var i = 0; i < assemblies.Length; i++)
                if (!OwnedInstrumentLibrary.Fingerprint(assemblies[i]).SequenceEqual(SHA256.HashData(bundled[i])))
                    throw new InvalidOperationException("An execution library loaded without a selected home must match the bundled 0.1.1 payload. Configure a trusted installed home to use a custom library.");
            return OwnedInstrumentLibrary.Directory(loaded);
        }

        var bytes = selected is null ? BundledPayload() : selected;
        if (selected is null) ValidateAssemblyIdentities(bytes);
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
            var assembly = OwnedInstrumentLibrary.Load(path);
            if (!OwnedInstrumentLibrary.Fingerprint(assembly).SequenceEqual(SHA256.HashData(bytes[i])))
                throw new InvalidOperationException("The selected Instrument Components payload differs from the loaded execution library. Restart the executing process to use this home.");
            if (i == 1) VerifyContract(assembly);
        }
        return directory;
    }

    private static void ValidateAssemblyIdentities(byte[][] payloads)
    {
        // Inspect both immutable buffers before either enters the CLR loader.
        for (var i = 0; i < Files.Length; i++)
        {
            try
            {
                using var stream = new MemoryStream(payloads[i], writable: false);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null)
                    throw new BadImageFormatException("Payload has no managed metadata.");
                var metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly)
                    throw new BadImageFormatException("Payload is not a managed assembly.");
                var identity = metadata.GetAssemblyDefinition();
                if (metadata.GetString(identity.Name) != Path.GetFileNameWithoutExtension(Files[i])
                    || identity.Version != new Version(0, 1, 1, 0))
                    throw new InvalidOperationException("Instrument Components execution requires current 0.1.1 library assemblies.");
            }
            catch (BadImageFormatException error)
            {
                throw new InvalidOperationException("Instrument Components execution requires current 0.1.1 library assemblies; malformed payload: " + Files[i], error);
            }
        }
    }

    private static byte[][]? SelectInstalledRoot(IEnumerable<string> trustedDirectories)
    {
        var claims = new List<byte[][]>();
        foreach (var candidate in trustedDirectories)
        {
            var directory = ExecutionLibraryHome.Validate(candidate);
            var metadata = Path.Combine(directory, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
            if (!Files.Any(file => File.Exists(Path.Combine(directory, file))) && !File.Exists(metadata)) continue;
            using var metadataStream = new MemoryStream(ExecutionLibraryHome.ReadPayload(directory,
                "Packages/" + PublishedInstrumentComponents.PackageName + "/package.xml"), writable: false);
            var document = XDocument.Load(metadataStream);
            var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            InstrumentLibraryMetadata.Validate(document.Root ?? throw new IOException("Library package metadata has no root element."), file =>
            {
                var payload = ExecutionLibraryHome.ReadPayload(directory, file.Replace('/', Path.DirectorySeparatorChar));
                captured.Add(file, payload);
                return payload;
            });
            var payloads = Files.Select(file => captured[file]).ToArray();
            ValidateAssemblyIdentities(payloads);
            claims.Add(payloads);
        }
        if (claims.Count == 0) return null;
        var selected = claims[0];
        if (claims.Skip(1).Any(payloads => Enumerable.Range(0, Files.Length).Any(index => !payloads[index].SequenceEqual(selected[index]))))
            throw new InvalidOperationException("Configured execution roots contain different Instrument Components payloads. Select one library home or configure identical current library payloads before execution.");
        return selected;
    }

    private static Assembly VerifyContract(Assembly assembly)
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
        return open.ReturnType.Assembly;
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
