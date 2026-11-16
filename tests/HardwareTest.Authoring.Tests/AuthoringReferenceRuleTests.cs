using System.Text.Json;
using System.Text.Json.Nodes;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringReferenceRuleTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Fact]
    public void Reference_edit_is_undoable_and_source_compiled_sidecar_reopen_preserve_stable_binding()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var vm = Open(root);
        try
        {
            var node = vm.SelectedSequence!.NodeId;
            vm.RequireGoldenReference = true;
            vm.ReferenceTolerance = "0.25";
            vm.ReferenceKey = "golden.voltage";
            vm.ReferenceComparison = "Relative difference (%)";
            var rule = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
            Assert.Equal(node, rule.ProducerStepId);
            Assert.Equal(vm.ChannelKey, rule.MetricKey);
            Assert.Equal(vm.YUnit, rule.Unit);
            Assert.Equal("percent", rule.Comparison);
            Assert.Equal(0.25, rule.Tolerance);
            vm.Undo();
            Assert.Equal("absolute", Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!).Comparison);
            vm.Redo();
            vm.SaveProgram("sample");
            Assert.False(vm.HasUnsavedChanges);
            var source = new AuthoringDocumentStore(root).Load("sample").Document!;
            Assert.Equal(4, source.SchemaVersion);
            Assert.Equal(rule, Assert.Single(source.Sidecar.ReferenceRules!));
            var compiled = new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan"));
            Assert.Equal(rule, Assert.Single(compiled.Sidecar.ReferenceRules!));
            var reopened = Open(root);
            try { Assert.Equal(rule, Assert.Single(reopened.SelectedProgram!.Sidecar.ReferenceRules!)); }
            finally { reopened.StopRecovery(); }
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Incomplete_tolerance_saves_source_without_publishing_and_recovers_in_one_undo_entry()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var vm = Open(root);
        try
        {
            vm.RequireGoldenReference = true;
            vm.ReferenceTolerance = "0.5";
            vm.SaveProgram("sample");
            var path = Path.Combine(root, "sample.TapPlan");
            var beforePlan = File.ReadAllBytes(path);
            var beforeSidecar = File.ReadAllBytes(PlanCompiler.SidecarPath(path));
            var node = vm.SelectedSequence!.NodeId;
            vm.ReferenceTolerance = "1e-";
            vm.SaveProgram("sample");
            Assert.True(vm.HasUncompiledSources);
            Assert.False(vm.CanPack);
            Assert.Equal(beforePlan, File.ReadAllBytes(path));
            Assert.Equal(beforeSidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(path)));
            var reopened = Open(root);
            try
            {
                reopened.SelectSequence(reopened.SequenceItems.ToList().FindIndex(row => row.NodeId == node));
                Assert.Equal("1e-", reopened.ReferenceTolerance);
                Assert.Contains(AuthoringIssueService.GetIssues(reopened.SelectedProgram!), finding => finding.Field is { } field && AuthoringReferenceRules.BaseField(field) == "ReferenceTolerance");
                reopened.ReferenceTolerance = "0.25";
                reopened.Undo();
                Assert.Equal("1e-", reopened.ReferenceTolerance);
                reopened.Redo();
                Assert.Equal("0.25", reopened.ReferenceTolerance);
                reopened.SaveProgram("sample");
                // Other imported fixture plans can still need their independent
                // legacy section migration. Verify this corrected program's own
                // source receipt and compiled value rather than the workspace-wide flag.
                var saved = new AuthoringDocumentStore(root).Load("sample").Document!;
                Assert.False(saved.RequiresCompilation, reopened.Error ?? reopened.Status);
                Assert.Equal(0.25, Assert.Single(new PlanCompiler().Load(path).Sidecar.ReferenceRules!).Tolerance);
                Assert.Equal(saved.CompiledSidecarHash, AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
            }
            finally { reopened.StopRecovery(); }
        }
        finally { vm.StopRecovery(); }
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("producer")]
    [InlineData("key")]
    [InlineData("comparison")]
    [InlineData("tolerance")]
    public void Invalid_reference_binding_is_rejected_before_compiled_files_are_changed(string defect)
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "sample.TapPlan");
        var draft = new PlanCompiler().Load(path);
        var node = AuthoringReferenceRules.Metrics(draft.Measure).First();
        var rule = Rule(node);
        draft.Sidecar.ReferenceRules = [rule];
        new PlanCompiler().Save(draft, path);
        rule = defect switch
        {
            "unit" => rule with { Unit = "wrong" },
            "producer" => rule with { ProducerStepId = Guid.NewGuid() },
            "key" => rule with { MetricKey = "wrong" },
            "comparison" => rule with { Comparison = "unknown" },
            _ => rule with { Tolerance = null }
        };
        draft.Sidecar.ReferenceRules = [rule];
        var beforePlan = File.ReadAllBytes(path);
        var beforeSidecar = File.ReadAllBytes(PlanCompiler.SidecarPath(path));
        Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().SaveSidecar(path, draft.Sidecar));
        Assert.Equal(beforePlan, File.ReadAllBytes(path));
        Assert.Equal(beforeSidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(path)));
    }

    [Fact]
    public void Unknown_reference_source_fields_are_read_only_and_original_bytes_are_preserved()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var draft = new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan"));
        draft.Sidecar.ReferenceRules = [Rule(AuthoringReferenceRules.Metrics(draft.Measure).First())];
        var store = new AuthoringDocumentStore(root);
        store.Save(AuthoringDocumentDto.FromDraft(draft));
        var path = store.GetDocumentPath("sample");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["sidecar"]!["referenceRules"]![0]!["futureComparison"] = true;
        File.WriteAllText(path, json.ToJsonString());
        var bytes = File.ReadAllBytes(path);
        var loaded = store.Load("sample");
        Assert.True(loaded.IsReadOnly);
        Assert.Contains("Unsupported", loaded.Error);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Preview_uses_recorded_frozen_values_and_proposed_tolerance_without_mutating_the_recording()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var draft = new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan"));
        var node = AuthoringReferenceRules.Metrics(draft.Measure).First();
        var rule = Rule(node) with { Tolerance = 0.25 };
        draft.Sidecar.ReferenceRules = [rule];
        var run = new TestRunRecord
        {
            RunId = "recorded-reference-run",
            PlanId = draft.PlanId,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            ExecutionScope = RunExecutionScopes.FullSuite,
            Samples = [new() { ProducerStepId = node.NodeId, MetricKey = node.Metric.ChannelKey, Unit = node.Metric.YUnit, Value = 2, Timestamp = DateTimeOffset.UtcNow }],
            ReferenceUses = [new()
            {
                Rule = rule, ReferenceId = "reference-immutable", ReferenceName = "Golden board", ReferenceRevision = 1,
                ReferenceSha256 = "exact-source-hash", SourceRunId = "golden-source-run", SourceDutSerial = "GOLDEN-001",
                ReferenceValue = new() { ReferenceKey = rule.ReferenceKey, MetricKey = rule.MetricKey, ProducerStepId = node.NodeId, Unit = rule.Unit, Value = 1, SampleCount = 1 }
            }]
        };
        var before = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        Assert.Contains("Proposed preview: Fail", AuthoringReferenceReview.Describe(draft, run));
        draft.Sidecar.ReferenceRules = [rule with { Tolerance = 1 }];
        var review = AuthoringReferenceReview.Describe(draft, run);
        Assert.Contains("Proposed preview: Pass", review);
        Assert.Contains("golden-source-run", review);
        Assert.Contains("reference-immutable", review);
        Assert.Contains("exact-source-hash", review);
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "ReferenceTolerance")] = "1e-";
        review = AuthoringReferenceReview.Describe(draft, run);
        Assert.Contains("Proposed preview unavailable: finish the current reference tolerance", review);
        Assert.DoesNotContain("Proposed preview: Pass", review);
        Assert.Contains("reference-immutable", review);
        Assert.Contains("exact-source-hash", review);
        draft.AuthoringState.IncompleteNumericText.Clear();
        draft.Sidecar.ReferenceRules = [rule with { Tolerance = 1, ReferenceKey = string.Empty }];
        review = AuthoringReferenceReview.Describe(draft, run);
        Assert.Contains("Proposed preview unavailable:", review);
        Assert.DoesNotContain("Proposed preview: Pass", review);
        Assert.Equal(before, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
    }

    private static AuthoringWorkspaceViewModel Open(string root)
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.SelectProgram("sample");
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
        return vm;
    }

    [Fact]
    public void Reference_plan_compiles_a_stable_first_setup_guard_and_decompiles_only_editable_source()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(path);
        var originalSetup = draft.Setup.ToArray();
        draft.Sidecar.ReferenceRules = [Rule(AuthoringReferenceRules.Metrics(draft.Measure).First())];
        compiler.Save(draft, path);
        var plan = TestPlan.Load(path);
        var setup = plan.ChildTestSteps.First();
        Assert.Equal(PlanCompiler.SetupGroupName, setup.Name);
        var guard = Assert.IsType<RequireOperatorReferenceAcceptanceStep>(setup.ChildTestSteps.First());
        Assert.Single(PlanCompiler.FlattenSteps(plan).OfType<RequireOperatorReferenceAcceptanceStep>());
        var reopened = compiler.Load(path);
        Assert.Equal(originalSetup, reopened.Setup);
        Assert.DoesNotContain(AuthoringSequence.Flatten(reopened), row => row.NodeId == guard.Id);
        Assert.Equal(draft.Sidecar.ReferenceRules, reopened.Sidecar.ReferenceRules);
        compiler.Save(reopened, path);
        Assert.Equal(guard.Id, Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<RequireOperatorReferenceAcceptanceStep>()).Id);
    }

    [Fact]
    public void Adding_and_removing_reference_requirement_requires_guard_compilation_and_preserves_old_pair_on_sidecar_only_save()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var vm = Open(root);
        try
        {
            var path = Path.Combine(root, "sample.TapPlan");
            Assert.Empty(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<RequireOperatorReferenceAcceptanceStep>());
            vm.RequireGoldenReference = true;
            vm.ReferenceTolerance = "0.5";
            vm.SaveProgram("sample");
            Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<RequireOperatorReferenceAcceptanceStep>());
            var beforePlan = File.ReadAllBytes(path);
            var beforeSidecar = File.ReadAllBytes(PlanCompiler.SidecarPath(path));
            vm.RequireGoldenReference = false;
            vm.SaveSidecar();
            Assert.Contains("REFERENCE_GUARD", vm.Error);
            Assert.Equal(beforePlan, File.ReadAllBytes(path));
            Assert.Equal(beforeSidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(path)));
            vm.SaveProgram("sample");
            Assert.Null(new PlanCompiler().Load(path).Sidecar.ReferenceRules);
            Assert.Empty(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<RequireOperatorReferenceAcceptanceStep>());
            Assert.False(new AuthoringDocumentStore(root).Load("sample").Document!.RequiresCompilation);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Guarded_compiled_program_with_missing_reference_sidecar_cannot_be_imported_as_an_ordinary_plan()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(path);
        draft.Sidecar.ReferenceRules = [Rule(AuthoringReferenceRules.Metrics(draft.Measure).First())];
        compiler.Save(draft, path);
        var before = File.ReadAllBytes(path);
        File.Delete(PlanCompiler.SidecarPath(path));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => compiler.Load(path));
        Assert.Contains("REFERENCE_REQUIREMENTS_MISSING", error.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
    }

    [Fact]
    public void Duplicate_measurement_clones_reference_binding_and_numeric_draft_state_as_one_undoable_edit()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var vm = Open(root);
        try
        {
            vm.RequireGoldenReference = true;
            vm.ReferenceTolerance = "1e-";
            var original = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
            vm.DuplicateSelectedSequence();
            var rules = vm.SelectedProgram.Sidecar.ReferenceRules!;
            Assert.Equal(2, rules.Count);
            var duplicate = Assert.Single(rules, rule => rule.RuleId != original.RuleId);
            Assert.NotEqual(original.ProducerStepId, duplicate.ProducerStepId);
            Assert.NotEqual(original.MetricKey, duplicate.MetricKey);
            Assert.Equal(original.ReferenceKey, duplicate.ReferenceKey);
            Assert.Equal(original.Unit, duplicate.Unit);
            Assert.Equal("1e-", vm.SelectedProgram.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(duplicate.ProducerStepId, AuthoringReferenceRules.ToleranceField(duplicate))]);
            Assert.Equal(2, AuthoringReferenceRules.Metrics(vm.SelectedProgram.Measure).Count(metric => metric.NodeId == original.ProducerStepId || metric.NodeId == duplicate.ProducerStepId));
            vm.Undo();
            Assert.Equal(original, Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!));
            vm.Redo();
            Assert.Contains(vm.SelectedProgram.Sidecar.ReferenceRules!, rule => rule == duplicate);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Removing_measurement_removes_only_its_reference_requirements_and_undo_restores_them()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var vm = Open(root);
        try
        {
            vm.CreateDemoProgram("reference-remove");
            vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
            vm.RequireGoldenReference = true;
            vm.ReferenceTolerance = "0.5";
            var original = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
            Assert.True(vm.CanRemoveSelectedSequence);
            vm.RemoveSelectedSequence();
            Assert.Null(vm.SelectedProgram.Sidecar.ReferenceRules);
            Assert.DoesNotContain(AuthoringReferenceRules.Validate(vm.SelectedProgram), finding => finding.NodeId == original.ProducerStepId);
            vm.Undo();
            Assert.Equal(original, Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!));
            Assert.Contains(AuthoringReferenceRules.Metrics(vm.SelectedProgram.Measure), metric => metric.NodeId == original.ProducerStepId);
            vm.Redo();
            Assert.Null(vm.SelectedProgram.Sidecar.ReferenceRules);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Duplicating_multicriterion_measurement_remaps_each_rules_unfinished_tolerance_independently()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var vm = Open(root);
        try
        {
            vm.RequireGoldenReference = true;
            vm.ReferenceKey = "golden.first";
            vm.ReferenceTolerance = "1e-";
            vm.AddReferenceCriterion();
            vm.ReferenceKey = "golden.second";
            vm.ReferenceTolerance = "2e-";
            var original = vm.SelectedProgram!.Sidecar.ReferenceRules!.ToArray();
            vm.DuplicateSelectedSequence();
            var duplicates = vm.SelectedProgram.Sidecar.ReferenceRules!.Where(rule => rule.ProducerStepId != original[0].ProducerStepId).ToArray();
            Assert.Equal(2, duplicates.Length);
            foreach (var cloned in duplicates)
            {
                Assert.DoesNotContain(original, rule => rule.RuleId == cloned.RuleId);
                var expected = cloned.ReferenceKey == "golden.first" ? "1e-" : "2e-";
                Assert.True(AuthoringReferenceRules.TryIncompleteTolerance(vm.SelectedProgram, cloned, out var unfinished));
                Assert.Equal(expected, unfinished);
            }
            Assert.Equal(4, vm.SelectedProgram.AuthoringState.IncompleteNumericText.Count);
            vm.Undo();
            Assert.Equal(original, vm.SelectedProgram.Sidecar.ReferenceRules);
            Assert.Equal(2, vm.SelectedProgram.AuthoringState.IncompleteNumericText.Count);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Catalog_file_selection_preserves_reference_metadata_and_exact_artifact_hashes()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "sample.TapPlan");
        var draft = new PlanCompiler().Load(path);
        var rule = Rule(AuthoringReferenceRules.Metrics(draft.Measure).First());
        draft.Sidecar.ReferenceRules = [rule];
        new PlanCompiler().Save(draft, path);
        var entry = ProgramCatalog.FromFile(path);
        Assert.Equal(rule, Assert.Single(entry.ReferenceRules));
        Assert.Null(entry.ReferenceDeclarationError);
        Assert.Equal(BenchProgramHandoff.Hash(File.ReadAllBytes(path)), entry.PlanSha256);
        Assert.Equal(BenchProgramHandoff.Hash(File.ReadAllBytes(PlanCompiler.SidecarPath(path))), entry.SidecarSha256);
        var json = JsonNode.Parse(File.ReadAllText(PlanCompiler.SidecarPath(path)))!;
        json["referenceRules"]![0]!["futureComparison"] = "not-understood";
        File.WriteAllText(PlanCompiler.SidecarPath(path), json.ToJsonString());
        Assert.NotNull(ProgramCatalog.FromFile(path).ReferenceDeclarationError);
    }

    [Fact]
    public void Known_previous_run_import_is_an_exact_read_only_preview_copy()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var source = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "run.json");
        var record = new TestRunRecord
        {
            SchemaVersion = 4,
            RunId = "previous-schema-run",
            PlanId = "sample",
            Samples = [new() { MetricKey = "VDC", Unit = "V", Value = 1 }]
        };
        File.WriteAllText(source, JsonSerializer.Serialize(record, AppJsonContext.Default.TestRunRecord));
        var bytes = File.ReadAllBytes(source);
        var imported = RunDatasetCatalog.Import(AuthoringWorkspaceLoader.Load(root), source, "previous-preview", "sample");
        Assert.True(imported.Run.IsSchemaReadOnly);
        Assert.Equal(4, imported.Run.StoredSchemaVersion);
        Assert.Equal(bytes, File.ReadAllBytes(imported.Path));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        var vm = Open(root);
        try
        {
            vm.SelectDataset(vm.Datasets.ToList().FindIndex(dataset => dataset.Run.RunId == record.RunId));
            Assert.Contains("Historical schema 4", vm.DataSourceDetails);
            Assert.Contains("read-only sample preview", vm.DataSourceDetails);
            Assert.Contains("read-only", Assert.Single(vm.DatasetItems));
            Assert.Single(vm.SelectedDataset!.Run.Samples);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Future_run_schema_is_still_refused_for_import_without_changing_source_bytes()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var source = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "run.json");
        var record = new TestRunRecord { SchemaVersion = SchemaVersions.TestRunRecord + 1, RunId = "future-run", PlanId = "sample" };
        File.WriteAllText(source, JsonSerializer.Serialize(record, AppJsonContext.Default.TestRunRecord));
        var bytes = File.ReadAllBytes(source);
        Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Import(AuthoringWorkspaceLoader.Load(root), source, "future-preview", "sample"));
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    [Fact]
    public void Multiple_imported_criteria_are_disclosed_and_first_edits_preserve_order_and_other_rules()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(path);
        var first = Rule(AuthoringReferenceRules.Metrics(draft.Measure).First());
        var second = first with { RuleId = "other-criterion", ReferenceKey = "golden.secondary", Comparison = "percent", Tolerance = 2 };
        draft.Sidecar.ReferenceRules = [first, second];
        compiler.Save(draft, path);
        var vm = Open(root);
        try
        {
            Assert.Contains("all 2", vm.ReferenceRequirementLabel);
            Assert.Contains("uncheck removes all", vm.ReferenceRequirementLabel);
            Assert.Contains("Select a criterion", vm.ReferenceCriteriaOverview);
            Assert.Contains(vm.ReferenceCriterionOptions, option => option.Label.Contains(first.ReferenceKey, StringComparison.Ordinal));
            Assert.Contains(vm.ReferenceCriterionOptions, option => option.Label.Contains(second.ReferenceKey, StringComparison.Ordinal)
                && option.Label.Contains(BenchReferenceEvidence.DescribeRule(second), StringComparison.Ordinal));
            var options = vm.ReferenceCriterionOptions;
            vm.SelectReferenceCriterion(second.RuleId);
            Assert.Same(options, vm.ReferenceCriterionOptions);
            Assert.Same(options[1], vm.SelectedReferenceCriterion);
            vm.SelectReferenceCriterion(first.RuleId);
            vm.ReferenceKey = "golden.first.edited";
            vm.ReferenceTolerance = "0.75";
            var edited = vm.SelectedProgram!.Sidecar.ReferenceRules!.ToArray();
            Assert.Equal(first.RuleId, edited[0].RuleId);
            Assert.Equal("golden.first.edited", edited[0].ReferenceKey);
            Assert.Equal(0.75, edited[0].Tolerance);
            Assert.Equal(second, edited[1]);
            vm.RequireGoldenReference = false;
            Assert.Null(vm.SelectedProgram.Sidecar.ReferenceRules);
            vm.Undo();
            Assert.Equal(edited, vm.SelectedProgram.Sidecar.ReferenceRules);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Criterion_selection_retains_per_rule_unfinished_text_and_repairs_selected_rule_after_reopen()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var path = Path.Combine(root, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(path);
        var first = Rule(AuthoringReferenceRules.Metrics(draft.Measure).First());
        var second = first with { RuleId = "second-criterion", ReferenceKey = "golden.second", Comparison = "percent", Tolerance = 2 };
        draft.Sidecar.ReferenceRules = [first, second];
        compiler.Save(draft, path);
        var vm = Open(root);
        try
        {
            vm.SelectReferenceCriterion(second.RuleId);
            vm.ReferenceTolerance = "2e-";
            vm.ReferenceKey = "golden.second.edited";
            Assert.Equal(second.RuleId, vm.SelectedReferenceCriterion!.RuleId);
            vm.SelectReferenceCriterion(first.RuleId);
            Assert.Equal("0.5", vm.ReferenceTolerance);
            Assert.Empty(vm.ReferenceToleranceError);
            vm.ReferenceTolerance = "0.75";
            vm.SelectReferenceCriterion(second.RuleId);
            Assert.Equal("2e-", vm.ReferenceTolerance);
            Assert.NotEmpty(vm.ReferenceToleranceError);
            vm.SaveProgram("sample");
            var reopened = Open(root);
            try
            {
                reopened.SelectReferenceCriterion(second.RuleId);
                Assert.Equal("2e-", reopened.ReferenceTolerance);
                Assert.Equal("golden.second.edited", reopened.ReferenceKey);
                reopened.ReferenceTolerance = "3";
                reopened.Undo();
                Assert.Equal("2e-", reopened.ReferenceTolerance);
                reopened.Redo();
                Assert.Equal("3", reopened.ReferenceTolerance);
                reopened.YUnit = "mV";
                Assert.NotEmpty(reopened.ReferenceTargetError);
                reopened.RebindReferenceToMeasurement();
                Assert.Empty(reopened.ReferenceTargetError);
                Assert.Equal("mV", reopened.SelectedProgram!.Sidecar.ReferenceRules!.Single(rule => rule.RuleId == second.RuleId).Unit);
                Assert.Equal(first.Unit, reopened.SelectedProgram.Sidecar.ReferenceRules!.Single(rule => rule.RuleId == first.RuleId).Unit);
                reopened.ReferenceTolerance = "3e-";
                reopened.RemoveSelectedReferenceCriterion();
                Assert.Single(reopened.SelectedProgram.Sidecar.ReferenceRules!);
                Assert.DoesNotContain(reopened.SelectedProgram.AuthoringState.IncompleteNumericText.Keys, key => key.EndsWith(":" + second.RuleId, StringComparison.Ordinal));
                reopened.Undo();
                Assert.Equal(second.RuleId, reopened.SelectedReferenceCriterion!.RuleId);
                Assert.Equal("3e-", reopened.ReferenceTolerance);
                reopened.AddReferenceCriterion();
                var added = reopened.SelectedReferenceCriterion!.RuleId;
                Assert.NotEqual(first.RuleId, added);
                Assert.NotEqual(second.RuleId, added);
                Assert.Equal(3, reopened.SelectedProgram.Sidecar.ReferenceRules!.Count);
                Assert.Equal(string.Empty, reopened.ReferenceTolerance);
                reopened.Undo();
                Assert.Equal(2, reopened.SelectedProgram.Sidecar.ReferenceRules!.Count);
            }
            finally { reopened.StopRecovery(); }
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Replacing_a_recording_with_another_run_of_the_same_plan_during_import_is_rejected()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var source = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "run.json");
        var record = new TestRunRecord { RunId = "expected-run", PlanId = "sample" };
        File.WriteAllText(source, JsonSerializer.Serialize(record, AppJsonContext.Default.TestRunRecord));
        Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Import(AuthoringWorkspaceLoader.Load(root), source, "changed-preview", "sample", () =>
        {
            record.RunId = "different-run";
            File.WriteAllText(source, JsonSerializer.Serialize(record, AppJsonContext.Default.TestRunRecord));
        }));
        Assert.False(Directory.Exists(Path.Combine(RunDatasetCatalog.ResolveRecordingsRoot(AuthoringWorkspaceLoader.Load(root)), "changed-preview")));
    }

    private static BenchReferenceRule Rule(MetricNode node) => new()
    {
        RuleId = "reference-criterion",
        MetricKey = node.Metric.ChannelKey,
        ProducerStepId = node.NodeId,
        ReferenceKey = "golden.voltage",
        Unit = node.Metric.YUnit,
        Comparison = "absolute",
        Tolerance = 0.5
    };
}
