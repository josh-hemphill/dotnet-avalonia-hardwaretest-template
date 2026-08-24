namespace HardwareTest.Core.Hardware;

/// Why a plan slot is ready or blocked for Run.
public enum StationSlotReadinessKind
{
    Ready = 0,
    Unbound = 1,
    DemoOnly = 2,
}

/// One slot's readiness for the selected program.
public sealed class StationSlotReadiness
{
    public required string SlotName { get; init; }
    public required StationSlotReadinessKind Kind { get; init; }
    public string EffectiveResource { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool BlocksRun => Kind is StationSlotReadinessKind.Unbound or StationSlotReadinessKind.DemoOnly;
}

/// Aggregate station check used by Run and Instruments (Core, OpenTAP-free).
public sealed class StationReadinessReport
{
    public IReadOnlyList<StationSlotReadiness> Slots { get; init; } = [];
    public bool CanRun { get; init; }
    public IReadOnlyList<string> BlockingSlotNames { get; init; } = [];
    public string OperatorSummary { get; init; } = string.Empty;
}

/// Input for <see cref="StationReadinessEvaluator"/> — primitive fields only.
public sealed class StationSlotSnapshot
{
    public required string SlotName { get; init; }
    public string PlanId { get; init; } = string.Empty;
    public string RoleHint { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string EffectiveResource { get; init; } = string.Empty;
}

/// Evaluates whether plan slots are bound to runnable resources.
public static class StationReadinessEvaluator
{
    public static StationReadinessReport Evaluate(
        IReadOnlyList<StationSlotSnapshot> slots,
        bool useMockVisa)
    {
        var results = slots.Select(s => EvaluateSlot(s, useMockVisa)).ToList();
        var blocking = results.Where(r => r.BlocksRun).ToList();
        return new StationReadinessReport
        {
            Slots = results,
            CanRun = blocking.Count == 0,
            BlockingSlotNames = blocking.Select(b => b.SlotName).ToList(),
            OperatorSummary = FormatSummary(results, blocking),
        };
    }

    public static StationSlotReadiness EvaluateSlot(StationSlotSnapshot slot, bool useMockVisa)
    {
        if (string.IsNullOrWhiteSpace(slot.EffectiveResource))
        {
            return new StationSlotReadiness
            {
                SlotName = slot.SlotName,
                Kind = StationSlotReadinessKind.Unbound,
                Detail = "Unbound — discover a resource and Apply, then Save.",
            };
        }

        var resource = slot.EffectiveResource.Trim();
        if (!useMockVisa
            && (MockResourceGuard.LooksLikeMockResource(resource)
                || MockResourceGuard.IsMockInstrumentType(slot.TypeName)))
        {
            return new StationSlotReadiness
            {
                SlotName = slot.SlotName,
                Kind = StationSlotReadinessKind.DemoOnly,
                EffectiveResource = resource,
                Detail = "Demo only — bind a real VISA address while Use mock VISA is off.",
            };
        }

        return new StationSlotReadiness
        {
            SlotName = slot.SlotName,
            Kind = StationSlotReadinessKind.Ready,
            EffectiveResource = resource,
            Detail = "Ready",
        };
    }

    private static string FormatSummary(
        IReadOnlyList<StationSlotReadiness> results,
        IReadOnlyList<StationSlotReadiness> blocking)
    {
        if (results.Count == 0)
        {
            return "No instrument slots on this program.";
        }

        if (blocking.Count == 0)
        {
            return $"Station ready ({results.Count} slot(s)).";
        }

        var unbound = blocking.Where(b => b.Kind == StationSlotReadinessKind.Unbound).Select(b => b.SlotName).ToList();
        var demo = blocking.Where(b => b.Kind == StationSlotReadinessKind.DemoOnly).Select(b => b.SlotName).ToList();
        if (unbound.Count > 0 && demo.Count == 0)
        {
            return $"Bind unbound instrument slots on Instruments: {string.Join(", ", unbound)}";
        }

        if (demo.Count > 0 && unbound.Count == 0)
        {
            return $"Mock instruments blocked while Use mock VISA is off. Bind real addresses for: {string.Join(", ", demo)}";
        }

        return $"Finish Instruments commissioning for: {string.Join(", ", blocking.Select(b => b.SlotName))}";
    }
}
