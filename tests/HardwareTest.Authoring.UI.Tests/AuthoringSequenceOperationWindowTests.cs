using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringSequenceOperationWindowTests
{
    [AvaloniaTheory]
    [InlineData(960, 600, 14, 1d)]
    [InlineData(1280, 800, 14, 1d)]
    [InlineData(960, 600, 20, 1.5d)]
    [InlineData(1280, 800, 20, 1.5d)]
    public void Search_insertion_and_selected_actions_dispatch_and_keep_the_row_visible(
        int width, int height, int fontSize, double scale)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = fontSize;
        window.SetRenderScaling(scale);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateProgram("sequence-actions");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var selectedId = vm.SelectedSequence!.NodeId;
        var search = fixture.Control<TextBox>("Search sequence palette");
        fixture.Type(search, "mean");
        Assert.Equal("mean", vm.RecipeSearch);
        Assert.All(vm.Recipes, recipe => Assert.Contains("mean", $"{recipe.ListLabel} {recipe.Summary}".ToLowerInvariant(), StringComparison.Ordinal));
        var recipes = fixture.Control<ComboBox>("Recipe");
        recipes.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(recipes.Focus());
        recipes.SelectedItem = vm.Recipes.Single(recipe => recipe.Id == AuthoringRecipeIds.MeanGte);
        fixture.Control<ComboBox>("Insertion point").SelectedItem = "Before selected";
        AuthoringUiFixture.Drain();
        ClickVisible(fixture, "Add recipe");
        Assert.Equal("Mean GTE", Assert.IsType<MetricNode>(vm.SelectedProgram!.Measure[0]).Metric.Name);
        Assert.Equal(selectedId, vm.SelectedProgram.Measure[1].NodeId);
        Assert.Equal(vm.SelectedProgram.Measure[0].NodeId, vm.SelectedSequence!.NodeId);
        fixture.Type(fixture.Control<TextBox>("Selected step name"), "Selected voltage");
        ClickVisible(fixture, "Rename selected step");
        Assert.Equal("Selected voltage", vm.SelectedMetric!.Name);
        var beforeDuplicate = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram);
        ClickVisible(fixture, "Duplicate selected step");
        Assert.Equal(3, vm.SelectedProgram.Measure.Count);
        Assert.Equal("VDC.mean_2", vm.SelectedMetric!.ChannelKey);
        ClickVisible(fixture, "Move selected step down");
        Assert.Equal(vm.SelectedSequence!.NodeId, vm.SelectedProgram.Measure[2].NodeId);
        ClickVisible(fixture, "Undo selected program");
        ClickVisible(fixture, "Undo selected program");
        Assert.True(beforeDuplicate.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram)));
        var sequence = fixture.Control<ListBox>("Program sequence");
        Assert.True(sequence.Bounds.Height >= 80);
        sequence.ScrollIntoView(sequence.SelectedItem!);
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(sequence, window);
        var row = Assert.IsAssignableFrom<Control>(sequence.ContainerFromIndex(sequence.SelectedIndex));
        ResponsiveShellTests.Inside(row, window);
        Assert.True(row.Focus());
        Assert.Null(vm.Error);
    }

    private static void ClickVisible(AuthoringUiFixture fixture, string name)
    {
        var button = fixture.Control<Button>(name);
        button.BringIntoView();
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(button, fixture.Window!);
        Assert.True(button.Focus());
        AuthoringUiFixture.Click(button);
    }
}
