using HardwareTest.Core.Time;

namespace HardwareTest.ViewModels.Tests.Time;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
