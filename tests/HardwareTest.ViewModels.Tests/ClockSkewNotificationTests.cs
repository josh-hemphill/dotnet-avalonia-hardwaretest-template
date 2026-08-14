using HardwareTest.Core.Time;
using HardwareTest.Features.Shell;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ClockSkewNotificationTests
{
    [Fact]
    public void Apply_publishes_warning_with_measured_delta()
    {
        var shell = new ShellNotificationViewModel();
        var result = new ClockSkewResult
        {
            HasReference = true,
            ExceedsThreshold = true,
            Delta = TimeSpan.FromMinutes(12),
            ReferenceKind = ClockSkew.ReferenceNtp,
            Message = ClockSkew.FormatWarning(TimeSpan.FromMinutes(12), ClockSkew.ReferenceNtp, 5),
        };

        ClockSkewNotification.Apply(shell, result);

        Assert.True(shell.HasContent);
        Assert.Equal(ShellNotificationSeverity.Warning, shell.Severity);
        Assert.Contains("12m", shell.Message, StringComparison.Ordinal);
        Assert.Contains("ahead of NTP", shell.Message, StringComparison.Ordinal);
        Assert.Contains("Run is not blocked", shell.Message, StringComparison.Ordinal);
        Assert.True(shell.IsDismissible);
    }

    [Fact]
    public void Apply_clears_strip_when_skew_is_within_threshold()
    {
        var shell = new ShellNotificationViewModel();
        ClockSkewNotification.Apply(shell, new ClockSkewResult
        {
            HasReference = true,
            ExceedsThreshold = true,
            Message = "Clock is skewed",
        });
        ClockSkewNotification.Apply(shell, new ClockSkewResult());
        Assert.False(shell.HasContent);
    }
}
