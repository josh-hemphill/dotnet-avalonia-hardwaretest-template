using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class InstrumentSlotCollectorTests
{
    [Fact]
    public void CreatePlan_sample_exposes_mock_dmm_slot_without_session_bind()
    {
        var entry = ProgramCatalog.Enumerate().First(e => e.Id == "sample");
        var plan = InstrumentSlotCollector.CreatePlan(entry);
        var slots = InstrumentSlotCollector.FromPlan(plan);

        Assert.Contains(slots, s =>
            string.Equals(s.TypeName, "MockDmmInstrument", StringComparison.Ordinal)
            && s.ResourceName.StartsWith("MOCK::", StringComparison.OrdinalIgnoreCase));
    }
}
