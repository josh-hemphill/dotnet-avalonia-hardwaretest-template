using System.Text;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

/// Recording-owned numbers are replay inputs only; no bench catalog or selection is changed.
public static class AuthoringReferenceReview
{
    public static string Describe(ProgramDraft? proposed, TestRunRecord recording)
    {
        if (recording.IsSchemaReadOnly)
            return "This recording has an unsupported schema. Reference preview is unavailable; its source bytes are preserved.";
        if (recording.ReferenceUses.Count == 0)
            return "This recording has no frozen bench reference inputs. It cannot supply a reference-based preview.";
        var text = new StringBuilder($"Recorded reference inputs — preview only\nRun: {recording.RunId}\nScope: {recording.ExecutionScopeText}\n");
        foreach (var use in recording.ReferenceUses)
        {
            text.AppendLine();
            text.AppendLine(BenchReferenceEvidence.DescribeUse(use));
            var matches = proposed?.Sidecar.ReferenceRules?.Where(rule => rule.RuleId == use.Rule.RuleId).ToArray() ?? [];
            if (matches.Length != 1)
            {
                text.AppendLine("Proposed preview: this recorded criterion is absent or ambiguous in the current draft.");
                continue;
            }
            var current = matches[0];
            var currentDraft = proposed!;
            if (currentDraft.Sidecar.ReferenceRules!.Where(rule => rule.ProducerStepId == current.ProducerStepId)
                .Any(rule => AuthoringReferenceRules.TryIncompleteTolerance(currentDraft, rule, out _)))
            {
                text.AppendLine("Proposed preview unavailable: finish the current reference tolerance before previewing. The draft retains your unfinished value.");
                continue;
            }
            var invalid = AuthoringReferenceRules.Validate(currentDraft).Where(finding => finding.NodeId == current.ProducerStepId).ToArray();
            if (invalid.Length > 0)
            {
                text.AppendLine("Proposed preview unavailable: " + string.Join(" ", invalid.Select(finding => finding.Message)));
                continue;
            }
            text.AppendLine("Proposed rule: " + BenchReferenceEvidence.DescribeRule(current));
            if (current.ProducerStepId != use.Rule.ProducerStepId
                || !string.Equals(current.MetricKey, use.Rule.MetricKey, StringComparison.Ordinal)
                || !string.Equals(current.ReferenceKey, use.Rule.ReferenceKey, StringComparison.Ordinal)
                || !string.Equals(current.Unit, use.Rule.Unit, StringComparison.Ordinal))
            {
                text.AppendLine("Proposed preview unavailable: reference input or measurement identity changed. Reacquire/requalify on the bench after compiling.");
                continue;
            }
            var result = BenchReferenceEvaluator.Evaluate(BenchReferenceEvaluator.CurrentSamples(recording), [use with { Rule = current }]).Single();
            text.AppendLine($"Proposed preview: {(result.Passed ? "Pass" : "Fail")} · {result.Message}");
        }
        text.AppendLine();
        text.AppendLine("Recorded values stay frozen. This evaluates the proposed criterion against the selected recording; it neither executes hardware nor approves a bench reference. Compile, hand off and requalify changed artifacts before production execution.");
        return text.ToString().TrimEnd();
    }

}
