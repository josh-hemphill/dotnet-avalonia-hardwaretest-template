using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Plan-shape fixtures for diagnosing Run board / OpenTAP host edge cases.
public static class PlanShapeFixtures
{
    public const string FlatLeavesName = "flat-leaves.TapPlan";
    public const string DeepNestName = "deep-nest.TapPlan";
    public const string DuplicateNamesName = "duplicate-names.TapPlan";
    public const string EmptyGroupName = "empty-group.TapPlan";
    public const string NoSafeShutdownName = "no-safe-shutdown.TapPlan";

    public static TestPlan CreateFlatLeaves()
    {
        var instrument = SharedInstrument();
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Name = "Leaf A",
            Instrument = instrument,
            SampleCount = 4,
            IntervalMs = 1,
        });
        plan.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Name = "Leaf B",
            Instrument = instrument,
            SampleCount = 4,
            IntervalMs = 1,
            Channel = "VDC2",
        });
        plan.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });
        return plan;
    }

    public static TestPlan CreateDeepNest()
    {
        var instrument = SharedInstrument();
        var leaf = new AcquireVoltageStep
        {
            Name = "Deep Acquire",
            Instrument = instrument,
            SampleCount = 4,
            IntervalMs = 1,
        };
        var g4 = Wrap("Level4", leaf);
        var g3 = Wrap("Level3", g4);
        var g2 = Wrap("Level2", g3);
        var g1 = Wrap("Level1", g2);
        var root = Wrap("Deep Nest Suite", g1);
        root.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        return plan;
    }

    public static TestPlan CreateDuplicateNames()
    {
        var instrument = SharedInstrument();
        var left = new TestGroupStep { Name = "Bank A" };
        left.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Name = "Acquire",
            Instrument = instrument,
            SampleCount = 4,
            IntervalMs = 1,
            Channel = "A",
        });
        var right = new TestGroupStep { Name = "Bank B" };
        right.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Name = "Acquire",
            Instrument = instrument,
            SampleCount = 4,
            IntervalMs = 1,
            Channel = "B",
        });
        var root = new TestGroupStep { Name = "Duplicate Names Suite" };
        root.ChildTestSteps.Add(left);
        root.ChildTestSteps.Add(right);
        root.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        return plan;
    }

    public static TestPlan CreateEmptyGroup()
    {
        var instrument = SharedInstrument();
        var empty = new TestGroupStep { Name = "Empty Section" };
        var withLeaf = new TestGroupStep { Name = "Populated Section" };
        withLeaf.ChildTestSteps.Add(new IdentityCheckStep
        {
            Name = "Identity Check",
            Instrument = instrument,
            Dut = new HardwareDut { Name = "DUT", Family = "demo" },
        });
        var root = new TestGroupStep { Name = "Empty Group Suite" };
        root.ChildTestSteps.Add(empty);
        root.ChildTestSteps.Add(withLeaf);
        root.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        return plan;
    }

    public static TestPlan CreateNoSafeShutdown()
    {
        var instrument = SharedInstrument();
        var root = new TestGroupStep { Name = "No Safe Shutdown Suite" };
        root.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Name = "Only Acquire",
            Instrument = instrument,
            SampleCount = 4,
            IntervalMs = 1,
        });

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        return plan;
    }

    public static IEnumerable<(string FileName, Func<TestPlan> Create)> All
    {
        get
        {
            yield return (FlatLeavesName, CreateFlatLeaves);
            yield return (DeepNestName, CreateDeepNest);
            yield return (DuplicateNamesName, CreateDuplicateNames);
            yield return (EmptyGroupName, CreateEmptyGroup);
            yield return (NoSafeShutdownName, CreateNoSafeShutdown);
        }
    }

    public static void SaveAllBeside(string directory)
    {
        Directory.CreateDirectory(directory);
        EnsurePlugins();
        foreach (var (fileName, create) in All)
        {
            create().Save(Path.Combine(directory, fileName));
        }
    }

    private static TestGroupStep Wrap(string name, ITestStep child)
    {
        var group = new TestGroupStep { Name = name };
        group.ChildTestSteps.Add(child);
        return group;
    }

    private static MockDmmInstrument SharedInstrument()
        => new() { Name = "DMM", ResourceName = "MOCK::INSTR0" };

    private static void EnsurePlugins()
    {
        var pluginDir = Path.GetDirectoryName(typeof(MockDmmInstrument).Assembly.Location)
                        ?? AppContext.BaseDirectory;
        if (!PluginManager.DirectoriesToSearch.Contains(pluginDir))
        {
            PluginManager.DirectoriesToSearch.Add(pluginDir);
        }

        PluginManager.Search();
    }
}
