using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public enum PlanStartingPoint { Empty, VoltageTask, DemoVoltageTask }

/// Explicit source choices, shared by guided UI and direct callers. No resource is implied.
public sealed record PlanInitializationRequest(string PlanId)
{
    public string? DisplayName { get; init; }
    public string DeviceFamily { get; init; } = "generic";
    public string? WorkspaceRoot { get; init; }
    public string? DestinationPath { get; init; }
    public IReadOnlyList<string> ExistingPlanIds { get; init; } = [];
    public PlanStartingPoint StartingPoint { get; init; }
    public bool IncludeTemplateMeasurement { get; init; } = true;
    public bool UseTemplateHardware { get; init; } = true;
    public IReadOnlyList<InstrumentRef> Instruments { get; init; } = [];
    public bool RequireSerial { get; init; } = true;
    public IReadOnlyList<string> RequiredOperatorFields { get; init; } = [];
    public string? IdentityInstrumentSlot { get; init; }
    public string? FixtureConfirmation { get; init; }
    public string? FixtureInputField { get; init; }
    public bool IncludeSafeShutdown { get; init; } = true;
    public PlanInitialMeasurement? Measurement { get; init; }
    public OpenTapHome? Home { get; init; }
    public string? HomeResolutionError { get; init; }
}

public sealed record PlanInitialMeasurement(string RecipeId, string InstrumentSlot)
{
    public string ChannelKey { get; init; } = "VDC";
    public string Unit { get; init; } = "V";
    public string SampleCount { get; init; } = "32";
    public string IntervalMs { get; init; } = "5";
    public string? ThresholdText { get; init; }
    public LimitSpec? Criterion { get; init; }
}

public sealed record PlanInitializationResult(ProgramDraft Draft, string? DestinationPath,
    IReadOnlyList<AuthoringEditingIssue> Issues, string Review, string NextAction);
