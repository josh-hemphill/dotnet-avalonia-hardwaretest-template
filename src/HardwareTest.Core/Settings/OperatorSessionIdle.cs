namespace HardwareTest.Core.Settings;

/// Idle-window defaults and normalization for operator session settings.
public static class OperatorSessionIdle
{
    public const int DefaultMinutes = 240;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 10080;
    public const int DefaultWarnPercent = 80;
    public const int MinWarnPercent = 50;
    public const int MaxWarnPercent = 95;

    public static int ClampMinutes(int minutes)
        => Math.Clamp(minutes, MinMinutes, MaxMinutes);

    public static int ClampWarnPercent(int percent)
        => Math.Clamp(percent, MinWarnPercent, MaxWarnPercent);

    public static int HoursToMinutes(int hours)
        => ClampMinutes(Math.Clamp(hours, 1, 168) * 60);

    public static int MinutesToHoursDisplay(int minutes)
        => Math.Max(1, (ClampMinutes(minutes) + 59) / 60);

    /// Syncs hours ↔ minutes. When <paramref name="preferMinutes"/> is false, hours is the source.
    public static void Normalize(AppSettings settings, bool preferMinutes)
    {
        if (preferMinutes)
        {
            settings.OperatorSessionIdleMinutes = ClampMinutes(settings.OperatorSessionIdleMinutes);
            settings.OperatorSessionIdleHours = MinutesToHoursDisplay(settings.OperatorSessionIdleMinutes);
        }
        else
        {
            settings.OperatorSessionIdleMinutes = HoursToMinutes(settings.OperatorSessionIdleHours);
            settings.OperatorSessionIdleHours = MinutesToHoursDisplay(settings.OperatorSessionIdleMinutes);
        }

        settings.OperatorSessionIdleWarnPercent = ClampWarnPercent(settings.OperatorSessionIdleWarnPercent);
    }

    /// After file load: migrate legacy hours-only documents; otherwise minutes is canonical.
    public static void NormalizeAfterFileLoad(AppSettings settings)
    {
        if (settings.OperatorSessionIdleMinutes == DefaultMinutes
            && settings.OperatorSessionIdleHours != DefaultMinutes / 60)
        {
            Normalize(settings, preferMinutes: false);
            return;
        }

        Normalize(settings, preferMinutes: true);
    }
}
