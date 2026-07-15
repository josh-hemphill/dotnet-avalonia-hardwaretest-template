using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class VisaDiscoveryTests
{
    [Fact]
    public async Task Mock_discovery_returns_catalog()
    {
        var discovery = new MockVisaResourceDiscovery();
        var found = await discovery.FindAsync();
        Assert.True(found.Count >= 3);
        Assert.Contains(found, r => r.Resource == "MOCK::INSTR0");
    }

    [Fact]
    public void Instrument_resolver_maps_id_and_falls_back_to_literal()
    {
        var settings = new AppSettings
        {
            DefaultVisaResource = "MOCK::INSTR0",
            Instruments =
            [
                new VisaInstrument { Id = "instr0", DisplayName = "Mock DMM", Resource = "MOCK::INSTR0", Enabled = true },
            ],
        };

        Assert.Equal("MOCK::INSTR0", InstrumentResourceResolver.Resolve("instr0", settings));
        Assert.Equal("MOCK::SCOPE1", InstrumentResourceResolver.Resolve("MOCK::SCOPE1", settings));
        Assert.Equal("MOCK::INSTR0", InstrumentResourceResolver.Resolve(null, settings));
    }
}
