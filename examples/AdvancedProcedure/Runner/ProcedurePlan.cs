using System.Security.Cryptography;
using System.Text;
using HardwareTest.Core.Settings;
using HardwareTest.Examples.AdvancedProcedure;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

internal static class ProcedurePlan
{
    public const string PlanName = "advanced-procedure.TapPlan";

    public static TestPlan Create()
    {
        new OpenTapHostCatalog(new AppSettings { UseMockVisa = true }, Serilog.Log.Logger).EnsurePlugins();
        var meter = new MockDmmInstrument { Name = "Simulated Fixture DMM", ResourceName = "MOCK::INSTR0" };
        var root = new FixtureProcedureStep
        {
            Name = "Advanced Fixture Procedure",
            Serial = new SerialDutPort { Name = "Simulated DUT Serial" },
        };
        var setup = Section("Station Bring-Up and Self-Test", gate: true,
            Suite("Low-Power and Data Rails",
                Measure<RailBringUpStep>("Enable and verify logic rail", "rail-logic"),
                Measure<RailBringUpStep>("Enable and verify data rail", "rail-data")),
            Suite("Analog Supply",
                Measure<RailBringUpStep>("Enable and verify analog rail", "rail-analog")),
            Suite("Harness Integrity",
                Change("Install self-test loopback",
                    "Simulated supplies are off. Leave the DUT disconnected, install the station loopback on J9 and check the J1 power and data harnesses. Continue when the fixture is in self-test mode."),
                Measure<HarnessResistanceStep>("Measure power harness continuity", "harness-power"),
                Measure<HarnessResistanceStep>("Measure data harness continuity", "harness-data"),
                Measure<HarnessResistanceStep>("Measure shield isolation", "harness-isolation")));
        var dut = Section("DUT Connection and Basic Checks", gate: true,
            Suite("DUT Connection",
                Change("Connect the DUT",
                    "Simulated supplies are off. Remove the J9 self-test loopback. Connect the DUT power, data, and serial leads at J1–J3. Continue when the DUT is seated.",
                    enableSupply: true),
                new IdentityCheckStep
                {
                    Name = "Record session DUT identity",
                    Instrument = meter,
                    Dut = new HardwareDut { Name = "Signal Board", Family = "demo" },
                },
                Measure<DutCommunicationsStep>("Check serial identity, ping and status", "dut-commands")),
            Suite("Idle Digital Signals", Measure<DutSignalsStep>("Check clock waveform and data levels", "dut-clock")));
        var differential = Section("Differential and Loaded Analog",
            false,
            Suite("Differential Spectrum", Measure<DifferentialProfileStep>("Measure settled unloaded and loaded ladder", "diff-response")),
            Suite("Curve Margin", Measure<DifferentialProfileStep>("Score curve against margin reference", "diff-margin")),
            Suite("Load Clamp", Measure<ClampAndSensorStep>("Measure clamp, reclamp, and DUT sensor agreement", "clamp-load")));
        var ac = Section("AC Equalization and Attenuation", false,
            Suite("Reciprocal Paths",
                Measure<ReciprocalAcStep>("Measure calibrated attenuation at two impedances", "ac-reciprocal")));
        var digital = Section("Counters and Embedded Digital", false,
            Suite("Trigger Counts", Measure<CounterTriggerStep>("Check relative, cumulative and serial counter values", "counter-relative")),
            Suite("Pulse Decoder", Measure<DecodePulseWidthStep>("Decode dual-generator digitizer capture", "pulse-decoder")));
        var ordered = new FixtureConfigurationsStep { Name = "Run Remaining Configurations" };
        AddChildren(ordered, [
            Measure<ConfigurationMeasurementStep>("Loopback", "config-loopback"),
            Measure<ConfigurationMeasurementStep>("Terminated", "config-terminated"),
            Measure<ConfigurationMeasurementStep>("Crossed", "config-crossed"),
        ]);
        var configurations = Section("Operator-Selected Configurations", false, ordered);
        var cleanup = Section("Cleanup", false, Suite("Return Fixture to Idle",
            new FixtureShutdownStep { Name = "Safe Shutdown" }));

        AddChildren(root, [setup, dut, differential, ac, digital, configurations, cleanup]);
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        AssignIds(root, "advanced-procedure");
        return plan;
    }

    private static T Measure<T>(string name, string id) where T : FixtureMeasurementStep, new()
    {
        var step = new T { Name = name, CaseId = id };
        _ = OpenTapMixinAttach.AttachPresentation(step, id, "timeseries", string.Empty);
        return step;
    }

    private static FixtureChangeStep Change(string name, string message, bool enableSupply = false)
    {
        var step = new FixtureChangeStep { Name = name, Message = message, EnableSupplyAfterConfirmation = enableSupply };
        _ = OpenTapMixinAttach.AttachPresentation(step, name.Replace(' ', '-').ToLowerInvariant(), "timeseries", string.Empty);
        return step;
    }

    private static FixtureSectionStep Section(string name, bool gate, params ITestStep[] suites)
    {
        var section = new FixtureSectionStep { Name = name, GatesRemainingSections = gate };
        AddChildren(section, suites);
        return section;
    }

    private static FixtureSuiteStep Suite(string name, params ITestStep[] steps)
    {
        var suite = new FixtureSuiteStep { Name = name };
        AddChildren(suite, steps);
        return suite;
    }

    private static void AddChildren(ITestStepParent parent, IEnumerable<ITestStep> children)
    {
        foreach (var child in children)
            parent.ChildTestSteps.Add(child);
    }

    private static void AssignIds(ITestStep step, string parent)
    {
        var path = $"{parent}/{step.Name}";
        step.Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(path)).AsSpan(0, 16));
        foreach (var child in step.ChildTestSteps)
            AssignIds(child, path);
    }
}
