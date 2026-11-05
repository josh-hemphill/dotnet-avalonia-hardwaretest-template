using HardwareTest.Core.BenchReferences;

namespace HardwareTest.Authoring;

/// Authoring validation preserves incomplete source while refusing invalid deployed rules.
public static class AuthoringReferenceRules
{
    public sealed record Finding(string Message, Guid NodeId, string Field)
    {
        public string? RuleId { get; init; }
    }

    public static string ToleranceField(BenchReferenceRule rule) => "ReferenceTolerance:" + rule.RuleId;
    public static string BaseField(string field) => field.StartsWith("Reference", StringComparison.Ordinal) ? field.Split(':', 2)[0] : field;
    public static string? FieldRuleId(string field) => field.StartsWith("Reference", StringComparison.Ordinal) && field.IndexOf(':') is var colon && colon >= 0 ? field[(colon + 1)..] : null;
    public static bool TryIncompleteTolerance(ProgramDraft draft, BenchReferenceRule rule, out string text)
    {
        if (draft.AuthoringState.IncompleteNumericText.TryGetValue(AuthoringDocumentState.FieldKey(rule.ProducerStepId, ToleranceField(rule)), out text!)) return true;
        // Early schema4 drafts stored one measurement-wide entry. It belongs to
        // the original first criterion only; never leak it to another criterion.
        return draft.Sidecar.ReferenceRules?.FirstOrDefault(item => item is not null && item.ProducerStepId == rule.ProducerStepId)?.RuleId == rule.RuleId
            && draft.AuthoringState.IncompleteNumericText.TryGetValue(AuthoringDocumentState.FieldKey(rule.ProducerStepId, "ReferenceTolerance"), out text!);
    }

    public static IReadOnlyList<Finding> Validate(ProgramDraft draft)
    {
        var findings = new List<Finding>();
        var metrics = Metrics(draft.Measure).ToArray();
        var excluded = AuthoringFormulaDeployment.ExcludedNodes(draft);
        var rules = draft.Sidecar.ReferenceRules ?? [];
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                findings.Add(new("A reference criterion is empty. Remove it or configure its measurement.", Guid.Empty, "ReferenceKey"));
                continue;
            }
            void Add(string message, Guid nodeId, string field) => findings.Add(new(message, nodeId, field) { RuleId = rule.RuleId });
            var matching = metrics.Where(metric => metric.NodeId == rule.ProducerStepId).ToArray();
            var node = matching.Length == 1 ? matching[0] : null;
            if (node is null || excluded.Contains(rule.ProducerStepId))
            {
                Add("The reference criterion's measurement is missing or excluded from deployment. Remove the criterion or bind a deployed measurement.", rule.ProducerStepId, "ReferenceKey");
                continue;
            }
            if (string.IsNullOrWhiteSpace(rule.RuleId) || rules.Count(item => item is not null && string.Equals(item.RuleId, rule.RuleId, StringComparison.OrdinalIgnoreCase)) != 1)
                Add("Reference rule identity is invalid. Disable and re-enable this measurement's reference requirement.", node.NodeId, "ReferenceRuleIdentity");
            if (string.IsNullOrWhiteSpace(rule.ReferenceKey))
                Add("Name the reference input for this measurement.", node.NodeId, "ReferenceKey");
            if (string.IsNullOrWhiteSpace(node.Metric.YUnit))
                Add("Set the measurement Y unit (1 if dimensionless), then rebind this reference criterion before compiling.", node.NodeId, "ReferenceMeasurementUnit");
            else if (!string.Equals(rule.MetricKey, node.Metric.ChannelKey, StringComparison.Ordinal)
                || !string.Equals(rule.Unit, node.Metric.YUnit, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(rule.Unit))
                Add("Measurement identity changed. Use the current measurement identity to rebind this criterion before compiling.", node.NodeId, "ReferenceTarget");
            if (rule.Comparison is not ("absolute" or "percent"))
                Add("Choose absolute difference or relative difference for the reference criterion.", node.NodeId, "ReferenceComparison");
            if (rule.Tolerance is not { } tolerance || !double.IsFinite(tolerance) || tolerance < 0)
                Add("Enter a finite, nonnegative reference tolerance. The boundary is inclusive.", node.NodeId, "ReferenceTolerance");
        }
        return findings;
    }

    public static void RequireValid(ProgramDraft draft)
    {
        if (Validate(draft).FirstOrDefault() is { } finding)
            throw new AuthoringWorkspaceException($"REFERENCE_CRITERION: {finding.Message}");
    }

    internal static IEnumerable<MetricNode> Metrics(IEnumerable<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is MetricNode metric) yield return metric;
            var children = node switch { SectionNode section => section.Children, RepeatNode repeat => repeat.Children, _ => null };
            if (children is not null)
                foreach (var child in Metrics(children)) yield return child;
        }
    }
}
