using System.Globalization;
using System.Text;
using HardwareTest.Core.Runs;

namespace HardwareTest.Core.BenchReferences;

/// Shared human-readable evidence for Results, authoring recordings, reports and exported diagnostics.
public static class BenchReferenceEvidence
{
    public static string DescribeContext(BenchReferenceContext? context)
        => context is null ? "Program/bench reference context: Not recorded."
            : string.Join(Environment.NewLine,
                $"Program: {context.PlanId}; VISA backend: {context.VisaMode}",
                $"Plan SHA-256: {context.PlanSha256}",
                $"Sidecar SHA-256: {context.SidecarSha256}",
                $"Configuration SHA-256: {context.ConfigurationSha256}");

    public static string DescribeRule(BenchReferenceRule rule)
    {
        var tolerance = rule.Tolerance?.ToString("G17", CultureInfo.InvariantCulture) ?? "not configured";
        return rule.Comparison == BenchReferenceComparisons.Percent
            ? $"{rule.MetricKey}: 100 × |mean − reference| / |reference| ≤ {tolerance}% (reference must be nonzero)"
            : $"{rule.MetricKey}: |mean − reference| ≤ {tolerance} {rule.Unit}";
    }

    public static string DescribeUse(BenchReferenceUse use)
    {
        var value = use.ReferenceValue;
        return string.Join(Environment.NewLine,
            $"Rule: {use.Rule.RuleId} — {DescribeRule(use.Rule)}",
            $"Reference: {use.ReferenceName}",
            $"Reference ID: {use.ReferenceId}; revision {use.ReferenceRevision}",
            $"Reference SHA-256: {use.ReferenceSha256}",
            $"Reference input: {value.ReferenceKey}; metric {value.MetricKey}; producer {value.ProducerStepId:D}",
            $"Value used: {value.Value.ToString("G17", CultureInfo.InvariantCulture)} {value.Unit}; {value.Reduction}; {value.SampleCount} samples",
            $"Source run: {use.SourceRunId}; golden DUT: {use.SourceDutSerial}; operator: {use.SourceOperator}",
            $"Source started: {use.SourceStartedAt:O}; completed: {use.SourceCompletedAt:O}",
            $"Source scope: {RunExecutionScopes.Describe(use.SourceScope, use.SourceSelectedStepPath)}",
            $"Captured: {use.CapturedAt:O}; expires: {(use.ExpiresAt is { } expiry ? expiry.ToString("O", CultureInfo.InvariantCulture) : "No expiry")}",
            $"Accepted for execution: {use.AcceptedAt:O}",
            DescribeContext(use.Context));
    }

    public static string Describe(TestRunRecord run)
    {
        if (run.ReferenceUses.Count == 0 && run.ReferenceRules.Count == 0
            && run.ExecutionScope != RunExecutionScopes.ReferenceAcquisition)
            return "No bench reference inputs recorded."
                + (run.ReferenceBackendResult is { } backend ? $"\nBackend execution result: {backend}" : string.Empty)
                + (run.ReferenceContext is null ? string.Empty : "\n" + DescribeContext(run.ReferenceContext));
        var text = new StringBuilder();
        text.AppendLine($"Backend execution result: {run.ReferenceBackendResult?.ToString() ?? "Not recorded"}");
        if (run.ExecutionScope == RunExecutionScopes.ReferenceAcquisition)
        {
            text.AppendLine("REFERENCE ACQUISITION ONLY — dependent reference acceptance was not evaluated. This is not production acceptance.");
            text.AppendLine($"Source run: {run.RunId}; golden DUT: {run.DutSerial ?? "Not recorded"}; operator: {run.OperatorName ?? "Not recorded"}");
            text.AppendLine($"Started: {run.StartedAt:O}; completed: {(run.CompletedAt is { } completed ? completed.ToString("O", CultureInfo.InvariantCulture) : "Not recorded")}");
            text.AppendLine($"Source scope: {run.ExecutionScopeText}");
            text.AppendLine(DescribeContext(run.ReferenceContext));
            text.AppendLine("Reduction: arithmetic mean of all current-acquisition samples for the declared metric and producer, including repeat invocations.");
            IReadOnlyList<StoredSample>? covered = null;
            try { covered = BenchReferenceEvaluator.CurrentSamples(run); }
            catch (InvalidOperationException error) { text.AppendLine($"Coverage unavailable: {error.Message}"); }
            foreach (var rule in run.ReferenceRules)
            {
                text.AppendLine(DescribeRule(rule));
                text.AppendLine($"Input: {rule.ReferenceKey}; metric {rule.MetricKey}; producer {rule.ProducerStepId:D}; unit {rule.Unit}");
                if (covered is not null)
                {
                    var count = covered.Count(sample => sample.ProducerStepId == rule.ProducerStepId
                        && string.Equals(sample.EffectiveMetricKey, rule.MetricKey, StringComparison.OrdinalIgnoreCase));
                    text.AppendLine($"Observed current-scope samples: {count.ToString(CultureInfo.InvariantCulture)} (capture qualification is checked separately).");
                }
            }
        }
        else
        {
            text.AppendLine(run.ReferenceUses.Count == 0 || run.ReferenceEvaluations.Count != run.ReferenceUses.Count
                ? "Reference acceptance: incomplete or not recorded."
                : run.ReferenceEvaluations.All(evaluation => evaluation.Passed)
                    ? "Reference acceptance: Passed"
                    : "Reference acceptance: Failed");
        }
        foreach (var use in run.ReferenceUses)
        {
            text.AppendLine().AppendLine(DescribeUse(use));
            foreach (var evaluation in run.ReferenceEvaluations.Where(evaluation => evaluation.RuleId == use.Rule.RuleId))
                text.AppendLine(evaluation.Message);
        }
        return text.ToString().TrimEnd();
    }
}
