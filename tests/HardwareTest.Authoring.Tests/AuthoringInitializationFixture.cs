namespace HardwareTest.Authoring.Tests;

internal static class AuthoringInitializationFixture
{
    // Initialize a durable demo, then make a normal edit for editor dirty-state coverage.
    internal static void CreateDemoProgram(this AuthoringWorkspaceViewModel vm, string id, params string[] additionalSlots)
    {
        vm.InitializePlan(new PlanInitializationRequest(id)
        {
            Instruments = new[] { "DMM" }.Concat(additionalSlots).Select(slot =>
                new InstrumentRef(slot, AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "Mock DMM").TypeId, slot == "DMM" ? "MOCK::INSTR0" : "MOCK::" + slot)).ToArray(),
            IdentityInstrumentSlot = "DMM"
        });
        vm.DisplayName += " edited";
    }
}
