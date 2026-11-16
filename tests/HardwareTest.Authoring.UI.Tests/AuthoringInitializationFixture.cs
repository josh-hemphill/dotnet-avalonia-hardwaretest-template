using Avalonia.Controls;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

internal static class AuthoringInitializationFixture
{
    // Tests of the full instrument/template wizard deliberately choose that path.
    // Quick-create coverage asserts its own dialog directly, without this helper.
    internal static PlanInitializationWindow AdvancedPlan(AuthoringUiFixture fixture)
    {
        var dialog = Assert.Single(fixture.Window!.OwnedWindows);
        if (dialog is QuickPlanCreationWindow)
            AuthoringUiFixture.Click(fixture.Control<Button>("Start from hardware or template", dialog));
        return Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
    }

    // Initialize a durable demo, then make a normal edit for editor dirty-state coverage.
    internal static void CreateDemoProgram(this AuthoringWorkspaceViewModel vm, string id, params string[] additionalSlots)
    {
        vm.InitializePlan(new PlanInitializationRequest(id)
        {
            Instruments = new[] { "DMM" }.Concat(additionalSlots).Select(slot =>
                new InstrumentRef(slot, AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "Mock DMM").TypeId, slot == "DMM" ? "MOCK::INSTR0" : "MOCK::" + slot)).ToArray(),
            IdentityInstrumentSlot = "DMM"
        });
        // These shared editor fixtures deliberately use a flat middle sequence.
        // Production initialization and dedicated section tests retain the ordinary Measure section.
        var section = (SectionNode)vm.SelectedProgram!.Measure.Single();
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = section.Children });
        vm.DisplayName += " edited";
    }
}
