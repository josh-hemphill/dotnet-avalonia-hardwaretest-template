using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class StepPaletteSearchRecoveryTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Clearing_an_unmatched_search_restores_browsing_without_changing_insertion_target(int fontSize)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = fontSize;
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("search-recovery");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.InsertionPosition = "Before selected";
        var targetId = vm.SelectedSequence!.NodeId;
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
        var palette = Assert.Single(window.OwnedWindows);
        var search = fixture.Control<TextBox>("Search sequence palette", palette);
        fixture.Type(search, "zzzz-no-recipe-matches-this-query");
        Assert.Empty(vm.Recipes);
        Assert.False(fixture.Control<Button>("Add recipe", palette).IsEnabled);
        Assert.True(fixture.Control<TextBlock>("No matching steps", palette).IsEffectivelyVisible);
        var clear = fixture.Control<Button>("Clear step search", palette);
        clear.BringIntoView();
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(clear, palette);
        Assert.True(clear.Focus(NavigationMethod.Tab));
        palette.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        palette.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Equal(string.Empty, vm.RecipeSearch);
        Assert.True(search.IsFocused);
        Assert.NotEmpty(vm.Recipes);
        Assert.False(fixture.Control<TextBlock>("No matching steps", palette).IsEffectivelyVisible);
        Assert.Equal(targetId, vm.SelectedSequence!.NodeId);
        Assert.Equal("Before selected", vm.InsertionPosition);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        var recipes = fixture.Control<ListBox>("Recipe", palette);
        recipes.SelectedItem = vm.Recipes.Single(recipe => recipe.Id == AuthoringRecipeIds.BandScalar);
        AuthoringUiFixture.Drain();
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(20));
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.Empty(window.OwnedWindows);
        Assert.Null(vm.Error);
        var insertedId = vm.SelectedSequence!.NodeId;
        Assert.NotEqual(targetId, insertedId);
        var rows = AuthoringSequence.Flatten(vm.SelectedProgram!).ToList();
        Assert.True(rows.FindIndex(row => row.NodeId == insertedId) < rows.FindIndex(row => row.NodeId == targetId));
    }
}
