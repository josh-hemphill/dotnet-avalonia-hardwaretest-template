using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Registers Basic + Mixins plugin directories for PluginManager.Search.
internal static class OpenTapPluginSearch
{
    private static readonly object SearchGate = new();

private static void EnsureCorePluginDirectories()
    {
        Add(typeof(MockDmmInstrument).Assembly.Location);
        Add(typeof(AnnotationMixinBuilder).Assembly.Location);

        // OpenTAP ships BasicSteps (Repeat/Sweep) under Packages/OpenTAP beside OpenTap.dll.
        var openTapDir = Path.GetDirectoryName(typeof(TestPlan).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(openTapDir))
        {
            Add(Path.Combine(openTapDir, "Packages", "OpenTAP", "OpenTap.Plugins.BasicSteps.dll"));
            var packagesRoot = Path.Combine(openTapDir, "Packages", "OpenTAP");
            if (Directory.Exists(packagesRoot)
                && !PluginManager.DirectoriesToSearch.Contains(Path.GetFullPath(packagesRoot)))
            {
                PluginManager.DirectoriesToSearch.Add(Path.GetFullPath(packagesRoot));
            }
        }
    }

    public static void Add(string? assemblyLocation)
    {
        var dir = Path.GetDirectoryName(assemblyLocation);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        var full = Path.GetFullPath(dir);
        if (!PluginManager.DirectoriesToSearch.Contains(full))
        {
            PluginManager.DirectoriesToSearch.Add(full);
        }
    }

    /// Serializes PluginManager.Search — OpenTAP plugin discovery is not safe under parallel test hosts.
    public static void SearchSerialized()
    {
        lock (SearchGate)
        {
            EnsureCorePluginDirectories();
            PluginManager.Search();
        }
    }
}
