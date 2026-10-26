using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring;

/// Explicit test hardware and lifecycle choices; production creates drafts through plan initialization.
internal static class MockDmmDraftFixture
{
    public static ProgramDraft Create(string planId)
    {
        var draft = new AuthoringPlanInitializer().Construct(new(planId)
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
        // Most domain tests exercise a legacy flat sequence rather than section layout.
        return draft with { Measure = Assert.IsType<SectionNode>(Assert.Single(draft.Measure)).Children };
    }
}
