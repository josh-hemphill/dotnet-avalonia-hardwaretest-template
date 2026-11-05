using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Tests.BenchReferences;

public sealed class BenchReferenceEvaluatorTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    internal static readonly Guid Producer = Guid.Parse("a1f9c625-40c5-47df-9ecb-390adfbd678b");

    internal static BenchReferenceRule Rule() => new()
    {
        RuleId = "rail-reference",
        ReferenceKey = "golden.rail",
        MetricKey = "rail.voltage",
        ProducerStepId = Producer,
        Unit = "V",
        Tolerance = 1,
    };

    internal static BenchReferenceContext Context() => new()
    {
        PlanId = "reference-test",
        PlanSha256 = BenchReferenceIdentity.HashText("compiled plan"),
        SidecarSha256 = BenchReferenceIdentity.HashText("compiled sidecar"),
        ConfigurationSha256 = BenchReferenceIdentity.ConfigurationHash([new("slot.DMM", "MOCK::DMM")]),
        VisaMode = BenchReferenceVisaModes.Simulated,
    };

    internal static TestRunRecord Source(double first = 5, double second = 7) => new()
    {
        RunId = "golden-source",
        PlanId = "reference-test",
        PlanName = "Reference acceptance",
        ExecutionScope = RunExecutionScopes.ReferenceAcquisition,
        DutSerial = "GOLDEN-1",
        OperatorName = "Bench technician",
        SessionId = "golden-session",
        StartedAt = Now.AddMinutes(-2),
        CompletedAt = Now.AddMinutes(-1),
        Result = RunResult.Passed,
        ReferenceContext = Context(),
        ReferenceRules = [Rule()],
        Samples = [Sample(first), Sample(second)],
    };

    internal static StoredSample Sample(double value) => new()
    {
        ProducerStepId = Producer,
        StepRunId = Guid.NewGuid(),
        Channel = "rail.voltage",
        MetricKey = "rail.voltage",
        Unit = "V",
        Value = value,
        StepPath = "Measure/Rail",
        Timestamp = Now.AddSeconds(-90),
    };

    [Fact]
    public void Same_actual_measurement_changes_verdict_with_the_frozen_reference_number()
    {
        var rule = Rule();
        var first = BenchReferenceEvaluator.Capture("Golden A", Source(), [rule], Now);
        var second = BenchReferenceEvaluator.Capture("Golden B", Source(15, 17), [rule], Now);
        var useA = BenchReferenceEvaluator.Freeze(rule, first, Context(), Now);
        var useB = BenchReferenceEvaluator.Freeze(rule, second, Context(), Now);
        var measured = Source(6, 8);

        var accepted = Assert.Single(BenchReferenceEvaluator.Evaluate(measured, [useA]));
        var rejected = Assert.Single(BenchReferenceEvaluator.Evaluate(measured, [useB]));
        Assert.True(accepted.Passed);
        Assert.False(rejected.Passed);
        Assert.Equal(7, accepted.MeasuredValue);
        Assert.Equal(7, rejected.MeasuredValue);
        Assert.Equal(6, accepted.ReferenceValue);
        Assert.Equal(16, rejected.ReferenceValue);
        Assert.NotEqual(accepted.ReferenceId, rejected.ReferenceId);
        Assert.Equal(rule, useA.Rule);

        // Caller/catalog DTO changes cannot redirect the accepted run-owned values.
        first.Values[0] = first.Values[0] with { Value = 1000 };
        Assert.Equal(6, useA.ReferenceValue.Value);
        Assert.True(Assert.Single(BenchReferenceEvaluator.Evaluate(measured, [useA])).Passed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("mismatched")]
    [InlineData("extra")]
    public void Required_acceptance_never_passes_with_missing_duplicate_or_misdirected_frozen_inputs(string mutation)
    {
        var rule = Rule();
        var profile = BenchReferenceEvaluator.Capture("Frozen selection", Source(), [rule], Now);
        var use = BenchReferenceEvaluator.Freeze(rule, profile, Context(), Now);
        BenchReferenceUse[] uses = mutation switch
        {
            "missing" => [],
            "duplicate" => [use, use],
            "mismatched" => [use with { Rule = rule with { Tolerance = 500 } }],
            _ => [use, use with { Rule = rule with { RuleId = "not-declared" } }],
        };
        var evaluations = BenchReferenceEvaluator.Evaluate(Source(6, 6), uses);
        Assert.NotEmpty(evaluations);
        Assert.Contains(evaluations, evaluation => !evaluation.Passed);
    }

    [Fact]
    public void Mean_covers_all_declared_producer_invocations_without_overflow()
    {
        var source = Source(double.MaxValue, double.MaxValue);
        source.Samples[0].LoopRunId = Guid.NewGuid(); source.Samples[0].IterationIndex = 1;
        source.Samples[1].LoopRunId = Guid.NewGuid(); source.Samples[1].IterationIndex = 2;
        var profile = BenchReferenceEvaluator.Capture("Repeated acquisition", source, [Rule()], Now);
        var value = Assert.Single(profile.Values);
        Assert.Equal(double.MaxValue, value.Value);
        Assert.Equal(2, value.SampleCount);
        Assert.Equal("arithmetic-mean", value.Reduction);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(199.99, false)]
    public void Percent_comparison_preserves_inclusive_boundary_when_finite_subtraction_overflows(double tolerance, bool expected)
    {
        var rule = Rule() with { Comparison = BenchReferenceComparisons.Percent, Tolerance = tolerance };
        var source = Source(-double.MaxValue, -double.MaxValue);
        source.ReferenceRules = [rule];
        var profile = BenchReferenceEvaluator.Capture("Negative extreme", source, [rule], Now);
        var use = BenchReferenceEvaluator.Freeze(rule, profile, Context(), Now);
        var evaluation = Assert.Single(BenchReferenceEvaluator.Evaluate([Sample(double.MaxValue)], [use]));
        Assert.Equal(200, evaluation.Deviation);
        Assert.Equal(expected, evaluation.Passed);
    }

    [Theory]
    [InlineData(RunResult.Failed)]
    [InlineData(RunResult.Error)]
    [InlineData(RunResult.Cancelled)]
    [InlineData(RunResult.Unknown)]
    public void Unsuccessful_source_never_qualifies_reference_values(RunResult result)
    {
        var source = Source(); source.Result = result;
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Invalid source", source, [Rule()], Now));
    }

    [Fact]
    public void Selected_production_is_not_reference_acquisition_and_unknown_legacy_context_is_not_upgraded()
    {
        var source = Source(); source.ExecutionScope = RunExecutionScopes.SelectedStep;
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Selected production", source, [Rule()], Now));
        source.ExecutionScope = RunExecutionScopes.ReferenceAcquisition; source.ReferenceContext = null;
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Unattributed", source, [Rule()], Now));
    }

    [Fact]
    public void A_production_pass_without_its_required_acceptance_evidence_is_not_a_qualified_golden_source()
    {
        var source = Source(); source.ExecutionScope = RunExecutionScopes.FullSuite;
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Incomplete acceptance", source, [Rule()], Now));
        source.ExecutionScope = RunExecutionScopes.ReferenceAcquisition;
        source.ReferenceBackendResult = RunResult.Failed;
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Contradictory backend", source, [Rule()], Now));
    }

    [Theory]
    [InlineData("prior-time")]
    [InlineData("outside-selection")]
    public void Preserved_history_samples_cannot_qualify_selected_acquisition_or_acceptance(string mutation)
    {
        var source = Source();
        var profile = BenchReferenceEvaluator.Capture("Valid current acquisition", source, [Rule()], Now);
        var use = BenchReferenceEvaluator.Freeze(Rule(), profile, Context(), Now);
        source.SelectedStepPath = "Measure/Rail";
        foreach (var sample in source.Samples)
        {
            if (mutation == "prior-time") sample.Timestamp = source.StartedAt.AddDays(-1);
            else sample.StepPath = "Measure/Unselected";
        }
        Assert.Empty(BenchReferenceEvaluator.CurrentSamples(source));
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Preserved data", source, [Rule()], Now));
        var evaluation = Assert.Single(BenchReferenceEvaluator.Evaluate(source, [use]));
        Assert.False(evaluation.Passed);
        Assert.Null(evaluation.MeasuredValue);
    }

    [Fact]
    public void Selected_acquisition_captures_only_the_declared_covered_metric()
    {
        var source = Source(); source.SelectedStepPath = "Measure/Rail";
        var unrelated = Sample(500); unrelated.StepPath = "Measure/Unselected"; source.Samples.Add(unrelated);
        var profile = BenchReferenceEvaluator.Capture("Selected rail", source, [Rule()], Now);
        Assert.Equal(6, Assert.Single(profile.Values).Value);
        Assert.Equal(2, Assert.Single(profile.Values).SampleCount);
        Assert.Equal("Measure/Rail", profile.SourceSelectedStepPath);
        Assert.Contains("Reference acquisition only", RunExecutionScopes.Describe(profile.SourceScope, profile.SourceSelectedStepPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("infinite")]
    [InlineData("mixed-unit")]
    [InlineData("missing-unit")]
    [InlineData("missing-producer")]
    [InlineData("wrong-producer")]
    public void Incomplete_nonfinite_or_incompatible_measurements_are_not_silently_reduced(string mutation)
    {
        var source = Source();
        switch (mutation)
        {
            case "nan": source.Samples[0].Value = double.NaN; break;
            case "infinite": source.Samples[0].Value = double.PositiveInfinity; break;
            case "mixed-unit": source.Samples[0].Unit = "mV"; break;
            case "missing-unit": source.Samples[0].Unit = null; break;
            case "missing-producer": source.Samples[0].ProducerStepId = null; break;
            default: source.Samples.ForEach(sample => sample.ProducerStepId = Guid.NewGuid()); break;
        }
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("Invalid data", source, [Rule()], Now));
    }

    [Theory]
    [InlineData("absolute", 1, 7)]
    [InlineData("percent", 50, 9)]
    public void Supported_comparisons_include_the_exact_tolerance_boundary(string comparison, double tolerance, double measured)
    {
        var rule = Rule() with { Comparison = comparison, Tolerance = tolerance };
        var source = Source(); source.ReferenceRules = [rule];
        var profile = BenchReferenceEvaluator.Capture("Boundary", source, [rule], Now);
        var use = BenchReferenceEvaluator.Freeze(rule, profile, Context(), Now);
        Assert.True(Assert.Single(BenchReferenceEvaluator.Evaluate([Sample(measured)], [use])).Passed);
    }

    [Fact]
    public void Percent_zero_reference_is_blocked_before_freeze_and_cannot_be_forced_into_a_pass()
    {
        var rule = Rule() with { Comparison = BenchReferenceComparisons.Percent };
        var source = Source(0, 0); source.ReferenceRules = [rule];
        var profile = BenchReferenceEvaluator.Capture("Zero", source, [rule], Now);
        Assert.Contains("zero", BenchReferenceEvaluator.CompatibilityError(rule, profile, Context(), Now), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Freeze(rule, profile, Context(), Now));
        var absolute = BenchReferenceEvaluator.Freeze(Rule(), profile, Context(), Now);
        Assert.False(Assert.Single(BenchReferenceEvaluator.Evaluate([Sample(0)], [absolute with { Rule = rule }])).Passed);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("sidecar")]
    [InlineData("configuration")]
    [InlineData("visa")]
    [InlineData("identity")]
    public void Context_changes_require_explicit_requalification(string change)
    {
        var profile = BenchReferenceEvaluator.Capture("Bound context", Source(), [Rule()], Now);
        var context = Context();
        context = change switch
        {
            "plan" => context with { PlanSha256 = BenchReferenceIdentity.HashText("changed plan") },
            "sidecar" => context with { SidecarSha256 = BenchReferenceIdentity.HashText("changed sidecar") },
            "configuration" => context with { ConfigurationSha256 = BenchReferenceIdentity.HashText("changed station") },
            "visa" => context with { VisaMode = BenchReferenceVisaModes.Physical },
            _ => context with { PlanId = "same-name-different-program" },
        };
        Assert.NotNull(BenchReferenceEvaluator.CompatibilityError(Rule(), profile, context, Now));
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Freeze(Rule(), profile, context, Now));
    }

    [Fact]
    public void Expiry_is_inclusive_at_acceptance_but_does_not_rewrite_a_frozen_run()
    {
        var expiry = Now.AddMinutes(1);
        var profile = BenchReferenceEvaluator.Capture("Timed", Source(), [Rule()], Now, expiry);
        var use = BenchReferenceEvaluator.Freeze(Rule(), profile, Context(), expiry.AddTicks(-1));
        Assert.NotNull(BenchReferenceEvaluator.CompatibilityError(Rule(), profile, Context(), expiry));
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Freeze(Rule(), profile, Context(), expiry));
        Assert.True(Assert.Single(BenchReferenceEvaluator.Evaluate([Sample(6)], [use])).Passed);
        Assert.Equal(expiry, use.ExpiresAt);
    }

    [Fact]
    public void Tampering_with_numeric_content_is_not_accepted_under_an_old_hash()
    {
        var profile = BenchReferenceEvaluator.Capture("Protected", Source(), [Rule()], Now);
        profile.Values[0] = profile.Values[0] with { Value = 123 };
        Assert.Contains("hash", BenchReferenceEvaluator.ProfileError(profile), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Freeze(Rule(), profile, Context(), Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Incomplete_or_invalid_tolerance_remains_a_deployment_block(double? tolerance)
        => Assert.NotEmpty(BenchReferenceEvaluator.ValidateRules([Rule() with { Tolerance = tolerance }]));

    [Fact]
    public void Configuration_hash_is_order_stable_and_length_delimited()
    {
        Assert.Equal(BenchReferenceIdentity.ConfigurationHash([new("a", "1"), new("b", "2")]),
            BenchReferenceIdentity.ConfigurationHash([new("b", "2"), new("a", "1")]));
        Assert.NotEqual(BenchReferenceIdentity.ConfigurationHash([new("a", "b:c")]),
            BenchReferenceIdentity.ConfigurationHash([new("a:b", "c")]));
        Assert.Throws<InvalidOperationException>(() => BenchReferenceIdentity.ConfigurationHash([new("a", "1"), new("a", "2")]));
    }

    [Fact]
    public void Acquisition_evidence_without_selected_references_exposes_exact_source_context_backend_and_coverage()
    {
        var source = Source(); source.ReferenceBackendResult = RunResult.Passed;
        var prior = Sample(900); prior.Timestamp = source.StartedAt.AddDays(-1); source.Samples.Add(prior);
        var evidence = BenchReferenceEvidence.Describe(source);
        Assert.Contains("Backend execution result: Passed", evidence, StringComparison.Ordinal);
        Assert.Contains("not production acceptance", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(source.RunId, evidence, StringComparison.Ordinal);
        Assert.Contains(source.DutSerial!, evidence, StringComparison.Ordinal);
        Assert.Contains(source.OperatorName!, evidence, StringComparison.Ordinal);
        Assert.Contains(source.ReferenceContext!.PlanSha256, evidence, StringComparison.Ordinal);
        Assert.Contains(source.ReferenceContext.SidecarSha256, evidence, StringComparison.Ordinal);
        Assert.Contains(source.ReferenceContext.ConfigurationSha256, evidence, StringComparison.Ordinal);
        Assert.Contains("VISA backend: simulated", evidence, StringComparison.Ordinal);
        Assert.Contains("arithmetic mean", evidence, StringComparison.Ordinal);
        Assert.Contains("Observed current-scope samples: 2", evidence, StringComparison.Ordinal);
        Assert.Contains(Producer.ToString("D"), evidence, StringComparison.Ordinal);
        Assert.Empty(source.ReferenceUses);
    }
}
