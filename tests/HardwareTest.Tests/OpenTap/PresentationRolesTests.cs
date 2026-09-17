using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Mixins;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

public sealed class PresentationRolesTests
{
    [Theory]
    [InlineData("timeseries", PresentationTileKind.Timeseries)]
    [InlineData("scalar", PresentationTileKind.Scalar)]
    [InlineData("passband", PresentationTileKind.Passband)]
    [InlineData("timing", PresentationTileKind.Timing)]
    [InlineData("TIMESERIES", PresentationTileKind.Timeseries)]
    public void TryMapRole_maps_known_roles(string role, PresentationTileKind expected)
        => Assert.Equal(expected, PresentationRoles.TryMapRole(role));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gauge")]
    [InlineData("unknown")]
    public void TryMapRole_returns_null_for_unknown(string? role)
        => Assert.Null(PresentationRoles.TryMapRole(role));

    [Fact]
    public void Role_constants_match_Mixins_PresentationDisplayRoles()
    {
        Assert.Equal(PresentationDisplayRoles.Timeseries, PresentationRoles.Timeseries);
        Assert.Equal(PresentationDisplayRoles.Scalar, PresentationRoles.Scalar);
        Assert.Equal(PresentationDisplayRoles.Passband, PresentationRoles.Passband);
        Assert.Equal(PresentationDisplayRoles.Timing, PresentationRoles.Timing);
    }
}
