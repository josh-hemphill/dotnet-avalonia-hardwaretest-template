using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.Authoring;

/// Registers in-tree Basic + Mixins + OpenTAP BasicSteps for PluginManager. Never the VISA adapter.
public static class AuthoringPluginSearch
{
    public const string VisaAssemblyFileName = "HardwareTest.OpenTap.Plugins.Visa.dll";

    private static readonly object SearchGate = new();

    public static void Search(IEnumerable<string>? extraDirectories = null)
    {
        lock (SearchGate)
        {
            AddAssemblyDirectory(typeof(MockDmmInstrument).Assembly.Location);
            AddAssemblyDirectory(typeof(AnnotationMixinBuilder).Assembly.Location);

            var openTapDir = Path.GetDirectoryName(typeof(TestPlan).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(openTapDir))
            {
                AddAssemblyDirectory(Path.Combine(openTapDir, "Packages", "OpenTAP", "OpenTap.Plugins.BasicSteps.dll"));
                AddDirectory(Path.Combine(openTapDir, "Packages", "OpenTAP"));
            }

            if (extraDirectories is not null)
            {
                foreach (var dir in extraDirectories)
                {
                    AddDirectory(dir);
                }
            }

            PluginManager.Search();
        }
    }

    public static bool DirectoryContainsVisaAdapter(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        return File.Exists(Path.Combine(directory, VisaAssemblyFileName));
    }

    private static void AddAssemblyDirectory(string? assemblyLocation)
        => AddDirectory(Path.GetDirectoryName(assemblyLocation));

    private static void AddDirectory(string? dir)
    {
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
