using System.IO.Compression;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.Authoring;

/// Builds an isolated OpenTAP tree with Editor packs (Basic, Mixins, optional IC/TUI). Never the Visa adapter.
public sealed class OpenTapHomeBootstrapper : IOpenTapHomeBootstrapper
{
    public const string DefaultHomeRelativePath = ".authoring/opentap";
    public const string InstrumentComponentsPackageName = "InstrumentComponents.OpenTap";
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
        Directory.CreateDirectory(homeRoot);
        Directory.CreateDirectory(Path.Combine(homeRoot, "Packages"));

        CopyOpenTapRuntime(homeRoot);
        InstallInTreePack(homeRoot, "HardwareTest Basic", typeof(MockDmmInstrument));
        InstallInTreePack(homeRoot, "HardwareTest Mixins", typeof(AnnotationMixinBuilder));
        InstallInstrumentComponentsIfRequired(workspace, options, homeRoot);
        InstallOptionalFilePackage(options.TuiPackagePath, homeRoot);

        AssertNoVisa(homeRoot);
        return new OpenTapHome(homeRoot);
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
        var sourceDir = ResolveOpenTapRuntimeDirectory();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            throw new AuthoringWorkspaceException("OpenTAP runtime directory was not found beside OpenTap.dll.");
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

        var packageXml = FindRepoPackageXml(packageName);
        var dest = Path.Combine(homeRoot, "Packages", packageName);
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
        string homeRoot)
    {
        var required = workspace.Manifest.Dependencies.Any(d =>
            string.Equals(d.Package, InstrumentComponentsPackageName, StringComparison.OrdinalIgnoreCase));
        if (!required)
        {
            return;
        }

        var path = FirstNonEmpty(
            options.InstrumentComponentsPackagePath,
            workspace.Manifest.InstrumentComponentsPackage,
            Environment.GetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE"));
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringBootstrapCodes.InstrumentComponentsPackageMissing}: InstrumentComponents.OpenTap is declared but no package path was provided (set BootstrapOptions.InstrumentComponentsPackagePath, authoring.json instrumentComponentsPackage, or HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE).");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringBootstrapCodes.InstrumentComponentsPackageMissing}: InstrumentComponents package not found at '{path}'.");
        }

        InstallFileOrDirectoryPackage(path, homeRoot);
    }

    private static void InstallOptionalFilePackage(string? path, string homeRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new AuthoringWorkspaceException($"Optional OpenTAP package not found at '{path}'.");
        }

        InstallFileOrDirectoryPackage(path, homeRoot);
    }

    private static void InstallFileOrDirectoryPackage(string path, string homeRoot)
    {
        if (Directory.Exists(path))
        {
            var xml = Path.Combine(path, "package.xml");
            if (!File.Exists(xml) || !TryReadPackageIdentity(xml, out var dirName, out _))
            {
                throw new AuthoringWorkspaceException($"Directory '{path}' is not an unpacked OpenTAP package.");
            }

            CopyDirectory(path, Path.Combine(homeRoot, "Packages", dirName));
            return;
        }

        if (!path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthoringWorkspaceException($"Unsupported package file '{path}'. Use a .TapPackage or an unpacked folder.");
        }

        var temp = Path.Combine(Path.GetTempPath(), "ht-tap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(path, temp);
            var xml = Directory.EnumerateFiles(temp, "package.xml", SearchOption.AllDirectories).FirstOrDefault();
            if (xml is null || !TryReadPackageIdentity(xml, out var name, out _))
            {
                throw new AuthoringWorkspaceException($"TapPackage '{path}' has no package.xml identity.");
            }

            var sourceDir = Path.GetDirectoryName(xml)!;
            CopyDirectory(sourceDir, Path.Combine(homeRoot, "Packages", name));
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of extract scratch.
            }
        }
    }

    private static void AssertNoVisa(string homeRoot)
    {
        var visa = Directory.EnumerateFiles(homeRoot, VisaAssemblyFileName, SearchOption.AllDirectories).FirstOrDefault();
        if (visa is not null)
        {
            throw new AuthoringWorkspaceException(
                $"Authoring OpenTAP home must not contain the VISA adapter ({visa}).");
        }
    }

    private static string FindRepoPackageXml(string packageName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                string[] candidates =
                [
                    Path.Combine(dir.FullName, "src", "HardwareTest.OpenTap.Plugins.Basic", "package.xml"),
                    Path.Combine(dir.FullName, "src", "HardwareTest.OpenTap.Plugins.Mixins", "package.xml"),
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
                continue;
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

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
