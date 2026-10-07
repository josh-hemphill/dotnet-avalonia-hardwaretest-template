using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using HardwareTest.OpenTap.Host;
using OpenTap;

namespace HardwareTest.Authoring;

public static partial class AuthoringInstrumentCatalog
{
    public const string LibraryPackage = "InstrumentComponents.OpenTap";
    private const string LibraryAssembly = "InstrumentComponents.OpenTap.dll";
    private static readonly Dictionary<string, AuthoringInstrumentAdapter> Library = new(StringComparer.Ordinal);
    private static readonly object LibraryGate = new();
    private static readonly Dictionary<string, string> DiscoveryIssues = new(StringComparer.Ordinal);
    public static IReadOnlyList<AuthoringInstrumentAdapter> All
    {
        get { lock (LibraryGate) return [.. Demo, .. Library.Values]; }
    }
    public static bool IsLibrary(string typeId) => typeId.StartsWith("InstrumentComponents.OpenTap.", StringComparison.Ordinal);

    /// Only the supported package payload in this home grants creation availability.
    /// Constructors configure resources; discovery never opens or queries an instrument.
    public static IReadOnlyList<AuthoringInstrumentAdapter> Discover(OpenTapHome home)
    {
        lock (LibraryGate)
        {
            var directory = Path.Combine(home.Root, "Packages", LibraryPackage);
            if (LibraryPayloadAvailability(home) is { Available: false } unavailable) return Missing(unavailable.Reason!);
            try
            {
                var manifest = new AuthoringManifest { Dependencies = [new() { Package = LibraryPackage, Version = PublishedInstrumentComponents.Version }] };
                var requirement = AuthoringEnvironmentAssessment.Packages(manifest, home).Single();
                if (!requirement.Satisfied) return Missing($"Instrument Components {requirement.State}; required {requirement.RequiredVersion}, installed {requirement.InstalledVersion}. Open Environment to prepare/import the compatible package.");
                if (AuthoringPluginSearch.DirectoryContainsVisaAdapter(directory))
                    return Missing("Instrument Components directory contains the legacy VISA plugin. Prepare an isolated library package in Environment before discovery.");
                var payloads = AuthoringAdapterPayloadInspection.CaptureLibraryPayload(home);
                var assemblyPath = Path.Combine(home.Root, LibraryAssembly);
                var contractPath = Path.Combine(home.Root, "InstrumentComponents.dll");
                var libraryBytes = payloads[LibraryAssembly];
                var contractBytes = payloads["InstrumentComponents.dll"];
                if (AuthoringPluginSearch.DirectoryContainsVisaAdapter(Path.GetDirectoryName(assemblyPath)!)) return Missing("Library payload directory contains the legacy VISA plugin; use an isolated authoring home.");
                // Keep the two validated payloads for OpenTAP's lazy metadata search,
                // while the shared loader records the exact bytes supplied to the CLR.
                var metadataDirectory = StableLibraryPayload(libraryBytes, contractBytes);
                var contract = OwnedInstrumentLibrary.Load(contractBytes, Path.Combine(metadataDirectory, "InstrumentComponents.dll"));
                var assembly = OwnedInstrumentLibrary.Load(libraryBytes, Path.Combine(metadataDirectory, LibraryAssembly));
                // OpenTAP may cache an assembly from an earlier home. Match actual bytes,
                // including the shared contract assembly, before trusting cached types.
                if (!SamePayload(assemblyPath, assembly)) return Missing("Selected Instrument Components binary differs from the loaded library. Restart authoring to use the newly installed library version.");
                if (!SamePayload(contractPath, contract)) return Missing("Selected Instrument Components contract binary differs from the loaded library. Restart authoring to use the newly installed library version.");
                AuthoringPluginSearch.Search([metadataDirectory]); // Never grant the home root or unrelated package payload.
                var baseType = assembly.GetType("InstrumentComponents.OpenTap.ScpiInstrument", throwOnError: true)!;
                var devices = DeviceTypes(assembly.GetTypes(), baseType);
                var result = new List<AuthoringInstrumentAdapter>();
                foreach (var type in devices)
                {
                    var id = type.FullName!;
                    if (!Library.TryGetValue(id, out var adapter))
                    {
                        var display = type.GetCustomAttribute<DisplayAttribute>();
                        adapter = new(id, display?.Name ?? type.Name, LibraryPackage, LibraryAssembly,
                            ["VisaAddress"], ["IoTimeoutMilliseconds"], [], HasLifecycle(assembly, type, "IdentityQueryStep"), HasLifecycle(assembly, type, "SafeShutdownStep"),
                            slot => ConstructLibrary(type, slot), resource => SerializeLibrary(resource))
                        { RequiredPayloadFiles = ["InstrumentComponents.dll"], Description = display?.Description ?? "SCPI instrument" };
                        Library.Add(id, adapter);
                    }
                    result.Add(adapter);
                }
                if (result.Count == 0) return Missing("Instrument Components payload contains no supported concrete device types. Open Environment to import a supported library package.");
                DiscoveryIssues.Remove(home.Root);
                return result;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException
                or ReflectionTypeLoadException or TypeLoadException or FileLoadException or ArgumentException or InvalidOperationException)
            { return Missing($"Instrument Components payload cannot be loaded: {error.Message}. Open Environment to import a valid compatible package."); }

            IReadOnlyList<AuthoringInstrumentAdapter> Missing(string reason) { DiscoveryIssues[home.Root] = reason; return []; }
        }
    }

    internal static AuthoringInstrumentAvailability LibraryPayloadAvailability(OpenTapHome home)
        => AuthoringAdapterPayloadInspection.Inspect(new("InstrumentComponents.OpenTap.Catalog", "Instrument Components", LibraryPackage, LibraryAssembly,
            [], [], [], true, true, _ => throw new NotSupportedException(), _ => throw new NotSupportedException())
        { RequiredPayloadFiles = ["InstrumentComponents.dll"] }, home);

    public static AuthoringInstrumentAvailability LibraryReadiness(OpenTapHome? home)
    {
        if (home is null) return new(false, "Select an authoring home before preparing Instrument Components in Environment.");
        lock (LibraryGate)
        {
            if (Discover(home).Count > 0) return new(true, "Ready in selected home — compatible installed Instrument Components package reused.");
            return new(false, DiscoveryIssues.GetValueOrDefault(home.Root) ?? "Instrument Components is unavailable; open Environment.");
        }
    }

    private static readonly Dictionary<string, string> MetadataPayloads = new(StringComparer.Ordinal);

    private static string StableLibraryPayload(byte[] library, byte[] contract)
    {
        var libraryHash = SHA256.HashData(library);
        var contractHash = SHA256.HashData(contract);
        var key = Convert.ToHexString(libraryHash) + Convert.ToHexString(contractHash);
        if (MetadataPayloads.TryGetValue(key, out var existing))
        {
            Verify(Path.Combine(existing, LibraryAssembly), libraryHash);
            Verify(Path.Combine(existing, "InstrumentComponents.dll"), contractHash);
            return existing;
        }
        var directory = Path.Combine(Path.GetTempPath(), "ht-library-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, LibraryAssembly), library);
            File.WriteAllBytes(Path.Combine(directory, "InstrumentComponents.dll"), contract);
            MetadataPayloads.Add(key, directory);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
            return directory;
        }
        catch { Directory.Delete(directory, recursive: true); throw; }
        static void Verify(string path, byte[] expected)
        {
            if (!SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(expected)) throw new IOException("Staged library metadata payload changed.");
        }
    }

    private static bool HasLifecycle(Assembly assembly, Type resource, string name)
    {
        var step = assembly.GetType("InstrumentComponents.OpenTap." + name);
        var binding = step?.GetProperty("Instrument");
        return step is not null && typeof(ITestStep).IsAssignableFrom(step) && step.GetConstructor(Type.EmptyTypes) is not null
            && binding?.CanWrite == true && binding.PropertyType.IsAssignableFrom(resource);
    }

    internal static Type[] DeviceTypes(IEnumerable<Type> catalog, Type baseType) => catalog
        .Where(type => type.IsPublic && !type.IsAbstract && !type.ContainsGenericParameters && type.IsSubclassOf(baseType)
            && typeof(Instrument).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    internal static bool LibraryPayloadMatches(OpenTapHome home)
    {
        try
        {
            var directory = Path.Combine(home.Root, "Packages", LibraryPackage);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == LibraryPackage);
            var contract = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "InstrumentComponents");
            if (assembly is null || contract is null) return false;
            var manifest = new AuthoringManifest { Dependencies = [new() { Package = LibraryPackage, Version = PublishedInstrumentComponents.Version }] };
            return AuthoringEnvironmentAssessment.Packages(manifest, home).Single().Satisfied
                && SamePayload(AuthoringAdapterPayloadInspection.LibraryPayloadPath(home, LibraryAssembly), assembly)
                && SamePayload(AuthoringAdapterPayloadInspection.LibraryPayloadPath(home, "InstrumentComponents.dll"), contract);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException or InvalidOperationException) { return false; }
    }

    private static bool SamePayload(string selected, Assembly loaded)
        => SHA256.HashData(File.ReadAllBytes(selected)).SequenceEqual(OwnedInstrumentLibrary.Fingerprint(loaded));

    private static Instrument ConstructLibrary(Type type, InstrumentRef slot)
    {
        var timeout = slot.Settings.TryGetValue("IoTimeoutMilliseconds", out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture) : 5000;
        if (timeout is < 100 or > 120000) throw new FormatException("I/O timeout must be between 100 and 120000 milliseconds.");
        var instrument = (Instrument)Activator.CreateInstance(type)!;
        instrument.Name = slot.SlotName;
        type.GetProperty("VisaAddress")!.SetValue(instrument, slot.VisaAddress);
        type.GetProperty("IoTimeoutMilliseconds")!.SetValue(instrument, timeout);
        return instrument;
    }

    private static InstrumentRef SerializeLibrary(Instrument resource) => new(resource.Name, resource.GetType().FullName!,
        (string)resource.GetType().GetProperty("VisaAddress")!.GetValue(resource)!)
    {
        Settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IoTimeoutMilliseconds"] = Convert.ToString(resource.GetType().GetProperty("IoTimeoutMilliseconds")!.GetValue(resource), CultureInfo.InvariantCulture)!
        }
    };
}
