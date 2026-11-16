using System.Globalization;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.BenchReferences;

/// Numeric capture, compatibility and final operator acceptance; no file/catalog defaults are applied here.
public static class BenchReferenceEvaluator
{
    public static IReadOnlyList<string> ValidateRules(IEnumerable<BenchReferenceRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var result = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                result.Add("A reference rule is missing.");
                continue;
            }
            var error = RuleError(rule);
            if (error is not null) result.Add(error);
            if (!ids.Add(rule.RuleId)) result.Add($"Reference rule ID '{rule.RuleId}' is duplicated.");
        }
        return result;
    }

    public static BenchReferenceRule CloneRule(BenchReferenceRule rule) => rule with { };
    public static BenchReferenceUse CloneUse(BenchReferenceUse use)
        => use with { Rule = use.Rule with { }, ReferenceValue = use.ReferenceValue with { }, Context = use.Context with { } };
    public static BenchReferenceProfile CloneProfile(BenchReferenceProfile profile)
        => profile with { Context = profile.Context with { }, Values = profile.Values.Select(value => value with { }).ToList() };

    public static BenchReferenceProfile Capture(
        string name,
        TestRunRecord source,
        IEnumerable<BenchReferenceRule> rules,
        DateTimeOffset now,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var selected = rules.Select(CloneRule).ToArray();
        RequireValidRules(selected);
        if (selected.Length == 0) throw new InvalidOperationException("Select at least one declared reference metric to capture.");
        if (source.IsSchemaReadOnly || source.SchemaVersion != SchemaVersions.TestRunRecord)
            throw new InvalidOperationException("Unsupported or legacy source cannot qualify a bench reference.");
        if (source.Result != RunResult.Passed || source.CompletedAt is null || source.CompletedAt.Value < source.StartedAt)
            throw new InvalidOperationException("Only a successfully completed acquisition or full run can qualify a bench reference.");
        if (source.ExecutionScope is not (RunExecutionScopes.ReferenceAcquisition or RunExecutionScopes.FullSuite))
            throw new InvalidOperationException("Selected production results cannot qualify a reference; use explicit reference acquisition for the covered metrics.");
        if (string.IsNullOrWhiteSpace(source.RunId) || string.IsNullOrWhiteSpace(source.DutSerial)
            || string.IsNullOrWhiteSpace(source.OperatorName) || source.StartedAt == default)
            throw new InvalidOperationException("Reference source needs its original run, golden DUT, operator and acquisition timestamps.");
        var context = source.ReferenceContext ?? throw new InvalidOperationException("The source has no recorded reference context. Acquire it again with the current application.");
        var contextError = ContextError(context);
        if (contextError is not null) throw new InvalidOperationException(contextError);
        if (!string.Equals(source.PlanId, context.PlanId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source program identity disagrees with its recorded reference context.");
        if (expiresAt is { } expiry && expiry <= now)
            throw new InvalidOperationException("Reference expiry must be later than the capture time.");
        var declaredErrors = ValidateRules(source.ReferenceRules);
        if (declaredErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", declaredErrors));
        if (source.ReferenceBackendResult is { } backendResult && backendResult != RunResult.Passed)
            throw new InvalidOperationException("The source backend did not complete successfully; it cannot qualify a golden reference.");
        if (source.ExecutionScope == RunExecutionScopes.FullSuite && source.ReferenceRules.Count > 0
            && (source.ReferenceUses.Count != source.ReferenceRules.Count
                || source.ReferenceEvaluations.Count != source.ReferenceRules.Count
                || source.ReferenceEvaluations.Any(evaluation => !evaluation.Passed)
                || source.ReferenceRules.Any(rule => !source.ReferenceUses.Any(use => use.Rule == rule)
                    || !source.ReferenceEvaluations.Any(evaluation => evaluation.RuleId == rule.RuleId && evaluation.Passed))))
            throw new InvalidOperationException("The production source has incomplete reference acceptance evidence. Use a qualified acquisition or complete accepted run.");
        foreach (var rule in selected)
        {
            if (!source.ReferenceRules.Any(declared => declared == rule))
                throw new InvalidOperationException($"Reference metric '{rule.MetricKey}' was not declared in the captured source. Capture the intended program again.");
        }

        var coveredSamples = CurrentSamples(source);
        var values = new List<BenchReferenceValue>();
        foreach (var rule in selected)
        {
            if (values.Any(value => Matches(value, rule))) continue;
            var (mean, count) = Mean(coveredSamples, rule);
            values.Add(new BenchReferenceValue
            {
                ReferenceKey = rule.ReferenceKey,
                MetricKey = rule.MetricKey,
                ProducerStepId = rule.ProducerStepId,
                Unit = rule.Unit,
                Value = mean,
                SampleCount = count,
            });
        }
        var profile = new BenchReferenceProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            CreatedAt = now.ToUniversalTime(),
            ExpiresAt = expiresAt?.ToUniversalTime(),
            SourceRunId = source.RunId,
            SourceDutSerial = source.DutSerial,
            SourceOperator = source.OperatorName,
            SourceStartedAt = source.StartedAt.ToUniversalTime(),
            SourceCompletedAt = source.CompletedAt.Value.ToUniversalTime(),
            SourceScope = source.ExecutionScope!,
            SourceSelectedStepPath = source.SelectedStepPath,
            Context = context with { },
            Values = values,
        };
        return profile with { ContentSha256 = BenchReferenceIdentity.ComputeProfileHash(profile) };
    }

    /// Null means compatible. Retirement must separately be checked by the store at acceptance.
    public static string? CompatibilityError(
        BenchReferenceRule rule,
        BenchReferenceProfile profile,
        BenchReferenceContext context,
        DateTimeOffset now)
    {
        var ruleError = RuleError(rule);
        if (ruleError is not null) return ruleError;
        var error = ProfileError(profile) ?? ContextError(context);
        if (error is not null) return error;
        if (profile.ExpiresAt is { } expiry && now >= expiry)
            return $"Reference '{profile.Name}' has expired; acquire or select a current reference.";
        var expected = profile.Context;
        if (!string.Equals(expected.PlanId, context.PlanId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expected.PlanSha256, context.PlanSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expected.SidecarSha256, context.SidecarSha256, StringComparison.OrdinalIgnoreCase))
            return "Reference belongs to a different program revision; requalify it for the loaded plan and sidecar.";
        if (!string.Equals(expected.VisaMode, context.VisaMode, StringComparison.Ordinal))
            return "Reference VISA mode does not match this bench. Simulated and physical references cannot be substituted.";
        if (!string.Equals(expected.ConfigurationSha256, context.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
            return "Reference bench configuration has changed; requalify the reference.";
        var values = profile.Values.Where(value => Matches(value, rule)).ToArray();
        if (values.Length != 1)
            return $"Reference does not contain exactly one compatible '{rule.ReferenceKey}' value for '{rule.MetricKey}', producer and unit {rule.Unit}.";
        if (rule.Comparison == BenchReferenceComparisons.Percent && values[0].Value == 0)
            return "A zero reference cannot be used with percent tolerance. Select a nonzero reference or author an absolute rule.";
        return null;
    }

    public static BenchReferenceUse Freeze(
        BenchReferenceRule rule,
        BenchReferenceProfile profile,
        BenchReferenceContext context,
        DateTimeOffset now)
    {
        var error = CompatibilityError(rule, profile, context, now);
        if (error is not null) throw new InvalidOperationException(error);
        return new BenchReferenceUse
        {
            Rule = CloneRule(rule),
            ReferenceId = profile.Id,
            ReferenceRevision = profile.Revision,
            ReferenceName = profile.Name,
            ReferenceSha256 = profile.ContentSha256,
            ReferenceValue = profile.Values.Single(value => Matches(value, rule)) with { },
            Context = context with { },
            SourceRunId = profile.SourceRunId,
            SourceDutSerial = profile.SourceDutSerial,
            SourceOperator = profile.SourceOperator,
            SourceStartedAt = profile.SourceStartedAt,
            SourceCompletedAt = profile.SourceCompletedAt,
            SourceScope = profile.SourceScope,
            SourceSelectedStepPath = profile.SourceSelectedStepPath,
            CapturedAt = profile.CreatedAt,
            AcceptedAt = now.ToUniversalTime(),
            ExpiresAt = profile.ExpiresAt,
        };
    }

    /// Evaluates only the run-owned inputs. Later expiry/retirement never changes accepted execution.
    public static IReadOnlyList<BenchReferenceEvaluation> Evaluate(TestRunRecord run, IEnumerable<BenchReferenceUse> uses)
    {
        var frozen = uses.ToArray();
        var declared = run.ReferenceRules ?? throw new InvalidOperationException("Declared reference rules are missing.");
        var evaluations = new List<BenchReferenceEvaluation>();
        foreach (var error in ValidateRules(declared)) evaluations.Add(Unavailable(null, error));
        var validUses = new List<BenchReferenceUse>();
        foreach (var rule in declared)
        {
            if (rule is null) continue;
            var matching = frozen.Where(use => use?.Rule is not null
                && string.Equals(use.Rule.RuleId, rule.RuleId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length != 1)
            {
                evaluations.Add(Unavailable(rule, "Exactly one frozen reference input is required; the selected input is missing or duplicated."));
                continue;
            }
            if (matching[0].Rule != rule)
            {
                evaluations.Add(Unavailable(rule, "The frozen reference input does not match the declared acceptance rule."));
                continue;
            }
            validUses.Add(matching[0]);
        }
        foreach (var use in frozen)
        {
            if (use?.Rule is null || !declared.Any(rule => rule is not null
                    && string.Equals(rule.RuleId, use.Rule.RuleId, StringComparison.OrdinalIgnoreCase)))
                evaluations.Add(Unavailable(use?.Rule, "A frozen reference input has no applicable declared acceptance rule."));
        }
        if (validUses.Count > 0) evaluations.AddRange(Evaluate(CurrentSamples(run), validUses));
        return evaluations;
    }

    /// Preserved samples outside this invocation's time/scope are display history, never acceptance inputs.
    public static IReadOnlyList<StoredSample> CurrentSamples(TestRunRecord run)
    {
        if (run.StartedAt == default || run.CompletedAt is null || run.CompletedAt.Value < run.StartedAt)
            throw new InvalidOperationException("Reference acceptance requires a completed acquisition interval.");
        if (run.Samples is null || run.Samples.Any(sample => sample is null))
            throw new InvalidOperationException("Reference acquisition samples are missing or malformed.");
        var selectedPath = run.SelectedStepPath?.TrimEnd('/');
        return run.Samples.Where(sample => sample.Timestamp >= run.StartedAt && sample.Timestamp <= run.CompletedAt.Value
                && (string.IsNullOrWhiteSpace(selectedPath)
                    || string.Equals(sample.StepPath, selectedPath, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(sample.StepPath)
                        && sample.StepPath.StartsWith(selectedPath + "/", StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    /// For callers with an already bounded current-run sample list (for example an explicit preview).
    public static IReadOnlyList<BenchReferenceEvaluation> Evaluate(
        IEnumerable<StoredSample> samples,
        IEnumerable<BenchReferenceUse> uses)
    {
        var snapshot = samples.ToArray();
        var result = new List<BenchReferenceEvaluation>();
        foreach (var use in uses)
        {
            var rule = use.Rule;
            var evaluation = new BenchReferenceEvaluation
            {
                RuleId = rule.RuleId,
                MetricKey = rule.MetricKey,
                ReferenceId = use.ReferenceId,
                Unit = rule.Unit,
                Comparison = rule.Comparison,
                Tolerance = rule.Tolerance,
                ReferenceValue = use.ReferenceValue.Value,
            };
            try
            {
                var error = RuleError(rule);
                if (error is not null) throw new InvalidOperationException(error);
                if (!Matches(use.ReferenceValue, rule) || !ValidValue(use.ReferenceValue))
                    throw new InvalidOperationException("The frozen reference value does not match its declared metric, producer or unit.");
                if (rule.Comparison == BenchReferenceComparisons.Percent && use.ReferenceValue.Value == 0)
                    throw new InvalidOperationException("Percent comparison cannot use a zero reference.");
                var (mean, _) = Mean(snapshot, rule);
                var deviation = Math.Abs(mean - use.ReferenceValue.Value);
                if (rule.Comparison == BenchReferenceComparisons.Percent)
                {
                    if (double.IsFinite(deviation))
                        deviation = deviation / Math.Abs(use.ReferenceValue.Value) * 100;
                    else
                    {
                        // Opposite finite extremes can overflow subtraction although their
                        // relative difference is representable (for example, +Max vs -Max).
                        var scale = Math.Max(Math.Abs(mean), Math.Abs(use.ReferenceValue.Value));
                        deviation = Math.Abs(mean / scale - use.ReferenceValue.Value / scale)
                            / (Math.Abs(use.ReferenceValue.Value) / scale) * 100;
                    }
                }
                if (!double.IsFinite(deviation))
                    throw new InvalidOperationException("Reference deviation overflowed; this comparison cannot establish acceptance.");
                var passed = deviation <= rule.Tolerance!.Value;
                result.Add(evaluation with
                {
                    MeasuredValue = mean,
                    Deviation = deviation,
                    Passed = passed,
                    Message = $"{rule.MetricKey}: mean {Number(mean)} {rule.Unit}; reference {Number(use.ReferenceValue.Value)} {rule.Unit}; "
                        + $"deviation {Number(deviation)}{(rule.Comparison == BenchReferenceComparisons.Percent ? "%" : " " + rule.Unit)} "
                        + $"<= {Number(rule.Tolerance.Value)}: {(passed ? "Passed" : "Failed")}",
                });
            }
            catch (InvalidOperationException ex)
            {
                result.Add(evaluation with { Passed = false, Message = $"{rule.MetricKey}: {ex.Message}" });
            }
        }
        return result;
    }

    public static string? ProfileError(BenchReferenceProfile? profile)
    {
        if (profile is null) return "Reference profile is missing.";
        if (profile.SchemaVersion != SchemaVersions.BenchReferenceProfile)
            return $"Reference schema {profile.SchemaVersion} is unsupported; preserve it and use a compatible application.";
        if (!Guid.TryParseExact(profile.Id, "N", out _) || profile.Revision < 1 || string.IsNullOrWhiteSpace(profile.Name))
            return "Reference profile has invalid identity or name.";
        var contextError = ContextError(profile.Context);
        if (contextError is not null) return contextError;
        if (string.IsNullOrWhiteSpace(profile.SourceRunId) || string.IsNullOrWhiteSpace(profile.SourceDutSerial)
            || string.IsNullOrWhiteSpace(profile.SourceOperator) || profile.CreatedAt == default
            || profile.SourceStartedAt == default || profile.SourceCompletedAt < profile.SourceStartedAt
            || profile.SourceScope is not (RunExecutionScopes.ReferenceAcquisition or RunExecutionScopes.FullSuite))
            return "Reference source provenance is incomplete or ineligible.";
        if (profile.ExpiresAt is { } expiry && expiry <= profile.CreatedAt)
            return "Reference expiry must be later than its creation time.";
        if (profile.Values is null || profile.Values.Count == 0 || profile.Values.Any(value => !ValidValue(value)))
            return "Reference values require a finite mean, sample count, stable metric/producer and unit.";
        if (profile.Values.GroupBy(ValueIdentity).Any(group => group.Count() != 1))
            return "Reference contains ambiguous duplicate values.";
        if (!BenchReferenceIdentity.IsHash(profile.ContentSha256)
            || !string.Equals(profile.ContentSha256, BenchReferenceIdentity.ComputeProfileHash(profile), StringComparison.OrdinalIgnoreCase))
            return "Reference content hash does not match its saved values or provenance; preserve it and recapture.";
        return null;
    }

    public static string? ContextError(BenchReferenceContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.PlanId)
            || !BenchReferenceIdentity.IsHash(context.PlanSha256) || !BenchReferenceIdentity.IsHash(context.SidecarSha256)
            || !BenchReferenceIdentity.IsHash(context.ConfigurationSha256))
            return "Reference context requires an exact program, compiled plan/sidecar hashes and effective configuration hash.";
        if (context.VisaMode is not (BenchReferenceVisaModes.Simulated or BenchReferenceVisaModes.Physical))
            return "Reference context must record simulated or physical VISA provenance.";
        return null;
    }

    private static string? RuleError(BenchReferenceRule? rule)
    {
        if (rule is null || string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.ReferenceKey)
            || string.IsNullOrWhiteSpace(rule.MetricKey) || rule.ProducerStepId == Guid.Empty || string.IsNullOrWhiteSpace(rule.Unit))
            return "Reference rule needs stable rule/input/metric/producer identities and an explicit unit (use '1' for dimensionless).";
        if (rule.Comparison is not (BenchReferenceComparisons.Absolute or BenchReferenceComparisons.Percent))
            return $"Reference comparison '{rule.Comparison}' is unsupported.";
        if (rule.Tolerance is not { } tolerance || !double.IsFinite(tolerance) || tolerance < 0)
            return "Reference tolerance must be a finite nonnegative number.";
        return null;
    }

    private static void RequireValidRules(IEnumerable<BenchReferenceRule> rules)
    {
        var errors = ValidateRules(rules);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static bool Matches(BenchReferenceValue value, BenchReferenceRule rule)
        => string.Equals(value.ReferenceKey, rule.ReferenceKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.MetricKey, rule.MetricKey, StringComparison.OrdinalIgnoreCase)
            && value.ProducerStepId == rule.ProducerStepId
            && string.Equals(value.Unit, rule.Unit, StringComparison.Ordinal);

    private static bool ValidValue(BenchReferenceValue? value)
        => value is not null && !string.IsNullOrWhiteSpace(value.ReferenceKey) && !string.IsNullOrWhiteSpace(value.MetricKey)
            && value.ProducerStepId != Guid.Empty && !string.IsNullOrWhiteSpace(value.Unit)
            && value.SampleCount > 0 && value.Reduction == "arithmetic-mean" && double.IsFinite(value.Value);

    private static string ValueIdentity(BenchReferenceValue value)
        => string.Join('\0', value.ReferenceKey.ToUpperInvariant(), value.MetricKey.ToUpperInvariant(), value.ProducerStepId.ToString("N"), value.Unit);

    private static (double Mean, int Count) Mean(IEnumerable<StoredSample> samples, BenchReferenceRule rule)
    {
        var snapshot = samples.ToArray();
        if (snapshot.Any(sample => sample is null)) throw new InvalidOperationException("Measurement sample data is malformed.");
        var candidates = snapshot.Where(sample => sample is not null
                && string.Equals(sample.EffectiveMetricKey, rule.MetricKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Any(sample => sample.ProducerStepId is null))
            throw new InvalidOperationException("Measurement producer identity is missing or ambiguous.");
        var matching = candidates.Where(sample => sample.ProducerStepId == rule.ProducerStepId).ToArray();
        if (matching.Length == 0)
            throw new InvalidOperationException("This acquisition has no samples for the declared metric and producer.");
        if (matching.Any(sample => !double.IsFinite(sample.Value)))
            throw new InvalidOperationException("Measurement contains a nonfinite sample.");
        if (matching.Any(sample => sample.Unit is null || !string.Equals(sample.Unit, rule.Unit, StringComparison.Ordinal)))
            throw new InvalidOperationException("Measurement units are missing, mixed or incompatible with the reference rule.");
        var scale = matching.Max(sample => Math.Abs(sample.Value));
        var mean = scale == 0 ? 0 : matching.Sum(sample => sample.Value / scale) / matching.Length * scale;
        if (!double.IsFinite(mean)) throw new InvalidOperationException("Measurement mean overflowed.");
        return (mean, matching.Length);
    }

    private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static BenchReferenceEvaluation Unavailable(BenchReferenceRule? rule, string message)
        => new()
        {
            RuleId = rule?.RuleId ?? "invalid-reference-contract",
            MetricKey = rule?.MetricKey ?? string.Empty,
            Unit = rule?.Unit ?? string.Empty,
            Comparison = rule?.Comparison ?? string.Empty,
            Tolerance = rule?.Tolerance is { } tolerance && double.IsFinite(tolerance) ? tolerance : null,
            Passed = false,
            Message = message,
        };
}
