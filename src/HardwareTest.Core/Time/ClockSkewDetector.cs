using HardwareTest.Core.Settings;
using Serilog;

namespace HardwareTest.Core.Time;

public sealed class ClockSkewResult
{
    public bool HasReference { get; init; }
    public bool ExceedsThreshold { get; init; }
    public TimeSpan Delta { get; init; }
    public string ReferenceKind { get; init; } = ClockSkew.ReferenceNone;
    public string Message { get; init; } = string.Empty;
}

public interface IClockSkewDetector
{
    /// Compares the injected clock to NTP (if configured) or last-known-good. Never throws; never blocks Run.
    ClockSkewResult Check();
}

/// Startup-only skew check. Safety Stop / worker kill must not call this (NTP wait).
public sealed class ClockSkewDetector : IClockSkewDetector
{
    private readonly AppSettings _settings;
    private readonly IClock _clock;
    private readonly LastKnownGoodClockStore _store;
    private readonly INtpTimeSource? _ntp;
    private readonly ILogger? _log;

    public ClockSkewDetector(
        AppSettings settings,
        IClock clock,
        string dataDirectory,
        INtpTimeSource? ntp = null,
        ILogger? log = null)
    {
        _settings = settings;
        _clock = clock;
        _store = new LastKnownGoodClockStore(dataDirectory, log);
        _ntp = ntp;
        _log = log;
    }

    public ClockSkewResult Check()
    {
        try
        {
            return CheckCore();
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Clock skew check failed");
            return new ClockSkewResult();
        }
    }

    private ClockSkewResult CheckCore()
    {
        var now = _clock.UtcNow;
        var thresholdMinutes = ClockSkew.ClampThresholdMinutes(_settings.ClockSkewWarnThresholdMinutes);
        var threshold = TimeSpan.FromMinutes(thresholdMinutes);

        if (!string.IsNullOrWhiteSpace(_settings.NtpHost) && _ntp is not null)
        {
            var timeout = ClockSkew.NtpTimeout();
            if (_ntp.TryGetUtcNow(_settings.NtpHost, timeout, out var ntpUtc, out var ntpError))
            {
                var delta = now - ntpUtc;
                if (delta.Duration() > threshold)
                {
                    var result = Warn(delta, ClockSkew.ReferenceNtp, thresholdMinutes);
                    _log?.Warning("{Message}", result.Message);
                    return result;
                }

                Persist(ntpUtc, ClockSkew.ReferenceNtp);
                return Ok(delta, ClockSkew.ReferenceNtp);
            }

            _log?.Information("NTP lookup skipped or failed ({Error}); falling back to last-known-good", ntpError);
        }

        var last = _store.Load();
        if (last is null)
        {
            Persist(now, "local");
            return new ClockSkewResult();
        }

        var backward = last.Utc - now;
        if (backward > threshold)
        {
            var result = Warn(now - last.Utc, ClockSkew.ReferenceLastKnownGood, thresholdMinutes);
            _log?.Warning("{Message}", result.Message);
            return result;
        }

        Persist(now, "local");
        return Ok(now - last.Utc, ClockSkew.ReferenceLastKnownGood);
    }

    private void Persist(DateTimeOffset utc, string source)
        => _store.Save(new ClockLastGoodRecord { Utc = utc, Source = source });

    private static ClockSkewResult Warn(TimeSpan delta, string referenceKind, int thresholdMinutes)
        => new()
        {
            HasReference = true,
            ExceedsThreshold = true,
            Delta = delta,
            ReferenceKind = referenceKind,
            Message = ClockSkew.FormatWarning(delta, referenceKind, thresholdMinutes),
        };

    private static ClockSkewResult Ok(TimeSpan delta, string referenceKind)
        => new()
        {
            HasReference = true,
            ExceedsThreshold = false,
            Delta = delta,
            ReferenceKind = referenceKind,
        };
}
