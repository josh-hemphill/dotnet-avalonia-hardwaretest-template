using System.Globalization;

namespace HardwareTest.Core.Time;

/// Shared clamp/format helpers for clock-skew settings and in-panel copy.
public static class ClockSkew
{
    public const int DefaultWarnThresholdMinutes = 5;
    public const int MinWarnThresholdMinutes = 1;
    public const int MaxWarnThresholdMinutes = 1440;
    public const int DefaultNtpTimeoutMilliseconds = 500;
    public const int MaxNtpTimeoutMilliseconds = 2000;
    public const string LastGoodFileName = "clock-last-good.json";
    public const string ReferenceNtp = "NTP";
    public const string ReferenceLastKnownGood = "last-known-good";
    public const string ReferenceNone = "none";

    public static int ClampThresholdMinutes(int minutes)
        => Math.Clamp(minutes, MinWarnThresholdMinutes, MaxWarnThresholdMinutes);

    public static TimeSpan NtpTimeout(TimeSpan? overrideTimeout = null)
    {
        if (overrideTimeout is { } value && value > TimeSpan.Zero)
        {
            var ms = Math.Clamp(
                value.TotalMilliseconds,
                50,
                MaxNtpTimeoutMilliseconds);
            return TimeSpan.FromMilliseconds(ms);
        }

        return TimeSpan.FromMilliseconds(DefaultNtpTimeoutMilliseconds);
    }

    public static string FormatWarning(TimeSpan delta, string referenceKind, int thresholdMinutes)
    {
        var ahead = delta >= TimeSpan.Zero;
        var magnitude = FormatMagnitude(delta.Duration());
        var direction = ahead ? "ahead of" : "behind";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Clock is {magnitude} {direction} {referenceKind} (threshold {thresholdMinutes} min). Run is not blocked; Engineer can quarantine if timestamps matter.");
    }

    public static string FormatMagnitude(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)duration.TotalSeconds)}s");
        }

        if (duration.TotalHours < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m");
        }

        if (duration.TotalDays < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{duration.TotalHours:0.#}h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{duration.TotalDays:0.#}d");
    }
}
