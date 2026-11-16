namespace HardwareTest.OpenTap.Host;

public static partial class OfflineBenchRuntime
{
    /// Inspects the complete bundle against this operator release without writes, loading plugins or executing package actions.
    public static OfflineBenchBundleManifest CheckBundle(string root, string operatorDirectory, string? targetOS = null,
        string? targetArchitecture = null, CancellationToken cancellationToken = default)
    {
        var snapshot = OfflineBenchBundle.Capture(root, targetOS ?? OfflineBenchBundle.CurrentOS,
            targetArchitecture ?? OfflineBenchBundle.CurrentArchitecture, cancellationToken);
        Capture(operatorDirectory, snapshot, cancellationToken);
        return snapshot.Manifest;
    }

    internal static UnixFileMode ArchiveMode(OfflineBenchArchiveData archive, string path)
        => archive.ExecutableModes.TryGetValue(path, out var mode) ? mode & (UnixFileMode)0x1FF : (UnixFileMode)0x1A4;

    private static void ValidateMergedPayload(IReadOnlyDictionary<string, byte[]> runtime, IReadOnlyDictionary<string, UnixFileMode> runtimeModes, OfflineBenchSnapshot bundle, CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, (string Path, byte[] Bytes, UnixFileMode Mode)>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, byte[] bytes, UnixFileMode? mode = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executableMode = OfflineBenchBundle.EffectiveExecutableMode(path, mode ?? 0, bundle.Manifest.TargetOS);
            if (merged.TryGetValue(path, out var existing))
            {
                if (path != existing.Path || !existing.Bytes.SequenceEqual(bytes) || executableMode != existing.Mode)
                    throw new InvalidDataException($"Offline payload conflicts with an existing release/package file, permissions or casing: {path}");
                return;
            }
            if (directories.ContainsKey(path))
                throw new InvalidDataException($"Offline payload has conflicting file and directory paths: {path}");
            for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            {
                var directory = path[..slash];
                if (merged.ContainsKey(directory) || directories.TryGetValue(directory, out var previous) && directory != previous)
                    throw new InvalidDataException($"Offline payload has conflicting file/directory paths or casing: {path}");
                directories.TryAdd(directory, directory);
            }
            merged.Add(path, (path, bytes, executableMode));
        }
        var declaredTapExecutable = bundle.Archives.Values.Any(archive => archive.SetsTapExecutable);
        foreach (var file in runtime)
        {
            UnixFileMode? mode = runtimeModes.TryGetValue(file.Key, out var original) ? original : null;
            if (file.Key == "tap" && declaredTapExecutable && mode is not null)
                mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            Add(file.Key, file.Value, mode);
        }
        foreach (var archive in bundle.Archives.Values)
            foreach (var file in archive.Payload) Add(file.Key, file.Value, OperatingSystem.IsWindows() ? null : ArchiveMode(archive, file.Key));
        var program = OfflineBenchBundle.ProgramDirectory(bundle.Manifest.PlanId) + "/";
        foreach (var name in new[] { bundle.Manifest.PlanId + ".TapPlan", bundle.Manifest.PlanId + ".program.json", BenchProgramHandoff.ReceiptFileName })
            Add(program + name, bundle.Files[program + name], OperatingSystem.IsWindows() ? null : (UnixFileMode)0x1A4);
        foreach (var reserved in new[] { OfflineBenchStartup.InstallationFileName, OfflineBenchStartup.LockFileName, OfflineBenchStartup.DataOwnershipFileName })
            Add(reserved, []);
    }
}
