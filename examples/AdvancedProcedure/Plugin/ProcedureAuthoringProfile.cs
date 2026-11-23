using HardwareTest.OpenTap.Plugins.Basic;
using static HardwareTest.OpenTap.Plugins.Basic.ProcedureFieldKind;

namespace HardwareTest.Examples.AdvancedProcedure;

public sealed partial class FixtureProcedureStep : IProcedureAuthoring
{
    ProcedureEditorProfile IProcedureAuthoring.EditorProfile => new(1,
    [
        Step<FixtureProcedureStep>("Procedure", [new("Serial", "Station serial profile", Instrument, StepTypeId<SerialDutPort>(), Optional: true),
            new("Serial/Name", "Logical serial slot name"), new("Serial/Address", "Serial address"),
            new("Serial/BaudRate", "Baud rate", Integer), new("Serial/TimeoutMs", "Reply timeout (ms)", Integer)],
            [StepTypeId<FixtureSectionStep>()]),
        Step<FixtureSectionStep>("Section", [new("GatesRemainingSections", "Gate subsequent sections", ProcedureFieldKind.Boolean),
            new("PrerequisiteIds", "Required sections", TextList, "sections", Optional: true), new("Purpose", "Purpose", Optional: true)],
            [StepTypeId<FixtureSuiteStep>(), StepTypeId<FixtureConfigurationsStep>(), StepTypeId<FixtureShutdownStep>()], true),
        Step<FixtureSuiteStep>("Suite", [new("Purpose", "Purpose", Optional: true),
            new("PrerequisiteIds", "Required sections", TextList, "sections", Optional: true),
            new("SetupNotes", "Setup notes", Optional: true)],
            [StepTypeId<FixtureChangeStep>(), StepTypeId<RailBringUpStep>(), StepTypeId<HarnessResistanceStep>(), StepTypeId<DutCommunicationsStep>(),
             StepTypeId<DutSignalsStep>(), StepTypeId<DifferentialProfileStep>(), StepTypeId<ClampAndSensorStep>(), StepTypeId<ReciprocalAcStep>(),
             StepTypeId<CounterTriggerStep>(), StepTypeId<DecodePulseWidthStep>(), StepTypeId<ConfigurationMeasurementStep>()], true),
        Step<FixtureConfigurationsStep>("Choose configurations", [new("Instructions", "Operator instructions", Optional: true),
            new("OfferRetryOnFailure", "Offer retry after a failed configuration", ProcedureFieldKind.Boolean)],
            [StepTypeId<FixtureSuiteStep>()], true, true),
        Step<FixtureChangeStep>("Operator handoff", [new("Message", "Operator instructions"),
            new("EnableSupplyAfterConfirmation", "Enable supply after confirmation", ProcedureFieldKind.Boolean)], [], true, true, true),
        Step<FixtureShutdownStep>("Safe shutdown", [], []),
        Measurement<RailBringUpStep>("Rail bring-up"), Measurement<HarnessResistanceStep>("Harness resistance"),
        Measurement<DutCommunicationsStep>("DUT serial commands"), Measurement<DutSignalsStep>("DUT signal checks"),
        Measurement<DifferentialProfileStep>("Differential profile", new("CaptureSamples", "Samples per capture channel", Integer, DefaultValue: "96")), Measurement<ClampAndSensorStep>("Clamp and sensor"),
        Measurement<ReciprocalAcStep>("Reciprocal AC"), Measurement<CounterTriggerStep>("Counter triggers"),
        Measurement<DecodePulseWidthStep>("Decode pulse width"), Measurement<ConfigurationMeasurementStep>("Configuration measurement")
    ],
    [
        new($"{TableDirectory}/station.json", "Station profile",
        [
            new("routes", "Switch routes", ["id"], [new("id", "Route ID"), new("path", "Switch path"), new("unit", "Unit"), new("nominal", "Simulated nominal", Number)]),
            new("calibrations", "Approved corrections", ["route", "impedanceOhms"],
                [new("route", "Route", ReferenceTable: "routes"), new("impedanceOhms", "Impedance (Ω)", Number), new("correctionDb", "Correction (dB)", Number)])
        ], [new("stationId", "Station ID"), new("revision", "Route revision"), new("calibrationRevision", "Calibration revision")]),
        new($"{TableDirectory}/tests.json", "Test configuration",
        [
            new("references", "Reference traces", ["id"], [new("id", "Trace ID"), new("values", "Reference points", NumberList)]),
            new("cases", "Test cases", ["id"], [new("id", "Case ID"), new("routes", "Routes", TextList, "routes"),
                new("levels", "Stimulus levels", NumberList), new("reference", "Reference trace", ReferenceTable: "references", Optional: true)],
                [new("criteria", "Evaluated rows", ["id"],
                    [new("id", "Criterion ID"), new("displayName", "Result display name", Optional: true), new("unit", "Unit"),
                     new("low", "Lower limit", Number), new("high", "Upper limit", Number)])])
        ], [new("stationId", "Station ID"), new("revision", "Test revision")])
    ]);

    void IProcedureAuthoring.ValidateAuthoring()
    {
        ValidateSerial();
        ValidateConfiguration();
    }

    OperatorInteractionRequest? IProcedureAuthoring.PreviewInteraction(Guid stepId, IReadOnlySet<string> completed)
        => Descendants(this).SingleOrDefault(step => step.Id == stepId) switch
        {
            FixtureChangeStep handoff => handoff.Interaction(),
            FixtureConfigurationsStep choices => choices.Interaction(completed, string.Empty),
            _ => null
        };

    private static ProcedureStepEditor Measurement<T>(string label, ProcedureField? setting = null)
        => Step<T>(label, setting is null ? [new("CaseId", "Test case", ReferenceTable: "cases")]
            : [new("CaseId", "Test case", ReferenceTable: "cases"), setting], [], true);
    private static ProcedureStepEditor Step<T>(string label, ProcedureField[] fields, string[] children, bool allowAdd = false, bool preview = false, bool isHandoff = false)
        => new(StepTypeId<T>(), label, fields, children, allowAdd, preview, isHandoff);
    private static string StepTypeId<T>() => typeof(T).FullName!;
}
