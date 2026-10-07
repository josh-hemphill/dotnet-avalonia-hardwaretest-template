using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSequenceSafetyTests
{
    [Theory]
    [InlineData(AuthoringRecipeIds.MeanGte, "palette")]
    [InlineData(AuthoringRecipeIds.MeanGte, "factory")]
    [InlineData(AuthoringRecipeIds.MeanGte, "duplicate")]
    [InlineData(AuthoringRecipeIds.StationHealth, "palette")]
    [InlineData(AuthoringRecipeIds.StationHealth, "factory")]
    [InlineData(AuthoringRecipeIds.StationHealth, "duplicate")]
    [InlineData(AuthoringRecipeIds.SeriesCompliance, "palette")]
    [InlineData(AuthoringRecipeIds.SeriesCompliance, "factory")]
    [InlineData(AuthoringRecipeIds.SeriesCompliance, "duplicate")]
    public void Fixed_output_conflicts_reject_new_producers_without_history_mutation(string recipe, string operation)
    {
        var draft = Program(recipe);
        var selected = draft.Measure[0];
        var session = WithRedo(draft, selected.NodeId);
        var before = session.Snapshot;
        var revision = session.Revision;
        var error = Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit(operation, current => operation switch
        {
            "palette" => AuthoringSequenceOperations.Insert(current, recipe, Row(current, selected.NodeId), false),
            "factory" => AuthoringRecipeCatalog.Apply(current, recipe),
            _ => AuthoringSequenceOperations.Duplicate(current, Row(current, selected.NodeId))
        }));
        Assert.Contains("runtime output", error.Message, StringComparison.Ordinal);
        Assert.True(before.ContentEquals(session.Snapshot));
        Assert.Equal(revision, session.Revision);
        Assert.True(session.CanRedo);
        Assert.False(session.CanUndo);
        Assert.Equal(selected.NodeId, session.SelectedNodeId);
        var results = Execute(session.Draft);
        var expected = recipe switch
        {
            AuthoringRecipeIds.MeanGte => new[] { "Mean" },
            AuthoringRecipeIds.StationHealth => new[] { ReportStationHealthStep.OffsetMetric, ReportStationHealthStep.AgeMetric },
            _ => new[] { "series.inband.pct", "series.excursion.max", "series.outband.ms" }
        };
        Assert.Equal(expected, results.Scalars);
        Assert.Equal(results.Scalars.Count, results.Scalars.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Running the same producer again through a loop does not introduce another producer.
        var repeated = AuthoringSequenceOperations.Repeat(draft, Row(draft, selected.NodeId));
        Assert.Equal(selected.NodeId, Assert.Single(Assert.IsType<RepeatNode>(repeated.Measure[0]).Children).NodeId);
        Assert.Equal(expected.Concat(expected), Execute(repeated).Scalars);
    }

    [Theory]
    [InlineData("duplicate", false)]
    [InlineData("repeat", false)]
    [InlineData("move", false)]
    [InlineData("duplicate", true)]
    [InlineData("repeat", true)]
    [InlineData("move", true)]
    public void Compiler_accepted_scalar_source_forms_support_structural_edits(string operation, bool ignoredAlgorithmInputs)
    {
        var draft = Program(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.BandScalar);
        var scalar = Assert.IsType<MetricNode>(draft.Measure[1]);
        var source = Assert.IsType<AlgorithmSource>(scalar.Metric.Source);
        scalar = scalar with
        {
            Metric = scalar.Metric with
            {
                Source = ignoredAlgorithmInputs
            ? source with { InputChannelKeys = ["ignored.missing"] }
            : new MeasureSource("", source.AlgorithmId, source.Settings)
            }
        };
        draft = draft with { Measure = [draft.Measure[0], scalar] };
        Assert.False(AuthoringDependencyIndex.Build(draft).HasOpaqueReferences);
        Assert.Empty(AuthoringDependencyIndex.Build(draft).Nodes.Single(node => node.NodeId == scalar.NodeId).InputChannels);
        var updated = operation switch
        {
            "duplicate" => AuthoringSequenceOperations.Duplicate(draft, Row(draft, scalar.NodeId)),
            "repeat" => AuthoringSequenceOperations.Repeat(draft, Row(draft, scalar.NodeId)),
            _ => AuthoringSequenceOperations.Move(draft, Row(draft, scalar.NodeId), -1)
        };
        var expected = operation == "duplicate" ? new[] { "rail.mean", "rail.mean_2" }
            : operation == "repeat" ? new[] { "rail.mean", "rail.mean" } : new[] { "rail.mean" };
        Assert.Equal(expected, Execute(updated).Scalars);
    }

    [Theory]
    [InlineData(AuthoringFunctionIds.BasicChannelAverage, false)]
    [InlineData(AuthoringFunctionIds.BasicApplyTransferFunction, false)]
    [InlineData(AuthoringFunctionIds.BasicApplyTransferFunction, true)]
    public void Effective_settings_consumers_remap_internal_inputs_and_reject_invalid_order(string function, bool algorithmSource)
    {
        var recipe = function == AuthoringFunctionIds.BasicChannelAverage ? AuthoringRecipeIds.Formula : AuthoringRecipeIds.TransferFunction;
        var draft = Program(AuthoringRecipeIds.Acquire, recipe);
        var consumer = Assert.IsType<MetricNode>(draft.Measure[1]);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inputchannel"] = "VDC",
            ["TsSeconds"] = "0.005",
            ["Numerator"] = "0.5,0.5",
            ["Denominator"] = "1",
            ["Method"] = "filter"
        };
        consumer = consumer with
        {
            Metric = consumer.Metric with
            {
                Source = algorithmSource
            ? new AlgorithmSource(function, ["ignored.missing"], settings)
            : new MeasureSource("", function, settings)
            }
        };
        draft = draft with { Measure = [draft.Measure[0], consumer] };
        Assert.Equal(new[] { "VDC" }, AuthoringDependencyIndex.Build(draft).Nodes.Single(node => node.NodeId == consumer.NodeId).InputChannels);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Move(draft, Row(draft, consumer.NodeId), -1));
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Repeat(draft, Row(draft, draft.Measure[0].NodeId)));
        var loop = new RepeatNode(1, draft.Measure);
        draft = draft with { Measure = [loop] };
        var duplicated = AuthoringSequenceOperations.Duplicate(draft, Row(draft, loop.NodeId));
        var clone = Assert.IsType<MetricNode>(Assert.IsType<RepeatNode>(duplicated.Measure[1]).Children[1]);
        var clonedSettings = clone.Metric.Source switch
        {
            MeasureSource source => source.Settings,
            AlgorithmSource source => source.Settings,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("VDC_2", clonedSettings["inputchannel"]);
        Assert.Equal("VDC", settings["inputchannel"]);
        var results = Execute(duplicated, plan =>
        {
            var steps = PlanCompiler.FlattenSteps(plan).ToArray();
            var producer = steps.OfType<AcquireVoltageStep>().Single(step => step.Channel == "VDC_2");
            if (function == AuthoringFunctionIds.BasicChannelAverage)
            {
                var check = steps.OfType<ChannelAverageStep>().Single(step => step.Channel == "VDC.mean_2");
                Assert.Equal(producer.Id, check.ProducerStepId);
                Assert.Equal("VDC_2", check.InputChannel);
            }
            else Assert.Equal("VDC_2", steps.OfType<ApplyTransferFunctionStep>().Single(step => step.Channel == "VDC.filt_2").InputChannel);
        });
        Assert.Contains("VDC_2", results.Samples);
        if (function == AuthoringFunctionIds.BasicChannelAverage) Assert.Contains("VDC.mean_2", results.Scalars);
        else Assert.Contains("VDC.filt_2", results.Samples);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("duplicate")]
    [InlineData("move")]
    public void Unrelated_unfinished_settings_preserve_edits_history_and_dependency_rejection(string operation)
    {
        var draft = Program(AuthoringRecipeIds.BandScalar, AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var scalar = Assert.IsType<MetricNode>(draft.Measure[0]);
        var source = Assert.IsType<AlgorithmSource>(scalar.Metric.Source);
        scalar = scalar with
        {
            Metric = scalar.Metric with
            {
                Source = source with
                {
                    Settings = new Dictionary<string, string>(source.Settings)
                    { ["Value"] = "unfinished" }
                }
            }
        };
        draft = draft with { Measure = [scalar, draft.Measure[1], draft.Measure[2]] };
        var session = new AuthoringDocumentSession(draft);
        Assert.True(session.ApplyEdit(operation, current => operation switch
        {
            "insert" => AuthoringSequenceOperations.Insert(current, AuthoringRecipeIds.Acquire, Row(current, current.Measure[2].NodeId), false),
            "duplicate" => AuthoringSequenceOperations.Duplicate(current, Row(current, current.Measure[1].NodeId)),
            _ => AuthoringSequenceOperations.Move(current, Row(current, scalar.NodeId), 1)
        }));
        Assert.Equal("unfinished", Assert.IsType<AlgorithmSource>(AuthoringRecipeCatalog.EnumerateMetrics(session.Draft.Measure)
            .Single(metric => metric.ChannelKey == "rail.mean").Source).Settings["Value"]);
        session.Undo();
        Assert.True(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(session.Snapshot));
        Assert.True(session.CanRedo);
        var before = session.Snapshot;
        var revision = session.Revision;
        var consumerId = draft.Measure[2].NodeId;
        var error = Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("invalid move", current =>
            AuthoringSequenceOperations.Move(current, Row(current, consumerId), -1)));
        Assert.Contains("preceding", error.Message, StringComparison.Ordinal);
        Assert.True(before.ContentEquals(session.Snapshot));
        Assert.Equal(revision, session.Revision);
        Assert.True(session.CanRedo);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Repeat(draft, Row(draft, draft.Measure[1].NodeId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_blank_channels_reserve_actual_runtime_name_fallback(bool filter)
    {
        var draft = Program(AuthoringRecipeIds.Acquire);
        var acquire = Assert.IsType<MetricNode>(draft.Measure[0]);
        draft = draft with { Measure = [acquire with { Metric = acquire.Metric with { ChannelKey = "input" } }] };
        ITestStep step = filter
            ? new ApplyTransferFunctionStep { Name = "VDC", Channel = " ", InputChannel = "input", Numerator = [0.5, 0.5], Denominator = [1], TsSeconds = 0.005 }
            : new ChannelAverageStep { Name = "VDC", Channel = "", InputChannel = "input", Threshold = 1.2 };
        var raw = Raw(step);
        draft = draft with { Measure = [draft.Measure[0], raw] };
        var inserted = AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Acquire, Row(draft, raw.NodeId), false);
        Assert.Equal("VDC_2", Assert.IsType<MetricNode>(inserted.Measure[2]).Metric.ChannelKey);
        var results = Execute(inserted);
        Assert.Contains("VDC_2", results.Samples);
        if (filter) Assert.Contains("VDC", results.Samples);
        else Assert.Contains("VDC", results.Scalars);
    }

    [Theory]
    [InlineData("measure-mean")]
    [InlineData("measure-filter")]
    [InlineData("lowered-mean")]
    [InlineData("lowered-filter")]
    [InlineData("specialized-filter")]
    public void Typed_blank_channels_reserve_runtime_name_fallback(string form)
    {
        var mean = form.EndsWith("mean", StringComparison.Ordinal);
        var draft = Program(AuthoringRecipeIds.Acquire, mean ? AuthoringRecipeIds.Formula : AuthoringRecipeIds.TransferFunction);
        var acquire = Assert.IsType<MetricNode>(draft.Measure[0]);
        var consumer = Assert.IsType<MetricNode>(draft.Measure[1]);
        consumer = consumer with
        {
            Metric = consumer.Metric with
            {
                Name = "VDC",
                ChannelKey = " ",
                Source = form switch
                {
                    "measure-mean" => new MeasureSource("", AuthoringFunctionIds.BasicChannelAverage, new Dictionary<string, string> { ["InputChannel"] = "input", ["Name"] = "VDC" }),
                    "measure-filter" => new MeasureSource("", AuthoringFunctionIds.BasicApplyTransferFunction, new Dictionary<string, string>
                    { ["InputChannel"] = "input", ["Numerator"] = "0.5,0.5", ["Denominator"] = "1", ["TsSeconds"] = "0.005" }),
                    "lowered-mean" => new ExpressionAlgorithm(["input"], "mean(input)"),
                    "lowered-filter" => new ExpressionAlgorithm(["input"], "filter([0.5,0.5],[1],input)"),
                    _ => new TransferFunctionAlgorithm("input", [0.5, 0.5], [1], 0.005, "filter")
                }
            }
        };
        if (form == "measure-mean") consumer = consumer with { Metric = consumer.Metric with { Name = "advertised.name" } };
        draft = draft with { Measure = [acquire with { Metric = acquire.Metric with { ChannelKey = "input" } }, consumer] };
        var added = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
        Assert.Equal("VDC_2", Assert.IsType<MetricNode>(added.Measure[2]).Metric.ChannelKey);
        var results = Execute(added);
        Assert.Contains("VDC_2", results.Samples);
        if (mean) Assert.Contains("VDC", results.Scalars);
        else Assert.Contains("VDC", results.Samples);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("duplicate")]
    [InlineData("move")]
    public void Vm_commands_and_binding_refresh_tolerate_unrelated_unfinished_scalar(string operation)
    {
        var root = TemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Safety\",\"plansDirectory\":\".\"}");
            var vm = new AuthoringWorkspaceViewModel();
            vm.Open(root);
            vm.CreateDemoProgram("safety");
            vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.BandScalar);
            vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
            vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Formula);
            var draft = vm.SelectedProgram!;
            var scalar = Assert.IsType<MetricNode>(draft.Measure[0]);
            var source = Assert.IsType<AlgorithmSource>(scalar.Metric.Source);
            scalar = scalar with
            {
                Metric = scalar.Metric with
                {
                    Source = source with
                    { Settings = new Dictionary<string, string>(source.Settings) { ["Value"] = "unfinished" } }
                }
            };
            var refreshes = 0;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.EditingIssues)) { _ = vm.EditingIssues; refreshes++; }
                if (args.PropertyName == nameof(vm.FormulaDeploymentStatus)) _ = vm.FormulaDeploymentStatus;
                if (args.PropertyName == nameof(vm.Preview)) _ = vm.Preview;
            };
            vm.ReplaceSelected(draft with { Measure = [scalar, draft.Measure[1], draft.Measure[2]] });
            var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == draft.Measure[2].NodeId));
            Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, vm.FormulaDeploymentStatus!.Kind);
            if (operation == "insert")
            {
                vm.SelectedRecipeId = AuthoringRecipeIds.Acquire;
                vm.InsertionPosition = "After selected";
                Assert.True(vm.CanInsertRecipe);
                vm.InsertSelectedRecipe();
            }
            else
            {
                var id = operation == "duplicate" ? draft.Measure[1].NodeId : scalar.NodeId;
                vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == id));
                if (operation == "duplicate") vm.DuplicateSelectedSequence();
                else vm.MoveSelectedSequence(1);
            }
            Assert.Null(vm.Error);
            Assert.True(vm.CanUndo);
            Assert.True(refreshes > 0);
            vm.Undo();
            Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            Assert.True(vm.CanRedo);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == draft.Measure[2].NodeId));
            vm.MoveSelectedSequence(-1);
            Assert.Contains("preceding", vm.Error!, StringComparison.Ordinal);
            Assert.True(vm.CanRedo);
            Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Missing_threshold_consumer_duplicates_as_draft_but_status_and_compile_stay_blocked()
    {
        var draft = Program(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var consumer = Assert.IsType<MetricNode>(draft.Measure[1]);
        consumer = consumer with { Metric = consumer.Metric with { Limits = new LimitSpec(null, null, null) } };
        draft = draft with { Measure = [draft.Measure[0], consumer] };
        var session = new AuthoringDocumentSession(draft);
        session.ApplyEdit("duplicate", current => AuthoringSequenceOperations.Duplicate(current, Row(current, consumer.NodeId)));
        var clone = Assert.IsType<MetricNode>(session.Draft.Measure[2]);
        Assert.Null(clone.Metric.Limits!.Threshold);
        var source = Assert.IsType<ExpressionAlgorithm>(consumer.Metric.Source);
        var clonedSource = Assert.IsType<ExpressionAlgorithm>(clone.Metric.Source);
        Assert.Equal(source.Source, clonedSource.Source);
        Assert.Equal(source.InputChannelKeys, clonedSource.InputChannelKeys);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, FormulaDeploymentClassifier.Classify(clone.Metric, session.Draft).Kind);
        var directory = TemporaryDirectory();
        try
        {
            var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(session.Draft, Path.Combine(directory, "safety.TapPlan")));
            Assert.Contains(AuthoringCompileCodes.MissingLimits, error.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, true); }
        Assert.True(session.Undo());
        Assert.True(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(session.Snapshot));
        Assert.True(session.CanRedo);
        Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("move", current => AuthoringSequenceOperations.Move(current, Row(current, consumer.NodeId), -1)));
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Unfinished_unrelated_scalar_does_not_hide_new_invalid_input_grid()
    {
        var draft = Program(AuthoringRecipeIds.BandScalar, AuthoringRecipeIds.Acquire);
        var scalar = Assert.IsType<MetricNode>(draft.Measure[0]);
        var scalarSource = Assert.IsType<AlgorithmSource>(scalar.Metric.Source);
        scalar = scalar with
        {
            Metric = scalar.Metric with
            {
                Source = scalarSource with
                { Settings = new Dictionary<string, string>(scalarSource.Settings) { ["Value"] = "unfinished" } }
            }
        };
        var acquire = Assert.IsType<MetricNode>(draft.Measure[1]);
        var source = Assert.IsType<MeasureSource>(acquire.Metric.Source);
        acquire = acquire with
        {
            Metric = acquire.Metric with
            {
                Source = source with
                { Settings = new Dictionary<string, string>(source.Settings) { ["IntervalMs"] = "10" } }
            }
        };
        draft = draft with { Measure = [scalar, acquire] };
        var session = WithRedo(draft, scalar.NodeId);
        var before = session.Snapshot;
        var error = Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("filter", current =>
            AuthoringSequenceOperations.Insert(current, AuthoringRecipeIds.TransferFunction, Row(current, acquire.NodeId), false)));
        Assert.Contains("TF_GRID", error.Message, StringComparison.Ordinal);
        Assert.True(before.ContentEquals(session.Snapshot));
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Relevant_numeric_closure_traverses_a_known_raw_filter()
    {
        var draft = Program(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var acquire = Assert.IsType<MetricNode>(draft.Measure[0]);
        var source = Assert.IsType<MeasureSource>(acquire.Metric.Source);
        acquire = acquire with
        {
            Metric = acquire.Metric with
            {
                Source = source with
                { Settings = new Dictionary<string, string>(source.Settings) { ["IntervalMs"] = "10" } }
            }
        };
        var raw = Raw(new ApplyTransferFunctionStep { Channel = "filtered", InputChannel = "VDC", TsSeconds = 0.005, Numerator = [1], Denominator = [1] });
        var formula = Assert.IsType<MetricNode>(draft.Measure[1]);
        formula = formula with { Metric = formula.Metric with { Source = new ExpressionAlgorithm(["filtered"], "mean(filtered)") } };
        draft = draft with { Measure = [acquire, raw, formula] };
        Assert.Contains("TF_GRID", FormulaDeploymentClassifier.Classify(formula.Metric, draft).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AuthoringFunctionIds.BasicChannelAverage)]
    [InlineData(AuthoringFunctionIds.BasicApplyTransferFunction)]
    public void Omitted_input_settings_keep_factory_empty_default_and_compilation_blocker(string function)
    {
        var draft = Program(AuthoringRecipeIds.Acquire, function == AuthoringFunctionIds.BasicChannelAverage ? AuthoringRecipeIds.Formula : AuthoringRecipeIds.TransferFunction);
        var consumer = Assert.IsType<MetricNode>(draft.Measure[1]);
        consumer = consumer with { Metric = consumer.Metric with { Source = new MeasureSource("", function, new Dictionary<string, string>()) } };
        var loop = new RepeatNode(1, [draft.Measure[0], consumer]);
        draft = draft with { Measure = [loop] };
        var duplicate = AuthoringSequenceOperations.Duplicate(draft, Row(draft, loop.NodeId));
        var clone = Assert.IsType<MetricNode>(Assert.IsType<RepeatNode>(duplicate.Measure[1]).Children[1]);
        Assert.Empty(Assert.IsType<MeasureSource>(clone.Metric.Source).Settings);
        Assert.Equal(new[] { string.Empty }, AuthoringDependencyIndex.Build(duplicate).Nodes.Single(node => node.NodeId == clone.NodeId).InputChannels);
        var directory = TemporaryDirectory();
        try { Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(duplicate, Path.Combine(directory, "safety.TapPlan"))); }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Renaming_a_blank_channel_fallback_rejects_new_actual_output_collision()
    {
        var draft = Program(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var consumer = Assert.IsType<MetricNode>(draft.Measure[1]);
        consumer = consumer with { Metric = consumer.Metric with { ChannelKey = "", Name = "mean.result" } };
        draft = draft with { Measure = [draft.Measure[0], consumer] };
        var session = WithRedo(draft, consumer.NodeId);
        var before = session.Snapshot;
        var error = Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("rename", current =>
            AuthoringSequenceOperations.Rename(current, Row(current, consumer.NodeId), "VDC")));
        Assert.Contains("Output 'VDC'", error.Message, StringComparison.Ordinal);
        Assert.True(before.ContentEquals(session.Snapshot));
        Assert.True(session.CanRedo);
    }

    [Theory]
    [InlineData("advertised.mean", false)]
    [InlineData("Mean", true)]
    public void Raw_average_bound_presentation_alias_is_the_effective_runtime_output(string alias, bool fixedConflict)
    {
        var draft = Program(AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Formula);
        var acquire = Assert.IsType<MetricNode>(draft.Measure[0]);
        var template = Assert.IsType<MetricNode>(draft.Measure[1]).Metric;
        AuthoringPluginSearch.Search();
        var step = new ChannelAverageStep { Name = "VDC", Channel = " ", InputChannel = "input", Threshold = 1.2 };
        PresentationAttach.Apply(step, template with { ChannelKey = alias });
        var raw = Raw(step);
        draft = draft with { Measure = [acquire with { Metric = acquire.Metric with { ChannelKey = "input" } }, raw] };
        if (fixedConflict)
        {
            var session = WithRedo(draft, acquire.NodeId);
            var before = session.Snapshot;
            var error = Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("mean", current =>
                AuthoringSequenceOperations.Insert(current, AuthoringRecipeIds.MeanGte, Row(current, raw.NodeId), false)));
            Assert.Contains("Output 'Mean'", error.Message, StringComparison.Ordinal);
            Assert.True(before.ContentEquals(session.Snapshot));
            Assert.True(session.CanRedo);
        }
        else
        {
            draft = AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Acquire, Row(draft, raw.NodeId), false);
            Assert.Equal("VDC", Assert.IsType<MetricNode>(draft.Measure[2]).Metric.ChannelKey);
        }
        var results = Execute(draft, plan => Assert.Equal(alias,
            Assert.Single(PlanCompiler.FlattenSteps(plan).OfType<ChannelAverageStep>()).Channel));
        Assert.Equal(new[] { alias }, results.Scalars);
        Assert.DoesNotContain("VDC", results.Scalars);
        if (!fixedConflict) Assert.Contains("VDC", results.Samples);
    }

    private static AuthoringDocumentSession WithRedo(ProgramDraft draft, Guid selected)
    {
        var session = new AuthoringDocumentSession(draft) { SelectedNodeId = selected };
        session.ApplyEdit("rename", current => AuthoringSequenceOperations.Rename(current, Row(current, selected), "Renamed"));
        session.Undo();
        Assert.True(session.CanRedo);
        return session;
    }
    private static ProgramDraft Program(params string[] recipes)
    {
        var draft = MockDmmDraftFixture.Create("safety") with { Setup = [], Cleanup = new CleanupPolicy(false, []) };
        foreach (var recipe in recipes) draft = AuthoringRecipeCatalog.Apply(draft, recipe);
        return draft;
    }
    private static SequenceRow Row(ProgramDraft draft, Guid id) => AuthoringSequence.Flatten(draft).Single(row => row.NodeId == id);
    private static RawStepNode Raw(ITestStep step)
    {
        AuthoringPluginSearch.Search();
        var directory = TemporaryDirectory();
        try
        {
            var plan = new TestPlan(); plan.ChildTestSteps.Add(step);
            var path = Path.Combine(directory, "raw.TapPlan"); plan.Save(path);
            var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
            return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        }
        finally { Directory.Delete(directory, true); }
    }
    private static Outputs Execute(ProgramDraft draft, Action<TestPlan>? inspect = null)
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "safety.TapPlan");
            new PlanCompiler().Save(draft, path);
            var plan = TestPlan.Load(path);
            inspect?.Invoke(plan);
            var outputs = new Outputs();
            Assert.Equal(Verdict.Pass, plan.Execute([outputs], []).Verdict);
            return outputs;
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ht-sequence-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
    private sealed class Outputs : ResultListener
    {
        public List<string> Scalars { get; } = [];
        public List<string> Samples { get; } = [];
        public override void OnResultPublished(Guid stepRunId, ResultTable result)
        {
            var column = result.Name switch { "Scalar" => "Name", "Sample" => "Channel", _ => null };
            if (column is null) return;
            var values = result.Columns.Single(value => value.Name == column).Data;
            foreach (var value in values) (result.Name == "Scalar" ? Scalars : Samples).Add(Convert.ToString(value) ?? string.Empty);
        }
    }
}
