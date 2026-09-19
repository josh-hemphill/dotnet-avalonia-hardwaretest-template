namespace HardwareTest.Authoring;

/// Fail-closed Identity / measure usage when deleting an instrument slot.
public static class AuthoringInstrumentUsage
{
    public static bool HasOpaqueInstrumentRefs(ProgramDraft? draft)
    {
        if (draft is null)
        {
            return true;
        }

        foreach (var action in draft.Setup)
        {
            switch (action)
            {
                case IdentitySetup:
                case OperatorPromptSetup:
                case OperatorInputSetup:
                    break;
                default:
                    return true;
            }
        }

        return WalkOpaque(draft.Measure);
    }

    public static ProgramDraft RetargetSlot(ProgramDraft draft, string fromSlot, string toSlot)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var from = AuthoringWorkspaceCatalog.Normalize(fromSlot);
        var to = AuthoringWorkspaceCatalog.Normalize(toSlot);
        if (from is null
            || to is null
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return draft;
        }

        var setup = draft.Setup
            .Select(action => action is IdentitySetup identity
                && string.Equals(identity.InstrumentSlot, from, StringComparison.OrdinalIgnoreCase)
                ? identity with { InstrumentSlot = to }
                : action)
            .ToArray();
        return draft with
        {
            Setup = setup,
            Measure = RetargetMeasure(draft.Measure, from, to),
        };
    }

    private static bool WalkOpaque(IReadOnlyList<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode:
                    break;
                case RepeatNode repeat:
                    if (WalkOpaque(repeat.Children))
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

    private static IReadOnlyList<MeasureNode> RetargetMeasure(
        IReadOnlyList<MeasureNode> nodes,
        string from,
        string to)
        => nodes.Select(node => node switch
        {
            MetricNode { Metric.Source: MeasureSource measure } metric
                when string.Equals(measure.InstrumentSlot, from, StringComparison.OrdinalIgnoreCase)
                => metric with
                {
                    Metric = metric.Metric with { Source = measure with { InstrumentSlot = to } },
                },
            RepeatNode repeat => repeat with { Children = RetargetMeasure(repeat.Children, from, to) },
            _ => node,
        }).ToArray();
}
