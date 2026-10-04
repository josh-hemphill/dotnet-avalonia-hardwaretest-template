using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringFormulaSourcePreviewTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_timed_publishers_are_valid_in_actual_vm_preview_and_compilation(bool exploration)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var formula = Formula("filtered", "input");
        var draft = Load(fixture) with { Measure = [Raw(fixture, 0), Raw(fixture, 5), formula] };
        if (exploration) draft.AuthoringState.FormulaIntent[formula.NodeId] = FormulaDeploymentIntent.Explore;
        Open(fixture, draft, formula);
        var vm = fixture.ViewModel;
        Assert.Equal("Deployable recipe", vm.FormulaDeploymentLabel);
        Assert.Null(vm.Preview.Note);
        Assert.NotEmpty(vm.Preview.CannedSamples);
        vm.Apply(); AuthoringUiFixture.Drain();
        Assert.False(vm.HasUncompiledSources, vm.Error ?? vm.SavePreviewWarning ?? vm.Status);
        var compiled = new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        Assert.Equal(exploration ? 2 : 3, compiled.Measure.Count);
        Assert.Equal(exploration, vm.FormulaExplorationOnly);
    }

    [AvaloniaFact]
    public void Raw_missing_elapsed_has_the_same_actual_vm_preview_status_and_compile_error()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var formula = Formula("filtered", "input");
        var draft = Load(fixture) with { Measure = [Raw(fixture, 0), Raw(fixture, null), formula] };
        Open(fixture, draft, formula);
        AssertBlocked(fixture, draft, "TF_MISSING_ELAPSED");
    }

    [AvaloniaFact]
    public void Repeat_scoped_input_is_not_a_publisher_for_an_outside_formula_preview()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var formula = Formula("filtered", "input");
        var draft = Load(fixture) with { Measure = [new RepeatNode(2, [Input()]), formula] };
        Open(fixture, draft, formula);
        AssertBlocked(fixture, draft, "MISSING_CHANNEL");
    }

    [AvaloniaFact]
    public void Excluded_upstream_formula_is_not_reintroduced_by_flattened_preview_siblings()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var upstream = Formula("upstream", "input"); var formula = Formula("filtered", "upstream");
        var draft = Load(fixture) with { Measure = [Input(), upstream, formula] };
        draft.AuthoringState.FormulaIntent[upstream.NodeId] = FormulaDeploymentIntent.Explore;
        Open(fixture, draft, formula);
        AssertBlocked(fixture, draft, "MISSING_CHANNEL");
        Assert.Equal(FormulaDeploymentIntent.Explore, fixture.ViewModel.SelectedProgram!.AuthoringState.FormulaIntent[upstream.NodeId]);
    }

    private static void AssertBlocked(AuthoringUiFixture fixture, ProgramDraft draft, string code)
    {
        var vm = fixture.ViewModel;
        Assert.Contains(code, vm.FormulaDeploymentMessage);
        Assert.Equal(vm.FormulaDeploymentMessage, vm.Preview.Note);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")));
        Assert.Equal(vm.Preview.Note, error.Message);
        vm.Apply(); AuthoringUiFixture.Drain();
        Assert.True(vm.HasUncompiledSources);
        Assert.False(vm.CanPack);
    }

    private static ProgramDraft Load(AuthoringUiFixture fixture) => new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
    private static void Open(AuthoringUiFixture fixture, ProgramDraft draft, MetricNode selected)
    {
        var path = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var document = AuthoringDocumentDto.FromDraft(draft, 8, compiledPlanHash: AuthoringDocumentStore.ComputeHash(path),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        document.RequiresCompilation = true;
        new AuthoringDocumentStore(fixture.WorkspaceRoot).Save(document);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.NodeId == selected.NodeId));
        AuthoringUiFixture.Drain();
        Assert.Equal(selected.NodeId, fixture.ViewModel.SelectedSequence!.NodeId);
    }
    private static RawStepNode Raw(AuthoringUiFixture fixture, double? elapsed)
    {
        var step = new PublishTimedSampleStep { Channel = "input", Value = 1, ElapsedMs = elapsed };
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step);
        var path = Path.Combine(fixture.WorkspaceRoot, "publisher.TapPlan"); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
        File.Delete(path);
        return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }
    private static MetricNode Input() => new(new MetricDraft("Input", "input", "timeseries", "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
    private static MetricNode Formula(string channel, string input) => new(new MetricDraft("Filter", channel, "timeseries", "V", null, null,
        new ExpressionAlgorithm([input], $"filter([1],[1],{input})")));
}
