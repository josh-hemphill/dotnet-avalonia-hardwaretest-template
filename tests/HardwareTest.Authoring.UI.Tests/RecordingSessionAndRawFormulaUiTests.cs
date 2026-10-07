using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class RecordingSessionAndRawFormulaUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_selector_returns_to_example_when_program_or_workspace_changes(bool replaceWorkspace)
    {
        using var first = new AuthoringUiFixture(rememberWorkspace: true);
        using var second = new AuthoringUiFixture();
        var compiler = new PlanCompiler();
        var draft = compiler.Load(Path.Combine(first.WorkspaceRoot, "sample.TapPlan"));
        compiler.Save(draft with { PlanId = "other" }, Path.Combine(first.WorkspaceRoot, "other.TapPlan"));
        Recording(first.WorkspaceRoot, draft.PlanId, "first", 2);
        Recording(first.WorkspaceRoot, "other", "other", 82);
        Recording(second.WorkspaceRoot, draft.PlanId, "second", 92);
        var window = first.Show();
        first.OpenRememberedWorkspace();
        var vm = first.ViewModel;
        vm.StopRecovery();
        vm.SelectProgram(draft.PlanId);
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        AuthoringUiFixture.Drain();
        var selector = first.Control<ListBox>("Recordings");
        selector.SelectedIndex = 0;
        AuthoringUiFixture.Drain();
        Assert.Equal("first", vm.SelectedDataset!.Run.RunId);
        if (replaceWorkspace) { vm.Open(second.WorkspaceRoot); vm.StopRecovery(); }
        else vm.SelectProgram("other");
        AuthoringUiFixture.Drain();
        Assert.NotEmpty(vm.Datasets);
        Assert.Null(vm.SelectedDataset);
        Assert.Equal(-1, selector.SelectedIndex);
        Assert.StartsWith("Example data", vm.DataSourceDetails);
        selector.SelectedIndex = 0;
        AuthoringUiFixture.Drain();
        Assert.Equal(replaceWorkspace ? "second" : "other", vm.SelectedDataset!.Run.RunId);
        Assert.StartsWith("Recording", vm.DataSourceDetails);
    }

    [AvaloniaFact]
    public void Actual_selector_preserves_run_across_same_program_refresh_and_reorder_then_clears_missing_run()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var draft = new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        Recording(fixture.WorkspaceRoot, draft.PlanId, "original", 2);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        AuthoringUiFixture.Drain();
        var selector = fixture.Control<ListBox>("Recordings");
        selector.SelectedIndex = 0;
        AuthoringUiFixture.Drain();
        var selectedPath = vm.SelectedDataset!.Path;
        Recording(fixture.WorkspaceRoot, draft.PlanId, "000-earlier", 91);
        vm.VisaAddress = "MOCK::EDITED";
        AuthoringUiFixture.Drain();
        Assert.Equal(1, selector.SelectedIndex);
        Assert.Equal(selectedPath, vm.SelectedDataset!.Path);
        Assert.Equal("original", vm.SelectedDataset.Run.RunId);
        File.Delete(selectedPath);
        vm.VisaAddress = "MOCK::AGAIN";
        AuthoringUiFixture.Drain();
        Assert.Single(vm.Datasets);
        Assert.Equal(-1, selector.SelectedIndex);
        Assert.Null(vm.SelectedDataset);
        Assert.StartsWith("Example data", vm.DataSourceDetails);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Imported_unknown_or_malformed_raw_source_and_formula_open_with_usable_requirements(bool malformed, bool filter)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var original = new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        var xml = malformed ? "<TestStep" : "<TestStep type='Missing.Plugin.Step'><Name>Unavailable plugin</Name></TestStep>";
        var raw = new RawStepNode("Missing.Plugin.Step", xml);
        var healthy = new MetricNode(new MetricDraft("Healthy input", "healthy", "timeseries", "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
        var derived = new MetricNode(new MetricDraft("Imported formula", "result", filter ? "timeseries" : "scalar", "V", new LimitSpec(null, null, 0), null,
            new ExpressionAlgorithm(["missing"], filter ? "filter([0.5,0.5],[1],missing)" : "mean(missing)")));
        var draft = original with { Measure = [raw, healthy, derived] };
        var document = AuthoringDocumentDto.FromDraft(draft);
        document.RequiresCompilation = true;
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        store.Save(document);
        var sourcePath = store.GetDocumentPath(draft.PlanId);
        var bytes = File.ReadAllBytes(sourcePath);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(item => item.NodeId == derived.NodeId));
        AuthoringUiFixture.Drain();
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, vm.FormulaDeploymentStatus!.Kind);
        Assert.Contains("Missing.Plugin.Step", vm.FormulaDeploymentStatus.Message);
        Assert.Equal("Missing deployment requirements", fixture.Control<TextBlock>("Formula deployment status").Text);
        Assert.Contains(vm.EditingIssues, issue => issue.NodeId == derived.NodeId && issue.Message.Contains("Missing.Plugin.Step", StringComparison.Ordinal));
        Assert.Empty(Assert.Single(vm.BoardTiles, tile => tile.NodeId == derived.NodeId).Preview.CannedSamples);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(item => item.NodeId == healthy.NodeId));
        AuthoringUiFixture.Drain();
        Assert.NotEmpty(vm.Preview.CannedSamples);
        Assert.Equal(xml, Assert.IsType<RawStepNode>(vm.SelectedProgram!.Measure[0]).XmlFragment);
        Assert.Equal(bytes, File.ReadAllBytes(sourcePath));
    }

    private static void Recording(string workspace, string planId, string runId, double value)
    {
        var folder = Path.Combine(workspace, "recordings", runId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "run.json"), JsonSerializer.Serialize(new TestRunRecord
        { PlanId = planId, RunId = runId, Samples = [new StoredSample { MetricKey = "VDC", Value = value }] }, AppJsonContext.Default.TestRunRecord));
    }
}
