using Avalonia.Threading;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

internal static class AuthoringInsertionFixture
{
    public static void InsertRecipeAtSectionEnd(this AuthoringWorkspaceViewModel vm, string recipeId)
    {
        var selected = vm.SelectedSequence;
        var parent = selected is { Section: SequenceSection.Measure }
            ? AuthoringSequence.Flatten(vm.SelectedProgram).LastOrDefault(row => row.IsSection &&
                row.IndexPath.Count <= selected.IndexPath.Count && selected.IndexPath.Take(row.IndexPath.Count).SequenceEqual(row.IndexPath))?.NodeId
            : null;
        vm.RecipeSearch = string.Empty;
        vm.SelectedRecipeId = recipeId;
        vm.InsertionPosition = "End of section";
        Assert.Equal(recipeId, vm.SelectedRecipe?.Id);
        WaitForRecipe(vm);
        Assert.True(vm.CanInsertRecipe, vm.SelectedRecipePrerequisites);
        vm.InsertSelectedRecipe();
        Assert.Null(vm.Error);
        if (recipeId is AuthoringRecipeIds.Identity or AuthoringRecipeIds.Prompt or AuthoringRecipeIds.Input)
            Assert.Equal(vm.SelectedProgram!.Setup[^1].NodeId, vm.SelectedSequence!.NodeId);
        else if (recipeId == AuthoringRecipeIds.TestGroup || parent is null && recipeId is not (AuthoringRecipeIds.Repeat or AuthoringRecipeIds.Shutdown))
            Assert.Equal(vm.SelectedProgram!.Measure[^1].NodeId, vm.SelectedSequence!.NodeId);
        else if (recipeId is not (AuthoringRecipeIds.Repeat or AuthoringRecipeIds.Shutdown))
        {
            var parentRow = AuthoringSequence.Flatten(vm.SelectedProgram).Single(row => row.NodeId == parent);
            var section = Assert.IsType<SectionNode>(AuthoringSequence.ResolveMeasure(vm.SelectedProgram!, parentRow.IndexPath));
            Assert.Equal(section.Children[^1].NodeId, vm.SelectedSequence!.NodeId);
        }
        if (recipeId == AuthoringRecipeIds.Repeat)
        {
            var loop = Assert.IsType<RepeatNode>(vm.SelectedRepeat);
            Assert.Equal(selected?.NodeId, Assert.Single(loop.Children).NodeId);
        }
    }

    private static void WaitForRecipe(AuthoringWorkspaceViewModel vm)
    {
        var frame = new DispatcherFrame();
        async Task AwaitAndReleaseFrame()
        {
            try { await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { frame.Continue = false; }
        }
        var readiness = AwaitAndReleaseFrame();
        if (!readiness.IsCompleted) Dispatcher.UIThread.PushFrame(frame);
        readiness.GetAwaiter().GetResult();
        AuthoringUiFixture.Drain();
    }
}
