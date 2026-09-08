using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Registers Basic + Visa + Mixins plugin directories for PluginManager.Search.
internal static class OpenTapPluginSearch
{
    private static readonly object SearchGate = new();

    /// Adds optional extra plugin roots, then runs PluginManager.Search under one lock.
    public static void SearchSerialized(
        IEnumerable<string>? extraDirectories = null,
        IVisaBroker? visaBroker = null)
    {
        lock (SearchGate)
        {
            if (visaBroker is not null)
            {
                VisaBrokerHost.Register(visaBroker);
            }

            EnsureCorePluginDirectories();
            if (extraDirectories is not null)
            {
                foreach (var dir in extraDirectories)
                {
                    AddDirectory(dir);
                }
            }

            PluginManager.Search();
            if (visaBroker is not null && !InstrumentComponentsScpiIo.TryRegisterProvider(visaBroker))
            {
                Serilog.Log.Debug(
                    "InstrumentComponents.OpenTap is not loaded; SCPI provider was not registered. Product plans that use that pack need it on the plugin search path.");
            }
        }
    }

    private static void EnsureCorePluginDirectories()
    {
        AddAssemblyDirectory(typeof(MockDmmInstrument).Assembly.Location);
        AddAssemblyDirectory(typeof(VisaDmmInstrument).Assembly.Location);
        AddAssemblyDirectory(typeof(AnnotationMixinBuilder).Assembly.Location);

        // OpenTAP ships BasicSteps (Repeat/Sweep) under Packages/OpenTAP beside OpenTap.dll.
        var openTapDir = Path.GetDirectoryName(typeof(TestPlan).Assembly.Location);
        if (string.IsNullOrWhiteSpace(openTapDir))
        {
            return;
        }

        AddAssemblyDirectory(Path.Combine(openTapDir, "Packages", "OpenTAP", "OpenTap.Plugins.BasicSteps.dll"));
        AddDirectory(Path.Combine(openTapDir, "Packages", "OpenTAP"));
    }

    private static void AddAssemblyDirectory(string? assemblyLocation)
    {
        var dir = Path.GetDirectoryName(assemblyLocation);
        AddDirectory(dir);
    }

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
