using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Short RepeatLoopStep fixture for Phase G iteration chrome (Count = 3).
public static class SweepDemoProgramFactory
{
    public const string DisplayName = "Sweep Demo (Repeat ×3)";
    public const string EmbeddedName = "embedded:sweep-demo";
    public const string FixtureFileName = "sweep-repeat.TapPlan";

    public static TestPlan Create()
    {
        OpenTapPluginSearch.EnsureCorePluginDirectories();
        PluginManager.Search();

        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
        var body = new AcquireVoltageStep
        {
            Name = "Acquire VDC",
            Instrument = instrument,
            SampleCount = 2,
            IntervalMs = 1,
        };
        OpenTapMixinAttach.AttachPresentation(body, "sweep.vdc", PresentationDisplayRoles.Timeseries, "V");

        var repeat = new RepeatLoopStep
        {
            Name = "Repeat Sweep",
            Count = 3,
        };
        repeat.ChildTestSteps.Add(body);

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(repeat);
        plan.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });
        return plan;
    }

    public static void SaveBeside(string directory)
    {
        Directory.CreateDirectory(directory);
        OpenTapPluginSearch.EnsureCorePluginDirectories();
        PluginManager.Search();
        Create().Save(Path.Combine(directory, FixtureFileName));
    }
}
