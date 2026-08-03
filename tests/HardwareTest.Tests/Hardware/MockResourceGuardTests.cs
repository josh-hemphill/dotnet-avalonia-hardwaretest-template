using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class MockResourceGuardTests
{
    [Theory]
    [InlineData("MOCK::INSTR0", true)]
    [InlineData("mock::x", true)]
    [InlineData("USB0::0::INSTR", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeMockResource_detects_prefix(string? resource, bool expected)
        => Assert.Equal(expected, MockResourceGuard.LooksLikeMockResource(resource));

    [Theory]
    [InlineData("MockDmmInstrument", true)]
    [InlineData("VisaDmmInstrument", false)]
    [InlineData(null, false)]
    public void IsMockInstrumentType_matches_name(string? typeName, bool expected)
        => Assert.Equal(expected, MockResourceGuard.IsMockInstrumentType(typeName));
}
