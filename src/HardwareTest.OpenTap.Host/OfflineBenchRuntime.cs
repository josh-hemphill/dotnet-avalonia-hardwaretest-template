using System.Text;
using System.Text.Json;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

public static partial class OfflineBenchRuntime
{
    public const string CapabilityFileName = "offline-bench-capability.json";

    public static OfflineBenchCapabilityReceipt ValidateCapability(string appDirectory, string? expectedRuntimeIdentifier = null)
    {
        var path = OfflineBenchPaths.Under(appDirectory, CapabilityFileName);
        if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024)
            throw new InvalidDataException("This operator has no supported offline-install capability receipt. Use a matching current operator release.");
        var receipt = ReadCapability(File.ReadAllText(path), expectedRuntimeIdentifier);
        foreach (var (name, hash) in new[] { ("HardwareTest.dll", receipt.OperatorSha256), ("HardwareTest.OpenTap.Host.dll", receipt.HostSha256) })
        {
            var file = OfflineBenchPaths.Under(appDirectory, name);
            if (!File.Exists(file) || !OfflineBenchPaths.Hash(file).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Operator capability receipt no longer matches {name}. Use the intact matching release.");
        }
        return receipt;
    }

    private static OfflineBenchCapabilityReceipt ReadCapability(string json, string? expectedRuntimeIdentifier)
    {
        var receipt = JsonSerializer.Deserialize(json.TrimStart('\uFEFF'), OfflineBenchJsonContext.Default.OfflineBenchCapabilityReceipt)
            ?? throw new InvalidDataException("The operator offline-install capability receipt is empty.");
        if (receipt.SchemaVersion != 1 || receipt.Capability != OfflineBenchBundle.InstallerCapability
            || !OfflineBenchPaths.IsHash(receipt.OperatorSha256) || !OfflineBenchPaths.IsHash(receipt.HostSha256)
            || receipt.RuntimeIdentifier is not ("win-x64" or "linux-x64" or "linux-arm64")
            || expectedRuntimeIdentifier is not null && receipt.RuntimeIdentifier != expectedRuntimeIdentifier)
            throw new InvalidDataException("Operator offline-install capability, schema or target does not match this installation.");
        return receipt;
    }

    internal static void ValidateCapturedCapability(IReadOnlyDictionary<string, byte[]> runtime, string rid)
    {
        var receipt = ReadCapability(Encoding.UTF8.GetString(runtime[CapabilityFileName]), rid);
        foreach (var (name, hash) in new[] { ("HardwareTest.dll", receipt.OperatorSha256), ("HardwareTest.OpenTap.Host.dll", receipt.HostSha256) })
            if (!OfflineBenchPaths.HashBytes(runtime[name]).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Captured operator capability does not match {name}. Source release changed while reading; check again.");
    }

    internal static IReadOnlyDictionary<string, byte[]> Capture(string directory, OfflineBenchSnapshot bundle, CancellationToken cancellationToken)
        => Capture(directory, bundle, cancellationToken, out _);

    internal static IReadOnlyDictionary<string, byte[]> Capture(string directory, OfflineBenchSnapshot bundle, CancellationToken cancellationToken,
        out IReadOnlyDictionary<string, UnixFileMode> executableModes)
    {
        var rid = RuntimeIdentifier(bundle.Manifest.TargetOS, bundle.Manifest.TargetArchitecture);
        ValidateCapability(directory, rid);
        var tree = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool TryPath(string relative, out string path)
        {
            path = OfflineBenchPaths.Under(directory, relative);
            if (!File.Exists(path)) return false;
            tree[relative] = path;
            return true;
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string relative, bool required = false)
        {
            OfflineBenchPaths.Relative(relative);
            if (!TryPath(relative, out _))
            {
                if (required) throw new InvalidDataException($"Operator release is missing '{relative}'. Use a complete published operator runtime.");
                return;
            }
            paths.Add(relative);
        }
        foreach (var name in new[] { "HardwareTest.dll", "HardwareTest.OpenTap.Host.dll", "HardwareTest.deps.json", "HardwareTest.runtimeconfig.json", CapabilityFileName }) Add(name, true);
        foreach (var name in new[] { OperatingSystem.IsWindows() ? "HardwareTest.OpenTap.Worker.exe" : "HardwareTest.OpenTap.Worker", "HardwareTest.OpenTap.Worker.dll", "HardwareTest.OpenTap.Worker.deps.json", "HardwareTest.OpenTap.Worker.runtimeconfig.json" }) Add(name, true);
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(tree["HardwareTest.OpenTap.Worker"]) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
            throw new InvalidDataException("Operator worker apphost is not executable. Use a complete matching operator release.");
        foreach (var name in new[] { "HardwareTest", "HardwareTest.exe", "HardwareTest.OpenTap.Worker", "HardwareTest.OpenTap.Worker.exe", "HardwareTest.OpenTap.Worker.dll", "HardwareTest.OpenTap.Worker.deps.json", "HardwareTest.OpenTap.Worker.runtimeconfig.json", "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap", "tap.exe", "tap.deps.json", "tap.runtimeconfig.json" }) Add(name);
        foreach (var deps in new[] { "HardwareTest.deps.json", "HardwareTest.OpenTap.Worker.deps.json" })
        {
            if (!tree.TryGetValue(deps, out var source)) continue;
            using var document = JsonDocument.Parse(File.ReadAllBytes(source));
            if (!document.RootElement.TryGetProperty("targets", out var targets)) throw new InvalidDataException("Operator release has no declared runtime dependency inventory.");
            foreach (var target in targets.EnumerateObject())
                foreach (var library in target.Value.EnumerateObject())
                    foreach (var kind in new[] { "runtime", "native", "resources", "runtimeTargets" })
                        if (library.Value.TryGetProperty(kind, out var assets))
                            foreach (var asset in assets.EnumerateObject())
                            {
                                if (asset.Value.TryGetProperty("rid", out var assetRid) && assetRid.GetString() != rid) continue;
                                var relative = OfflineBenchPaths.Relative(asset.Name);
                                if (!IsCodeFile(relative)) continue;
                                if (TryPath(relative, out _)) Add(relative);
                                else if (kind == "resources" && asset.Value.TryGetProperty("locale", out var locale)) Add(locale.GetString() + "/" + Path.GetFileName(relative), true);
                                else Add(Path.GetFileName(relative), true);
                            }
        }
        // Only declared dependency assets and the two fixed shipped archives are release content.
        foreach (var path in new[] { "PublishedArtifacts/InstrumentComponents.OpenTap.0.1.1.TapPackage", "PublishedArtifacts/HardwareTest.Standalone.VISA.0.1.0.TapPackage" }) Add(path);
        var owned = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var capturedModes = new Dictionary<string, UnixFileMode>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.StartsWith("Programs/", StringComparison.OrdinalIgnoreCase) || path.Split('/').Any(segment => new[] { "runs", "logs", "credentials", "bench-references", "cache", "settings", "data" }.Contains(segment, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Operator dependency inventory attempts to include station data or old programs.");
            var file = tree[path];
            if ((total += new FileInfo(file).Length) > 1024L * 1024 * 1024) throw new InvalidDataException("Operator release exceeds the supported clone size.");
            if (!OperatingSystem.IsWindows()) capturedModes.Add(path, File.GetUnixFileMode(file) & (UnixFileMode)0x1FF);
            using var stream = File.OpenRead(file);
            owned.Add(path, OfflineBenchReadBudget.Read(stream, OfflineBenchArchive.MaximumFileBytes, cancellationToken));
        }
        executableModes = capturedModes;
        if (!OperatingSystem.IsWindows())
            foreach (var launcher in new[] { "HardwareTest", "HardwareTest.OpenTap.Worker" })
                if (capturedModes.TryGetValue(launcher, out var mode) && (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                    throw new InvalidDataException($"Operator apphost '{launcher}' is not executable. Use an intact release.");
        ValidateCapturedCapability(owned, rid);
        ValidateProtectedPayload(owned, bundle.Archives);
        ValidateMergedPayload(owned, capturedModes, bundle, cancellationToken);
        return owned;
    }

    internal static string RuntimeIdentifier(string os, string architecture) => (os == "Windows" ? "win" : "linux") + "-" + architecture;
    internal static bool IsCodeFile(string path)
        => new[] { ".dll", ".exe", ".so", ".dylib" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains(".so.", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path) is "HardwareTest" or "HardwareTest.OpenTap.Worker" or "tap";
    internal static bool IsProtectedFile(string path)
    {
        var file = Path.GetFileName(path);
        return file.StartsWith("HardwareTest", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("OpenTap", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("InstrumentComponents", StringComparison.OrdinalIgnoreCase)
            || file is "tap" or "tap.exe" or "tap.dll" or "tap.runtimeconfig.json" or "tap.deps.json";
    }

    private static void ValidateProtectedPayload(IReadOnlyDictionary<string, byte[]> runtime, IReadOnlyDictionary<string, OfflineBenchArchiveData> archives)
    {
        foreach (var archive in archives.Values)
        {
            if (OfflineBenchBundle.IsRuntimeProvidedPackage(archive.Name))
            {
                var expected = archive.Name.ToLowerInvariant() switch
                {
                    "opentap" => typeof(TestPlan).Assembly.GetName().Version!.ToString(3),
                    "hardwaretest basic" => typeof(HardwareTest.OpenTap.Plugins.Basic.IdentityCheckStep).Assembly.GetName().Version!.ToString(3),
                    "hardwaretest mixins" => typeof(HardwareTest.OpenTap.Plugins.Mixins.PresentationMixin).Assembly.GetName().Version!.ToString(3),
                    "instrumentcomponents.opentap" => PublishedInstrumentComponents.Version,
                    _ => StandaloneVisaPackage.Version,
                };
                var anchor = archive.Name.ToLowerInvariant() switch
                {
                    "opentap" => typeof(TestPlan).Assembly,
                    "hardwaretest basic" => typeof(HardwareTest.OpenTap.Plugins.Basic.IdentityCheckStep).Assembly,
                    "hardwaretest mixins" => typeof(HardwareTest.OpenTap.Plugins.Mixins.PresentationMixin).Assembly,
                    _ => null,
                };
                if (anchor is not null)
                {
                    var anchorName = Path.GetFileName(anchor.Location);
                    if (!runtime.TryGetValue(anchorName, out var existingAnchor)
                        || !existingAnchor.SequenceEqual(File.ReadAllBytes(anchor.Location)))
                        throw new InvalidDataException($"Runtime baseline {anchorName} is missing or differs from this operator release.");
                    var declaredAnchor = archive.Payload.Where(file => Path.GetFileName(file.Key).Equals(anchorName, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (declaredAnchor.Length != 1 || !declaredAnchor[0].Value.SequenceEqual(existingAnchor))
                        throw new InvalidDataException($"Package '{archive.Name}' does not carry its exact current operator baseline {anchorName}.");
                }
                if (archive.Version.Split('+')[0] != expected)
                    throw new InvalidDataException($"Runtime-provided package '{archive.Name}' is pinned to {expected}; archive {archive.Version} cannot upgrade or downgrade this operator.");
                if (archive.Name.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase)
                    || archive.Name.Equals(StandaloneVisaPackage.PackageName, StringComparison.OrdinalIgnoreCase))
                {
                    using var bundled = archive.Name.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase)
                        ? PublishedInstrumentComponents.OpenArchive() : StandaloneVisaPackage.OpenArchive();
                    using var memory = new MemoryStream(); bundled.CopyTo(memory);
                    var actual = OfflineBenchArchive.Read(memory.ToArray(), OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture, default);
                    var expectedPayload = actual.Payload.Where(item => !item.Key.EndsWith("package.xml", StringComparison.OrdinalIgnoreCase)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                    var suppliedPayload = archive.Payload.Where(item => !item.Key.EndsWith("package.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (suppliedPayload.Length != expectedPayload.Count
                        || !archive.Dependencies.Select(item => item.Name + "\0" + item.VersionRange).Order(StringComparer.Ordinal)
                            .SequenceEqual(actual.Dependencies.Select(item => item.Name + "\0" + item.VersionRange).Order(StringComparer.Ordinal)))
                        throw new InvalidDataException("Adapter archive omits exact release payload or dependencies.");
                    foreach (var item in suppliedPayload)
                        if (!expectedPayload.TryGetValue(item.Key, out var release) || !release.SequenceEqual(item.Value))
                            throw new InvalidDataException("Adapter payload differs from the operator's exact bundled release.");
                }
            }
            foreach (var file in archive.Payload)
            {
                if (file.Key.EndsWith("package.xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (!OfflineBenchBundle.IsRuntimeProvidedPackage(archive.Name) && IsProtectedFile(file.Key))
                    throw new InvalidDataException($"Extension '{archive.Name}' cannot supply protected operator/engine/adapter payload '{file.Key}'.");
                if (IsProtectedFile(file.Key))
                {
                    var existing = runtime.Where(item => Path.GetFileName(item.Key).Equals(Path.GetFileName(file.Key), StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (existing.Any(item => !item.Value.SequenceEqual(file.Value)))
                        throw new InvalidDataException($"Protected runtime payload '{file.Key}' differs from this operator release. Use the matching baseline; no existing payload will be replaced.");
                }
            }
        }
    }
}
