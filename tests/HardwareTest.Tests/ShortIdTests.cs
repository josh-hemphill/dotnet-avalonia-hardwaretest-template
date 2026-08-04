using HardwareTest.Core.Text;
using Xunit;

namespace HardwareTest.Tests;

public sealed class ShortIdTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("abcd", "abcd")]
    [InlineData("abcdefgh", "abcdefgh")]
    [InlineData("abcdefghi", "abcdefgh")]
    [InlineData("0123456789abcdef", "01234567")]
    public void Display_truncates_to_eight_by_default(string? input, string expected)
        => Assert.Equal(expected, ShortId.Display(input));

    [Fact]
    public void Display_respects_custom_length()
        => Assert.Equal("0123", ShortId.Display("01234567", 4));
}
