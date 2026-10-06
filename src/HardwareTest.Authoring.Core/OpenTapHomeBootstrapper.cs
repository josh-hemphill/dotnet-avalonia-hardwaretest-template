using System.IO.Compression;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.Authoring;

/// Builds an isolated OpenTAP tree with Editor packs and explicitly declared instrument adapters.
public sealed class OpenTapHomeBootstrapper : IOpenTapHomeBootstrapper
{
    public const string DefaultHomeRelativePath = ".authoring/opentap";
    public const string InstrumentComponentsPackageName = "InstrumentComponents.OpenTap";
    public const string VisaPackageName = "HardwareTest VISA";
    public const string VisaAssemblyFileName = "HardwareTest.OpenTap.Plugins.Visa.dll";

    private static readonly XNamespace PackageNs = "http://opentap.io/schemas/package";

    private static readonly string[] OpenTapRuntimeFiles =
    [
        "OpenTap.dll",
        "OpenTap.Package.dll",
        "tap.dll",
        "tap.runtimeconfig.json",
    ];

    public OpenTapHome Bootstrap(AuthoringWorkspace workspace, BootstrapOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);

        var homeRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(options.HomeDirectory)
                ? Path.Combine(workspace.Root, DefaultHomeRelativePath)
                : options.HomeDirectory);
        var owned = Path.Combine(Path.GetTempPath(), "ht-bootstrap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var selected = AuthoringBuildService.CaptureTree(homeRoot, "operation-home", true);
            var identity = new AuthoringBuildRequest(workspace.Root, new PackOptions(), [selected], AuthoringBuildService.CaptureEnvironment());
            Directory.CreateDirectory(owned);
            foreach (var file in selected.Files)
            {
                var destination = Path.Combine(owned, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                AuthoringBuildService.Materialize(file, destination);
            }
            BootstrapOwned(workspace, new BootstrapOptions
            {
                HomeDirectory = owned,
                Offline = options.Offline,
                OfflinePackagePath = options.OfflinePackagePath,
                InstrumentComponentsPackagePath = options.InstrumentComponentsPackagePath,
                TuiPackagePath = options.TuiPackagePath
            }, identity.EnvironmentValues);
            AuthoringBuildService.Recheck(identity);
            AuthoringBuildService.Publish(owned, homeRoot, CancellationToken.None);
            return new OpenTapHome(homeRoot);
        }
        finally { if (Directory.Exists(owned)) Directory.Delete(owned, recursive: true); }
    }

    // The operation child already owns a cloned home and publishes through its coordinator.
    internal OpenTapHome BootstrapOwned(AuthoringWorkspace workspace, BootstrapOptions options,
        IReadOnlyDictionary<string, string?>? capturedEnvironment = null)
    {
        capturedEnvironment ??= AuthoringBuildService.CaptureEnvironment();
        var homeRoot = Path.GetFullPath(options.HomeDirectory!);
        Directory.CreateDirectory(homeRoot);
        Directory.CreateDirectory(Path.Combine(homeRoot, "Packages"));

        InstallOptionalFilePackage(options.OfflinePackagePath, homeRoot, workspace.Manifest);
        CopyOpenTapRuntime(homeRoot);
        InstallInTreePack(homeRoot, "HardwareTest Basic", typeof(MockDmmInstrument));
        InstallInTreePack(homeRoot, "HardwareTest Mixins", typeof(AnnotationMixinBuilder));
        var requiresVisa = workspace.Manifest.Dependencies.Any(d =>
            string.Equals(d.Package, VisaPackageName, StringComparison.OrdinalIgnoreCase));
        if (requiresVisa)
        {
            InstallInTreePack(homeRoot, VisaPackageName, AuthoringVisaInstrumentAdapter.InstrumentType);
        }

        if (string.IsNullOrWhiteSpace(options.OfflinePackagePath)) InstallInstrumentComponentsIfRequired(workspace, options, homeRoot, capturedEnvironment);
        InstallOptionalFilePackage(options.TuiPackagePath, homeRoot, workspace.Manifest);

        if (!requiresVisa)
        {
            AssertNoVisa(homeRoot);
        }
        var home = new OpenTapHome(homeRoot);
        var unsafePaths = AuthoringEnvironmentAssessment.UnsafeInstalledPaths(home);
        if (unsafePaths.Count != 0) throw new AuthoringWorkspaceException(string.Join("; ", unsafePaths));
        if (string.IsNullOrWhiteSpace(options.OfflinePackagePath))
        {
            var missing = AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Where(p => !p.Optional && !p.Satisfied).ToArray();
            if (missing.Length != 0) throw new AuthoringWorkspaceException(string.Join("; ", missing.Select(p => p.DisplayText)));
        }
        return home;
    }

    public static IReadOnlyList<AuthoringInstalledPackage> ListInstalledPackages(OpenTapHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        var packagesDir = Path.Combine(home.Root, "Packages");
        if (!Directory.Exists(packagesDir))
        {
            return [];
        }

        var found = new List<AuthoringInstalledPackage>();
        foreach (var child in Directory.EnumerateDirectories(packagesDir))
        {
            var xmlPath = Path.Combine(child, "package.xml");
            if (!File.Exists(xmlPath))
            {
                continue;
            }

            if (!TryReadPackageIdentity(xmlPath, out var name, out var version))
            {
                continue;
            }

            found.Add(new AuthoringInstalledPackage(name, version, Path.GetFullPath(child)));
        }

        return found
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveOpenTapRuntimeDirectory()
    {
        string[] candidates =
        [
            Path.GetDirectoryName(typeof(TestPlan).Assembly.Location) ?? string.Empty,
            Path.GetDirectoryName(typeof(OpenTapHomeBootstrapper).Assembly.Location) ?? string.Empty,
            AppContext.BaseDirectory,
        ];
        foreach (var candidate in candidates.Where(c => c.Length > 0).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(Path.Combine(candidate, "Packages", "OpenTAP")))
            {
                return candidate;
            }
        }

        return Path.GetDirectoryName(typeof(TestPlan).Assembly.Location)
               ?? throw new AuthoringWorkspaceException("OpenTAP runtime directory was not found beside OpenTap.dll.");
    }

    private static void CopyOpenTapRuntime(string homeRoot)
    {
        if (OpenTapRuntimeFiles.All(file => File.Exists(Path.Combine(homeRoot, file)))
            && File.Exists(Path.Combine(homeRoot, "Packages", "OpenTAP", "package.xml"))) return;
        var sourceDir = ResolveOpenTapRuntimeDirectory();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            throw new AuthoringWorkspaceException("OpenTAP runtime directory was not found beside OpenTap.dll.");
        }

        var installedMetadata = Path.Combine(homeRoot, "Packages", "OpenTAP", "package.xml");
        if (File.Exists(installedMetadata))
        {
            var bundledMetadata = Path.Combine(sourceDir, "Packages", "OpenTAP", "package.xml");
            if (!TryReadPackageIdentity(installedMetadata, out var installedName, out var installedVersion)
                || !TryReadPackageIdentity(bundledMetadata, out var bundledName, out var bundledVersion)
                || installedName != bundledName || installedVersion != bundledVersion)
                throw new AuthoringWorkspaceException("The selected incomplete OpenTAP runtime differs from the bundled version. Restore its matching runtime payload or import a complete matching package; preparation preserved the selected home.");
        }

        foreach (var file in OpenTapRuntimeFiles)
        {
            CopyIfExists(Path.Combine(sourceDir, file), Path.Combine(homeRoot, file));
        }

        var tapExe = Path.Combine(sourceDir, OperatingSystem.IsWindows() ? "tap.exe" : "tap");
        CopyIfExists(tapExe, Path.Combine(homeRoot, Path.GetFileName(tapExe)));

        var deps = Path.Combine(sourceDir, "Dependencies");
        if (Directory.Exists(deps))
        {
            CopyDirectory(deps, Path.Combine(homeRoot, "Dependencies"));
        }

        var openTapPack = Path.Combine(sourceDir, "Packages", "OpenTAP");
        if (Directory.Exists(openTapPack))
        {
            CopyDirectory(openTapPack, Path.Combine(homeRoot, "Packages", "OpenTAP"));
        }
    }

    private static void InstallInTreePack(string homeRoot, string packageName, Type marker)
    {
        var assemblyPath = marker.Assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            throw new AuthoringWorkspaceException($"Authoring pack assembly not found for '{packageName}'.");
        }

        var packageXml = FindPackageXml(packageName);
        var dest = Path.Combine(homeRoot, "Packages", packageName);
        if (File.Exists(Path.Combine(dest, "package.xml"))) return;
        Directory.CreateDirectory(dest);
        File.Copy(packageXml, Path.Combine(dest, "package.xml"), overwrite: true);
        foreach (var fileName in ReadPackageFileNames(packageXml))
        {
            var source = Path.Combine(Path.GetDirectoryName(assemblyPath)!, fileName);
            if (!File.Exists(source))
            {
                source = Path.Combine(Path.GetDirectoryName(packageXml)!, fileName);
            }

            if (!File.Exists(source))
            {
                throw new AuthoringWorkspaceException($"Pack file '{fileName}' for '{packageName}' was not found.");
            }

            File.Copy(source, Path.Combine(dest, fileName), overwrite: true);
        }
    }

    private static void InstallInstrumentComponentsIfRequired(
        AuthoringWorkspace workspace,
        BootstrapOptions options,
        string homeRoot,
        IReadOnlyDictionary<string, string?> capturedEnvironment)
    {
        var required = workspace.Manifest.Dependencies.Any(d =>
            string.Equals(d.Package, InstrumentComponentsPackageName, StringComparison.OrdinalIgnoreCase));
        if (!required)
        {
            return;
        }

        var requirement = AuthoringEnvironmentAssessment.Packages(workspace.Manifest, new(homeRoot))
            .First(package => package.Package.Equals(InstrumentComponentsPackageName, StringComparison.OrdinalIgnoreCase));
        if (requirement.Satisfied && AuthoringInstrumentCatalog.LibraryPayloadAvailability(new(homeRoot)).Available) return;

        var path = FirstNonEmpty(
            options.InstrumentComponentsPackagePath,
            workspace.Manifest.InstrumentComponentsPackage,
            capturedEnvironment.GetValueOrDefault("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE"));
        if (string.IsNullOrWhiteSpace(path))
        {
            var archive = Path.Combine(Path.GetTempPath(), "ht-bundled-" + Guid.NewGuid().ToString("N") + ".TapPackage");
            try
            {
                PublishedInstrumentComponents.MaterializeArchive(archive);
                InstallFileOrDirectoryPackage(archive, homeRoot, workspace.Manifest);
                var provenance = Path.Combine(homeRoot, "Packages", InstrumentComponentsPackageName, "hardwaretest-provenance.json");
                using var output = File.Create(provenance);
                using var writer = new System.Text.Json.Utf8JsonWriter(output);
                writer.WriteStartObject();
                writer.WriteString("package", PublishedInstrumentComponents.PackageName);
                writer.WriteString("version", PublishedInstrumentComponents.Version);
                writer.WriteString("origin", PublishedInstrumentComponents.Origin);
                writer.WriteString("archiveSha256", PublishedInstrumentComponents.Sha256);
                writer.WriteEndObject();
            }
            finally { if (File.Exists(archive)) File.Delete(archive); }
            return;
        }

        path = ResolveAgainstWorkspace(workspace.Root, path);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringBootstrapCodes.InstrumentComponentsPackageMissing}: InstrumentComponents package not found at '{path}'.");
        }

        InstallFileOrDirectoryPackage(path, homeRoot, workspace.Manifest);
    }

    private static void InstallOptionalFilePackage(string? path, string homeRoot, AuthoringManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new AuthoringWorkspaceException($"Optional OpenTAP package not found at '{path}'.");
        }

        InstallFileOrDirectoryPackage(path, homeRoot, manifest);
    }

    private static void InstallFileOrDirectoryPackage(string path, string homeRoot, AuthoringManifest manifest)
        => AuthoringPackageImport.Install(path, homeRoot, manifest, CopyDirectory);

    private static void AssertNoVisa(string homeRoot)
    {
        var visa = Directory.EnumerateFiles(homeRoot, VisaAssemblyFileName, SearchOption.AllDirectories).FirstOrDefault();
        if (visa is not null)
        {
            throw new AuthoringWorkspaceException(
                $"Authoring OpenTAP home must not contain the VISA adapter ({visa}).");
        }
    }

    private static string FindPackageXml(string packageName)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "AuthoringPackages", packageName, "package.xml");
        return File.Exists(bundled) ? bundled : FindRepoPackageXml(packageName);
    }

    private static string FindRepoPackageXml(string packageName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("dirs.proj").Any())
            {
                string[] candidates =
                [
                    Path.Combine(dir.FullName, "src", "HardwareTest.OpenTap.Plugins.Basic", "package.xml"),
                    Path.Combine(dir.FullName, "src", "HardwareTest.OpenTap.Plugins.Mixins", "package.xml"),
                    Path.Combine(dir.FullName, "src", "HardwareTest.OpenTap.Plugins.Visa", "package.xml"),
                ];
                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate)
                        && TryReadPackageIdentity(candidate, out var name, out _)
                        && string.Equals(name, packageName, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }

                break;
            }

            dir = dir.Parent;
        }

        throw new AuthoringWorkspaceException($"Could not locate package.xml for '{packageName}'.");
    }

    private static IReadOnlyList<string> ReadPackageFileNames(string packageXml)
    {
        var doc = XDocument.Load(packageXml);
        return doc.Descendants(PackageNs + "File")
            .Concat(doc.Descendants("File"))
            .Select(e => (string?)e.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFileName(p)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadPackageIdentity(string packageXml, out string name, out string version)
    {
        name = string.Empty;
        version = string.Empty;
        try
        {
            var root = XDocument.Load(packageXml).Root;
            if (root is null || !string.Equals(root.Name.LocalName, "Package", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            name = ((string?)root.Attribute("Name"))?.Trim() ?? string.Empty;
            version = ((string?)root.Attribute("Version"))?.Trim() ?? string.Empty;
            return name.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (string.Equals(Path.GetFileName(file), VisaAssemblyFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new AuthoringWorkspaceException(
                    $"Authoring OpenTAP home must not copy the VISA adapter ({file}).");
            }

            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var child in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(child, Path.Combine(dest, Path.GetFileName(child)));
        }
    }

    private static void CopyIfExists(string source, string dest)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }

    private static string ResolveAgainstWorkspace(string workspaceRoot, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(workspaceRoot, path));
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
