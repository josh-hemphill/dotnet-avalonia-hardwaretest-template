using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class VisaResourceParserTests
{
    [Theory]
    [InlineData("TCPIP0::192.168.1.10::INSTR", "TCPIP", "192.168.1.10", false, true)]
    [InlineData("USB0::0x2A8D::0x1301::INSTR", "USB", "0x2A8D::0x1301", false, true)]
    [InlineData("MOCK::INSTR0", "MOCK", "INSTR0", false, true)]
    [InlineData("MyScope", "Other", "possible alias", true, false)]
    [InlineData("PXI0::CHASSIS1::SLOT2::INSTR", "PXI", "CHASSIS1", false, false)]
    public void Parse_extracts_interface_and_query_heuristic(
        string resource,
        string expectedInterface,
        string expectedDetail,
        bool expectedAlias,
        bool expectedQuery)
    {
        var parsed = VisaResourceParser.Parse(resource);
        Assert.Equal(expectedInterface, parsed.Interface);
        Assert.Equal(expectedDetail, parsed.Detail);
        Assert.Equal(expectedAlias, parsed.LooksLikeAlias);
        Assert.Equal(expectedQuery, parsed.SupportsMessageQuery);
    }

    [Fact]
    public void FormatIdn_splits_csv_summary()
    {
        var (_, model, serial, _, summary) = VisaResourceParser.FormatIdn("MOCK,HardwareTestDemo,SN-0001,1.0");
        Assert.Equal("HardwareTestDemo", model);
        Assert.Equal("SN-0001", serial);
        Assert.Contains("MOCK", summary, StringComparison.Ordinal);
        Assert.Contains("S/N SN-0001", summary, StringComparison.Ordinal);
    }
}
