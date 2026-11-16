using System.Text.Json;

namespace HardwareTest.OpenTap.Host;

/// Publishes a new immutable-code installation and a separately owned fresh data directory.
/// No package actions, network, existing station data or application launch are involved.
internal enum OfflineBenchPublicationStage { BeforeCommit, AfterDataPublication }

public static class OfflineBenchInstaller
{
    public static OfflineBenchInstallResult Install(string root, string sourceOperatorDirectory, string destination,
        string freshDataDirectory, CancellationToken cancellationToken = default)
        => Install(root, sourceOperatorDirectory, destination, freshDataDirectory, cancellationToken, null);

    internal static OfflineBenchInstallResult Install(string root, string sourceOperatorDirectory, string destination,
        string freshDataDirectory, CancellationToken cancellationToken, Action<OfflineBenchPublicationStage>? publicationObserver)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(destination) || !Path.IsPathFullyQualified(freshDataDirectory))
            throw new InvalidDataException("Choose absolute fresh application and data directories.");
        root = Path.GetFullPath(root); sourceOperatorDirectory = Path.GetFullPath(sourceOperatorDirectory);
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        freshDataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(freshDataDirectory));
        foreach (var path in new[] { root, sourceOperatorDirectory, destination, freshDataDirectory }) OfflineBenchPaths.RejectLinks(path);
        if (Exists(destination) || Exists(freshDataDirectory))
            throw new InvalidDataException("Offline installation requires two new directories. Existing applications or data are never overwritten or reused.");
        if (Overlap(destination, freshDataDirectory) || Overlap(destination, root) || Overlap(freshDataDirectory, root)
            || Overlap(destination, sourceOperatorDirectory) || Overlap(freshDataDirectory, sourceOperatorDirectory))
            throw new InvalidDataException("Fresh application/data destinations cannot overlap each other, the bundle or the running operator runtime.");
        var snapshot = OfflineBenchBundle.Capture(root, OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture, cancellationToken);
        var runtime = OfflineBenchRuntime.Capture(sourceOperatorDirectory, snapshot, cancellationToken, out var runtimeModes);
        var id = Guid.NewGuid().ToString("N");
        var appParent = Path.GetDirectoryName(destination)!;
        var dataParent = Path.GetDirectoryName(freshDataDirectory)!;
        var appStage = Path.Combine(appParent, ".offline-app-" + id);
        var dataStage = Path.Combine(dataParent, ".offline-data-" + id);
        var published = false;
        var reservedData = false;
        var installation = new OfflineBenchInstallation
        {
            InstallId = id,
            AppDirectory = destination,
            FreshDataDirectory = freshDataDirectory,
            LockSha256 = snapshot.LockSha256,
            RequiredOperatorBuild = snapshot.Manifest.RequiredOperatorBuild,
            PlanId = snapshot.Manifest.PlanId,
            ExportId = snapshot.Manifest.ExportId,
        };
        try
        {
            Directory.CreateDirectory(appParent); Directory.CreateDirectory(dataParent);
            OfflineBenchPaths.RejectLinks(appParent); OfflineBenchPaths.RejectLinks(dataParent);
            if (Exists(appStage) || Exists(dataStage)) throw new IOException("Offline staging identity already exists.");
            Directory.CreateDirectory(appStage); Directory.CreateDirectory(dataStage);
            var installationPath = OfflineBenchPaths.Under(appStage, OfflineBenchStartup.InstallationFileName);
            File.WriteAllBytes(installationPath, JsonSerializer.SerializeToUtf8Bytes(installation, OfflineBenchJsonContext.Default.OfflineBenchInstallation));
            var installed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            void Put(string path, byte[] bytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = OfflineBenchPaths.Under(appStage, path);
                if (installed.TryGetValue(path, out var existing))
                {
                    if (!existing.SequenceEqual(bytes)) throw new InvalidDataException($"Offline payload conflicts with an existing release/package file: {path}");
                    return; // Identical declared bytes do not overwrite existing payload.
                }
                if (File.Exists(target)) throw new InvalidDataException("Offline package attempts to replace installation ownership.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes);
                installed.Add(path, bytes);
            }
            foreach (var file in runtime.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                Put(file.Key, file.Value);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(OfflineBenchPaths.Under(appStage, file.Key), runtimeModes[file.Key]);
            }
            foreach (var archive in snapshot.Archives.Values.OrderBy(archive => archive.Name, StringComparer.Ordinal))
                foreach (var file in archive.Payload.OrderBy(file => file.Key, StringComparer.Ordinal))
                {
                    var alreadyPresent = installed.ContainsKey(file.Key);
                    Put(file.Key, file.Value);
                    if (!OperatingSystem.IsWindows())
                    {
                        if (!alreadyPresent) File.SetUnixFileMode(OfflineBenchPaths.Under(appStage, file.Key), OfflineBenchRuntime.ArchiveMode(archive, file.Key));
                        else if (archive.SetsTapExecutable && file.Key == "tap")
                        {
                            var current = File.GetUnixFileMode(OfflineBenchPaths.Under(appStage, file.Key));
                            File.SetUnixFileMode(OfflineBenchPaths.Under(appStage, file.Key), current | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                        }
                    }
                }
            var programDirectory = OfflineBenchBundle.ProgramDirectory(snapshot.Manifest.PlanId) + "/";
            foreach (var name in new[] { snapshot.Manifest.PlanId + ".TapPlan", snapshot.Manifest.PlanId + ".program.json", BenchProgramHandoff.ReceiptFileName })
                Put(programDirectory + name, snapshot.Files[programDirectory + name]);
            installation = installation with
            {
                RuntimeFiles = installed.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new OfflineBenchFile
                { Path = file.Key, Length = file.Value.LongLength, Sha256 = OfflineBenchPaths.HashBytes(file.Value) }).ToList(),
            };
            File.WriteAllBytes(OfflineBenchPaths.Under(appStage, OfflineBenchStartup.LockFileName), snapshot.ManifestBytes);
            var installationBytes = JsonSerializer.SerializeToUtf8Bytes(installation, OfflineBenchJsonContext.Default.OfflineBenchInstallation);
            File.WriteAllBytes(installationPath, installationBytes);
            var ownership = new OfflineBenchDataOwnership
            { InstallId = id, AppDirectory = destination, InstallationSha256 = OfflineBenchPaths.HashBytes(installationBytes) };
            File.WriteAllBytes(OfflineBenchPaths.Under(dataStage, OfflineBenchStartup.DataOwnershipFileName),
                JsonSerializer.SerializeToUtf8Bytes(ownership, OfflineBenchJsonContext.Default.OfflineBenchDataOwnership));
            publicationObserver?.Invoke(OfflineBenchPublicationStage.BeforeCommit);
            RecheckInputs(root, sourceOperatorDirectory, snapshot, runtime, runtimeModes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            OfflineBenchPaths.RejectLinks(destination); OfflineBenchPaths.RejectLinks(freshDataDirectory);
            Directory.Move(dataStage, freshDataDirectory);
            reservedData = true;
            // Publication is deliberately noncancellable once the first owned directory moves.
            publicationObserver?.Invoke(OfflineBenchPublicationStage.AfterDataPublication);
            Directory.Move(appStage, destination);
            published = true;
            var launcher = Path.Combine(destination, OperatingSystem.IsWindows() ? "HardwareTest.exe" : "HardwareTest");
            var command = File.Exists(launcher) ? Quote(launcher) : "dotnet " + Quote(Path.Combine(destination, "HardwareTest.dll"));
            return new(destination, snapshot.Manifest,
                $"Prepared new operator: {destination}\nFresh bound data: {freshDataDirectory}\nInstallation: {id}\nThe existing station was not changed. Stop the old operator before activating this new copy.\nLaunch: {command} --data-directory {Quote(freshDataDirectory)}\nNothing was launched; golden/reference and health qualification must be established in this fresh data context.");
        }
        finally
        {
            if (!published)
            {
                DeleteOwnedAppStage(appStage, id);
                DeleteOwnedData(dataStage, id);
                if (Directory.Exists(dataStage) && !Directory.EnumerateFileSystemEntries(dataStage).Any()) Directory.Delete(dataStage);
                if (reservedData) DeleteOwnedData(freshDataDirectory, id);
            }
        }
    }

    private static void RecheckInputs(string bundleDirectory, string runtimeDirectory, OfflineBenchSnapshot snapshot,
        IReadOnlyDictionary<string, byte[]> runtime, IReadOnlyDictionary<string, UnixFileMode> runtimeModes, CancellationToken cancellationToken)
    {
        var tree = OfflineBenchPaths.Tree(bundleDirectory);
        if (tree.Count != snapshot.Files.Count + 1
            || !OfflineBenchPaths.Hash(OfflineBenchPaths.Under(bundleDirectory, OfflineBenchBundle.ManifestFileName)).Equals(snapshot.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Offline bundle changed before publication. Check the original complete bundle again.");
        foreach (var (directory, files) in new[] { (bundleDirectory, snapshot.Files), (runtimeDirectory, runtime) })
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = OfflineBenchPaths.Under(directory, file.Key);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Value.LongLength
                    || OfflineBenchPaths.Hash(path) != OfflineBenchPaths.HashBytes(file.Value))
                    throw new InvalidDataException("Offline bundle or source operator changed before publication. Nothing was activated.");
                if (!OperatingSystem.IsWindows() && directory == runtimeDirectory
                    && (File.GetUnixFileMode(path) & (UnixFileMode)0x1FF) != runtimeModes[file.Key])
                    throw new InvalidDataException("Source operator permissions changed before publication. Nothing was activated.");
            }
    }

    private static void DeleteOwnedAppStage(string directory, string id)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            OfflineBenchPaths.RejectLinks(directory);
            var path = Path.Combine(directory, OfflineBenchStartup.InstallationFileName);
            var marker = JsonSerializer.Deserialize(File.ReadAllBytes(path), OfflineBenchJsonContext.Default.OfflineBenchInstallation);
            if (marker?.InstallId == id) Directory.Delete(directory, recursive: true);
        }
        catch (Exception) { /* Never turn failed cleanup into deletion of unowned data. */ }
    }
    private static void DeleteOwnedData(string directory, string id)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            OfflineBenchPaths.RejectLinks(directory);
            if (Directory.EnumerateDirectories(directory).Any()) return;
            var tree = OfflineBenchPaths.Tree(directory);
            if (tree.Count != 1 || !tree.TryGetValue(OfflineBenchStartup.DataOwnershipFileName, out var path)) return;
            var marker = JsonSerializer.Deserialize(File.ReadAllBytes(path), OfflineBenchJsonContext.Default.OfflineBenchDataOwnership);
            if (marker?.InstallId == id) Directory.Delete(directory, recursive: true);
        }
        catch (Exception) { /* Preserve anything that is not still the empty owned reservation. */ }
    }
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
    private static bool Overlap(string left, string right)
        => string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || OfflineBenchPaths.IsUnder(left, right) || OfflineBenchPaths.IsUnder(right, left);
    private static string Quote(string value) => OperatingSystem.IsWindows() ? "\"" + value.Replace("\"", "\\\"") + "\"" : "'" + value.Replace("'", "'\\''") + "'";
}
