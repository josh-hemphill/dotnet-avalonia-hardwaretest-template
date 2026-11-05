using System.Text.Json;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Core.StationHealth;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using HardwareTest.ViewModels.Tests.Time;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class BenchReferenceWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static Task OnUi(Action action) { action(); return Task.CompletedTask; }

    [Fact]
    public async Task Actual_run_pipeline_bootstraps_and_consumes_independent_references_in_the_persisted_final_verdict()
    {
        var root = Temp();
        try
        {
            var rules = Rules();
            var path = Path.Combine(root, "reference-workflow.TapPlan");
            File.WriteAllText(path, "<TestPlan />");
            File.WriteAllText(Path.ChangeExtension(path, ".program.json"), JsonSerializer.Serialize(new ProgramSidecar
            { DisplayName = "Golden comparison test", ReferenceRules = rules.ToList(), ProgramKind = ProgramKinds.StationHealth }, ProgramCatalogJsonContext.Default.ProgramSidecar));
            var settings = new AppSettings { DataDirectory = root, UseMockVisa = true, IsEngineerDebugMode = true };
            var clock = new FakeClock(DateTimeOffset.UtcNow);
            var references = new FileBenchReferenceStore(root, clock);
            var management = new DeferredRetirementStore(references);
            var runs = new FileRunStore(Path.Combine(root, "runs"));
            var tap = new FakeOpenTapSession { ReportSamples = false };
            var health = new FakeStationHealthStore();
            var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(clock), new FakeRunControl(), new FakeReportService(),
                runs, settings, clock: clock, benchReferenceStore: management, stationHealthStore: health)
            { UiScheduler = action => action() };
            await vm.ProgramSelection.RefreshProgramsAsync();
            vm.ProgramSelection.RequestPlanFilePath = _ => Task.FromResult<string?>(path);
            await vm.ProgramSelection.OpenPlanFileCommand.ExecuteAsync();
            Assert.True(vm.IsSelectedProgramReady);
            await Confirm("NO-REFERENCE");
            Assert.False(vm.CanStartRun);
            await vm.Run.RunCommand.ExecuteAsync();
            Assert.Equal(0, tap.RunCount);
            Assert.Null(tap.LastDut); // No hardware/station execution is accepted without required inputs.

            var first = await Acquire("GOLDEN-A", 1, 2);
            var second = await Acquire("GOLDEN-B", 10, 20);
            Assert.Empty(health.Written);
            vm.References.IsAcquisition = false;
            await vm.RefreshBenchReferencesAsync();
            foreach (var row in vm.References.Rules) row.SelectedChoice = row.Choices.Single(choice => choice.Profile.Id == first.Id);
            await Confirm("PRODUCT-1");
            SetSamples(1, 2);
            await vm.Run.RunCommand.ExecuteAsync();
            var passed = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
            Assert.Equal(RunResult.Passed, passed.Result);
            Assert.Equal(StationHealthVerdicts.Pass, Assert.Single(health.Written).Verdict);
            Assert.All(passed.ReferenceEvaluations, evaluation => Assert.True(evaluation.Passed));
            var original = File.ReadAllBytes(Path.Combine(runs.GetRunDirectory(passed.RunId), "run.json"));

            foreach (var row in vm.References.Rules) row.SelectedChoice = null;
            await Confirm("SELECTED-DIAGNOSTIC");
            vm.StepTree.SelectedStep = Flatten(vm.StepTree.Hierarchy).First(node => node.Children.Count == 0);
            Assert.False(vm.CanStartRun);
            Assert.True(vm.CanStartRunSelected); // This unrelated selected test has no declared reference input.
            SetSamples(1, 2);
            await vm.Run.RunSelectedCommand.ExecuteAsync();
            Assert.Single(health.Written); // Partial health evidence must not refresh full qualification.
            Assert.Contains("Health qualification was not updated", vm.Status, StringComparison.Ordinal);

            vm.References.Rules[0].SelectedChoice = vm.References.Rules[0].Choices.Single(choice => choice.Profile.Id == second.Id);
            vm.References.Rules[1].SelectedChoice = vm.References.Rules[1].Choices.Single(choice => choice.Profile.Id == first.Id);
            Assert.True(vm.SessionPanel.SessionBlocked);
            await Confirm("PRODUCT-2");
            SetSamples(1, 2);
            await vm.Run.RunCommand.ExecuteAsync();
            var failed = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
            Assert.Equal(RunResult.Passed, failed.ReferenceBackendResult);
            Assert.Equal(RunResult.Failed, failed.Result);
            Assert.Equal(2, health.Written.Count);
            Assert.Equal(StationHealthVerdicts.Fail, health.Written.Last().Verdict);
            Assert.False(failed.ReferenceEvaluations.Single(evaluation => evaluation.MetricKey == "left").Passed);
            Assert.True(failed.ReferenceEvaluations.Single(evaluation => evaluation.MetricKey == "right").Passed);
            Assert.Equal([second.Id, first.Id], failed.ReferenceUses.Select(use => use.ReferenceId));
            Assert.Equal("Last run: reference acceptance Failed (1/2 passed).", vm.ReferenceOutcomeSummary);
            Assert.Contains("reference 10", vm.ReferenceOutcomeDetails, StringComparison.Ordinal);
            Assert.Contains("Saved result", vm.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("Session attempts rolled up", vm.Status, StringComparison.Ordinal);
            Assert.Contains(failed.Steps, step => step.StepPath == "Reference acceptance/left [left-rule]" && !step.Passed);
            Assert.Equal(1, vm.StepTree.SuiteFailedCount);
            Assert.Equal("Fail", vm.StepTree.Stages.Single(stage => stage.Step is null).ChipText);
            Assert.Equal("Fail", Assert.Single(vm.StepTree.FullHierarchy).ChipText);
            await vm.StepTree.FilterFailCommand.ExecuteAsync();
            var failedEvidence = Assert.Single(vm.StepTree.StepRows);
            Assert.Equal("reference:left-rule", failedEvidence.Id);
            Assert.True(failedEvidence.IsReadOnlyEvidence);
            Assert.Contains("reference 10", failedEvidence.KeyValue, StringComparison.Ordinal);
            vm.StepTree.SelectedStep = failedEvidence;
            Assert.False(vm.CanStartRunSelected);
            var beforeEvidenceAttempt = tap.RunCount;
            await vm.Run.RunSelectedCommand.ExecuteAsync();
            Assert.Equal(beforeEvidenceAttempt, tap.RunCount);
            Assert.Contains("read-only evidence", vm.Status, StringComparison.Ordinal);

            // A -> B -> A must replace terminal evidence, counts and any automatic fail filter.
            foreach (var row in vm.References.Rules) row.SelectedChoice = row.Choices.Single(choice => choice.Profile.Id == first.Id);
            await Confirm("PRODUCT-3"); SetSamples(1, 2);
            await vm.Run.RunCommand.ExecuteAsync();
            Assert.Equal(RunResult.Passed, (await runs.LoadAsync(vm.LastRunId!))!.Result);
            Assert.Equal(0, vm.StepTree.SuiteFailedCount);
            Assert.Equal("Pass", vm.StepTree.Stages.Single(stage => stage.Step is null).ChipText);
            Assert.Equal("Pass", Assert.Single(vm.StepTree.FullHierarchy).ChipText);
            Assert.Equal(2, Flatten(vm.StepTree.FullHierarchy).Count(node => node.IsReadOnlyEvidence && node.Children.Count == 0));
            Assert.False(vm.StepTree.IsFilteredToFail);
            Assert.NotEmpty(vm.StepTree.StepRows);
            await vm.ClearRunCommand.ExecuteAsync();
            Assert.DoesNotContain(Flatten(vm.StepTree.FullHierarchy), node => node.IsReadOnlyEvidence);
            Assert.Equal(0, vm.StepTree.SuiteFailedCount);
            Assert.False(vm.StepTree.IsReadOnlyEvidenceSelection);
            // The dialog may be dismissed while this operation is pending. Its view has
            // no authority over the VM transaction; even an explicit Run command is blocked.
            await vm.RefreshBenchReferencesAsync();
            vm.References.SelectedProfile = vm.References.Profiles.Single(item => item.Entry.Profile?.Id == first.Id);
            vm.References.RetirementReason = "Replaced after review";
            management.DeferRetirement = true;
            var retirement = vm.References.RetireCommand.ExecuteAsync();
            await management.Started.Task;
            Assert.True(vm.References.IsManagementPending);
            Assert.False(vm.References.CanManage);
            Assert.True(vm.SessionPanel.SessionBlocked);
            Assert.Contains("retirement", vm.References.ReadinessText, StringComparison.OrdinalIgnoreCase);
            await Confirm("PRODUCT-DURING-RETIREMENT");
            var previousAttempts = tap.RunCount;
            await vm.Run.RunCommand.ExecuteAsync();
            Assert.Equal(previousAttempts, tap.RunCount);
            Assert.Contains("still in progress", vm.Status, StringComparison.Ordinal);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.References.FreezeAsync(passed.ReferenceContext!));
            management.Continue.SetResult();
            await retirement;
            Assert.False(vm.References.IsManagementPending);
            Assert.True(vm.SessionPanel.SessionBlocked);
            Assert.All(vm.References.Rules, row => Assert.DoesNotContain(row.Choices, choice => choice.Profile.Id == first.Id));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(runs.GetRunDirectory(passed.RunId), "run.json")));

            async Task Confirm(string serial)
            {
                vm.SessionPanel.DutSerialInput = serial; vm.SessionPanel.OperatorInput = "Reference technician";
                await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            }
            void SetSamples(double left, double right)
                => tap.SummarySamplesFactory = (started, completed) => rules.Select((rule, index) => new StoredSample
                {
                    ProducerStepId = rule.ProducerStepId,
                    MetricKey = rule.MetricKey,
                    Channel = rule.MetricKey,
                    Unit = rule.Unit,
                    Timestamp = started + (completed - started) / 2,
                    Value = index == 0 ? left : right,
                    StepPath = "Measure/" + rule.MetricKey,
                }).Append(new StoredSample
                {
                    MetricKey = StationHealthRecorder.OffsetMetric,
                    Channel = StationHealthRecorder.OffsetMetric,
                    Value = 0.01,
                    Unit = "V",
                    Timestamp = started + (completed - started) / 2,
                    StepPath = "Measure/health",
                }).ToList();
            async Task<BenchReferenceProfile> Acquire(string serial, double left, double right)
            {
                vm.References.IsAcquisition = true;
                await Confirm(serial); SetSamples(left, right);
                await vm.Run.RunCommand.ExecuteAsync();
                var source = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
                Assert.Equal(RunExecutionScopes.ReferenceAcquisition, source.ExecutionScope);
                Assert.Empty(source.ReferenceUses);
                Assert.Contains("not a production pass", vm.Status, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Health qualification was not updated", vm.Status, StringComparison.Ordinal);
                clock.UtcNow = DateTimeOffset.UtcNow;
                var profile = BenchReferenceEvaluator.Capture(serial, source, rules, clock.UtcNow);
                await references.SaveAsync(profile);
                return profile;
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Reference_evidence_keeps_duplicate_metric_criteria_distinct_and_does_not_modify_executable_plan_children()
    {
        var plan = new OpenTapStepNode
        {
            Id = "authored-root",
            Name = "Authored program",
            Path = "Authored program",
            IsStage = true,
            Children = [new() { Id = "measure", Name = "Measured voltage", Path = "Authored program/Measured voltage" }],
        };
        var tree = new StepTreeViewModel(() => [plan]);
        tree.RebuildFromHost();
        tree.ApplyStepResults([
            new() { StepId = "measure", StepPath = "Authored program/Measured voltage", Passed = true },
            new() { StepId = "reference:lower", StepType = "Bench reference acceptance", StepPath = "Reference acceptance/VDC [lower]", Passed = true, Message = "Within absolute tolerance" },
            new() { StepId = "reference:upper", StepType = "Bench reference acceptance", StepPath = "Reference acceptance/VDC [upper]", Passed = false, Message = "No qualified current samples" },
        ]);
        Assert.Equal(2, tree.SuitePassedCount);
        Assert.Equal(1, tree.SuiteFailedCount);
        Assert.Equal("Fail", Assert.Single(tree.FullHierarchy).ChipText);
        var evidence = Flatten(tree.FullHierarchy).Where(node => node.IsReadOnlyEvidence && node.Children.Count == 0).ToArray();
        Assert.Equal(2, evidence.Length);
        Assert.Equal("Pass", evidence.Single(node => node.Id == "reference:lower").ChipText);
        Assert.Equal("Fail", evidence.Single(node => node.Id == "reference:upper").ChipText);
        Assert.Equal("No qualified current samples", evidence.Single(node => node.Id == "reference:upper").KeyValue);
        Assert.Single(plan.Children); // UI evidence cannot accidentally enter host Run Selected execution.
        tree.ClearRunState();
        Assert.DoesNotContain(Flatten(tree.FullHierarchy), node => node.IsReadOnlyEvidence);
        Assert.Equal(1, tree.SuitePendingCount);
        Assert.Single(plan.Children);
    }

    [Fact]
    public async Task Identical_serialized_logical_slots_hash_once_but_conflicting_bindings_block_with_a_correction_route()
    {
        var root = Temp();
        try
        {
            var path = Path.Combine(root, "reference-workflow.TapPlan");
            File.WriteAllText(path, "<TestPlan />");
            File.WriteAllText(Path.ChangeExtension(path, ".program.json"), JsonSerializer.Serialize(new ProgramSidecar
            { ReferenceRules = Rules().ToList() }, ProgramCatalogJsonContext.Default.ProgramSidecar));
            var tap = new FakeOpenTapSession();
            var original = tap.Slots.Single();
            tap.Slots.Add(new() { Name = original.Name, TypeName = original.TypeName, ResourceName = original.ResourceName, RoleHint = original.RoleHint });
            using var runs = new FakeRunStore();
            var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(), new FakeRunControl(), new FakeReportService(), runs,
                new AppSettings { IsEngineerDebugMode = true }, benchReferenceStore: new MemoryStore([]))
            { UiScheduler = action => action() };
            vm.ProgramSelection.RequestPlanFilePath = _ => Task.FromResult<string?>(path);
            await vm.ProgramSelection.OpenPlanFileCommand.ExecuteAsync();
            Assert.True(vm.IsSelectedProgramReady);
            vm.References.IsAcquisition = true;
            vm.SessionPanel.DutSerialInput = "GOLDEN-DUPLICATE"; vm.SessionPanel.OperatorInput = "Technician";
            await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            await vm.Run.RunCommand.ExecuteAsync();
            var withDuplicates = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!)).ReferenceContext!.ConfigurationSha256;
            tap.Slots.RemoveAt(1);
            await ((IRunBoardHost)vm).LoadSelectedProgramAsync();
            await vm.Run.RunCommand.ExecuteAsync();
            var withoutDuplicates = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!)).ReferenceContext!.ConfigurationSha256;
            Assert.Equal(withoutDuplicates, withDuplicates);

            tap.Slots.Add(new() { Name = original.Name, TypeName = original.TypeName, ResourceName = "MOCK::CONFLICT", RoleHint = original.RoleHint });
            await ((IRunBoardHost)vm).LoadSelectedProgramAsync();
            Assert.False(vm.IsSelectedProgramReady);
            Assert.Contains(original.Name, vm.ProgramAvailabilityMessage, StringComparison.Ordinal);
            Assert.Contains("conflicting", vm.ProgramAvailabilityMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reload", vm.ProgramAvailabilityMessage, StringComparison.OrdinalIgnoreCase);
            var attempts = tap.RunCount;
            await vm.Run.RunCommand.ExecuteAsync();
            Assert.Equal(attempts, tap.RunCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Independent_test_inputs_use_exact_producer_values_and_choices_survive_a_real_settings_restart()
    {
        var root = Temp();
        try
        {
            var clock = new FakeClock(Now);
            var settings = new SettingsStore(root); await settings.LoadAsync();
            var store = new FileBenchReferenceStore(root, clock);
            var rules = Rules(); var context = Context();
            // The same input key across distinct producers must not use name-only Single().
            var first = BenchReferenceEvaluator.Capture("Duplicate name", Source(rules, 1, 2), rules, Now);
            var second = BenchReferenceEvaluator.Capture("Duplicate name", Source(rules, 10, 20), rules, Now);
            await store.SaveAsync(first); await store.SaveAsync(second);
            var confirmations = 0;
            var panel = Panel(store, settings.AppSettings, settings, clock, () => confirmations++);
            panel.Configure(Program(rules), context); await panel.RefreshAsync();
            panel.Rules[0].SelectedChoice = panel.Rules[0].Choices.Single(choice => choice.Profile.Id == first.Id);
            panel.Rules[1].SelectedChoice = panel.Rules[1].Choices.Single(choice => choice.Profile.Id == second.Id);
            Assert.Equal(2, confirmations);
            Assert.True(panel.IsReady);
            await panel.SaveChoicesCommand.ExecuteAsync();

            var reopenedSettings = new SettingsStore(root); await reopenedSettings.LoadAsync();
            var reopened = Panel(new FileBenchReferenceStore(root, clock), reopenedSettings.AppSettings, reopenedSettings, clock);
            reopened.Configure(Program(rules), context); await reopened.RefreshAsync();
            Assert.Equal(first.Id, reopened.Rules[0].SelectedChoice!.Profile.Id);
            Assert.Equal(second.Id, reopened.Rules[1].SelectedChoice!.Profile.Id);
            var frozen = await reopened.FreezeAsync(context);
            Assert.Equal([1d, 20d], frozen.Uses.Select(use => use.ReferenceValue.Value));
            var evaluated = BenchReferenceEvaluator.Evaluate(Source(rules, 1, 20), frozen.Uses);
            Assert.All(evaluated, evaluation => Assert.True(evaluation.Passed));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Compatible_case_variants_keep_exact_producer_and_unit_binding_in_the_chooser()
    {
        var rules = Rules(); var context = Context(); var clock = new FakeClock(Now);
        var captured = BenchReferenceEvaluator.Capture("Golden", Source(rules, 1, 2), rules, Now);
        var profile = captured with
        {
            Values = captured.Values.Select(value => value with
            { ReferenceKey = value.ReferenceKey.ToUpperInvariant(), MetricKey = value.MetricKey.ToUpperInvariant() }).ToList()
        };
        profile = profile with { ContentSha256 = BenchReferenceIdentity.ComputeProfileHash(profile) };
        var panel = Panel(new MemoryStore([profile]), new AppSettings(), null, clock);
        panel.Configure(Program(rules), context); await panel.RefreshAsync();
        Assert.All(panel.Rules, row => Assert.Single(row.Choices));
        Assert.Equal([1d, 2d], panel.Rules.Select(row => row.Choices.Single().Value.Value));
    }

    [Fact]
    public async Task Retirement_blocks_the_next_run_but_never_changes_an_already_frozen_evaluation()
    {
        var root = Temp();
        try
        {
            var clock = new FakeClock(Now); var rules = Rules(); var context = Context();
            var store = new FileBenchReferenceStore(root, clock);
            var profile = BenchReferenceEvaluator.Capture("Golden controller", Source(rules, 1, 2), rules, Now);
            await store.SaveAsync(profile);
            var panel = Panel(store, new AppSettings(), null, clock);
            panel.Configure(Program(rules), context); await panel.RefreshAsync();
            foreach (var row in panel.Rules) row.SelectedChoice = Assert.Single(row.Choices);
            var frozen = await panel.FreezeAsync(context);
            await store.RetireAsync(profile.Id, "Replaced after fixture maintenance");
            Assert.All(BenchReferenceEvaluator.Evaluate(Source(rules, 1, 2), frozen.Uses), evaluation => Assert.True(evaluation.Passed));
            await Assert.ThrowsAnyAsync<Exception>(() => panel.FreezeAsync(context));
            await panel.RefreshAsync();
            Assert.False(panel.IsReady);
            Assert.All(panel.Rules, row => Assert.Null(row.SelectedChoice));
            Assert.Contains(panel.Profiles, row => row.Entry.IsRetired);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Exact_expiry_boundary_blocks_next_acceptance_and_keeps_active_snapshot_independent()
    {
        var rules = Rules(); var context = Context(); var clock = new FakeClock(Now);
        var profile = BenchReferenceEvaluator.Capture("Timed reference", Source(rules, 1, 2), rules, Now, Now.AddHours(1));
        var store = new MemoryStore([profile]);
        var panel = Panel(store, new AppSettings(), null, clock);
        panel.Configure(Program(rules), context); await panel.RefreshAsync();
        foreach (var row in panel.Rules) row.SelectedChoice = Assert.Single(row.Choices);
        var frozen = await panel.FreezeAsync(context);
        clock.UtcNow = Now.AddHours(1);
        Assert.False(panel.IsReady);
        Assert.Contains("expired", panel.ReadinessText, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAnyAsync<Exception>(() => panel.FreezeAsync(context));
        Assert.All(BenchReferenceEvaluator.Evaluate(Source(rules, 1, 2), frozen.Uses), evaluation => Assert.True(evaluation.Passed));
    }

    [Fact]
    public async Task Rewritten_same_ID_content_cannot_silently_replace_the_value_shown_at_confirmation()
    {
        var rules = Rules(); var context = Context(); var clock = new FakeClock(Now);
        var profile = BenchReferenceEvaluator.Capture("Golden", Source(rules, 1, 2), rules, Now);
        var store = new MemoryStore([profile]); var confirmations = 0;
        var panel = Panel(store, new AppSettings(), null, clock, () => confirmations++);
        panel.Configure(Program(rules), context); await panel.RefreshAsync();
        foreach (var row in panel.Rules) row.SelectedChoice = Assert.Single(row.Choices);
        var edited = profile with { Values = profile.Values.Select(value => value with { Value = value.Value + 10 }).ToList() };
        store.Saved[0] = edited with { ContentSha256 = BenchReferenceIdentity.ComputeProfileHash(edited) };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => panel.FreezeAsync(context));
        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        var beforeReload = confirmations;
        await panel.RefreshAsync();
        Assert.True(confirmations > beforeReload); // New displayed content requires an explicit session confirmation.
        Assert.Equal(11, panel.Rules[0].SelectedChoice!.Value.Value);
    }

    [Fact]
    public async Task All_inputs_share_one_acceptance_instant_when_an_earlier_profile_expires_during_a_later_load()
    {
        var rules = Rules(); var context = Context(); var clock = new FakeClock(Now);
        var expiring = BenchReferenceEvaluator.Capture("Expiring first input", Source(rules, 1, 2), rules, Now, Now.AddHours(1));
        var later = BenchReferenceEvaluator.Capture("Later input", Source(rules, 1, 2), rules, Now);
        var store = new MemoryStore([expiring, later]);
        var panel = Panel(store, new AppSettings(), null, clock);
        panel.Configure(Program(rules), context); await panel.RefreshAsync();
        panel.Rules[0].SelectedChoice = panel.Rules[0].Choices.Single(choice => choice.Profile.Id == expiring.Id);
        panel.Rules[1].SelectedChoice = panel.Rules[1].Choices.Single(choice => choice.Profile.Id == later.Id);
        Assert.True(panel.IsReady);
        store.BeforeLoad = id => { if (id == later.Id) clock.UtcNow = Now.AddHours(1); };
        await Assert.ThrowsAnyAsync<Exception>(() => panel.FreezeAsync(context));
    }

    [Fact]
    public async Task Selected_tests_require_only_their_own_declared_inputs()
    {
        var rules = Rules(); var context = Context(); var clock = new FakeClock(Now);
        var profile = BenchReferenceEvaluator.Capture("Golden", Source(rules, 1, 2), rules, Now);
        var panel = Panel(new MemoryStore([profile]), new AppSettings(), null, clock);
        panel.Configure(Program(rules), context); await panel.RefreshAsync();
        panel.Rules[1].SelectedChoice = Assert.Single(panel.Rules[1].Choices);
        Assert.False(panel.IsReady);
        Assert.True(panel.IsReadyForRules([rules[1]]));
        Assert.True(panel.IsReadyForRules([]));
        var selected = await panel.FreezeAsync(context, [rules[1]]);
        Assert.Equal("right-rule", Assert.Single(selected.Uses).Rule.RuleId);
        await Assert.ThrowsAnyAsync<Exception>(() => panel.FreezeAsync(context));
    }

    [Fact]
    public async Task Active_run_refuses_choice_and_acquisition_mutation_and_old_refresh_cannot_replace_new_program()
    {
        var rules = Rules(); var context = Context(); var clock = new FakeClock(Now);
        var profile = BenchReferenceEvaluator.Capture("Golden", Source(rules, 1, 2), rules, Now);
        var store = new MemoryStore([profile]); var running = false;
        var panel = new BenchReferencePanelViewModel(store, new AppSettings(), null, clock, () => running, () => true, OnUi, () => { });
        panel.Configure(Program(rules), context); await panel.RefreshAsync();
        var first = panel.Rules[0]; first.SelectedChoice = Assert.Single(first.Choices);
        running = true;
        first.SelectedChoice = null; panel.IsAcquisition = true;
        Assert.NotNull(first.SelectedChoice); Assert.False(panel.IsAcquisition);
        running = false;
        var oldLoad = new TaskCompletionSource<IReadOnlyList<BenchReferenceCatalogEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.PendingList = oldLoad.Task;
        var pending = panel.RefreshAsync();
        var otherRules = rules.Select(rule => rule with { RuleId = "new-" + rule.RuleId }).ToArray();
        panel.Configure(Program(otherRules, "new-program"), context with { PlanId = "new-program" });
        store.PendingList = null;
        await panel.RefreshAsync();
        var status = panel.Status;
        oldLoad.SetResult([new() { Profile = profile }]); await pending;
        Assert.Equal(status, panel.Status);
        Assert.All(panel.Rules, row => Assert.StartsWith("new-", row.Rule.RuleId));
        Assert.All(panel.Rules, row => Assert.Empty(row.Choices));
    }

    [Fact]
    public async Task Reported_choice_write_failure_preserves_selection_and_future_settings_bytes()
    {
        var root = Temp();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            File.WriteAllText(settingsPath, $$"""{"schemaVersion":{{SchemaVersions.AppSettings + 1}}}""");
            var before = File.ReadAllBytes(settingsPath);
            var settings = new SettingsStore(root); await settings.LoadAsync();
            Assert.False(settings.IsSettingsWritable);
            var rules = Rules(); var clock = new FakeClock(Now);
            var profile = BenchReferenceEvaluator.Capture("Golden", Source(rules, 1, 2), rules, Now);
            var panel = Panel(new MemoryStore([profile]), settings.AppSettings, settings, clock);
            panel.Configure(Program(rules), Context()); await panel.RefreshAsync();
            panel.Rules[0].SelectedChoice = Assert.Single(panel.Rules[0].Choices);
            await panel.SaveChoicesCommand.ExecuteAsync();
            Assert.Contains("could not be saved", panel.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(profile.Id, panel.Rules[0].SelectedChoice!.Profile.Id);
            Assert.Equal(before, File.ReadAllBytes(settingsPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Failed_promotion_preserves_edits_and_source_bytes_then_retry_saves_only_current_covered_metrics()
    {
        var rules = Rules(); var source = Source(rules, 1, 2);
        source.SelectedStepPath = "Measure/left";
        source.Samples[1].Timestamp = source.StartedAt.AddSeconds(-1); // Preserved previous result must not qualify.
        var before = JsonSerializer.Serialize(source, AppJsonContext.Default.TestRunRecord);
        var store = new MemoryStore([]) { FailSave = true };
        var vm = new BenchReferencePromotionViewModel(store, new FakeClock(Now), () => true, OnUi);
        vm.Configure(source);
        Assert.Single(vm.Candidates, row => row.CanSelect);
        Assert.False(vm.Candidates.Single(row => row.Rule.MetricKey == "right").CanSelect);
        vm.Name = "Bench golden"; vm.ValidDays = "30";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Contains("Could not save", vm.Status, StringComparison.Ordinal);
        Assert.Equal("Bench golden", vm.Name);
        Assert.True(vm.Candidates.Single(row => row.CanSelect).IsSelected);
        Assert.Empty(store.Saved);
        store.FailSave = false;
        await vm.SaveCommand.ExecuteAsync();
        var saved = Assert.Single(store.Saved);
        Assert.Equal("left", Assert.Single(saved.Values).MetricKey);
        Assert.Equal(Now.AddDays(30), saved.ExpiresAt);
        Assert.Equal(before, JsonSerializer.Serialize(source, AppJsonContext.Default.TestRunRecord));
    }

    [Fact]
    public async Task Promotion_candidates_identify_independent_inputs_and_criteria_for_the_same_measurement()
    {
        var primary = Rules()[0] with { MetricKey = "voltage", ReferenceKey = "golden.primary" };
        var secondary = primary with { RuleId = "secondary-rule", ReferenceKey = "golden.secondary" };
        var percent = primary with { RuleId = "primary-percent-rule", Comparison = BenchReferenceComparisons.Percent, Tolerance = 2 };
        var source = Source([primary, secondary, percent], 1, 1);
        source.Samples = [source.Samples[0]];
        var before = JsonSerializer.Serialize(source, AppJsonContext.Default.TestRunRecord);
        var store = new MemoryStore([]);
        var vm = new BenchReferencePromotionViewModel(store, new FakeClock(Now), () => true, OnUi);
        vm.Configure(source);

        Assert.Equal(3, vm.Candidates.Count);
        Assert.All(vm.Candidates, row =>
        {
            Assert.True(row.CanSelect);
            Assert.Contains(row.Rule.ReferenceKey, row.Label, StringComparison.Ordinal);
            Assert.Contains(row.Rule.RuleId, row.AutomationName, StringComparison.Ordinal);
            Assert.Contains(row.Rule.ReferenceKey, row.Details, StringComparison.Ordinal);
            Assert.Contains(row.Rule.RuleId, row.Details, StringComparison.Ordinal);
            Assert.Contains(BenchReferenceEvidence.DescribeRule(row.Rule), row.Details, StringComparison.Ordinal);
        });
        Assert.Equal(3, vm.Candidates.Select(row => row.AutomationName).Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(vm.Candidates[0].Label, vm.Candidates[1].Label);
        vm.Candidates.Single(row => row.Rule.RuleId == percent.RuleId).IsSelected = false;
        vm.Name = "Independent voltage inputs";
        await vm.SaveCommand.ExecuteAsync();
        var saved = Assert.Single(store.Saved);
        Assert.Equal(["golden.primary", "golden.secondary"], saved.Values.Select(value => value.ReferenceKey).Order(StringComparer.Ordinal));
        Assert.All(saved.Values, value => { Assert.Equal(1, value.Value); Assert.Equal(1, value.SampleCount); });
        Assert.Equal(before, JsonSerializer.Serialize(source, AppJsonContext.Default.TestRunRecord));
    }

    private static BenchReferencePanelViewModel Panel(IBenchReferenceStore store, AppSettings settings, ISettingsStore? settingsStore, FakeClock clock, Action? changed = null)
    {
        if (store is MemoryStore memory) memory.Clock = clock;
        return new(store, settings, settingsStore, clock, () => false, () => true, OnUi, changed ?? (() => { }));
    }
    private static IEnumerable<HierarchyStepViewModel> Flatten(IEnumerable<HierarchyStepViewModel> nodes)
    { foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; } }

    private static BenchReferenceRule[] Rules() =>
    [
        new() { RuleId = "left-rule", MetricKey = "left", ProducerStepId = Guid.Parse("11111111-1111-1111-1111-111111111111"), ReferenceKey = "nominal", Unit = "V", Tolerance = 0.01 },
        new() { RuleId = "right-rule", MetricKey = "right", ProducerStepId = Guid.Parse("22222222-2222-2222-2222-222222222222"), ReferenceKey = "nominal", Unit = "V", Tolerance = 0.01 },
    ];
    private static BenchReferenceContext Context() => new()
    {
        PlanId = "reference-workflow",
        PlanSha256 = BenchReferenceIdentity.HashText("plan"),
        SidecarSha256 = BenchReferenceIdentity.HashText("sidecar"),
        ConfigurationSha256 = BenchReferenceIdentity.HashText("configuration"),
        VisaMode = BenchReferenceVisaModes.Simulated,
    };
    private static ProgramItemViewModel Program(IReadOnlyList<BenchReferenceRule> rules, string id = "reference-workflow") => new()
    { Id = id, DisplayName = "Reference workflow", Path = "reference.TapPlan", ReferenceRules = rules };
    private static TestRunRecord Source(IReadOnlyList<BenchReferenceRule> rules, double left, double right) => new()
    {
        RunId = Guid.NewGuid().ToString("N"),
        PlanId = "reference-workflow",
        PlanName = "Reference workflow",
        DutSerial = "GOLDEN-1",
        OperatorName = "Reference technician",
        StartedAt = Now.AddMinutes(-1),
        CompletedAt = Now,
        Result = RunResult.Passed,
        ReferenceBackendResult = RunResult.Passed,
        ExecutionScope = RunExecutionScopes.ReferenceAcquisition,
        ReferenceContext = Context(),
        ReferenceRules = rules.ToList(),
        Samples = rules.Select((rule, index) => new StoredSample
        {
            ProducerStepId = rule.ProducerStepId,
            MetricKey = rule.MetricKey,
            Channel = rule.MetricKey,
            Unit = rule.Unit,
            Value = index == 0 ? left : right,
            Timestamp = Now.AddSeconds(-10),
            StepPath = "Measure/" + rule.MetricKey,
        }).ToList(),
    };
    private static string Temp() { var path = Path.Combine(Path.GetTempPath(), "bench-reference-vm-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

    private sealed class DeferredRetirementStore(IBenchReferenceStore inner) : IBenchReferenceStore
    {
        public bool DeferRetirement { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<BenchReferenceCatalogEntry>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<BenchReferenceProfile> LoadAsync(string id, CancellationToken cancellationToken = default) => inner.LoadAsync(id, cancellationToken);
        public Task SaveAsync(BenchReferenceProfile profile, CancellationToken cancellationToken = default) => inner.SaveAsync(profile, cancellationToken);
        public Task<BenchReferenceAcceptance> FreezeAsync(IReadOnlyList<BenchReferenceSelection> selections, BenchReferenceContext context, CancellationToken cancellationToken = default)
            => inner.FreezeAsync(selections, context, cancellationToken);
        public async Task RetireAsync(string id, string reason, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (DeferRetirement) await Continue.Task.WaitAsync(cancellationToken);
            await inner.RetireAsync(id, reason, cancellationToken);
        }
    }

    private sealed class MemoryStore(IEnumerable<BenchReferenceProfile> profiles) : IBenchReferenceStore
    {
        public List<BenchReferenceProfile> Saved { get; } = profiles.ToList();
        public FakeClock Clock { get; set; } = new(Now);
        public bool FailSave { get; set; }
        public Action<string>? BeforeLoad { get; set; }
        public Task<IReadOnlyList<BenchReferenceCatalogEntry>>? PendingList { get; set; }
        public Task<IReadOnlyList<BenchReferenceCatalogEntry>> ListAsync(CancellationToken cancellationToken = default)
            => PendingList ?? Task.FromResult<IReadOnlyList<BenchReferenceCatalogEntry>>(Saved.Select(profile => new BenchReferenceCatalogEntry { Profile = profile }).ToArray());
        public Task<BenchReferenceProfile> LoadAsync(string id, CancellationToken cancellationToken = default)
        {
            BeforeLoad?.Invoke(id);
            return Task.FromResult(BenchReferenceEvaluator.CloneProfile(Saved.Single(profile => profile.Id == id)));
        }
        public Task SaveAsync(BenchReferenceProfile profile, CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new IOException("Reference storage is unavailable");
            Saved.Add(BenchReferenceEvaluator.CloneProfile(profile)); return Task.CompletedTask;
        }
        public Task RetireAsync(string id, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<BenchReferenceAcceptance> FreezeAsync(IReadOnlyList<BenchReferenceSelection> selections,
            BenchReferenceContext context, CancellationToken cancellationToken = default)
        {
            var loaded = new List<(BenchReferenceSelection Selection, BenchReferenceProfile Profile)>();
            foreach (var selection in selections)
            {
                var profile = await LoadAsync(selection.ReferenceId, cancellationToken);
                if (profile.Revision != selection.ExpectedRevision || profile.ContentSha256 != selection.ExpectedSha256)
                    throw new InvalidOperationException("Reference changed since selection.");
                loaded.Add((selection, profile));
            }
            var acceptedAt = Clock.UtcNow;
            return new() { AcceptedAt = acceptedAt, Uses = loaded.Select(item => BenchReferenceEvaluator.Freeze(item.Selection.Rule, item.Profile, context, acceptedAt)).ToArray() };
        }
    }
}
