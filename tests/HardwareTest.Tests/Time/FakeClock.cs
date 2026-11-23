using HardwareTest.Core.Time;

namespace HardwareTest.Tests.Time;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
