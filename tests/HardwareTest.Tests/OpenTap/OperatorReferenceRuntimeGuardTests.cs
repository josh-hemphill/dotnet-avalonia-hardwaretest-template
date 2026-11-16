using System.Text.Json;
using System.Text.Json.Nodes;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.Tests.Fixtures;
using OpenTap;
using Serilog;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class OperatorReferenceRuntimeGuardTests
{
    [Fact]
    public void Strict_reference_program_validation_accepts_supported_rules_and_does_not_demand_measurement_presentation_on_the_guard()
    {
        using var temp = new TempDataDirectory();
        var (path, _, _) = SaveContractProgram(temp);
        var report = PlanContractValidator.ValidateFile(path, new PlanContractOptions { Strict = true, EnablePhysicalExecution = false });
        Assert.False(report.HasErrors, string.Join(Environment.NewLine, report.Findings.Select(finding => finding.Code + ": " + finding.Message)));
        Assert.Empty(report.Findings);
    }

    [Theory]
    [InlineData("missing-guard")]
    [InlineData("disabled-guard")]
    [InlineData("missing-rules")]
    [InlineData("invalid-tolerance")]
    [InlineData("producer")]
    [InlineData("metric")]
    [InlineData("unit")]
    [InlineData("disabled-parent")]
    [InlineData("unknown-rule-field")]
    public void Strict_handoff_validation_refuses_mixed_or_unsupported_reference_semantics_without_rewriting(string mutation)
    {
        using var temp = new TempDataDirectory();
        var (path, plan, sidecar) = SaveContractProgram(temp);
        var sidecarPath = Path.ChangeExtension(path, ".program.json");
        switch (mutation)
        {
            case "missing-guard": plan.ChildTestSteps.RemoveAt(0); break;
            case "disabled-guard": plan.ChildTestSteps[0].Enabled = false; break;
            case "missing-rules": sidecar.ReferenceRules = null; break;
            case "invalid-tolerance": sidecar.ReferenceRules![0] = sidecar.ReferenceRules[0] with { Tolerance = null }; break;
            case "producer": sidecar.ReferenceRules![0] = sidecar.ReferenceRules[0] with { ProducerStepId = Guid.NewGuid() }; break;
            case "metric": sidecar.ReferenceRules![0] = sidecar.ReferenceRules[0] with { MetricKey = "unpublished-channel" }; break;
            case "unit": sidecar.ReferenceRules![0] = sidecar.ReferenceRules[0] with { Unit = "mV" }; break;
            case "disabled-parent": plan.ChildTestSteps[1].Enabled = false; break;
        }
        plan.Save(path);
        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));
        if (mutation == "unknown-rule-field")
        {
            var json = JsonNode.Parse(File.ReadAllText(sidecarPath))!;
            json["referenceRules"]![0]!["futureComparison"] = "must not be ignored";
            File.WriteAllText(sidecarPath, json.ToJsonString());
        }
        var planBefore = File.ReadAllBytes(path); var sidecarBefore = File.ReadAllBytes(sidecarPath);
        var report = PlanContractValidator.ValidateFile(path, new PlanContractOptions { Strict = true, EnablePhysicalExecution = false });
        Assert.True(report.HasErrors);
        var expected = mutation switch
        {
            "missing-guard" or "disabled-guard" or "missing-rules" => PlanContractValidator.Codes.ReferenceGuard,
            "unknown-rule-field" => PlanContractValidator.Codes.SidecarInvalid,
            _ => PlanContractValidator.Codes.ReferenceRules,
        };
        Assert.Contains(report.Findings, finding => finding.Code == expected && finding.Severity == PlanContractSeverity.Error);
        Assert.Equal(planBefore, File.ReadAllBytes(path));
        Assert.Equal(sidecarBefore, File.ReadAllBytes(sidecarPath));
    }

    [Fact]
    public async Task Reference_sidecar_cannot_be_loaded_with_an_unguarded_compiled_plan()
    {
        using var temp = new TempDataDirectory();
        var guarded = SaveGuardedPlan(temp);
        var plan = TestPlan.Load(guarded);
        var guard = StepRuntimeBinder.Flatten(plan).OfType<RequireOperatorReferenceAcceptanceStep>().Single();
        Assert.IsType<TestGroupStep>(plan.ChildTestSteps[0]).ChildTestSteps.Remove(guard);
        var unguarded = Path.Combine(temp.Path, "unguarded-reference.TapPlan");
        plan.Save(unguarded);
        File.WriteAllText(Path.ChangeExtension(unguarded, ".program.json"), JsonSerializer.Serialize(new ProgramSidecar
        {
            DisplayName = "Same display name cannot supply the missing runtime guard",
            ReferenceRules = [new BenchReferenceRule
            {
                RuleId = "rail", MetricKey = "guard.measurement", ReferenceKey = "golden.rail", Unit = "V",
                ProducerStepId = Guid.NewGuid(), Tolerance = 1,
            }],
        }, ProgramCatalogJsonContext.Default.ProgramSidecar));
        var session = new OpenTapSession(new AppSettings { UseMockVisa = true });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.LoadPlanAsync(unguarded));
        Assert.Contains("OPERATOR_REFERENCE_GUARD_MISSING", error.Message, StringComparison.Ordinal);
        Assert.Null(session.LoadedPlanPath);
    }

    [Fact]
    public void Standalone_serialized_plan_has_no_persisted_capability_and_cannot_report_pass()
    {
        using var temp = new TempDataDirectory();
        var path = SaveGuardedPlan(temp);
        using (var logger = new LoggerConfiguration().CreateLogger())
        using (var prepared = new OpenTapRunContext(new AppSettings(), logger,
            referenceAcceptancePrepared: true, referenceAcquisition: true))
        {
            var preparedPlan = TestPlan.Load(path);
            StepRuntimeBinder.Attach(preparedPlan, prepared);
            preparedPlan.Save(path);
            StepRuntimeBinder.Detach(preparedPlan);
        }
        var xml = File.ReadAllText(path);
        Assert.Contains(nameof(RequireOperatorReferenceAcceptanceStep), xml, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(StationProfile.ReferenceAcceptancePrepared), xml, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(StationProfile.ReferenceAcquisition), xml, StringComparison.Ordinal);
        var plan = TestPlan.Load(path);
        var guard = StepRuntimeBinder.Flatten(plan).OfType<RequireOperatorReferenceAcceptanceStep>().Single();
        var required = Assert.Throws<InvalidOperationException>(guard.PrePlanRun);
        Assert.Contains("OPERATOR_REFERENCE_REQUIRED", required.Message, StringComparison.Ordinal);
        Verdict? verdict = null;
        var error = Record.Exception(() => verdict = plan.Execute().Verdict);
        if (error is null) Assert.NotEqual(Verdict.Pass, verdict);
        else Assert.Contains("OPERATOR_REFERENCE_REQUIRED", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepared_operator_or_explicit_acquisition_capability_is_consumed_after_one_run(bool acquisition)
    {
        using var temp = new TempDataDirectory();
        var session = new OpenTapSession(new AppSettings { UseMockVisa = true });
        await session.LoadPlanAsync(SaveGuardedPlan(temp));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
        Assert.Contains("OPERATOR_REFERENCE_REQUIRED", missing.Message, StringComparison.Ordinal);
        await Prepare(session, acquisition);
        var summary = await session.RunAsync();
        Assert.Equal(RunResult.Passed, summary.Result);
        Assert.Contains(summary.Samples, sample => sample.Channel == "guard.measurement");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
        Assert.False(session.IsExecuting);
    }

    [Fact]
    public async Task Selected_execution_cannot_mask_the_guard_and_requires_a_fresh_preparation()
    {
        using var temp = new TempDataDirectory();
        var session = new OpenTapSession(new AppSettings { UseMockVisa = true });
        await session.LoadPlanAsync(SaveGuardedPlan(temp));
        var measurement = Flatten(session.StepTree).Single(node => node.Name == "Observed reference measurement");
        var guard = Flatten(session.StepTree).Single(node => node.Name == "Check reference inputs");
        Assert.False(session.TrySetStepEnabled(guard.Path, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunSelectionAsync(measurement.Path, includeCleanup: false));
        await Prepare(session, acquisition: true);
        var selected = await session.RunSelectionAsync(measurement.Path, includeCleanup: false);
        Assert.Equal(RunResult.Passed, selected.Result);
        Assert.Contains(selected.Steps, step => step.StepId == guard.Id && step.Passed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunSelectionAsync(measurement.Path, includeCleanup: false));
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("failed-load")]
    [InlineData("cancelled-load")]
    [InlineData("cancelled-apply")]
    [InlineData("invalid-selection")]
    [InlineData("step-setting")]
    [InlineData("parameter")]
    [InlineData("abort")]
    public async Task New_load_or_station_mutation_cannot_reuse_a_prepared_context(string mutation)
    {
        using var temp = new TempDataDirectory();
        var path = SaveGuardedPlan(temp);
        var session = new OpenTapSession(new AppSettings { UseMockVisa = true });
        await session.LoadPlanAsync(path);
        var measurement = Flatten(session.StepTree).Single(node => node.Name == "Observed reference measurement");
        await Prepare(session, acquisition: false);
        switch (mutation)
        {
            case "reload": await session.LoadPlanAsync(path); break;
            case "failed-load":
                await Assert.ThrowsAsync<FileNotFoundException>(() => session.LoadPlanAsync(Path.Combine(temp.Path, "absent.TapPlan")));
                break;
            case "cancelled-load":
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.LoadPlanAsync(path, cancel.Token));
                }
                break;
            case "cancelled-apply":
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ApplyStationAndDutAsync(
                        new StationProfile(new Dictionary<string, string>()), new DutIdentity("CANCELLED"), cancel.Token));
                }
                break;
            case "invalid-selection":
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunSelectionAsync("not-a-step"));
                break;
            case "step-setting": Assert.True(session.TrySetStepEnabled(measurement.Path, true)); break;
            case "parameter":
                Assert.True(session.TrySetParameter(OpenTapParameterInfo.FormatStepMemberKey(measurement.Id, nameof(PublishBandScalarStep.Value)), "1.5"));
                break;
            default: session.Abort(); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
        await Prepare(session, acquisition: false);
        Assert.Equal(RunResult.Passed, (await session.RunAsync()).Result);
    }

    [Fact]
    public async Task Worker_ipc_forwards_the_capability_without_persisting_or_reusing_it()
    {
        using var temp = new TempDataDirectory();
        var path = SaveGuardedPlan(temp);
        using var worker = new OpenTapWorkerClient(new AppSettings
        {
            UseMockVisa = true,
            CrashEnabled = false,
            DataDirectory = temp.Path,
        });
        await worker.LoadPlanAsync(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync());
        await Prepare(worker, acquisition: true);
        var acquired = await worker.RunAsync();
        Assert.Equal(RunResult.Passed, acquired.Result);
        Assert.Contains(acquired.Samples, sample => sample.Channel == "guard.measurement");
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync());
        await Prepare(worker, acquisition: false);
        Assert.Equal(RunResult.Passed, (await worker.RunAsync()).Result);
    }

    private static Task Prepare(IOpenTapStationSession session, bool acquisition)
        => session.ApplyStationAndDutAsync(new StationProfile(new Dictionary<string, string>())
        {
            ReferenceAcceptancePrepared = true,
            ReferenceAcquisition = acquisition,
        }, new DutIdentity("GUARD-REFERENCE-DUT"));

    private static string SaveGuardedPlan(TempDataDirectory temp)
    {
        var plan = new TestPlan();
        var setup = new TestGroupStep { Name = "Setup" };
        setup.ChildTestSteps.Add(new RequireOperatorReferenceAcceptanceStep());
        var measure = new TestGroupStep { Name = "Measure" };
        measure.ChildTestSteps.Add(new PublishBandScalarStep
        {
            Name = "Observed reference measurement",
            MetricName = "guard.measurement",
            Value = 1,
            Unit = "V",
            LimitLow = 0,
            LimitHigh = 2,
        });
        plan.ChildTestSteps.Add(setup);
        plan.ChildTestSteps.Add(measure);
        var path = Path.Combine(temp.Path, "guarded-reference.TapPlan");
        plan.Save(path);
        return path;
    }

    private static (string Path, TestPlan Plan, ProgramSidecar Sidecar) SaveContractProgram(TempDataDirectory temp)
    {
        var plan = SampleProgramFactory.Create();
        plan.ChildTestSteps.Insert(0, new RequireOperatorReferenceAcceptanceStep());
        var producer = StepRuntimeBinder.Flatten(plan).OfType<AcquireVoltageStep>().Single();
        var sidecar = new ProgramSidecar
        {
            DisplayName = "Checked reference program",
            DutFamily = "demo",
            RequireSerial = true,
            RequireOperator = true,
            SelectionIncludesCleanup = true,
            ReferenceRules = [new BenchReferenceRule
            {
                RuleId = "rail", MetricKey = "VDC", ReferenceKey = "golden.rail", ProducerStepId = producer.Id,
                Unit = "V", Tolerance = 1,
            }],
        };
        var path = Path.Combine(temp.Path, "checked-reference.TapPlan");
        plan.Save(path);
        File.WriteAllText(Path.ChangeExtension(path, ".program.json"), JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));
        return (path, plan, sidecar);
    }

    private static IEnumerable<OpenTapStepNode> Flatten(IEnumerable<OpenTapStepNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
