namespace HardwareTest.Core.Time;

/// Last trusted UTC snapshot used when NTP is unavailable (offline appliance).
public sealed class ClockLastGoodRecord
{
    public DateTimeOffset Utc { get; set; }
    public string Source { get; set; } = string.Empty;
}
