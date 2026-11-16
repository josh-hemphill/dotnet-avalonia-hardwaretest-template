using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class InspectorRecipeSelectionTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Moving_between_acquisition_and_mean_keeps_the_current_recipe_visible_without_editing_either_step(int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(1280, 800); window.FontSize = font;
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        var program = vm.SelectedProgram;
        var document = vm.SelectedDocument!;
        var revision = document.Revision;
        var before = AuthoringDocumentSnapshot.Capture(program!);
        var compiled = vm.Workspace!.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == "sample");
        var compiledBytes = File.ReadAllBytes(compiled);
        var sidecarBytes = File.ReadAllBytes(PlanCompiler.SidecarPath(compiled));
        var sequence = fixture.Control<ListBox>("Program sequence");

        foreach (var (label, id) in new[]
        {
            ("Acquire VDC", AuthoringFunctionIds.BasicAcquireVoltage),
            ("Mean GTE", AuthoringFunctionIds.BasicMeanGte),
            ("Acquire VDC", AuthoringFunctionIds.BasicAcquireVoltage),
            ("Mean GTE", AuthoringFunctionIds.BasicMeanGte)
        })
        {
            sequence.SelectedItem = Assert.Single(vm.SequenceItems, row => row.Label == label);
            AuthoringUiFixture.Drain();
            await fixture.WaitForReadinessAsync();
            AssertCurrentRecipe(id);
            // Validation/preview refreshes must not drop the selection after the row switch.
            vm.SelectSequence(vm.SelectedSequenceIndex);
            AuthoringUiFixture.Drain();
            AssertCurrentRecipe(id);
        }

        Assert.Same(program, vm.SelectedProgram);
        Assert.Same(document, vm.SelectedDocument);
        Assert.Equal(revision, document.Revision);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.CanUndo);
        Assert.Equal(compiledBytes, File.ReadAllBytes(compiled));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(PlanCompiler.SidecarPath(compiled)));

        // Synchronizing the caption must retain the user's normal two-way recipe edit.
        var choices = fixture.Control<ComboBox>(AuthoringInspectorCopy.MeasureRecipeLabel);
        choices.SelectedItem = choices.Items.OfType<AuthoringFunctionDisplay>()
            .Single(choice => choice.Id == AuthoringFunctionIds.BasicPublishBandScalar);
        AuthoringUiFixture.Drain();
        await fixture.WaitForReadinessAsync();
        AssertCurrentRecipe(AuthoringFunctionIds.BasicPublishBandScalar);
        Assert.Equal(revision + 1, document.Revision);
        Assert.True(vm.HasUnsavedChanges);
        vm.Undo(); AuthoringUiFixture.Drain();
        await fixture.WaitForReadinessAsync();
        AssertCurrentRecipe(AuthoringFunctionIds.BasicMeanGte);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        Assert.False(vm.HasUnsavedChanges);

        void AssertCurrentRecipe(string expectedId)
        {
            var recipe = fixture.Control<ComboBox>(AuthoringInspectorCopy.MeasureRecipeLabel);
            recipe.BringIntoView(); AuthoringUiFixture.Drain();
            Assert.Equal(expectedId, vm.MetricFunctionId);
            var selected = Assert.IsType<AuthoringFunctionDisplay>(recipe.SelectedItem);
            Assert.Equal(expectedId, selected.Id);
            Assert.Same(vm.MetricFunctionChoices, recipe.ItemsSource);
            Assert.Same(vm.SelectedMetricFunction, selected);
            Assert.Same(Assert.Single(recipe.Items.OfType<AuthoringFunctionDisplay>(), choice => choice.Id == expectedId), selected);
            var caption = Assert.Single(recipe.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == selected.Title);
            Assert.True(caption.Bounds.Width > 0);
            Assert.True(caption.Bounds.Height > 0);
        }
    }
}
