using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests.Time;

public sealed class OperatorSessionClockTests
{
    [Fact]
    public void Idle_stale_uses_injected_clock_not_wall_clock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var session = new OperatorSession(clock);
        session.ConfirmDut("SN-1");
        Assert.Equal(clock.UtcNow, session.LastActivityAt);

        clock.Advance(TimeSpan.FromMinutes(59));
        session.CheckIdleStale(TimeSpan.FromHours(1));
        Assert.Equal(OperatorSessionState.Active, session.State);

        clock.Advance(TimeSpan.FromMinutes(2));
        session.CheckIdleStale(TimeSpan.FromHours(1));
        Assert.Equal(OperatorSessionState.Stale, session.State);
        Assert.False(session.CanRun);
    }

    [Fact]
    public void Soft_warn_uses_injected_clock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var session = new OperatorSession(clock);
        session.ConfirmDut("SN-1");

        clock.Advance(TimeSpan.FromMinutes(80));
        session.EvaluateIdle(TimeSpan.FromMinutes(100), warnPercent: 80);
        Assert.True(session.IsIdleWarning);
        Assert.Equal(OperatorSessionState.Active, session.State);

        clock.Advance(TimeSpan.FromMinutes(20));
        session.EvaluateIdle(TimeSpan.FromMinutes(100), warnPercent: 80);
        Assert.Equal(OperatorSessionState.Stale, session.State);
        Assert.False(session.IsIdleWarning);
    }
}
