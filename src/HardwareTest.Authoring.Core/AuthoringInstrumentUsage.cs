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

        if (draft.Instruments.Any(instrument => instrument.OpaqueResourceXml is not null)) return true;

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
            Cleanup = draft.Cleanup with { InstrumentSlots = RetargetList(draft.Cleanup.InstrumentSlots, from, to) },
            Sidecar = RetargetSidecar(draft.Sidecar, from, to),
        };
    }

    public static IReadOnlyList<string> DescribeSlotUsage(ProgramDraft draft, string slot)
    {
        var nodes = new List<string> { "Instrument definition" };
        for (var i = 0; i < draft.Setup.Count; i++)
            if (draft.Setup[i] is IdentitySetup identity && string.Equals(identity.InstrumentSlot, slot, StringComparison.OrdinalIgnoreCase)) nodes.Add($"Setup[{i}] Identity");
        void Walk(IReadOnlyList<MeasureNode> children, string path)
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] is MetricNode { Metric.Source: MeasureSource source } metric && string.Equals(source.InstrumentSlot, slot, StringComparison.OrdinalIgnoreCase)) nodes.Add($"{path}[{i}] {metric.Metric.Name}");
                if (children[i] is MetricNode { Metric.Source: AlgorithmSource algorithm } algorithmMetric && AuthoringFunctionCatalog.HasInstrumentDependency(algorithm.AlgorithmId) && string.Equals(algorithm.InstrumentSlot, slot, StringComparison.OrdinalIgnoreCase)) nodes.Add($"{path}[{i}] {algorithmMetric.Metric.Name}");
                if (children[i] is RepeatNode repeat) Walk(repeat.Children, $"{path}[{i}].Children");
            }
        }
        Walk(draft.Measure, "Measure");
        if (draft.Cleanup.InstrumentSlots.Contains(slot, StringComparer.OrdinalIgnoreCase)) nodes.Add("Cleanup explicit instrument membership");
        if (draft.Sidecar.CleanupInstrumentSlots?.Contains(slot, StringComparer.OrdinalIgnoreCase) == true) nodes.Add("Sidecar explicit cleanup membership");
        if (draft.Cleanup.IncludeMeasureSlots) nodes.Add("Cleanup measure-slot inclusion policy");
        return nodes;
    }

    private static IReadOnlyList<string> RetargetList(IEnumerable<string> slots, string from, string to)
        => slots.Select(s => string.Equals(s, from, StringComparison.OrdinalIgnoreCase) ? to : s)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static HardwareTest.OpenTap.Host.ProgramSidecar RetargetSidecar(HardwareTest.OpenTap.Host.ProgramSidecar original, string from, string to)
    {
        var sidecar = PlanCompiler.CloneSidecar(original);
        if (sidecar.CleanupInstrumentSlots is { } slots) sidecar.CleanupInstrumentSlots = RetargetList(slots, from, to).ToArray();
        return sidecar;
    }

    private static bool WalkOpaque(IReadOnlyList<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode { Metric.Source: MeasureSource measure }:
                    if (!AuthoringFunctionCatalog.TryGet(measure.FunctionId, out var function) || function.IsAlgorithm) return true;
                    break;
                case MetricNode { Metric.Source: AlgorithmSource algorithm }:
                    if (!AuthoringFunctionCatalog.TryGet(algorithm.AlgorithmId, out var spec)
                        || !spec.IsAlgorithm
                        || (spec.NeedsInstrument && string.IsNullOrWhiteSpace(algorithm.InstrumentSlot))) return true;
                    break;
                case MetricNode { Metric.Source: ExpressionAlgorithm }:
                case MetricNode { Metric.Source: TransferFunctionAlgorithm }:
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
            MetricNode { Metric.Source: AlgorithmSource algorithm } metric
                when AuthoringFunctionCatalog.HasInstrumentDependency(algorithm.AlgorithmId) && string.Equals(algorithm.InstrumentSlot, from, StringComparison.OrdinalIgnoreCase)
                => metric with
                {
                    Metric = metric.Metric with { Source = algorithm with { InstrumentSlot = to } },
                },
            RepeatNode repeat => repeat with { Children = RetargetMeasure(repeat.Children, from, to) },
            _ => node,
        }).ToArray();
}
