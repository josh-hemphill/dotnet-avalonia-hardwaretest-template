using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Resolves supported editor destinations without changing the current selection.
internal static class AuthoringFindingNavigation
{
    internal sealed record Destination(PlanContractTarget Target, string Label, string Reason);

    internal static Destination Resolve(ProgramDraft draft, Guid? nodeId, PlanContractTarget? target)
    {
        Destination Fallback(string reason) => new(new(ProgramId: draft.PlanId, Section: "ProgramSettings"),
            "Open program settings", reason);
        if (nodeId is not { } id || target is null)
            return Fallback("No verified source field is available; opens program settings.");
        var matches = AuthoringSequence.Flatten(draft).Where(row => row.NodeId == id).ToArray();
        if (matches.Length != 1)
            return Fallback("The source node is unavailable or ambiguous; opens program settings.");
        if (target.Section is not null && target.Section is not ("Configure" or "Advanced"))
            return Fallback("The structured section has no supported editor destination; opens program settings.");
        if (target.Field is null)
            return target.Section is null
                ? Fallback("No supported editor section was provided; opens program settings.")
                : new(target with { ProgramId = draft.PlanId, NodeId = id }, "Open step section",
                    "The source step and editor section are verified; no precise field was provided.");
        var metric = matches[0].Kind == SequenceRowKind.Metric
            ? (AuthoringSequence.ResolveMeasure(draft, matches[0].IndexPath) as MetricNode)?.Metric : null;
        var supported = metric is not null && (target.Field switch
        {
            "Threshold" => AuthoringCriteria.Requirements(metric)?.RequiresThreshold == true,
            "LimitLow" => AuthoringCriteria.Requirements(metric)?.RequiresBand == true,
            "ChannelKey" => true,
            _ => false,
        });
        return supported
            ? new(target with
            {
                ProgramId = draft.PlanId,
                NodeId = id,
                Section = target.Field == "ChannelKey" ? "Advanced" : "Configure"
            }, "Go to field",
                "Explicit field target resolved through the checked compiler source map and supported editor.")
            : Fallback("The structured field has no supported editor control; opens program settings.");
    }
}
