using HardwareTest.Core.Presentation;
using Xunit;

namespace HardwareTest.Tests.Presentation;

public sealed class ContrastMathTests
{
    [Fact]
    public void White_on_black_exceeds_aa()
    {
        var ratio = ContrastMath.RatioHex("#FFFFFF", "#000000");
        Assert.True(ratio >= ContrastMath.WcagAaNormalText, $"ratio={ratio}");
    }

    [Fact]
    public void ParseRgb_accepts_hash_prefix()
    {
        var (r, g, b) = ContrastMath.ParseRgb("#BF360C");
        Assert.Equal(0xBF, r);
        Assert.Equal(0x36, g);
        Assert.Equal(0x0C, b);
    }
}
