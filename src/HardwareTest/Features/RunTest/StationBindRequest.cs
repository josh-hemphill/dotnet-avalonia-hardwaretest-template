namespace HardwareTest.Features.RunTest;

/// Deep-link payload from the Run unbound/demo-slot gate to Instruments commissioning.
public sealed class StationBindRequest : EventArgs
{
    public required string PlanId { get; init; }
    public required IReadOnlyList<string> SlotNames { get; init; }
}
