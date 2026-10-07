using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringFormulaScalarExampleTests
{
    [AvaloniaFact]
    public void Scalar_expression_example_is_visible_while_deployment_stays_blocked_and_exploration_saves()
    {
        using var fixture = Open(); var vm = fixture.ViewModel;
        Assert.Equal("Missing deployment requirements", vm.FormulaDeploymentLabel);
        Assert.Contains("MISSING_CHANNEL", vm.FormulaDeploymentMessage);
        Assert.Equal(1.25, Assert.Single(vm.Preview.CannedSamples));
        Assert.Contains("Preview only", vm.Preview.Note);
        Assert.Contains("Scalar publisher", vm.PreviewNote);
        vm.Apply(); AuthoringUiFixture.Drain(); Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
        vm.FormulaExplorationOnly = true; vm.Apply(); AuthoringUiFixture.Drain();
        Assert.False(vm.HasUncompiledSources, vm.Error ?? vm.SavePreviewWarning ?? vm.Status); Assert.True(vm.CanPack);
        Assert.NotEmpty(vm.Preview.CannedSamples); Assert.Equal("Missing deployment requirements", vm.FormulaDeploymentLabel);
    }

    [AvaloniaFact]
    public void Incomplete_scalar_publisher_value_is_not_replaced_with_a_stale_preview_example()
    {
        using var fixture = Open(); var vm = fixture.ViewModel;
        var formula = vm.SelectedSequence!.NodeId;
        var scalar = vm.SelectedProgram!.Measure.OfType<MetricNode>().Single(node => node.Metric.ChannelKey == "rail.mean");
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == scalar.NodeId));
        vm.SetMetricSetting("Value", "abc");
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == formula)); AuthoringUiFixture.Drain();
        Assert.Empty(vm.Preview.CannedSamples); Assert.Contains("BUILD_INCOMPLETE", vm.Preview.Note);
        vm.Apply(); AuthoringUiFixture.Drain(); Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
    }

    private static AuthoringUiFixture Open()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot); workspace.Manifest.Package.Name = "Scalar example inclusion";
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel; vm.CreateProgram("scalar-example");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.BandScalar); vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.FormulaSource = "mean(rail.mean)"; AuthoringUiFixture.Drain(); return fixture;
    }
}
