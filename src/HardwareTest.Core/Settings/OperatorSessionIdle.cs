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

    /// Normalizes the current minute window and warning threshold.
    public static void Normalize(AppSettings settings)
    {
        settings.OperatorSessionIdleMinutes = ClampMinutes(settings.OperatorSessionIdleMinutes);
        settings.OperatorSessionIdleWarnPercent = ClampWarnPercent(settings.OperatorSessionIdleWarnPercent);
    }
}
