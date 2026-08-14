namespace HardwareTest.Core.Time;

/// Optional NTP lookup. Implementations must honor <paramref name="timeout"/> and never throw.
public interface INtpTimeSource
{
    bool TryGetUtcNow(string host, TimeSpan timeout, out DateTimeOffset utc, out string? error);
}
