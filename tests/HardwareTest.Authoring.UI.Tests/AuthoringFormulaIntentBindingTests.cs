using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringFormulaIntentBindingTests
{
    [AvaloniaFact]
    public void ExplorationCheckboxTracksFormulaProgramSelectionAndUndoRedo()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        var firstId = vm.SelectedProgram.PlanId; var firstNode = vm.SelectedSequence!.NodeId;
        vm.FormulaExplorationOnly = true;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        var secondNode = vm.SelectedSequence!.NodeId;
        AuthoringUiFixture.Drain();
        var checkbox = fixture.Control<CheckBox>("Formula exploration only");
        Assert.False(checkbox.IsChecked);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == firstNode)); AuthoringUiFixture.Drain();
        Assert.True(checkbox.IsChecked);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == secondNode)); AuthoringUiFixture.Drain();
        Assert.False(checkbox.IsChecked);
        checkbox.IsChecked = true; AuthoringUiFixture.Drain(); Assert.True(vm.FormulaExplorationOnly);
        vm.Undo(); AuthoringUiFixture.Drain(); Assert.False(checkbox.IsChecked); Assert.False(vm.FormulaExplorationOnly);
        vm.Redo(); AuthoringUiFixture.Drain(); Assert.True(checkbox.IsChecked); Assert.True(vm.FormulaExplorationOnly);
        vm.InitializePlan(new("intent-other") { Instruments = [] }); vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1); AuthoringUiFixture.Drain(); Assert.False(checkbox.IsChecked);
        vm.SelectProgram(firstId);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == secondNode)); AuthoringUiFixture.Drain();
        Assert.True(checkbox.IsChecked); Assert.True(vm.FormulaExplorationOnly);
        vm.SelectProgram("intent-other"); AuthoringUiFixture.Drain(); Assert.False(checkbox.IsChecked);
    }
    [AvaloniaFact]
    public void DeploymentStatusRefreshesPerRevisionAndMissingThresholdNavigatesToField()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "std(VDC)";
        AuthoringUiFixture.Drain();
        Assert.Equal("Preview only", fixture.Control<TextBlock>("Formula deployment status").Text);
        vm.FormulaSource = "mean(VDC)";
        vm.Threshold = "";
        AuthoringUiFixture.Drain();
        Assert.Equal("Missing deployment requirements", fixture.Control<TextBlock>("Formula deployment status").Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Set formula deployment threshold"));
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<TextBox>("Threshold").IsFocused);
        Assert.Contains("preceding Sample producer", vm.FormulaDeploymentRequirements);
        Assert.Contains("same actual execution scope", vm.FormulaDeploymentRequirements);
        vm.Threshold = "0";
        AuthoringUiFixture.Drain();
        Assert.Equal("Deployable recipe", fixture.Control<TextBlock>("Formula deployment status").Text);
        vm.FormulaSource = "std(VDC)";
        vm.Undo();
        AuthoringUiFixture.Drain();
        Assert.Equal("Deployable recipe", fixture.Control<TextBlock>("Formula deployment status").Text);
    }

    [AvaloniaFact]
    public void ExcludedIncompleteFormulaSavesWithBuildEnabledAndSourceUndoRemainsIntact()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "std(VDC)";
        vm.FormulaExplorationOnly = true;
        vm.Threshold = "abc";
        var nodeId = vm.SelectedSequence!.NodeId!.Value;
        var planId = vm.SelectedProgram.PlanId;
        vm.Apply(); AuthoringUiFixture.Drain();
        Assert.False(vm.HasUncompiledSources, vm.Error ?? vm.SavePreviewWarning ?? vm.Status);
        Assert.True(vm.CanPack);
        Assert.Equal("Preview only", fixture.Control<TextBlock>("Formula deployment status").Text);
        var reopened = new AuthoringDocumentStore(fixture.WorkspaceRoot).Load(planId).Document!.ToDraft();
        Assert.Equal("abc", reopened.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(nodeId, "Threshold")]);
        Assert.Equal(FormulaDeploymentIntent.Explore, reopened.AuthoringState.FormulaIntent[nodeId]);
        vm.FormulaExplorationOnly = false;
        vm.Undo(); AuthoringUiFixture.Drain();
        Assert.True(vm.FormulaExplorationOnly);
        Assert.Equal("abc", vm.Threshold);
        vm.Redo(); AuthoringUiFixture.Drain();
        Assert.False(vm.FormulaExplorationOnly);
        Assert.Equal("abc", vm.Threshold);
        vm.Apply(); AuthoringUiFixture.Drain();
        Assert.True(vm.HasUncompiledSources);
        Assert.False(vm.CanPack);
    }

    [AvaloniaFact]
    public void MissingFilterProducerStatusAndSavedBuildEligibilityAgree()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "filter([1],[1],missing)";
        vm.Apply(); AuthoringUiFixture.Drain();
        Assert.Equal("Missing deployment requirements", fixture.Control<TextBlock>("Formula deployment status").Text);
        Assert.Contains("MISSING_CHANNEL", vm.FormulaDeploymentMessage);
        Assert.True(vm.HasUncompiledSources);
        Assert.False(vm.CanPack);
    }

    [AvaloniaFact]
    public void IrregularSelectedRecordingIsVisiblePreviewEvidenceWhileSourceDeploymentBuildStaysReady()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var recording = Path.Combine(fixture.WorkspaceRoot, "recordings", "sample", "irregular");
        Directory.CreateDirectory(recording);
        var run = new TestRunRecord
        {
            PlanId = "sample",
            Samples = [
            new() { Channel = "VDC", MetricKey = "VDC", Value = 1, ElapsedMs = 0 },
            new() { Channel = "VDC", MetricKey = "VDC", Value = 2, ElapsedMs = 5 },
            new() { Channel = "VDC", MetricKey = "VDC", Value = 3, ElapsedMs = 15 }]
        };
        File.WriteAllText(Path.Combine(recording, "run.json"), JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        Assert.Null(vm.SelectedDataset);
        Assert.StartsWith("Example data", vm.DataSourceDetails);
        vm.SelectDataset(0);
        Assert.NotNull(vm.SelectedDataset);
        Assert.Equal(new double?[] { 0, 5, 15 }, vm.SelectedDataset.Run.Samples.Select(sample => sample.ElapsedMs));
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "filter([1],[1],VDC)";
        vm.ChannelKey = "VDC.recording-filter";
        vm.Apply(); AuthoringUiFixture.Drain();
        Assert.Equal("Deployable recipe", fixture.Control<TextBlock>("Formula deployment status").Text);
        Assert.Contains("TF_GRID", fixture.Control<TextBlock>("Selected recording formula evidence").Text);
        Assert.Contains("TF_GRID", vm.Preview.Note);
        Assert.False(vm.HasUncompiledSources, vm.Error ?? vm.SavePreviewWarning ?? vm.Status);
        Assert.True(vm.CanPack);
    }

}
