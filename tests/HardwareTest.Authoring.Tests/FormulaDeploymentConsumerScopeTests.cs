using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class FormulaDeploymentConsumerScopeTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("mean(input)", false)]
    [InlineData("mean(input)", true)]
    [InlineData("filter([1],[1],input)", false)]
    [InlineData("filter([1],[1],input)", true)]
    public void Valid_consumer_status_and_issues_ignore_broken_siblings_while_saved_build_stays_blocked(string expression, bool brokenFirst)
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var path = Path.Combine(root, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(path);
        var input = Input();
        var valid = Formula("valid.result", expression);
        var broken = Formula("broken.result", "filter([1],[1],missing)");
        draft = draft with { Measure = brokenFirst ? [input, broken, valid] : [input, valid, broken] };
        var validStatus = FormulaDeploymentClassifier.Classify(valid.Metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, validStatus.Kind);
        var brokenStatus = FormulaDeploymentClassifier.Classify(broken.Metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, brokenStatus.Kind);
        Assert.Contains("missing", brokenStatus.Message);
        var issues = AuthoringIssueService.GetIssues(draft).Where(issue => issue.Code is "MISSING_CHANNEL" or "FORMULA_DEPLOYMENT").ToArray();
        Assert.Equal(broken.NodeId, Assert.Single(issues).NodeId);
        if (expression.StartsWith("mean", StringComparison.Ordinal))
        {
            Assert.Contains("threshold", validStatus.Requirements);
            Assert.Contains("preceding Sample producer", validStatus.Requirements);
            Assert.Contains("same actual execution scope", validStatus.Requirements);
        }
        var store = new AuthoringDocumentStore(root);
        var document = AuthoringDocumentDto.FromDraft(draft, 21, compiledPlanHash: AuthoringDocumentStore.ComputeHash(path), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        document.RequiresCompilation = true; store.Save(document);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, AuthoringBuildSnapshotTests.Temp()));
        Assert.Equal(brokenStatus.Message, error.Message);
    }

    [Theory]
    [InlineData("mean(upstream.result)")]
    [InlineData("filter([1],[1],upstream.result)")]
    public void Invalid_upstream_filter_requirement_propagates_to_its_actual_dependents(string expression)
    {
        var upstream = Formula("upstream.result", "filter([1],[1],missing)");
        var dependent = Formula("dependent.result", expression);
        var draft = MockDmmDraftFixture.Create("dependency") with { Measure = [Input(), upstream, dependent] };
        var status = FormulaDeploymentClassifier.Classify(dependent.Metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
        Assert.Contains("missing", status.Message);
        var issues = AuthoringIssueService.GetIssues(draft).Where(issue => issue.Code == "MISSING_CHANNEL").ToArray();
        Assert.Equal(new[] { upstream.NodeId, dependent.NodeId }.Order(), issues.Select(issue => issue.NodeId).Order());
        Assert.All(issues, issue => Assert.Equal(status.Message, issue.Message));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(AuthoringBuildSnapshotTests.Temp(), "dependency.TapPlan")));
        Assert.Equal(status.Message, error.Message);
    }

    [Fact]
    public void Unrelated_invalid_channel_average_cardinality_does_not_change_valid_filter_status()
    {
        var invalid = new MetricNode(new MetricDraft("Invalid mean", "invalid.result", "scalar", "V", new LimitSpec(null, null, 0), null,
            new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, [], new Dictionary<string, string>())));
        var valid = Formula("valid.result", "filter([1],[1],input)");
        var draft = MockDmmDraftFixture.Create("unrelated-mean") with { Measure = [Input(), invalid, valid] };
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(valid.Metric, draft).Kind);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(AuthoringBuildSnapshotTests.Temp(), "unrelated-mean.TapPlan")));
        Assert.Contains("requires exactly one", error.Message);
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == valid.NodeId);
    }

    private static MetricNode Input() => new(new MetricDraft("Input", "input", "timeseries", "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
    private static MetricNode Formula(string channel, string source) => new(new MetricDraft("Formula", channel, "scalar", "V", new LimitSpec(null, null, 0), null,
        new ExpressionAlgorithm([], source)));
}
