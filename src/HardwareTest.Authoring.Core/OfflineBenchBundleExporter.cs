using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed record OfflineBenchExportOptions(string TargetOS, string TargetArchitecture,
    string? PackageDirectory = null, string? OpenTapHomeDirectory = null, bool DownloadMissing = false,
    string RepositoryUrl = "https://packages.opentap.io")
{
    public Action<string>? Progress { get; init; }
}

public sealed record OfflineBenchExportResult(string Directory, OfflineBenchBundleManifest Manifest, string Summary);

/// Publishes a checked program with a pinned, target-compatible dependency closure.
public static class OfflineBenchBundleExporter
{
    public static OfflineBenchExportResult Export(AuthoringWorkspace workspace, string planId, string destinationRoot,
        OfflineBenchExportOptions options, Func<bool>? ownerIsCurrent = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        BenchProgramHandoff.RequireCustomPlanId(planId);
        if ((options.TargetOS, options.TargetArchitecture) is not (("Windows", "x64") or ("Linux", "x64") or ("Linux", "arm64")))
            throw new AuthoringWorkspaceException("Offline bundles currently support Windows x64, Linux x64 and Linux arm64. Choose a matching supported operator release and target.");
        if (options.DownloadMissing) AuthoringPackageDownloads.RepositoryUri(options.RepositoryUrl);
        cancellationToken.ThrowIfCancellationRequested();
        options.Progress?.Invoke("Checking the saved workspace and selected bench target…");
        var saved = AuthoringWorkspaceLoader.Load(workspace.Root);
        if (workspace.IsReadOnly || saved.IsReadOnly) throw new AuthoringWorkspaceException("A read-only workspace cannot prepare an offline bench bundle.");
        var root = Path.GetFullPath(destinationRoot);
        OfflineBenchPackageInventory.RequireRegular(root);
        Directory.CreateDirectory(root);
        var name = planId + "-offline-" + options.TargetOS.ToLowerInvariant() + "-" + options.TargetArchitecture;
        var destination = Path.Combine(root, name);
        OfflineBenchPackageInventory.RequireRegular(destination);
        var lockPath = Path.Combine(root, ".offline-export-" + name + ".lock");
        OfflineBenchPackageInventory.RequireRegular(lockPath);
        using var publicationLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        options.Progress?.Invoke("Checking the output folder and any previous owned bundle…");
        var prior = Directory.Exists(destination) ? ValidatePrior(destination, planId, options) : null;
        var exportId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, ".offline-stage-" + exportId);
        var backup = Path.Combine(root, ".offline-backup-" + exportId);
        var inputs = Path.Combine(root, ".offline-inputs-" + exportId);
        Directory.CreateDirectory(staging); Directory.CreateDirectory(inputs);
        var published = false;
        try
        {
            var inventory = new OfflineBenchPackageInventory(inputs, options, cancellationToken);
            var manifestBytes = inventory.Capture(Path.Combine(saved.Root, AuthoringWorkspaceLoader.ManifestFileName));
            var capturedManifest = JsonSerializer.Deserialize(manifestBytes, AuthoringJsonContext.Default.AuthoringManifest)
                ?? throw new AuthoringWorkspaceException("The saved workspace manifest is empty.");
            // Load and snapshot must describe the same saved manifest, even when
            // another process changes it during this operation's first reads.
            saved = AuthoringWorkspaceLoader.Load(workspace.Root);
            inventory.Capture(Path.Combine(saved.Root, AuthoringWorkspaceLoader.ManifestFileName));
            if (JsonSerializer.Serialize(capturedManifest, AuthoringJsonContext.Default.AuthoringManifest)
                != JsonSerializer.Serialize(saved.Manifest, AuthoringJsonContext.Default.AuthoringManifest))
                throw new AuthoringWorkspaceException("The saved workspace manifest changed during preparation; retry after saving.");
            saved = saved with { Manifest = capturedManifest };
            var plan = saved.TapPlanPaths.SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == planId)
                ?? throw new AuthoringWorkspaceException("Save and compile the selected program before preparing an offline bundle.");
            inventory.Capture(plan); inventory.Capture(PlanCompiler.SidecarPath(plan));
            var source = new AuthoringDocumentStore(saved.Root).GetDocumentPath(planId);
            if (File.Exists(source)) inventory.Capture(source);
            void RequireCurrent()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ownerIsCurrent is not null && !ownerIsCurrent()) throw new AuthoringWorkspaceException("The selected authoring context changed; the prior bundle was preserved. Reopen preparation for the intended program.");
                inventory.Recheck();
            }
            RequireCurrent();
            options.Progress?.Invoke("Preparing the checked program and operator settings…");
            var checkedProgram = BenchProgramExporter.Export(saved, planId, Path.Combine(staging, "Programs"), ownerIsCurrent, cancellationToken);
            var compiledRequirements = XDocument.Load(checkedProgram.PlanPath).Descendants()
                .Where(element => element.Name.LocalName == "Package" && element.Parent?.Name.LocalName == "Package.Dependencies")
                .Select(element => new AuthoringPackageDependency { Package = (string?)element.Attribute("Name") ?? "", Version = (string?)element.Attribute("Version") ?? "" });
            var productionRequirements = saved.Manifest.Dependencies.Concat(compiledRequirements)
                .Select(requirement => new AuthoringPackageDependency { Package = requirement.Package, Version = requirement.Version }).ToList();
            var resolverManifest = new AuthoringManifest { Dependencies = productionRequirements, OptionalDependencies = saved.Manifest.OptionalDependencies };
            // The inner checked-pair publisher's coordination file is not a bundle artifact.
            File.Delete(Path.Combine(staging, "Programs", ".bench-export-" + planId + ".lock"));
            options.Progress?.Invoke("Collecting compatible package archives without changing the authoring home…");
            if (!string.IsNullOrWhiteSpace(options.PackageDirectory)) inventory.AddFolder(options.PackageDirectory, 0);
            foreach (var plugin in saved.Manifest.PluginProjects)
            {
                var path = Path.IsPathRooted(plugin) ? plugin : Path.Combine(saved.Root, plugin);
                if (File.Exists(path)) inventory.AddArchive(path, 0);
                else throw new AuthoringWorkspaceException("Declared plugin archive is missing: " + path + ". Restore it or choose a matching manual package folder.");
            }
            inventory.AddInstalledHome(options.OpenTapHomeDirectory ?? Path.Combine(saved.Root, ".authoring", "opentap"));
            var releases = Path.Combine(AppContext.BaseDirectory, "PublishedArtifacts");
            if (Directory.Exists(releases)) inventory.AddFolder(releases, 2);
            var packages = new OfflineBenchPackageResolver(inventory, inputs, options, cancellationToken).Resolve(resolverManifest);
            var manifest = new OfflineBenchBundleManifest
            {
                PlanId = planId,
                ExportId = checkedProgram.Receipt.ExportId,
                TargetOS = options.TargetOS,
                TargetArchitecture = options.TargetArchitecture,
                CreatedAt = DateTimeOffset.UtcNow,
                RequiredOperatorBuild = OfflineBenchBundle.CurrentOperatorBuild,
                InstallerCapability = "offline-bench-v1",
                Requirements = productionRequirements.Select(requirement => new OfflineBenchRequirement { Name = requirement.Package, VersionRange = requirement.Version }).ToList(),
            };
            Directory.CreateDirectory(Path.Combine(staging, "packages"));
            for (var index = 0; index < packages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = packages[index];
                options.Progress?.Invoke($"Copying pinned package {package.Name} {package.Version} ({index + 1} of {packages.Count})…");
                var relative = $"packages/{index + 1:D3}.TapPackage";
                var target = Path.Combine(staging, relative);
                File.Copy(package.Archive, target);
                manifest.Packages.Add(new()
                {
                    Name = package.Name,
                    Version = package.Version.ToString(),
                    OS = package.OS,
                    Architecture = package.Architecture,
                    Path = relative,
                    Sha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(target)),
                    Dependencies = [.. package.Dependencies],
                });
            }
            options.Progress?.Invoke("Writing offline check/install helpers and the transport manifest…");
            OfflineBenchBundleHelpers.Write(staging, options);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                manifest.Files.Add(new() { Path = Path.GetRelativePath(staging, file).Replace('\\', '/'), Length = new FileInfo(file).Length, Sha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(file)) });
            File.WriteAllText(Path.Combine(staging, OfflineBenchBundle.ManifestFileName), JsonSerializer.Serialize(manifest, OfflineBenchJsonContext.Default.OfflineBenchBundleManifest));
            options.Progress?.Invoke("Checking the complete bundle, dependency closure and file integrity…");
            OfflineBenchBundle.Validate(staging, options.TargetOS, options.TargetArchitecture, cancellationToken);
            options.Progress?.Invoke("Rechecking captured inputs and publishing the complete offline bundle…");
            RequireCurrent();
            if (Directory.Exists(destination))
            {
                if (prior is null || TreeFingerprint(destination) != prior) throw new AuthoringWorkspaceException("The prior bundle changed during preparation; it was preserved. Choose a new destination or restore its verified bytes.");
                ValidatePrior(destination, planId, options);
                Directory.Move(destination, backup);
            }
            try { Directory.Move(staging, destination); }
            catch (Exception error)
            {
                if (Directory.Exists(backup))
                {
                    try { Directory.Move(backup, destination); }
                    catch (Exception restore) { throw new AuthoringWorkspaceException($"Offline publication failed; the prior bundle remains at '{backup}'. Restore it before retrying. {error.Message} {restore.Message}"); }
                }
                throw;
            }
            published = true;
            return new(destination, manifest, $"Prepared {manifest.Packages.Count} pinned packages for {options.TargetOS}/{options.TargetArchitecture}. Copy this folder to the bench, then check it with the matching operator release before installing into a fresh app folder.");
        }
        finally
        {
            DeleteOwned(staging); DeleteOwned(inputs);
            if (published) DeleteOwned(backup);
        }
    }

    private static string ValidatePrior(string directory, string planId, OfflineBenchExportOptions options)
    {
        var manifest = OfflineBenchBundle.Validate(directory, options.TargetOS, options.TargetArchitecture);
        if (manifest.PlanId != planId) throw new AuthoringWorkspaceException("The destination belongs to another program; choose another output folder.");
        return TreeFingerprint(directory);
    }

    private static string TreeFingerprint(string root)
    {
        OfflineBenchPackageInventory.RequireRegular(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var expectedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        foreach (var file in files)
        {
            OfflineBenchPackageInventory.RequireRegular(file);
            values.Add(Path.GetRelativePath(root, file).Replace('\\', '/') + ":" + BenchProgramHandoff.Hash(File.ReadAllBytes(file)));
            for (var parent = Path.GetDirectoryName(file); parent is not null && parent != root; parent = Path.GetDirectoryName(parent)) expectedDirectories.Add(parent);
        }
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            OfflineBenchPackageInventory.RequireRegular(directory);
            if (!expectedDirectories.Contains(directory)) throw new AuthoringWorkspaceException("The destination contains unrelated directories; existing files were preserved. Choose another output folder.");
        }
        return BenchProgramHandoff.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', values)));
    }

    private static void DeleteOwned(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { /* Keep recoverable owned data if cleanup is unavailable. */ }
        catch (UnauthorizedAccessException) { /* Successful publication remains successful. */ }
    }
}
