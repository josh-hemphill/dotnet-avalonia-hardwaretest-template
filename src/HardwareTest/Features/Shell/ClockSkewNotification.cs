using HardwareTest.Core.Time;

namespace HardwareTest.Features.Shell;

/// Publishes startup clock-skew warnings onto the reserved shell strip (no dialog, does not block Run).
public static class ClockSkewNotification
{
    public static void Apply(ShellNotificationViewModel shell, ClockSkewResult result)
    {
        if (!result.ExceedsThreshold || string.IsNullOrWhiteSpace(result.Message))
        {
            shell.Clear(ShellNotificationViewModel.SourceClock);
            return;
        }

        shell.Publish(
            ShellNotificationSeverity.Warning,
            result.Message,
            dismissible: true,
            sourceKey: ShellNotificationViewModel.SourceClock);
    }
}
