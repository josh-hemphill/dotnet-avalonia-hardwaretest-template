using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Builds the hierarchical sample program that replaces the former JSON sample suite.
/// Demo coverage: confirm-only prompt, typed operator input, and station-overridable measure settings.
public static class SampleProgramFactory
{
    public const string EmbeddedName = "sample.TapPlan";

    /// Stable Ids so station parameter overrides survive sample plan reloads.
    public static readonly Guid AcquireStepId = Guid.Parse("a1111111-1111-4111-8111-111111111101");
    public static readonly Guid MeanGteStepId = Guid.Parse("a1111111-1111-4111-8111-111111111102");

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

        // Operator prompt lane (technician): confirm-only, then typed input (string + number).
        var confirmFixture = new OperatorPromptStep
        {
            Name = "Confirm Sweep Area Clear",
            Message = "Confirm the sweep area is clear of tools, then Continue.",
        };
        var inputFixture = new OperatorInputStep
        {
            Name = "Install Sweep Fixture",
            Title = "Install Sweep Fixture",
            Message = "Install the voltage-sweep fixture, enter the fixture id and optional torque, then Continue.",
            StringFieldId = "fixtureId",
            StringFieldLabel = "Fixture id",
            StringFieldRequired = false,
            NumberFieldId = "fixtureTorqueNm",
            NumberFieldLabel = "Fixture torque (N·m)",
            NumberFieldRequired = false,
        };

        // Station override lane (Engineer/Debug): SampleCount / IntervalMs / Channel / Threshold / Enabled.
        var measureGroup = new TestGroupStep { Name = "Voltage Sweep" };
        measureGroup.ChildTestSteps.Add(new AcquireVoltageStep
        {
            Id = AcquireStepId,
            Name = "Acquire VDC",
            Instrument = instrument,
            Channel = "VDC",
            SampleCount = 32,
            IntervalMs = 5,
        });
        measureGroup.ChildTestSteps.Add(new MeanGteStep
        {
            Id = MeanGteStepId,
            Name = "Mean GTE",
            Instrument = instrument,
            SampleCount = 8,
            Threshold = 0,
        });

        var safety = new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument };

        var subsystem = new TestGroupStep { Name = "Sample Hardware Suite" };
        subsystem.ChildTestSteps.Add(identityGroup);
        subsystem.ChildTestSteps.Add(confirmFixture);
        subsystem.ChildTestSteps.Add(inputFixture);
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
