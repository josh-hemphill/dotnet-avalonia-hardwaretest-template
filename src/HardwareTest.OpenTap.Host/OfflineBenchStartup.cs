using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HardwareTest.Core.Settings;

namespace HardwareTest.OpenTap.Host;

/// Enforces an offline installation's data ownership before settings or logging can write.
public static class OfflineBenchStartup
{
    public const string InstallationFileName = "offline-bench-installation.json";
    public const string DataOwnershipFileName = ".offline-bench-data.json";
    public const string LockFileName = "offline-bench-lock.json";

    public static string[] PrepareArguments(string[] args, string appDirectory, IDictionary? environment = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        appDirectory = Path.GetFullPath(appDirectory);
        var marker = Path.Combine(appDirectory, InstallationFileName);
        if (!File.Exists(marker) && !Directory.Exists(marker) && new FileInfo(marker).LinkTarget is null) return args;
        try
        {
            OfflineBenchPaths.RejectLinks(appDirectory);
            var (installation, bytes) = ReadMarker(marker, OfflineBenchJsonContext.Default.OfflineBenchInstallation, 4 * 1024 * 1024);
            if (installation.SchemaVersion != 1 || installation.Capability != OfflineBenchBundle.InstallerCapability
                || installation.RequiredOperatorBuild != OfflineBenchBundle.CurrentOperatorBuild
                || !Guid.TryParseExact(installation.InstallId, "N", out _) || !Guid.TryParseExact(installation.ExportId, "N", out _)
                || !OfflineBenchPaths.IsHash(installation.LockSha256) || !SamePath(installation.AppDirectory, appDirectory))
                throw new InvalidDataException("The installation marker does not match this operator copy or release.");
            BenchProgramHandoff.RequireCustomPlanId(installation.PlanId);
            var lockPath = Path.Combine(appDirectory, LockFileName);
            OfflineBenchPaths.RejectLinks(lockPath);
            if (!File.Exists(lockPath) || new FileInfo(lockPath).Length > 4 * 1024 * 1024
                || !OfflineBenchPaths.Hash(lockPath).Equals(installation.LockSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installed bundle lock is missing or changed. Reinstall the matching bundle into fresh folders.");
            OfflineBenchPaths.RejectLinks(lockPath);
            var data = AbsolutePath(installation.FreshDataDirectory);
            if (SamePath(data, appDirectory) || OfflineBenchPaths.IsUnder(appDirectory, data) || OfflineBenchPaths.IsUnder(data, appDirectory))
                throw new InvalidDataException("The installation and its bound data folder must be separate.");
            OfflineBenchPaths.RejectLinks(data);
            if (!Directory.Exists(data)) throw new InvalidDataException("The bound fresh data folder is missing. Restore the complete installation and its owned data folder.");
            var (ownership, _) = ReadMarker(Path.Combine(data, DataOwnershipFileName), OfflineBenchJsonContext.Default.OfflineBenchDataOwnership);
            if (ownership.SchemaVersion != 1 || ownership.InstallId != installation.InstallId
                || !SamePath(ownership.AppDirectory, appDirectory)
                || !OfflineBenchPaths.IsHash(ownership.InstallationSha256)
                || !ownership.InstallationSha256.Equals(OfflineBenchPaths.HashBytes(bytes), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The data ownership marker does not belong to this exact installation. Existing station data cannot be reused.");
            OfflineBenchRuntime.ValidateCapability(appDirectory, OfflineBenchRuntime.RuntimeIdentifier(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture));
            ValidateRuntimeFiles(appDirectory, installation.RuntimeFiles);
            ValidateSavedPluginConfiguration(data);

            environment ??= Environment.GetEnvironmentVariables();
            ValidateEnvironment(environment, data);
            var result = new List<string>();
            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index]; var separator = token.IndexOfAny(['=', ':']);
                if (token == "--") { result.AddRange(args[index..]); break; }
                var option = separator >= 0 ? token[..separator] : token;
                if (option.Equals("--settings", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Offline installations use settings in their bound fresh data folder. Remove --settings.");
                if (option.Equals("--opentap-plugin-dirs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Offline installations use their verified local packages. Remove --opentap-plugin-dirs.");
                if (!option.Equals("--data-directory", StringComparison.OrdinalIgnoreCase)) { result.Add(token); continue; }
                var value = separator >= 0 ? token[(separator + 1)..] : ++index < args.Length ? args[index] : string.Empty;
                if (!SamePath(value, data))
                    throw new InvalidDataException("This offline operator copy can only use its bound fresh data folder. Remove --data-directory or supply that exact folder; existing station data cannot be reused.");
            }
            // Canonicalize even explicitly supplied matching paths and insert before the terminator.
            var terminator = result.IndexOf("--");
            result.InsertRange(terminator < 0 ? result.Count : terminator, ["--data-directory", data]);
            var prepared = result.ToArray();
            // Prove the exact production parser agrees before settings or logging can write.
            var parsed = ConfigurationArgs.Parse(prepared);
            if (parsed.SettingsPath is not null || parsed.Overlays.ContainsKey("OpenTapPluginDirectories")
                || !SamePath(ConfigurationBootstrap.ResolveStage1(parsed, environment).RootDirectory, data))
                throw new InvalidDataException("The effective launch configuration redirects this offline installation. Remove settings, plugin and alternate data overrides.");
            return prepared;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidDataException($"Offline installation could not start: {error.Message} Preserve the current folders and reinstall the matching bundle into NEW application and FRESH data folders if recovery is needed.", error);
        }
    }

    private static void ValidateEnvironment(IDictionary environment, string data)
    {
        foreach (DictionaryEntry entry in environment)
        {
            if (entry.Key is not string name || entry.Value is null) continue;
            var value = entry.Value.ToString() ?? string.Empty;
            if (name.Equals("HARDWARETEST_DATA_DIRECTORY", StringComparison.OrdinalIgnoreCase) && !SamePath(value, data))
                throw new InvalidDataException("HARDWARETEST_DATA_DIRECTORY must name this installation's bound fresh data folder. Remove the override or use that exact folder.");
            if ((name.Equals("HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES", StringComparison.OrdinalIgnoreCase)
                 || name.StartsWith("HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES__", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Remove external HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES overrides; this offline installation uses its verified local packages.");
        }
    }

    private static string AbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('\r') || path.Contains('\n'))
            throw new InvalidDataException("The installation requires an absolute, single-line owned folder path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool SamePath(string? first, string second) => string.Equals(AbsolutePath(first), AbsolutePath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void ValidateSavedPluginConfiguration(string data)
    {
        // Settings recovery can read the backup, so both possible sources must be checked.
        foreach (var name in new[] { "settings.json", "settings.json.bak" })
        {
            var path = Path.Combine(data, name);
            if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Offline settings cannot be symbolic links.");
            OfflineBenchPaths.RejectLinks(path);
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The bound settings document is too large to verify.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("The bound settings document changed to an unsupported size.");
            OfflineBenchPaths.RejectLinks(path);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The bound settings document must be an object.");
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name.Equals("openTapPluginDirectories", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind != JsonValueKind.Null
                    && (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0))
                    throw new InvalidDataException("Remove OpenTapPluginDirectories from the bound settings document and its backup. This offline installation can only use its verified local packages.");
        }
    }

    private static void ValidateRuntimeFiles(string appDirectory, List<OfflineBenchFile>? inventory)
    {
        if (inventory is not { Count: > 0 }) throw new InvalidDataException("The immutable runtime inventory is missing. Reinstall the matching bundle.");
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in inventory)
        {
            if (file is null || !OfflineBenchPaths.IsHash(file.Sha256) || file.Length < 0)
                throw new InvalidDataException("The immutable runtime inventory is incomplete.");
            var relative = OfflineBenchPaths.Relative(file.Path);
            if (relative.Equals(InstallationFileName, StringComparison.OrdinalIgnoreCase) || relative.Equals(LockFileName, StringComparison.OrdinalIgnoreCase)
                || !expected.TryAdd(relative, relative))
                throw new InvalidDataException("The immutable runtime inventory has duplicate or ownership-marker paths.");
            var path = OfflineBenchPaths.Under(appDirectory, relative);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Length
                || !OfflineBenchPaths.Hash(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Installed runtime or program file changed: {relative}. Existing qualification cannot be reused; reinstall into fresh folders.");
            OfflineBenchPaths.RejectLinks(path);
        }
        void Walk(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                OfflineBenchPaths.RejectLinks(path);
                if (Directory.Exists(path)) { Walk(path); continue; }
                var relative = Path.GetRelativePath(appDirectory, path).Replace('\\', '/');
                if (IsCodeFile(path) && (!expected.TryGetValue(relative, out var exact) || relative != exact))
                    throw new InvalidDataException($"Unverified code was added to this offline runtime: {relative}. Use the original installed runtime or reinstall into fresh folders.");
            }
        }
        Walk(appDirectory);
    }

    private static bool IsCodeFile(string path)
    {
        var name = Path.GetFileName(path);
        return new[] { ".dll", ".exe", ".so", ".dylib" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            || name.Contains(".so.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("HardwareTest", StringComparison.OrdinalIgnoreCase) || name.Equals("HardwareTest.OpenTap.Worker", StringComparison.OrdinalIgnoreCase)
            || name.Equals("tap", StringComparison.OrdinalIgnoreCase);
    }

    private static (T Value, byte[] Bytes) ReadMarker<T>(string path, JsonTypeInfo<T> type, int maximum = 64 * 1024) where T : class
    {
        OfflineBenchPaths.RejectLinks(path);
        if (!File.Exists(path)) throw new InvalidDataException($"Required ownership marker is missing: {Path.GetFileName(path)}.");
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("The ownership marker is too large.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > maximum) throw new InvalidDataException("The ownership marker changed to an unsupported size.");
        OfflineBenchPaths.RejectLinks(path);
        using (var document = JsonDocument.Parse(bytes))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The ownership marker must be an object.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new InvalidDataException("The ownership marker has duplicate or case-colliding properties.");
        }
        return (JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("The ownership marker is empty."), bytes);
    }
}
