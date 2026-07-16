using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Builds the hierarchical sample program that replaces the former JSON sample suite.
public static class SampleProgramFactory
{
    public const string EmbeddedName = "sample.TapPlan";

    public static TestPlan Create()
    {
        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
        var dut = new HardwareDut { Name = "DUT", Family = "demo" };

        var identityGroup = new TestGroupStep { Name = "Identity" };
        identityGroup.ChildTestSteps.Add(new IdentityCheckStep
        {
            Name = "Identity Check",
            Instrument = instrument,
            Dut = dut,
        });

        var prompt = new OperatorPromptStep
        {
            Name = "Install Sweep Fixture",
            Message = "Install the voltage-sweep fixture, then Continue.",
        };

        var measureGroup = new TestGroupStep { Name = "Voltage Sweep" };
        measureGroup.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Name = "Acquire VDC",
            Instrument = instrument,
            SampleCount = 32,
            IntervalMs = 5,
        });
        measureGroup.ChildTestSteps.Add(new MeanGteStep
        {
            Name = "Mean GTE",
            Instrument = instrument,
            SampleCount = 8,
            Threshold = 0,
        });

        var safety = new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument };

        var subsystem = new TestGroupStep { Name = "Sample Hardware Suite" };
        subsystem.ChildTestSteps.Add(identityGroup);
        subsystem.ChildTestSteps.Add(prompt);
        subsystem.ChildTestSteps.Add(measureGroup);
        subsystem.ChildTestSteps.Add(safety);

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(subsystem);
        return plan;
    }

    public static void SaveBeside(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, EmbeddedName);
        var pluginDir = Path.GetDirectoryName(typeof(MockDmmInstrument).Assembly.Location)
                        ?? AppContext.BaseDirectory;
        if (!PluginManager.DirectoriesToSearch.Contains(pluginDir))
        {
            PluginManager.DirectoriesToSearch.Add(pluginDir);
        }

        PluginManager.Search();
        var plan = Create();
        plan.Save(path);
    }
}
