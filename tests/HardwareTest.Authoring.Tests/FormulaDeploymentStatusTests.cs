using System.IO.Compression;
using HardwareTest.Core.Runs;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class FormulaDeploymentStatusTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("std(input)", FormulaDeploymentStatusKind.PreviewOnly, "None")]
    [InlineData("mean(input)", FormulaDeploymentStatusKind.MissingRequirements, "Channel Average")]
    [InlineData("mean(", FormulaDeploymentStatusKind.InvalidExpression, "None")]
    public void Classification_reports_supported_target_and_actual_requirements(string source, FormulaDeploymentStatusKind kind, string target)
    {
        var status = FormulaDeploymentClassifier.Classify(Metric(source));
        Assert.Equal(kind, status.Kind);
        Assert.Equal(target, status.Target);
        if (kind == FormulaDeploymentStatusKind.MissingRequirements) Assert.Contains(AuthoringCompileCodes.MissingLimits, status.Message);
        if (kind == FormulaDeploymentStatusKind.PreviewOnly) Assert.Contains("No equivalent", status.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_grid_failure_is_visible_evidence_and_does_not_claim_to_be_deployment_input(bool missingClock)
    {
        var metric = Metric("filter([1],[1],input)");
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series = new Dictionary<string, IReadOnlyList<StoredSample>>
        {
            ["input"] = [new() { Value = 1, ElapsedMs = 0 }, new() { Value = 2, ElapsedMs = missingClock ? null : 5 }, new() { Value = 3, ElapsedMs = 15 }]
        };
        var status = FormulaDeploymentClassifier.Classify(metric, series: series);
        var lower = Assert.Throws<AuthoringWorkspaceException>(() => FormulaLowerer.Lower((ExpressionAlgorithm)metric.Source, null, series));
        var preview = MetricPreviewBuilder.From(metric, recorded: series);
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, status.Kind);
        Assert.Contains(lower.Message, status.RecordingEvidence);
        Assert.Contains(lower.Message, preview.Note!);
        Assert.Contains(AuthoringCompileCodes.TfGrid, status.RecordingEvidence);
    }

    [Fact]
    public void Untimed_input_reports_same_requirement_in_status_preview_and_compiler()
    {
        var metric = Metric("filter([1],[1],input)");
        var input = Metric("std(input)") with { ChannelKey = "input", Limits = new LimitSpec(0, 1, null), Source = new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], new Dictionary<string, string>()) };
        var draft = AuthoringRecipeCatalog.CreateProgram("untimed") with { Measure = [new MetricNode(input), new MetricNode(metric)] };
        var status = FormulaDeploymentClassifier.Classify(metric, draft);
        var preview = MetricPreviewBuilder.From(metric, [input, metric]);
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "untimed.TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains(AuthoringCompileCodes.TfMissingElapsed, status.Message);
        Assert.Equal(status.Message, preview.Note);
        Assert.Equal(status.Message, error.Message);
    }

    [Fact]
    public void Exploration_std_source_reopens_unchanged_and_saved_build_excludes_it_from_artifact_and_receipt_map()
    {
        var (workspace, draft, store) = SavedFormula(explore: true);
        var node = Assert.IsType<MetricNode>(draft.Measure.Last());
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "Threshold")] = "abc";
        SaveSource(workspace, draft, store);
        var before = File.ReadAllBytes(store.GetDocumentPath(draft.PlanId));
        var reopened = store.Load(draft.PlanId).Document!.ToDraft();
        Assert.Equal("std(input)", ((ExpressionAlgorithm)((MetricNode)reopened.Measure.Last()).Metric.Source).Source);
        Assert.Equal(FormulaDeploymentIntent.Explore, reopened.AuthoringState.FormulaIntent[node.NodeId]);
        Assert.Equal("abc", reopened.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "Threshold")]);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var output = AuthoringBuildSnapshotTests.Temp();
        var result = AuthoringBuildService.Execute(request, output);
        Assert.Equal(before, File.ReadAllBytes(store.GetDocumentPath(draft.PlanId)));
        var compilation = result.Receipt.Compilation.Single(c => c.PlanId == draft.PlanId);
        Assert.Contains(node.NodeId, compilation.ExcludedExplorationNodes);
        Assert.DoesNotContain(compilation.SourceMap, entry => entry.NodeId == node.NodeId);
        var package = result.Receipt.Outputs.First(o => o.Path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase));
        using var archive = ZipFile.OpenRead(Path.Combine(output, package.Path));
        var plan = archive.Entries.Single(e => e.FullName.EndsWith("sample.TapPlan", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(plan.Open());
        var xml = reader.ReadToEnd();
        Assert.DoesNotContain(node.NodeId.ToString(), xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exploration std", xml);
    }

    [Fact]
    public void Unsupported_deployment_source_blocks_saved_build()
    {
        var (workspace, _, _) = SavedFormula(explore: false);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, AuthoringBuildSnapshotTests.Temp()));
        Assert.Contains(AuthoringCompileCodes.FormulaNoLower, error.Message);
    }

    [Fact]
    public void Supported_exploration_mean_keeps_deployable_capability_and_explicit_exclusion()
    {
        var input = new MetricNode(Metric("std(input)") with { ChannelKey = "input", Source = new MeasureSource("", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>()) });
        var formula = new MetricNode(Metric("mean(input)") with { Limits = new LimitSpec(null, null, 0) });
        var draft = AuthoringRecipeCatalog.CreateProgram("mean-exploration") with { Measure = [input, formula] };
        draft.AuthoringState.FormulaIntent[formula.NodeId] = FormulaDeploymentIntent.Explore;
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(formula.Metric, draft).Kind);
        Assert.Contains(formula.NodeId, AuthoringFormulaDeployment.ExcludedNodes(draft));
        Assert.Equal(FormulaDeploymentIntent.Explore, draft.AuthoringState.FormulaIntent[formula.NodeId]);
    }

    [Fact]
    public void Exploration_only_projection_has_no_deployment_measure_and_retains_source_identity()
    {
        var node = new MetricNode(Metric("std(input)"));
        var draft = AuthoringRecipeCatalog.CreateProgram("exploration-only") with { Measure = [node] };
        draft.AuthoringState.FormulaIntent[node.NodeId] = FormulaDeploymentIntent.Explore;
        var original = AuthoringDocumentSnapshot.Capture(draft);
        Assert.Empty(AuthoringFormulaDeployment.Project(draft).Measure);
        Assert.Single(draft.Measure);
        Assert.True(original.ContentEquals(AuthoringDocumentSnapshot.Capture(draft)));
    }

    private static (AuthoringWorkspace Workspace, ProgramDraft Draft, AuthoringDocumentStore Store) SavedFormula(bool explore)
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var path = Path.Combine(root, "sample.TapPlan");
        var draft = new PlanCompiler().Load(path);
        var node = new MetricNode(Metric("std(input)"));
        draft = draft with { Measure = [.. draft.Measure, node] };
        draft.AuthoringState.FormulaIntent[node.NodeId] = explore ? FormulaDeploymentIntent.Explore : FormulaDeploymentIntent.Deploy;
        var store = new AuthoringDocumentStore(root);
        var doc = AuthoringDocumentDto.FromDraft(draft, 12, compiledPlanHash: AuthoringDocumentStore.ComputeHash(path), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        doc.RequiresCompilation = true;
        store.Save(doc);
        return (workspace, draft, store);
    }

    private static void SaveSource(AuthoringWorkspace workspace, ProgramDraft draft, AuthoringDocumentStore store)
    {
        var path = Path.Combine(workspace.Root, "sample.TapPlan");
        var doc = AuthoringDocumentDto.FromDraft(draft, 13, compiledPlanHash: AuthoringDocumentStore.ComputeHash(path), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        doc.RequiresCompilation = true;
        store.Save(doc);
    }

    [Fact]
    public void Missing_filter_producer_blocks_actual_saved_build_with_same_status_requirement()
    {
        var (workspace, draft, store) = SavedFormula(explore: false);
        var node = (MetricNode)draft.Measure.Last();
        var metric = node.Metric with { Source = new ExpressionAlgorithm(["missing"], "filter([1],[1],missing)") };
        draft = draft with { Measure = [.. draft.Measure.Take(draft.Measure.Count - 1), node with { Metric = metric }] };
        SaveSource(workspace, draft, store);
        var status = FormulaDeploymentClassifier.Classify(metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, AuthoringBuildSnapshotTests.Temp()));
        Assert.Equal(status.Message, error.Message);
        Assert.Contains("MISSING_CHANNEL", error.Message);
    }

    [Fact]
    public void Included_incomplete_input_still_blocks_saved_build_after_exploration_projection()
    {
        var (workspace, draft, store) = SavedFormula(explore: true);
        var included = draft.Measure.OfType<MetricNode>().First();
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(included.NodeId, "SampleCount")] = "abc";
        SaveSource(workspace, draft, store);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, AuthoringBuildSnapshotTests.Temp()));
        Assert.Contains("BUILD_INCOMPLETE", error.Message);
    }

    [Fact]
    public void Deployment_dependency_on_excluded_exploration_producer_blocks_saved_build()
    {
        var (workspace, draft, store) = SavedFormula(explore: true);
        var consumer = new MetricNode(Metric("filter([1],[1],formula.result)") with { ChannelKey = "filter.result" });
        draft = draft with { Measure = [.. draft.Measure, consumer] };
        SaveSource(workspace, draft, store);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, AuthoringBuildSnapshotTests.Temp()));
        Assert.Contains("MISSING_CHANNEL", error.Message);
    }

    private static MetricDraft Metric(string source) => new("Exploration std", "formula.result", "scalar", "V", null, null, new ExpressionAlgorithm(["input"], source));
}
