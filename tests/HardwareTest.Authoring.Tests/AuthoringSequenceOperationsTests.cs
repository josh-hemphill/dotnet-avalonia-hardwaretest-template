using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSequenceOperationsTests
{
    [Fact]
    public void Insertion_and_repeat_use_selected_nested_target()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.BandScalar);
        var first = draft.Measure[0];
        var last = draft.Measure[1];
        var wrapped = AuthoringSequenceOperations.Repeat(draft, Row(draft, first.NodeId));
        Assert.Equal(last.NodeId, wrapped.Measure[1].NodeId);
        var loop = Assert.IsType<RepeatNode>(wrapped.Measure[0]);
        Assert.NotEqual(first.NodeId, loop.NodeId);
        Assert.Equal(first.NodeId, Assert.Single(loop.Children).NodeId);
        var inserted = AuthoringSequenceOperations.Insert(wrapped, AuthoringRecipeIds.BandScalar,
            Row(wrapped, first.NodeId), before: true, "DMM");
        var children = Assert.IsType<RepeatNode>(inserted.Measure[0]).Children;
        Assert.Equal("rail.mean_2", Assert.IsType<MetricNode>(children[0]).Metric.ChannelKey);
        Assert.Equal(first.NodeId, children[1].NodeId);
        Assert.Equal(last.NodeId, inserted.Measure[1].NodeId);
    }

    [Fact]
    public void Nested_duplication_remaps_internal_refs_keeps_external_refs_and_copies_source_state()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.MeanGte);
        var external = Assert.IsType<MetricNode>(draft.Measure[1]);
        var producer = draft.Measure[0];
        var template = external.Metric;
        var formula = new MetricNode(template with
        {
            Name = "Mix",
            ChannelKey = "mix",
            Source = new ExpressionAlgorithm(["VDC", "VDC.mean"], "mean(VDC) + VDC.mean")
        });
        var inner = new RepeatNode(3, [producer, formula]);
        draft = draft with { Measure = [external, new RepeatNode(2, [inner])] };
        draft.AuthoringState.FormulaIntent[formula.NodeId] = FormulaDeploymentIntent.Explore;
        draft.AuthoringState.IncompleteNumericText[$"{formula.NodeId:D}/Threshold"] = "-";
        var selected = draft.Measure[1];
        // External reference in this expression is deliberately outside the loop, so it is
        // already incomplete. Remapping must preserve it without inventing a producer.
        var duplicate = AuthoringSequenceOperations.Duplicate(draft, Row(draft, selected.NodeId));
        var clonedLoop = Assert.IsType<RepeatNode>(duplicate.Measure[2]);
        var clonedInner = Assert.IsType<RepeatNode>(Assert.Single(clonedLoop.Children));
        var clonedProducer = Assert.IsType<MetricNode>(clonedInner.Children[0]);
        var clonedFormula = Assert.IsType<MetricNode>(clonedInner.Children[1]);
        var source = Assert.IsType<ExpressionAlgorithm>(clonedFormula.Metric.Source);
        Assert.Equal(new[] { "VDC_2", "VDC.mean" }, source.InputChannelKeys);
        Assert.Equal("mean(VDC_2) + VDC.mean", source.Source);
        Assert.Equal(FormulaDeploymentIntent.Explore, duplicate.AuthoringState.FormulaIntent[clonedFormula.NodeId]);
        Assert.Equal("-", duplicate.AuthoringState.IncompleteNumericText[$"{clonedFormula.NodeId:D}/Threshold"]);
        var ids = AuthoringDependencyIndex.Build(duplicate).Nodes.Select(node => node.NodeId).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        var channels = AuthoringRecipeCatalog.EnumerateMetrics(duplicate.Measure).Select(metric => metric.ChannelKey).ToArray();
        Assert.Equal(channels.Length, channels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Formula_remapping_preserves_scientific_literals_and_function_names()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var producer = Assert.IsType<MetricNode>(draft.Measure[0]);
        producer = producer with { Metric = producer.Metric with { ChannelKey = "e3" } };
        var formula = Assert.IsType<MetricNode>(draft.Measure[1]);
        formula = formula with { Metric = formula.Metric with { Source = new ExpressionAlgorithm(["e3"], "mean(e3)+1e3+1.2e-3") } };
        var loop = new RepeatNode(2, [producer, formula]);
        draft = draft with { Measure = [loop] };
        draft.AuthoringState.FormulaIntent[formula.NodeId] = FormulaDeploymentIntent.Explore;
        var duplicate = AuthoringSequenceOperations.Duplicate(draft, Row(draft, loop.NodeId));
        var copied = Assert.IsType<RepeatNode>(duplicate.Measure[1]);
        var expression = Assert.IsType<ExpressionAlgorithm>(Assert.IsType<MetricNode>(copied.Children[1]).Metric.Source);
        Assert.Equal("mean(e3_2)+1e3+1.2e-3", expression.Source);
        Assert.NotNull(FormulaParser.Parse(expression.Source));
    }

    [Fact]
    public void Selected_incomplete_deployment_consumer_duplicates_without_losing_its_raw_field()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var consumer = draft.Measure[1];
        draft.AuthoringState.IncompleteNumericText[$"{consumer.NodeId:D}/Threshold"] = "-";
        var duplicated = AuthoringSequenceOperations.Duplicate(draft, Row(draft, consumer.NodeId));
        var clone = Assert.IsType<MetricNode>(duplicated.Measure[2]);
        Assert.Equal("-", duplicated.AuthoringState.IncompleteNumericText[$"{clone.NodeId:D}/Threshold"]);
        Assert.Equal(new[] { "VDC" }, Assert.IsType<ExpressionAlgorithm>(clone.Metric.Source).InputChannelKeys);
        Assert.Equal("VDC.mean_2", clone.Metric.ChannelKey);
    }

    [Fact]
    public void Compiled_nested_clone_uses_its_own_producer_and_external_sibling_reference_survives()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var loop = new RepeatNode(2, draft.Measure);
        draft = draft with { Measure = [new RepeatNode(3, [loop])] };
        var duplicate = AuthoringSequenceOperations.Duplicate(draft, Row(draft, draft.Measure[0].NodeId));
        var path = Path.Combine(TemporaryDirectory(), "operations.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(duplicate, path);
        var reload = compiler.Load(path);
        var copied = Assert.IsType<RepeatNode>(Assert.Single(Assert.IsType<RepeatNode>(reload.Measure[1]).Children));
        var cloneProducer = Assert.IsType<MetricNode>(copied.Children[0]);
        var cloneCheck = Assert.IsType<MetricNode>(copied.Children[1]);
        Assert.Equal("VDC_2", cloneProducer.Metric.ChannelKey);
        Assert.Equal(new[] { cloneProducer.Metric.ChannelKey }, Assert.IsType<AlgorithmSource>(cloneCheck.Metric.Source).InputChannelKeys);
        var flat = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        flat = AuthoringSequenceOperations.Duplicate(flat, Row(flat, flat.Measure[1].NodeId));
        compiler.Save(flat, path);
        var checks = compiler.Load(path).Measure.OfType<MetricNode>().Skip(1).ToArray();
        Assert.All(checks, check => Assert.Equal(new[] { "VDC" }, Assert.IsType<AlgorithmSource>(check.Metric.Source).InputChannelKeys));
    }

    [Fact]
    public void Move_and_repeat_reject_new_dependency_scope_errors_and_opaque_order()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var consumer = draft.Measure[1];
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Move(draft, Row(draft, consumer.NodeId), -1));
        Assert.Contains("VDC", error.Message, StringComparison.Ordinal);
        Assert.Contains("preceding", error.Message, StringComparison.Ordinal);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Repeat(draft, Row(draft, draft.Measure[0].NodeId)));
        var raw = new RawStepNode("Unknown", "<TestStep type='Unknown'/>");
        var opaque = draft with { Measure = [draft.Measure[0], raw, consumer] };
        Assert.Contains("Opaque", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Move(opaque,
            Row(opaque, consumer.NodeId), -1)).Message, StringComparison.Ordinal);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Duplicate(opaque, Row(opaque, raw.NodeId)));
        Assert.Same(draft, AuthoringSequenceOperations.Move(draft, Row(draft, draft.Measure[0].NodeId), -1));
        var invalidFormula = Assert.IsType<MetricNode>(consumer);
        invalidFormula = invalidFormula with { Metric = invalidFormula.Metric with { Source = new ExpressionAlgorithm([], "unknown(") } };
        var unparsed = draft with { Measure = [draft.Measure[0], invalidFormula] };
        Assert.Contains("Opaque", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Move(unparsed,
            Row(unparsed, invalidFormula.NodeId), -1)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void All_legacy_adds_and_duplicate_compile_with_unique_presentation_and_publisher_aliases()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.MeanGte, AuthoringRecipeIds.BandScalar,
            AuthoringRecipeIds.SeriesCompliance, AuthoringRecipeIds.StationHealth, AuthoringRecipeIds.Formula);
        foreach (var recipe in new[] { AuthoringRecipeIds.MeanGte, AuthoringRecipeIds.SeriesCompliance, AuthoringRecipeIds.StationHealth })
            Assert.Throws<AuthoringWorkspaceException>(() => AuthoringRecipeCatalog.Apply(draft, recipe));
        foreach (var recipe in new[] { AuthoringRecipeIds.Acquire, AuthoringRecipeIds.BandScalar, AuthoringRecipeIds.Formula })
            draft = AuthoringRecipeCatalog.Apply(draft, recipe);
        var band = draft.Measure.OfType<MetricNode>().First(node => node.Metric.Name == "Band Scalar");
        draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, band.NodeId));
        var path = Path.Combine(TemporaryDirectory(), "operations.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(draft, path);
        var reloaded = compiler.Load(path);
        var metrics = AuthoringRecipeCatalog.EnumerateMetrics(reloaded.Measure).ToArray();
        Assert.Equal(metrics.Length, metrics.Select(metric => metric.ChannelKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var bands = metrics.Where(metric => metric.Name == "Band Scalar").ToArray();
        Assert.Equal(3, bands.Length);
        foreach (var metric in bands)
            Assert.Equal(metric.ChannelKey, Assert.IsType<AlgorithmSource>(metric.Source).Settings["MetricName"]);
        var xml = XDocument.Load(path);
        var aliases = xml.Descendants().Where(element => element.Name.LocalName == "MetricName").Select(element => element.Value).ToArray();
        Assert.Contains("rail.mean_3", aliases);
        Assert.Equal(aliases.Length, aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Palette_history_preserve_incomplete_drafts_noops_and_failed_commands()
    {
        var vm = new AuthoringWorkspaceViewModel();
        var root = TemporaryDirectory();
        File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Sequence\",\"plansDirectory\":\".\"}");
        vm.Open(root);
        vm.CreateDemoProgram("operations");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.Threshold = "-";
        vm.FormulaExplorationOnly = true;
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        var formulaId = vm.SelectedSequence!.NodeId!.Value;
        vm.RecipeSearch = "mean";
        Assert.All(vm.Recipes, recipe => Assert.Contains("mean", $"{recipe.ListLabel} {recipe.Summary}".ToLowerInvariant(), StringComparison.Ordinal));
        vm.SelectedRecipeId = AuthoringRecipeIds.MeanGte;
        vm.InsertionPosition = "After selected";
        Assert.True(vm.CanInsertRecipe);
        vm.InsertSelectedRecipe();
        Assert.Equal("VDC.mean_2", Assert.IsType<MetricNode>(vm.SelectedProgram!.Measure[^1]).Metric.ChannelKey);
        vm.Undo();
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        Assert.True(vm.CanRedo);
        var consumerRow = Row(vm.SelectedProgram!, formulaId);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == consumerRow.NodeId));
        vm.MoveSelectedSequence(-1);
        Assert.Contains("VDC", vm.Error!, StringComparison.Ordinal);
        Assert.True(vm.CanRedo);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == vm.SelectedProgram!.Measure[0].NodeId));
        vm.MoveSelectedSequence(-1);
        Assert.True(vm.CanRedo);
        vm.SequenceRename = "Voltage";
        vm.RenameSelectedSequence();
        vm.Undo();
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
    }

    [Fact]
    public void Palette_preflight_explains_missing_inputs_section_and_instrument_compatibility()
    {
        var vm = new AuthoringWorkspaceViewModel();
        var root = TemporaryDirectory();
        File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Sequence\",\"plansDirectory\":\".\"}");
        vm.Open(root);
        vm.CreateDemoProgram("operations");
        vm.SelectedRecipeId = AuthoringRecipeIds.Formula;
        Assert.False(vm.CanInsertRecipe);
        Assert.Contains("Measure", vm.SelectedRecipePrerequisites, StringComparison.Ordinal);
        vm.InsertionPosition = "End of section";
        Assert.False(vm.CanInsertRecipe);
        Assert.Contains("VDC", vm.SelectedRecipePrerequisites, StringComparison.Ordinal);
        vm.SelectedRecipeId = AuthoringRecipeIds.Acquire;
        Assert.True(vm.CanInsertRecipe);
        vm.InsertSelectedRecipe();
        vm.SelectedRecipeId = AuthoringRecipeIds.Repeat;
        Assert.True(vm.CanInsertRecipe);
        Assert.Contains("Acquire VDC", vm.SelectedRecipePrerequisites, StringComparison.Ordinal);
        vm.SelectedRecipeId = AuthoringRecipeIds.Acquire;
        vm.ReplaceSelected(vm.SelectedProgram! with { Instruments = [vm.SelectedProgram.Instruments[0] with { TypeId = "Unsupported.Instrument" }] });
        Assert.False(vm.CanInsertRecipe);
        Assert.Contains("INSTRUMENT", vm.SelectedRecipePrerequisites, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Derived_recipe_uses_preceding_inputs_from_the_explicit_loop_scope()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Acquire);
        var outer = draft.Measure[0];
        var inner = draft.Measure[1];
        draft = draft with { Measure = [outer, new RepeatNode(2, [inner])] };
        var inserted = AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Formula, Row(draft, inner.NodeId), false);
        var loop = Assert.IsType<RepeatNode>(inserted.Measure[1]);
        var formula = Assert.IsType<MetricNode>(loop.Children[1]);
        Assert.Equal(new[] { "VDC_2" }, Assert.IsType<ExpressionAlgorithm>(formula.Metric.Source).InputChannelKeys);
        Assert.Equal("mean(VDC_2)", Assert.IsType<ExpressionAlgorithm>(formula.Metric.Source).Source);
        var path = Path.Combine(TemporaryDirectory(), "operations.TapPlan");
        new PlanCompiler().Save(inserted, path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Operations_resolve_stale_paths_by_stable_identity_and_reject_removed_targets()
    {
        var draft = ProgramWith(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.MeanGte);
        var selected = Row(draft, draft.Measure[1].NodeId);
        var reordered = draft with { Measure = [draft.Measure[1], draft.Measure[0]] };
        var renamed = AuthoringSequenceOperations.Rename(reordered, selected, "Selected check");
        Assert.Equal("Selected check", Assert.IsType<MetricNode>(renamed.Measure[0]).Metric.Name);
        Assert.Equal("Acquire VDC", Assert.IsType<MetricNode>(renamed.Measure[1]).Metric.Name);
        var removed = draft with { Measure = [draft.Measure[0]] };
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Rename(removed, selected, "Gone"));
    }

    [Theory]
    [InlineData(AuthoringRecipeIds.Formula)]
    [InlineData(AuthoringRecipeIds.TransferFunction)]
    public void Recognized_raw_publishers_share_compiler_insertion_and_output_allocation(string recipe)
    {
        RawStepNode Raw(double elapsed)
        {
            AuthoringPluginSearch.Search();
            var step = new PublishTimedSampleStep { Channel = "VDC", ElapsedMs = elapsed, Value = 2 };
            var plan = new TestPlan();
            plan.ChildTestSteps.Add(step);
            var rawPath = Path.Combine(TemporaryDirectory(), "raw.TapPlan");
            plan.Save(rawPath);
            var xml = XDocument.Load(rawPath).Descendants().First(element => element.Name.LocalName == "TestStep");
            return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        }
        var first = Raw(0);
        var last = recipe == AuthoringRecipeIds.Formula ? first : Raw(5);
        var draft = ProgramWith() with { Measure = recipe == AuthoringRecipeIds.Formula ? [first] : [first, last] };
        var inserted = AuthoringSequenceOperations.Insert(draft, recipe, Row(draft, last.NodeId), false);
        var path = Path.Combine(TemporaryDirectory(), "operations.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(inserted, path);
        Assert.NotNull(compiler.Load(path));
        var acquired = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
        Assert.Equal("VDC_2", Assert.IsType<MetricNode>(acquired.Measure[^1]).Metric.ChannelKey);
        compiler.Save(acquired, path);
        var saved = TestPlan.Load(path);
        Assert.Equal("VDC_2", Assert.Single(PlanCompiler.FlattenSteps(saved).OfType<AcquireVoltageStep>()).Channel);
        var duplicated = AuthoringSequenceOperations.Duplicate(acquired, Row(acquired, acquired.Measure[^1].NodeId));
        Assert.Equal("VDC_2_2", Assert.IsType<MetricNode>(duplicated.Measure[^1]).Metric.ChannelKey);
        compiler.Save(duplicated, path);
        var outputs = PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<AcquireVoltageStep>().Select(step => step.Channel).ToArray();
        Assert.Equal(new[] { "VDC_2", "VDC_2_2" }, outputs);
    }

    [Fact]
    public void Raw_station_scalar_output_rejects_another_fixed_station_publisher()
    {
        AuthoringPluginSearch.Search();
        var step = new ReportStationHealthStep();
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step);
        var path = Path.Combine(TemporaryDirectory(), "raw.TapPlan"); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
        var raw = new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        var draft = ProgramWith() with { Measure = [raw] };
        var snapshot = AuthoringDocumentSnapshot.Capture(draft);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.StationHealth));
        Assert.Contains(ReportStationHealthStep.OffsetMetric, error.Message, StringComparison.Ordinal);
        Assert.True(snapshot.ContentEquals(AuthoringDocumentSnapshot.Capture(draft)));
    }

    private static SequenceRow Row(ProgramDraft draft, Guid id) => AuthoringSequence.Flatten(draft).Single(row => row.NodeId == id);
    private static ProgramDraft ProgramWith(params string[] recipes)
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("operations");
        foreach (var recipe in recipes) draft = AuthoringRecipeCatalog.Apply(draft, recipe);
        return draft;
    }
    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ht-sequence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
