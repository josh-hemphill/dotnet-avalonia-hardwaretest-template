using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring;

/// Explicit test hardware and lifecycle choices; production creates drafts through plan initialization.
internal static class MockDmmDraftFixture
{
    public static ProgramDraft Create(string planId) => new AuthoringPlanInitializer().Construct(new(planId)
    {
        StartingPoint = PlanStartingPoint.Empty,
        UseTemplateHardware = false,
        IncludeTemplateMeasurement = false,
        Instruments = [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")],
        IdentityInstrumentSlot = "DMM",
        IncludeSafeShutdown = true,
        RequireSerial = true,
        DeviceFamily = "generic"
    }).Draft;
}

internal static class AuthoringInsertionFixture
{
    /// Append to the recipe's section through the current validated editor operation.
    /// Repeat deliberately retains the selected measurement or loop even at end of section.
    public static void InsertRecipeAtSectionEnd(this AuthoringWorkspaceViewModel vm, string recipeId)
    {
        var selectedNode = vm.SelectedSequence?.NodeId;
        vm.RecipeSearch = string.Empty;
        vm.SelectedRecipeId = recipeId;
        vm.InsertionPosition = "End of section";
        Assert.Equal(recipeId, vm.SelectedRecipe?.Id);
        Assert.True(vm.CanInsertRecipe, vm.SelectedRecipePrerequisites);
        vm.InsertSelectedRecipe();
        Assert.Null(vm.Error);
        if (recipeId is AuthoringRecipeIds.Identity or AuthoringRecipeIds.Prompt or AuthoringRecipeIds.Input)
            Assert.Equal(vm.SelectedProgram!.Setup[^1].NodeId, vm.SelectedSequence!.NodeId);
        else if (recipeId is not (AuthoringRecipeIds.Repeat or AuthoringRecipeIds.Shutdown))
            Assert.Equal(vm.SelectedProgram!.Measure[^1].NodeId, vm.SelectedSequence!.NodeId);
        if (recipeId == AuthoringRecipeIds.Repeat)
        {
            var loop = Assert.IsType<RepeatNode>(vm.SelectedRepeat);
            Assert.Equal(selectedNode, Assert.Single(loop.Children).NodeId);
        }
    }
}
