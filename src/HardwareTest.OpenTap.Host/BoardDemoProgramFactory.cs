using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Nested demo plan for board UX (stages, subsections, longer acquires).
/// Demo coverage: confirm-only + typed operator prompts, multi-rail settings, Presentation roles.
public static class BoardDemoProgramFactory
{
    public const string EmbeddedName = "board-demo.TapPlan";
    public const string DisplayName = "Board Demo (nested / long run)";

    /// Stable Ids for Engineer/Debug station overrides on the 3V3 rail.
    public static readonly Guid Acquire3V3StepId = Guid.Parse("b2222222-2222-4222-8222-222222222201");
    public static readonly Guid MeanGte3V3StepId = Guid.Parse("b2222222-2222-4222-8222-222222222202");

    public static TestPlan Create()
    {
        OpenTapPluginSearch.EnsureCorePluginDirectories();
        PluginManager.Search();

        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
        var dut = new HardwareDut { Name = "DUT", Family = "demo" };

        var rail3v3 = new TestGroupStep { Name = "3V3 Rail" };
        var acquire3v3 = new AcquireVoltageStep
        {
            Id = Acquire3V3StepId,
            Name = "Acquire 3V3",
            Instrument = instrument,
            Channel = "3V3",
            SampleCount = 64,
            IntervalMs = 15,
        };
        OpenTapMixinAttach.AttachPresentation(acquire3v3, "rail.3v3", PresentationDisplayRoles.Timeseries, "V");
        rail3v3.ChildTestSteps.Add(acquire3v3);

        var mean3v3 = new MeanGteStep
        {
            Id = MeanGte3V3StepId,
            Name = "Mean GTE 3V3",
            Instrument = instrument,
            SampleCount = 12,
            Threshold = 0,
        };
        OpenTapMixinAttach.AttachPresentation(mean3v3, "rail.3v3.mean", PresentationDisplayRoles.Passband, "V");
        rail3v3.ChildTestSteps.Add(mean3v3);

        var rail5v = new TestGroupStep { Name = "5V Rail" };
        var acquire5v = new AcquireVoltageStep
        {
            Name = "Acquire 5V",
            Instrument = instrument,
            Channel = "5V",
            SampleCount = 48,
            IntervalMs = 20,
        };
        OpenTapMixinAttach.AttachPresentation(acquire5v, "rail.5v", PresentationDisplayRoles.Timeseries, "V");
        rail5v.ChildTestSteps.Add(acquire5v);

        var mean5v = new MeanGteStep
        {
            Name = "Mean GTE 5V",
            Instrument = instrument,
            SampleCount = 10,
            Threshold = 0,
        };
        OpenTapMixinAttach.AttachPresentation(mean5v, "rail.5v.mean", PresentationDisplayRoles.Scalar, "V");
        rail5v.ChildTestSteps.Add(mean5v);

        var powerRails = new TestGroupStep { Name = "Power Rails" };
        powerRails.ChildTestSteps.Add(rail3v3);
        powerRails.ChildTestSteps.Add(rail5v);

        var identity = new TestGroupStep { Name = "Identity" };
        identity.ChildTestSteps.Add(new IdentityCheckStep
        {
            Name = "Identity Check",
            Instrument = instrument,
            Dut = dut,
        });

        var busStress = new TestGroupStep { Name = "Bus Stress" };
        var longAcquire = new AcquireVoltageStep
        {
            Name = "Long Acquire VDC",
            Instrument = instrument,
            Channel = "VDC",
            SampleCount = 120,
            IntervalMs = 25,
        };
        OpenTapMixinAttach.AttachPresentation(longAcquire, "bus.vdc", PresentationDisplayRoles.Timeseries, "V");
        busStress.ChildTestSteps.Add(longAcquire);

        var meanBus = new MeanGteStep
        {
            Name = "Mean GTE Bus",
            Instrument = instrument,
            SampleCount = 16,
            Threshold = 0,
        };
        OpenTapMixinAttach.AttachPresentation(meanBus, "bus.vdc.mean", PresentationDisplayRoles.Passband, "V");
        busStress.ChildTestSteps.Add(meanBus);

        var communications = new TestGroupStep { Name = "Communications" };
        communications.ChildTestSteps.Add(identity);
        communications.ChildTestSteps.Add(busStress);

        var operatorGroup = new TestGroupStep { Name = "Operator" };
        operatorGroup.ChildTestSteps.Add(new OperatorPromptStep
        {
            Name = "Seat Board Fixture",
            Message = "Seat the board in the demo fixture, then Continue.",
        });
        operatorGroup.ChildTestSteps.Add(new OperatorInputStep
        {
            Name = "Record Board Sticker",
            Title = "Board sticker",
            Message = "Enter the board lot / sticker id from the label, then Continue.",
            StringFieldId = "boardLotId",
            StringFieldLabel = "Lot / sticker id",
            StringFieldRequired = false,
            NumberFieldId = string.Empty,
        });

        var safety = new TestGroupStep { Name = "Safety" };
        safety.ChildTestSteps.Add(new SafeShutdownStep
        {
            Name = "Safe Shutdown",
            Instrument = instrument,
        });

        var root = new TestGroupStep { Name = DisplayName };
        root.ChildTestSteps.Add(powerRails);
        root.ChildTestSteps.Add(communications);
        root.ChildTestSteps.Add(operatorGroup);
        root.ChildTestSteps.Add(safety);

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        return plan;
    }

    public static void SaveBeside(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, EmbeddedName);
        OpenTapPluginSearch.EnsureCorePluginDirectories();
        PluginManager.Search();
        Create().Save(path);
    }
}
