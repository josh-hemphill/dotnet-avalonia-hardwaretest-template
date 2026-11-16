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
        if (target.Section is not null && target.Section is not ("Configure" or "Advanced" or "OperatorDisplay"))
            return Fallback("The structured section has no supported editor destination; opens program settings.");
        if (target.Field is null)
            return target.Section is null
                ? Fallback("No supported editor section was provided; opens program settings.")
                : new(target with { ProgramId = draft.PlanId, NodeId = id }, "Open step section",
                    "The source step and editor section are verified; no precise field was provided.");
        var metric = matches[0].Kind == SequenceRowKind.Metric
            ? (AuthoringSequence.ResolveMeasure(draft, matches[0].IndexPath) as MetricNode)?.Metric : null;
        var sourceField = AuthoringReferenceRules.BaseField(target.Field);
        var supported = metric is not null && (sourceField switch
        {
            "Threshold" => AuthoringCriteria.Requirements(metric)?.RequiresThreshold == true,
            "LimitLow" => AuthoringCriteria.Requirements(metric)?.RequiresBand == true,
            "ChannelKey" => true,
            "ReferenceKey" or "ReferenceComparison" or "ReferenceTolerance" or "ReferenceTarget" or "ReferenceMeasurementUnit" or "ReferenceRuleIdentity" => true,
            _ => HasVisibleSetting(metric, sourceField),
        });
        return supported
            ? new(target with
            {
                ProgramId = draft.PlanId,
                NodeId = id,
                Section = sourceField == "ReferenceMeasurementUnit" ? "OperatorDisplay" : target.Field == "ChannelKey" ? "Advanced" : "Configure"
            }, "Go to field",
                "Explicit field target resolved through the checked compiler source map and supported editor.")
            : Fallback("The structured field has no supported editor control; opens program settings.");
    }

    private static bool HasVisibleSetting(MetricDraft metric, string field)
    {
        var (id, settings, isAlgorithm) = metric.Source switch
        {
            MeasureSource measure => (measure.FunctionId, measure.Settings, false),
            AlgorithmSource algorithm => (algorithm.AlgorithmId, algorithm.Settings, true),
            _ => (string.Empty, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(), false)
        };
        if (!AuthoringFunctionCatalog.TryGet(id, out var function) || function.IsAlgorithm != isAlgorithm
            || AuthoringCriteria.IsRuntimeLimit(field) || field.Equals("Channel", StringComparison.OrdinalIgnoreCase)
            || AuthoringFunctionCatalog.ConsumesInputChannels(id)
                && new[] { "InputChannel", "ProducerStepId", "Unit" }.Contains(field, StringComparer.OrdinalIgnoreCase))
            return false;
        // Match the inspector's settings projection, including not-yet-edited defaults.
        // A similarly named field in another node or an unavailable plugin is not a destination.
        var projected = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (key, value) in PlanCompiler.FunctionSettings(id)) projected.TryAdd(key, value);
        }
        catch (AuthoringWorkspaceException)
        {
            // The editor can still show explicitly retained settings for a known unavailable adapter.
        }
        return projected.Keys.Contains(field, StringComparer.Ordinal);
    }
}
