using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        vm.CreateProgram("intent-other"); vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1); AuthoringUiFixture.Drain(); Assert.False(checkbox.IsChecked);
        vm.SelectProgram(firstId);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == secondNode)); AuthoringUiFixture.Drain();
        Assert.True(checkbox.IsChecked); Assert.True(vm.FormulaExplorationOnly);
        vm.SelectProgram("intent-other"); AuthoringUiFixture.Drain(); Assert.False(checkbox.IsChecked);
    }
}
