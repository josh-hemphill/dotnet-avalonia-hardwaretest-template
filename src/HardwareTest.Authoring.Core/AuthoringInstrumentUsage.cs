namespace HardwareTest.Authoring;

/// Fail-closed Identity / measure references that block deleting an instrument slot.
public static class AuthoringInstrumentUsage
{
    public static bool IsReferencedByIdentityOrMeasure(ProgramDraft? draft, string? slot)
    {
        var token = AuthoringWorkspaceCatalog.Normalize(slot);
        if (draft is null || token is null)
        {
            return true;
        }

        foreach (var action in draft.Setup)
        {
            if (action is IdentitySetup identity
                && string.Equals(identity.InstrumentSlot, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return WalkMeasure(draft.Measure, token);
    }

    private static bool WalkMeasure(IReadOnlyList<MeasureNode> nodes, string slot)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode { Metric.Source: MeasureSource measure }
                    when string.Equals(measure.InstrumentSlot, slot, StringComparison.OrdinalIgnoreCase):
                    return true;
                case MetricNode:
                    break;
                case RepeatNode repeat:
                    if (WalkMeasure(repeat.Children, slot))
                    {
                        return true;
                    }

                    break;
                default:
                    return true;
            }
        }

        return false;
    }
}
