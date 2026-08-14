using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Time;

public sealed class ClockSkewDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_reference_does_not_warn_or_throw()
    {
        using var temp = new TempDataDirectory();
        var clock = new FakeClock(T0);
        var detector = new ClockSkewDetector(new AppSettings { ClockSkewWarnThresholdMinutes = 5 }, clock, temp.Path);
        var result = detector.Check();
        Assert.False(result.HasReference);
        Assert.False(result.ExceedsThreshold);
        Assert.True(string.IsNullOrEmpty(result.Message));
        Assert.True(File.Exists(Path.Combine(temp.Path, ClockSkew.LastGoodFileName)));
    }

    [Fact]
    public void Backward_jump_vs_last_known_good_warns_with_delta()
    {
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { ClockSkewWarnThresholdMinutes = 5 };
        var clock = new FakeClock(T0);
        new ClockSkewDetector(settings, clock, temp.Path).Check();

        clock.UtcNow = T0.AddMinutes(-20);
        var result = new ClockSkewDetector(settings, clock, temp.Path).Check();
        Assert.True(result.HasReference);
        Assert.True(result.ExceedsThreshold);
        Assert.Equal(ClockSkew.ReferenceLastKnownGood, result.ReferenceKind);
        Assert.Equal(TimeSpan.FromMinutes(-20), result.Delta);
        Assert.Contains("20m", result.Message, StringComparison.Ordinal);
        Assert.Contains("behind", result.Message, StringComparison.Ordinal);
        Assert.Contains("Run is not blocked", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Forward_progress_vs_last_known_good_does_not_warn()
    {
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { ClockSkewWarnThresholdMinutes = 5 };
        var clock = new FakeClock(T0);
        new ClockSkewDetector(settings, clock, temp.Path).Check();

        clock.Advance(TimeSpan.FromHours(48));
        var result = new ClockSkewDetector(settings, clock, temp.Path).Check();
        Assert.True(result.HasReference);
        Assert.False(result.ExceedsThreshold);
        Assert.Equal(ClockSkew.ReferenceLastKnownGood, result.ReferenceKind);
    }

    [Fact]
    public void Ntp_skew_above_threshold_warns_and_does_not_block()
    {
        using var temp = new TempDataDirectory();
        var clock = new FakeClock(T0);
        var ntp = new FakeNtpTimeSource { Utc = T0.AddMinutes(-12) };
        var settings = new AppSettings
        {
            ClockSkewWarnThresholdMinutes = 5,
            NtpHost = "ntp.lab.local",
        };
        var result = new ClockSkewDetector(settings, clock, temp.Path, ntp).Check();
        Assert.Equal(1, ntp.CallCount);
        Assert.True(result.ExceedsThreshold);
        Assert.Equal(ClockSkew.ReferenceNtp, result.ReferenceKind);
        Assert.Equal(TimeSpan.FromMinutes(12), result.Delta);
        Assert.Contains("12m", result.Message, StringComparison.Ordinal);
        Assert.Contains("ahead of NTP", result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(temp.Path, ClockSkew.LastGoodFileName)));
    }

    [Fact]
    public void Ntp_within_threshold_persists_last_known_good()
    {
        using var temp = new TempDataDirectory();
        var clock = new FakeClock(T0);
        var ntp = new FakeNtpTimeSource { Utc = T0.AddSeconds(-30) };
        var settings = new AppSettings
        {
            ClockSkewWarnThresholdMinutes = 5,
            NtpHost = "ntp.lab.local",
        };
        var result = new ClockSkewDetector(settings, clock, temp.Path, ntp).Check();
        Assert.True(result.HasReference);
        Assert.False(result.ExceedsThreshold);
        Assert.True(File.Exists(Path.Combine(temp.Path, ClockSkew.LastGoodFileName)));
    }

    [Fact]
    public void Failed_ntp_falls_back_to_last_known_good_without_throwing()
    {
        using var temp = new TempDataDirectory();
        var settings = new AppSettings
        {
            ClockSkewWarnThresholdMinutes = 5,
            NtpHost = "ntp.lab.local",
        };
        var clock = new FakeClock(T0);
        new ClockSkewDetector(settings, clock, temp.Path).Check();

        clock.UtcNow = T0.AddMinutes(-30);
        var ntp = new FakeNtpTimeSource { Utc = null };
        var result = new ClockSkewDetector(settings, clock, temp.Path, ntp).Check();
        Assert.Equal(1, ntp.CallCount);
        Assert.True(result.ExceedsThreshold);
        Assert.Equal(ClockSkew.ReferenceLastKnownGood, result.ReferenceKind);
    }
}

public sealed class FakeNtpTimeSource : INtpTimeSource
{
    public int CallCount { get; private set; }
    public DateTimeOffset? Utc { get; set; }
    public TimeSpan? ObservedTimeout { get; private set; }

    public bool TryGetUtcNow(string host, TimeSpan timeout, out DateTimeOffset utc, out string? error)
    {
        CallCount++;
        ObservedTimeout = timeout;
        if (Utc is { } value)
        {
            utc = value;
            error = null;
            return true;
        }

        utc = default;
        error = "ntp unavailable";
        return false;
    }
}
