using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Registers Basic + Mixins plugin directories (Visa adapter optional) for PluginManager.Search.
internal static class OpenTapPluginSearch
{
    private static readonly object SearchGate = new();

    /// Adds optional extra plugin roots, then runs PluginManager.Search under one lock.
    public static void SearchSerialized(
        IEnumerable<string>? extraDirectories = null,
        IVisaBroker? visaBroker = null,
        bool includeVisaAdapter = true)
    {
        lock (SearchGate)
        {
            var extras = extraDirectories?.ToArray() ?? [];
            if (includeVisaAdapter && visaBroker is not null)
            {
                var executionDirectory = ExecutionInstrumentLibrary.EnsureLoaded(extras);
                VisaBrokerHost.Register(visaBroker);
                AddDirectory(executionDirectory);
            }

            EnsureCorePluginDirectories(includeVisaAdapter);
            if (extraDirectories is not null)
            {
                foreach (var dir in extras)
                {
                    AddDirectory(dir);
                }
            }

            PluginManager.Search();
            if (includeVisaAdapter && visaBroker is not null && !InstrumentComponentsScpiIo.TryRegisterProvider(visaBroker))
            {
                throw new InvalidOperationException("Instrument Components broker provider could not be bound. Repair the selected execution library before running the plan.");
            }
        }
    }

    private static void EnsureCorePluginDirectories(bool includeVisaAdapter)
    {
        AddAssemblyDirectory(typeof(MockDmmInstrument).Assembly.Location);
        if (includeVisaAdapter)
        {
            AddVisaAdapterDirectory();
        }

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

    private static void AddVisaAdapterDirectory()
        => AddAssemblyDirectory(typeof(VisaDmmInstrument).Assembly.Location);

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
