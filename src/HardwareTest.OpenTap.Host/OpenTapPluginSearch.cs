using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Registers Basic + Mixins plugin directories for PluginManager.Search.
internal static class OpenTapPluginSearch
{
    public static void EnsureCorePluginDirectories()
    {
        Add(typeof(MockDmmInstrument).Assembly.Location);
        Add(typeof(AnnotationMixinBuilder).Assembly.Location);
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
}
