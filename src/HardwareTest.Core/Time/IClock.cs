namespace HardwareTest.Core.Time;

/// UTC clock used by idle/stale, retention, run-complete stamps, and skew checks.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// Production clock. The only idle/retention/run-complete path allowed to read the OS clock.
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private readonly TimeProvider _provider;

    public SystemClock(TimeProvider? provider = null)
    {
        _provider = provider ?? TimeProvider.System;
    }

    public DateTimeOffset UtcNow => _provider.GetUtcNow();
}
