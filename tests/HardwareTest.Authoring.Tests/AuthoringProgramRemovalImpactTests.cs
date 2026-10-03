using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringProgramRemovalImpactTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-program-impact-" + Guid.NewGuid().ToString("N"));
    public AuthoringProgramRemovalImpactTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(dir!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
    }
    [Theory]
    [InlineData("sidecar")]
    [InlineData("settings")]
    [InlineData("raw")]
    [InlineData("session")]
    [InlineData("selection")]
    public void Reviewed_program_removal_rejects_stale_content_session_and_selection_before_deleting_files(string change)
    {
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.CreateProgram("b"); vm.CreateProgram("a"); vm.ApplyRecipe(AuthoringRecipeIds.Acquire); Assert.True(vm.SaveAll().Succeeded);
        var metric = Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram!.Measure)); var settings = new Dictionary<string, string> { ["Samples"] = "2" };
        MeasureNode[] nodes = [new RepeatNode(2, [metric with { Metric = metric.Metric with { Source = new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, settings) } }]), new RawStepNode("Raw", "<a/>")];
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = nodes }); var selected = vm.SelectedProgram; var impact = vm.PrepareSelectedProgramRemoval();
        Assert.Equal("a", impact.PlanId); Assert.Equal(Path.Combine(_root, "a.TapPlan"), impact.TapPlanPath); Assert.Contains("a", impact.Scope);
        var files = Directory.EnumerateFiles(_root).ToDictionary(p => p, File.ReadAllBytes);
        switch (change)
        {
            case "sidecar": selected!.Sidecar.DisplayName = "changed while reviewing"; break;
            case "settings": settings["Samples"] = "3"; break;
            case "raw": nodes[1] = Assert.IsType<RawStepNode>(nodes[1]) with { XmlFragment = "<b/>" }; break;
            case "session": vm.CommitOpen(vm.PrepareOpen(_root), discardUnsavedChanges: true); break;
            default: vm.SelectProgram("b"); break;
        }
        if (change is "sidecar" or "settings" or "raw") Assert.Same(selected, vm.SelectedProgram);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyProgramRemoval(impact)); Assert.Equal(2, vm.Programs.Count);
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }
    [Fact]
    public void Applying_current_named_program_impact_deletes_only_reviewed_files()
    {
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.CreateProgram("b"); vm.CreateProgram("a"); Assert.True(vm.SaveAll().Succeeded);
        var bBytes = File.ReadAllBytes(Path.Combine(_root, "b.TapPlan")); var impact = vm.PrepareSelectedProgramRemoval();
        Assert.True(vm.ApplyProgramRemoval(impact)); Assert.False(File.Exists(impact.TapPlanPath)); Assert.False(File.Exists(impact.SidecarPath));
        Assert.Equal("b", Assert.Single(vm.Programs).PlanId); Assert.Equal(bBytes, File.ReadAllBytes(Path.Combine(_root, "b.TapPlan")));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
