using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringFormulaIncompleteInputTests
{
    [AvaloniaFact]
    public void Incomplete_threshold_exposes_readiness_and_navigation_and_survives_exploration_save_reopen()
    {
        using var fixture = Open(); var vm = fixture.ViewModel;
        vm.FormulaSource = "mean(VDC)"; vm.Threshold = "0";
        Assert.Equal("Deployable recipe", vm.FormulaDeploymentLabel);
        fixture.Type(fixture.Control<TextBox>("Threshold"), "abc");
        AssertIncomplete(vm, "Threshold");
        Assert.True(vm.FormulaNeedsThreshold);
        AuthoringUiFixture.Click(fixture.Control<Button>("Set formula deployment threshold"));
        Assert.True(fixture.Control<TextBox>("Threshold").IsFocused);
        vm.Apply(); Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
        vm.Undo(); AuthoringUiFixture.Drain();
        Assert.Equal("0", vm.Threshold); Assert.Equal("Deployable recipe", vm.FormulaDeploymentLabel);
        Assert.False(vm.FormulaNeedsThreshold);
        vm.Redo(); AuthoringUiFixture.Drain(); AssertIncomplete(vm, "Threshold");
        var id = vm.SelectedSequence!.NodeId; var plan = vm.SelectedProgram!.PlanId;
        vm.FormulaExplorationOnly = true; vm.Apply(); AuthoringUiFixture.Drain();
        Assert.False(vm.HasUncompiledSources, vm.Error ?? vm.SavePreviewWarning ?? vm.Status); Assert.True(vm.CanPack);
        vm.Open(fixture.WorkspaceRoot); fixture.NavigateTask(0); vm.SelectProgram(plan);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == id)); AuthoringUiFixture.Drain();
        Assert.True(vm.FormulaExplorationOnly); Assert.Equal("abc", vm.Threshold); AssertIncomplete(vm, "Threshold");
        fixture.Type(fixture.Control<TextBox>("Threshold"), "0"); vm.FormulaExplorationOnly = false; AuthoringUiFixture.Drain(); vm.Apply(); AuthoringUiFixture.Drain();
        Assert.Equal("Deployable recipe", vm.FormulaDeploymentLabel); Assert.False(vm.HasUncompiledSources, vm.Error ?? vm.SavePreviewWarning ?? vm.Status); Assert.True(vm.CanPack);
    }

    [AvaloniaTheory]
    [InlineData("IntervalMs")]
    [InlineData("SampleCount")]
    public void Incomplete_actual_publisher_setting_blocks_formula_status_preview_and_build_and_undo_repairs_it(string setting)
    {
        using var fixture = Open(); var vm = fixture.ViewModel;
        vm.FormulaSource = "filter([1],[1],VDC)";
        var formula = vm.SelectedSequence!.NodeId;
        var input = vm.SequenceItems.First(row => row.Kind == SequenceRowKind.Metric && row.NodeId != formula).NodeId;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == input)); AuthoringUiFixture.Drain();
        fixture.Type(fixture.Control<TextBox>(setting), "abc");
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == formula)); AuthoringUiFixture.Drain();
        AssertIncomplete(vm, "MetricSetting:" + setting);
        Assert.False(vm.FormulaNeedsThreshold);
        vm.Apply(); Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
        vm.Undo(); vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == formula)); AuthoringUiFixture.Drain(); Assert.Equal("Deployable recipe", vm.FormulaDeploymentLabel); Assert.Null(vm.Preview.Note);
        vm.Redo(); vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == formula)); AuthoringUiFixture.Drain(); AssertIncomplete(vm, "MetricSetting:" + setting);
    }

    [AvaloniaFact]
    public void Unrelated_incomplete_publisher_keeps_selected_formula_ready_but_whole_build_blocked()
    {
        using var fixture = Open(); var vm = fixture.ViewModel;
        vm.FormulaSource = "filter([1],[1],VDC)"; var formula = vm.SelectedSequence!.NodeId;
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire); vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.ChannelKey = "unrelated"; vm.SetMetricSetting("IntervalMs", "abc");
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == formula)); AuthoringUiFixture.Drain();
        Assert.Equal("Deployable recipe", vm.FormulaDeploymentLabel); Assert.Null(vm.Preview.Note);
        vm.Apply(); Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
    }

    private static void AssertIncomplete(AuthoringWorkspaceViewModel vm, string field)
    {
        Assert.Equal("Missing deployment requirements", vm.FormulaDeploymentLabel);
        Assert.Contains("BUILD_INCOMPLETE", vm.FormulaDeploymentMessage); Assert.Contains(field, vm.FormulaDeploymentMessage);
        Assert.Equal(vm.FormulaDeploymentMessage, vm.Preview.Note);
    }
    private static AuthoringUiFixture Open()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true); fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Formula);
        fixture.ViewModel.SelectMeasure(fixture.ViewModel.SelectedProgram!.Measure.Count - 1);
        fixture.ViewModel.ChannelKey = "VDC.incomplete-formula"; AuthoringUiFixture.Drain();
        return fixture;
    }
}
