using HardwareTest.Core.Engine;
using Xunit;

namespace HardwareTest.Tests.Engine;

public sealed class NoOpSafetyControllerTests
{
    [Fact]
    public void No_op_is_never_armed_and_does_not_throw()
    {
        var safety = new NoOpSafetyController();
        Assert.False(safety.IsArmed);
        Assert.Equal(NoOpSafetyController.NotWiredStatus, safety.StatusText);
        Assert.DoesNotContain("armed", safety.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(safety.Channels);
        safety.SafeIdle();
        safety.SafeIdle();
    }
}
