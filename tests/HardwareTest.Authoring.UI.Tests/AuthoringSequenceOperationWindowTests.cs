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
        vm.CreateDemoProgram("sequence-actions");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        var selectedId = vm.SelectedSequence!.NodeId;
        var search = fixture.Control<TextBox>("Search sequence palette");
        fixture.Type(search, "band");
        Assert.Equal("band", vm.RecipeSearch);
        Assert.All(vm.Recipes, recipe => Assert.Contains("band", $"{recipe.ListLabel} {recipe.Summary}".ToLowerInvariant(), StringComparison.Ordinal));
        var recipes = fixture.Control<ComboBox>("Recipe");
        recipes.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(recipes.Focus());
        recipes.SelectedItem = vm.Recipes.Single(recipe => recipe.Id == AuthoringRecipeIds.BandScalar);
        fixture.Control<ComboBox>("Insertion point").SelectedItem = "Before selected";
        AuthoringUiFixture.Drain();
        ClickVisible(fixture, "Add recipe");
        Assert.Equal("Band Scalar", Assert.IsType<MetricNode>(vm.SelectedProgram!.Measure[0]).Metric.Name);
        Assert.Equal(selectedId, vm.SelectedProgram.Measure[1].NodeId);
        Assert.Equal(vm.SelectedProgram.Measure[0].NodeId, vm.SelectedSequence!.NodeId);
        fixture.Type(fixture.Control<TextBox>("Selected step name"), "Selected voltage");
        ClickVisible(fixture, "Rename selected step");
        Assert.Equal("Selected voltage", vm.SelectedMetric!.Name);
        var beforeDuplicate = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram);
        ClickVisible(fixture, "Duplicate selected step");
        Assert.Equal(3, vm.SelectedProgram.Measure.Count);
        Assert.Equal("rail.mean_2", vm.SelectedMetric!.ChannelKey);
        ClickVisible(fixture, "Move selected step down");
        Assert.Equal(vm.SelectedSequence!.NodeId, vm.SelectedProgram.Measure[2].NodeId);
        ClickVisible(fixture, "Undo selected program");
        ClickVisible(fixture, "Undo selected program");
        Assert.True(beforeDuplicate.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram)));
        Assert.Null(vm.Error);
        var survivingId = vm.SelectedSequence!.NodeId;
        Assert.NotNull(survivingId);
        fixture.Type(search, "mean");
        recipes.SelectedItem = vm.Recipes.Single(recipe => recipe.Id == AuthoringRecipeIds.MeanGte);
        AuthoringUiFixture.Drain();
        ClickVisible(fixture, "Add recipe");
        var beforeRejectedDuplicate = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram);
        ClickVisible(fixture, "Duplicate selected step");
        Assert.Contains("runtime output", vm.Error!, StringComparison.Ordinal);
        Assert.True(beforeRejectedDuplicate.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram)));
        ClickVisible(fixture, "Undo selected program");
        Assert.True(beforeDuplicate.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram)));
        Assert.Equal(survivingId, vm.SelectedSequence?.NodeId);
        var sequence = fixture.Control<ListBox>("Program sequence");
        Assert.Equal(vm.SelectedSequenceIndex, sequence.SelectedIndex);
        Assert.NotNull(sequence.SelectedItem);
        Assert.True(sequence.Bounds.Height >= 80);
        sequence.ScrollIntoView(sequence.SelectedItem!);
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(sequence, window);
        var row = Assert.IsAssignableFrom<Control>(sequence.ContainerFromIndex(sequence.SelectedIndex));
        ResponsiveShellTests.Inside(row, window);
        Assert.True(row.Focus());
        Assert.Contains("runtime output", vm.Error!, StringComparison.Ordinal);
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
