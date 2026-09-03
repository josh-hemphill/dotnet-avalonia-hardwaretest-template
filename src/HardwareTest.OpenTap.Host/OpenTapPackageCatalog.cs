using System.Xml.Linq;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

public sealed class OpenTapPluginDirectoryInfo
{
    public required string Path { get; init; }
    public required string Source { get; init; }
}

public sealed class OpenTapPackageInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Path { get; init; }
}

/// Enumerates plugin search directories and installed packages via package.xml (no feed APIs).
public static class OpenTapPackageCatalog
{
    private static readonly XNamespace PackageNs = "http://opentap.io/schemas/package";

    /// Merge Basic, Mixins, settings, env, and PluginManager search dirs (first source wins).
    public static IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories(AppSettings settings)
    {
        var byPath = new Dictionary<string, OpenTapPluginDirectoryInfo>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir, string source)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            string full;
            try
            {
                full = Path.GetFullPath(dir);
            }
            catch
            {
                return;
            }

            if (!Directory.Exists(full) || byPath.ContainsKey(full))
            {
                return;
            }

            byPath[full] = new OpenTapPluginDirectoryInfo { Path = full, Source = source };
        }

        Add(Path.GetDirectoryName(typeof(MockDmmInstrument).Assembly.Location), "Basic");
        Add(Path.GetDirectoryName(typeof(VisaDmmInstrument).Assembly.Location), "Visa");
        Add(Path.GetDirectoryName(typeof(AnnotationMixinBuilder).Assembly.Location), "Mixins");

        foreach (var dir in settings.OpenTapPluginDirectories)
        {
            Add(dir, "Settings");
        }

        var env = Environment.GetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            foreach (var part in env.Split(
                         [Path.PathSeparator, ';'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Add(part, "Env");
            }
        }

        foreach (var dir in PluginManager.DirectoriesToSearch)
        {
            Add(dir, "PluginManager");
        }

        return byPath.Values
            .OrderBy(d => d.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Scan plugin dirs and OpenTAP assembly neighborhood for package.xml.
    public static IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages(
        AppSettings settings,
        ILogger? logger = null)
    {
        var log = logger ?? Serilog.Log.ForContext(typeof(OpenTapPackageCatalog));
        var byKey = new Dictionary<string, OpenTapPackageInfo>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in ListPluginDirectories(settings))
        {
            roots.Add(dir.Path);
        }

        var openTapDir = Path.GetDirectoryName(typeof(TestPlan).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(openTapDir))
        {
            roots.Add(Path.GetFullPath(openTapDir));
            var packagesSibling = Path.Combine(Path.GetFullPath(openTapDir), "Packages");
            if (Directory.Exists(packagesSibling))
            {
                roots.Add(packagesSibling);
            }

            var parentPackages = Path.Combine(Path.GetFullPath(openTapDir), "..", "Packages");
            try
            {
                var fullParent = Path.GetFullPath(parentPackages);
                if (Directory.Exists(fullParent))
                {
                    roots.Add(fullParent);
                }
            }
            catch
            {
                // ignore invalid paths
            }
        }

        foreach (var root in roots)
        {
            CollectPackages(root, byKey, log);
        }

        return byKey.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectPackages(
        string root,
        IDictionary<string, OpenTapPackageInfo> byKey,
        ILogger log)
    {
        TryAddPackageXml(Path.Combine(root, "package.xml"), byKey, log);

        try
        {
            foreach (var child in Directory.EnumerateDirectories(root))
            {
                TryAddPackageXml(Path.Combine(child, "package.xml"), byKey, log);
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "Could not enumerate package folders under {Root}", root);
        }

        var nestedPackages = Path.Combine(root, "Packages");
        if (!Directory.Exists(nestedPackages)
            || string.Equals(Path.GetFullPath(nestedPackages), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            foreach (var child in Directory.EnumerateDirectories(nestedPackages))
            {
                TryAddPackageXml(Path.Combine(child, "package.xml"), byKey, log);
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "Could not enumerate Packages under {Root}", root);
        }
    }

    private static void TryAddPackageXml(
        string packageXmlPath,
        IDictionary<string, OpenTapPackageInfo> byKey,
        ILogger log)
    {
        if (!File.Exists(packageXmlPath))
        {
            return;
        }

        try
        {
            var doc = XDocument.Load(packageXmlPath);
            var root = doc.Root;
            if (root is null
                || !string.Equals(root.Name.LocalName, "Package", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var name = (string?)root.Attribute("Name")
                       ?? root.Element(PackageNs + "Name")?.Value
                       ?? root.Element("Name")?.Value;
            var version = (string?)root.Attribute("Version")
                          ?? root.Element(PackageNs + "Version")?.Value
                          ?? root.Element("Version")?.Value
                          ?? "";

            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var folder = Path.GetDirectoryName(packageXmlPath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var full = Path.GetFullPath(folder);
            var key = $"{name}|{version}|{full}";
            if (byKey.ContainsKey(key))
            {
                return;
            }

            byKey[key] = new OpenTapPackageInfo
            {
                Name = name.Trim(),
                Version = version.Trim(),
                Path = full,
            };
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "Ignoring malformed package.xml at {Path}", packageXmlPath);
        }
    }
}
