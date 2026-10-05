namespace HardwareTest.Authoring.UI.Tests;

internal static class AuthoringInitializationFixture
{
    // Older editor fixtures explicitly request their DMM/identity/shutdown context.
    internal static void CreateDemoProgram(this AuthoringWorkspaceViewModel vm, string id, params string[] additionalSlots)
        => vm.CreateProgram(new PlanInitializationRequest(id)
        {
            Instruments = new[] { "DMM" }.Concat(additionalSlots).Select(slot =>
                new InstrumentRef(slot, AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "Mock DMM").TypeId, slot == "DMM" ? "MOCK::INSTR0" : "MOCK::" + slot)).ToArray(),
            IdentityInstrumentSlot = "DMM"
        });
}
